using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Schedules isochronous transfers of one endpoint on the USB frame cadence. The head transfer
/// completes one packet per frame, in packet order; when its last packet completes the transfer is
/// raised through <see cref="TransferCompleted"/> once and the next queued transfer starts on the
/// following frame. With catch-up enabled (the OUT path), a late timer catches up on the missed
/// frames so the long-run packet rate stays on the USB cadence instead of drifting with the host
/// timer resolution; the IN path keeps its original one-packet-per-callback cadence. Enqueue, cancellation
/// and clearing may run on the session thread while the frame clock advances on a timer thread:
/// one lock guards the queue and the current transfer, and <see cref="TransferCompleted"/> is
/// raised outside it. A cancelled or cleared transfer never completes; <see cref="Stop"/> drops all
/// pending work without raising events. One scheduler drives one ISO endpoint.
/// </summary>
/// <remarks>
/// The packet status of a failed packet is <see cref="UsbIpStatus.Stall"/> while the transfer-level
/// status stays <see cref="UsbIpStatus.Success"/>; failed packets are counted in
/// <see cref="UsbTransfer.ErrorCount"/> and contribute nothing to
/// <see cref="UsbTransfer.ActualLength"/>. Successful and zero-length (zero-filled) packets never
/// increment the error count.
/// <para>
/// Under-submission schedules nothing: a frame with no current or queued transfer is idle, and the
/// next enqueued transfer starts on its arrival frame - the first frame after
/// <see cref="Enqueue"/> - with no catch-up burst for the idle frames. With catch-up enabled,
/// frames missed while a timer callback was late are processed on the next callback, so a 1 ms
/// cadence remains 1 ms on average even when the host timer fires at 1.25 ms or 15.6 ms
/// granularity. Over-submission is bounded by
/// the host's own URB flow control: the queue is FIFO and has no artificial depth cap, so every
/// accepted transfer that is not cancelled or cleared completes exactly once, in order, one packet
/// per frame.
/// </para>
/// <para>
/// <see cref="UsbTransfer.StartFrame"/> is not interpreted for scheduling: the sentinel
/// <c>0xFFFFFFFF</c>, past values and future values all schedule from the arrival frame (a future
/// value is clamped to arrival). This is a ruled deviation pending live observation; the submitted
/// value is preserved only so the server can echo it in RET_SUBMIT.
/// </para>
/// </remarks>
public sealed class IsoScheduler : IDisposable
{
    private readonly UsbFrameClock _clock;

    private readonly TimeProvider _timeProvider;

    private readonly TimeSpan _frameDuration;

    private readonly Func<UsbTransfer, int, int> _producePacket;

    private readonly bool _catchUp;

    private readonly object _gate = new object();

    private readonly List<UsbTransfer> _pending = new List<UsbTransfer>();

    private UsbTransfer? _current;

    private int _packetIndex;

    private long _frameOriginTimestamp;

    private long _framesProcessed;

    private bool _frameOriginSet;

    private bool _isStopped;

    /// <summary>
    /// Initializes a scheduler whose frame clock fires at <paramref name="frameDuration"/> after
    /// <see cref="Start"/> is called.
    /// </summary>
    /// <param name="timeProvider">Provider that schedules the frames: system time in production, a manual provider in tests.</param>
    /// <param name="frameDuration">Duration of one USB frame; must be positive.</param>
    /// <param name="producePacket">
    /// Callback for one packet of a transfer; the transfer direction decides what it does. It
    /// returns the number of bytes completed; a negative return marks the packet as stalled. It is
    /// invoked on the timer thread while the scheduler lock is held, with the transfer and the
    /// zero-based packet index.
    /// </param>
    /// <param name="catchUp">
    /// When <see langword="true"/>, a late timer callback processes every frame whose deadline has
    /// elapsed, keeping the long-run cadence on the USB frame instead of drifting with the host
    /// timer resolution. When <see langword="false"/>, exactly one packet is processed per callback,
    /// preserving the original isochronous IN cadence.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> or <paramref name="producePacket"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameDuration"/> is not positive.</exception>
    public IsoScheduler(
        TimeProvider timeProvider,
        TimeSpan frameDuration,
        Func<UsbTransfer, int, int> producePacket,
        bool catchUp = false)
    {
        ArgumentNullException.ThrowIfNull(producePacket);
        _timeProvider = timeProvider;
        _frameDuration = frameDuration;
        _producePacket = producePacket;
        _catchUp = catchUp;
        _clock = new UsbFrameClock(timeProvider, frameDuration);
    }

    /// <summary>
    /// Raised once on the timer thread after the last packet of the current transfer completed,
    /// outside the scheduler lock. The transfer carries its final packet descriptors,
    /// <see cref="UsbTransfer.ActualLength"/> and <see cref="UsbTransfer.ErrorCount"/>.
    /// </summary>
    public event Action<UsbTransfer>? TransferCompleted;

    /// <summary>Number of transfers that are current or queued, including the current one.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count + (_current is null ? 0 : 1);
            }
        }
    }

    /// <summary>
    /// Starts the frame clock. Transfers enqueued before this call wait for the first frame tick.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scheduler is already running or has been stopped.</exception>
    public void Start()
    {
        lock (_gate)
        {
            if (_isStopped)
            {
                throw new InvalidOperationException("The ISO scheduler has been stopped.");
            }

            _clock.Start(HandleFrame);
            if (_catchUp)
            {
                _frameOriginTimestamp = _timeProvider.GetTimestamp();
                _framesProcessed = 0;
                _frameOriginSet = true;
            }
        }
    }

    /// <summary>
    /// Stops the frame clock and drops the current and queued transfers without raising
    /// <see cref="TransferCompleted"/>. Stopping a scheduler that is not running does nothing; the
    /// stop is terminal, so later <see cref="Enqueue"/> calls throw.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _isStopped = true;
            _current = null;
            _packetIndex = 0;
            _pending.Clear();
            _clock.Stop();
        }
    }

    /// <summary>
    /// Appends <paramref name="transfer"/> to the FIFO queue; it starts when it becomes the current
    /// transfer, on the first frame after it was enqueued (its arrival frame). Enqueueing before
    /// <see cref="Start"/> is allowed and waits for the first frame. Idle frames before the enqueue
    /// are not replayed and produce no catch-up burst.
    /// </summary>
    /// <param name="transfer">Isochronous transfer with one descriptor per packet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transfer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="transfer"/> has no <see cref="UsbTransfer.IsoPackets"/>, declares zero
    /// packets, or its packet count does not match the descriptor array.
    /// </exception>
    /// <exception cref="InvalidOperationException">The scheduler has been stopped.</exception>
    public void Enqueue(UsbTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        lock (_gate)
        {
            if (_isStopped)
            {
                throw new InvalidOperationException("The ISO scheduler has been stopped.");
            }

            IsoPacketDescriptor[]? packets = transfer.IsoPackets;
            if (packets is null)
            {
                throw new ArgumentException("An isochronous transfer must carry packet descriptors.", nameof(transfer));
            }

            if (transfer.NumberOfPackets == 0)
            {
                throw new ArgumentException("An isochronous transfer must declare at least one packet.", nameof(transfer));
            }

            if (transfer.NumberOfPackets != (uint)packets.Length)
            {
                throw new ArgumentException(
                    "The number of packets must match the number of packet descriptors.",
                    nameof(transfer));
            }

            PrepareTransfer(transfer, packets);
            bool wasIdle = _current is null && _pending.Count == 0;
            _pending.Add(transfer);
            if (_catchUp && wasIdle)
            {
                // Idle frames are not replayed: the new transfer starts on the first frame
                // after its arrival, even when the timer has been idle for a while.
                _frameOriginTimestamp = _timeProvider.GetTimestamp();
                _framesProcessed = 0;
                _frameOriginSet = true;
            }
        }
    }

    /// <summary>
    /// Removes the current or queued transfer with sequence number <paramref name="seqNum"/>. A
    /// cancelled transfer never raises <see cref="TransferCompleted"/>; the order of the remaining
    /// transfers is preserved.
    /// </summary>
    /// <param name="seqNum">Sequence number of the URB to cancel.</param>
    /// <param name="transfer">Receives the cancelled transfer when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the transfer was current or queued; otherwise <see langword="false"/>.</returns>
    public bool TryCancel(uint seqNum, out UsbTransfer transfer)
    {
        lock (_gate)
        {
            if (_current is not null && _current.SeqNum == seqNum)
            {
                transfer = _current;
                _current = null;
                _packetIndex = 0;
                return true;
            }

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].SeqNum == seqNum)
                {
                    transfer = _pending[i];
                    _pending.RemoveAt(i);
                    return true;
                }
            }

            transfer = null!;
            return false;
        }
    }

    /// <summary>
    /// Drops the current and queued transfers without raising <see cref="TransferCompleted"/>. The
    /// frame clock keeps running, so transfers enqueued afterwards are scheduled normally.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
            _packetIndex = 0;
            _pending.Clear();
        }
    }

    /// <summary>Stops the scheduler, dropping pending work without completions; equivalent to <see cref="Stop"/>.</summary>
    public void Dispose() => Stop();

    /// <summary>
    /// Frame callback: processes the packet whose frame deadline has elapsed. Without catch-up
    /// exactly one packet is processed per callback (the original IN cadence); with catch-up every
    /// missed frame is processed so a late timer does not permanently slow the endpoint. Idle frames
    /// consume their deadline and are never replayed. Completion events are raised after the lock is
    /// released so subscribers can call back into the scheduler.
    /// </summary>
    private void HandleFrame()
    {
        if (!_catchUp)
        {
            UsbTransfer? singleCompleted;
            lock (_gate)
            {
                if (_isStopped)
                {
                    return;
                }

                singleCompleted = ProcessOneFrameLocked();
            }

            if (singleCompleted is not null)
            {
                TransferCompleted?.Invoke(singleCompleted);
            }

            return;
        }

        List<UsbTransfer>? completed = null;
        lock (_gate)
        {
            if (_isStopped)
            {
                return;
            }

            if (!_frameOriginSet)
            {
                _frameOriginTimestamp = _timeProvider.GetTimestamp();
                _framesProcessed = 0;
                _frameOriginSet = true;
                return;
            }

            long now = _timeProvider.GetTimestamp();
            long dueFrames = (long)Math.Floor(
                _timeProvider.GetElapsedTime(_frameOriginTimestamp, now).Ticks / (double)_frameDuration.Ticks);
            long framesToProcess = dueFrames - _framesProcessed;
            if (framesToProcess <= 0)
            {
                return;
            }

            _framesProcessed = dueFrames;
            while (framesToProcess > 0)
            {
                UsbTransfer? completedTransfer = ProcessOneFrameLocked();
                if (completedTransfer is null && _current is null && _pending.Count == 0)
                {
                    // No work remains: consume the remaining idle deadlines without replaying them.
                    break;
                }

                if (completedTransfer is not null)
                {
                    (completed ??= new List<UsbTransfer>()).Add(completedTransfer);
                }

                framesToProcess--;
            }
        }

        if (completed is not null)
        {
            foreach (UsbTransfer transfer in completed)
            {
                TransferCompleted?.Invoke(transfer);
            }
        }
    }

    /// <summary>
    /// Processes exactly one packet of the current transfer, promoting the next queued transfer
    /// when needed, and returns the transfer when its last packet completed. The caller holds
    /// <see cref="_gate"/>.
    /// </summary>
    /// <returns>The completed transfer, or <see langword="null"/> when the transfer continues or no work is pending.</returns>
    private UsbTransfer? ProcessOneFrameLocked()
    {
        if (_current is null)
        {
            if (_pending.Count == 0)
            {
                return null;
            }

            _current = _pending[0];
            _pending.RemoveAt(0);
            _packetIndex = 0;
        }

        UsbTransfer transfer = _current!;
        IsoPacketDescriptor[] packets = transfer.IsoPackets!;
        IsoPacketDescriptor descriptor = packets[_packetIndex];
        int actualLength;
        try
        {
            actualLength = _producePacket(transfer, _packetIndex);
        }
        catch (Exception)
        {
            actualLength = -1;
        }

        if (actualLength < 0)
        {
            descriptor = descriptor with
            {
                ActualLength = 0,
                Status = UsbIpStatus.Stall
            };
            transfer.ErrorCount++;
        }
        else
        {
            int packetLength = (int)descriptor.Length;
            int acceptedLength = Math.Min(actualLength, packetLength);
            descriptor = descriptor with
            {
                ActualLength = (uint)acceptedLength,
                Status = UsbIpStatus.Success
            };
            transfer.ActualLength += acceptedLength;
        }

        packets[_packetIndex] = descriptor;
        _packetIndex++;
        if (_packetIndex < packets.Length)
        {
            return null;
        }

        _current = null;
        _packetIndex = 0;
        return transfer;
    }

    /// <summary>
    /// Resets the completion state of a newly enqueued transfer and makes sure its data buffer can
    /// hold every packet at its declared offset and length.
    /// </summary>
    /// <param name="transfer">Transfer being enqueued.</param>
    /// <param name="packets">Packet descriptors of <paramref name="transfer"/>.</param>
    /// <exception cref="ArgumentException">The buffer geometry is negative or too large.</exception>
    private static void PrepareTransfer(UsbTransfer transfer, IsoPacketDescriptor[] packets)
    {
        if (transfer.MaxTransferLength < 0)
        {
            throw new ArgumentException("The transfer buffer length must not be negative.", nameof(transfer));
        }

        long requiredLength = transfer.MaxTransferLength;
        foreach (IsoPacketDescriptor packet in packets)
        {
            long packetEnd = (long)packet.Offset + packet.Length;
            if (packetEnd > requiredLength)
            {
                requiredLength = packetEnd;
            }
        }

        if (requiredLength > int.MaxValue)
        {
            throw new ArgumentException("The transfer buffer is too large.", nameof(transfer));
        }

        if (transfer.Data is null || transfer.Data.Length < requiredLength)
        {
            transfer.Data = new byte[(int)requiredLength];
        }

        transfer.Status = UsbIpStatus.Success;
        transfer.ActualLength = 0;
        transfer.ErrorCount = 0;
    }
}