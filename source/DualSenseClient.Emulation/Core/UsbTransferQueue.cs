namespace DualSenseClient.Emulation.Core;

/// <summary>
/// FIFO queue of the pending <see cref="UsbTransfer"/> instances of one endpoint. Every member is
/// thread-safe and synchronized on a single lock, so a session thread can enqueue and cancel
/// URBs while a completion thread dequeues them. An instance holds transfers of a single endpoint;
/// the order of the transfers that stay queued is preserved by cancellation.
/// </summary>
public sealed class UsbTransferQueue
{
    private readonly object _lock = new object();

    private readonly List<UsbTransfer> _pending = new List<UsbTransfer>();

    /// <summary>Number of transfers currently queued.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>
    /// Appends <paramref name="transfer"/> to the back of the queue.
    /// </summary>
    /// <param name="transfer">The transfer to enqueue.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transfer"/> is <see langword="null"/>.</exception>
    public void Enqueue(UsbTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        lock (_lock)
        {
            _pending.Add(transfer);
        }
    }

    /// <summary>
    /// Removes and returns the transfer at the front of the queue (FIFO order).
    /// </summary>
    /// <param name="transfer">Receives the dequeued transfer when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a transfer was dequeued; <see langword="false"/> when the queue is empty.</returns>
    public bool TryDequeue(out UsbTransfer transfer)
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                transfer = null!;
                return false;
            }

            transfer = _pending[0];
            _pending.RemoveAt(0);
            return true;
        }
    }

    /// <summary>
    /// Removes and returns the queued transfer with sequence number <paramref name="seqNum"/>,
    /// wherever it is in the queue; the order of the remaining transfers is preserved.
    /// A transfer that was already dequeued or never queued is unknown.
    /// </summary>
    /// <param name="seqNum">Sequence number of the transfer to cancel.</param>
    /// <param name="transfer">Receives the cancelled transfer when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when a queued transfer was removed; <see langword="false"/> when no queued transfer has that sequence number.</returns>
    public bool TryCancel(uint seqNum, out UsbTransfer transfer)
    {
        lock (_lock)
        {
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

    /// <summary>Removes every queued transfer.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _pending.Clear();
        }
    }
}