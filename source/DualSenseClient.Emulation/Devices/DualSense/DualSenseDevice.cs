using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Persona device of the DualSense (Sony 054C:0CE6, or Sony 054C:0DF2 when constructed as the Edge):
/// serves the persona's composite descriptor set, the 64-byte input report 0x01 built by
/// <see cref="DualSenseInputReport"/> from the stored input state, decodes the host's output reports
/// 0x02 (rumble, volumes, adaptive-trigger effect blocks, lightbar and LEDs) into
/// <see cref="OutputReportReceived"/>, and answers the feature-report surface of
/// <see cref="DualSenseFeatureReports"/> from the metadata state <see cref="SetMetaState"/> updates.
/// The composite configuration carries a UAC1 audio function (audio control interface 0, speaker
/// streaming interface 1 with isochronous OUT endpoint 0x01, microphone streaming interface 2 with
/// isochronous IN endpoint 0x82) beside the HID interface 3. The device forwards speaker packets to
/// <see cref="AudioOutReceived"/>, extracts their channels 2/3 into
/// <see cref="RealtimeHapticsReceived"/>, accepts capture PCM through <see cref="SetMicrophonePcm"/> and
/// drains its ring on the ISO IN endpoint, raises <see cref="SpeakerResetReceived"/> when the speaker
/// alternate setting changes, and routes class requests of the audio interfaces to its
/// <see cref="UsbAudioFunction"/>.
/// The device owns the report counters: every provided input report increments the packet counter,
/// the first touch counter advances once per report in which the first point is active, and the
/// second touch counter advances once per report in which only the second point is active (the
/// report carries one rolling timestamp for the pair, so the counter of the point the sample
/// describes advances and the other holds). The device never throws on host payloads: an unknown
/// report id or a truncated payload raises no event, and a feature id no persona descriptor
/// declares stalls. The state
/// copy and the counter updates share one lock, so <see cref="SetInput"/> (any thread), the metadata
/// updates of <see cref="SetMetaState"/> and concurrent report pulls (the endpoint pacer thread and
/// a control GET_REPORT on the session loop) can never emit a duplicate or skipped counter or a
/// torn metadata read. A published raw input report (<see cref="SetRawInputReport"/>) takes
/// precedence over the synthetic builder: every pull serves its bytes verbatim and the counters
/// freeze until <see cref="ClearRawInputReport"/> resumes them.
/// </summary>
public sealed class DualSenseDevice : VirtualHidDevice
{
    /// <summary>Neutral stick value (center) written to an untouched device's input state.</summary>
    private const byte NeutralStick = 128;

    /// <summary>Interface number of the speaker audio streaming interface (UAC1 AS OUT).</summary>
    private const byte SpeakerInterfaceNumber = 1;

    /// <summary>Interface number of the microphone audio streaming interface (UAC1 AS IN).</summary>
    private const byte MicrophoneInterfaceNumber = 2;

    /// <summary>Interface class value of every UAC1 audio interface (bInterfaceClass 0x01).</summary>
    private const byte AudioInterfaceClass = 0x01;

    /// <summary>Endpoint address of the speaker isochronous OUT endpoint (EP1 OUT).</summary>
    private const byte AudioOutEndpointAddress = 0x01;

    /// <summary>Endpoint address of the microphone isochronous IN endpoint (EP2 IN).</summary>
    private const byte AudioInEndpointAddress = 0x82;

    /// <summary>
    /// Capacity of the speaker packet scratch buffer: the wMaxPacketSize of the AS OUT endpoint
    /// (392 bytes; the nominal four-channel packet is 384 bytes and carries eight spare bytes).
    /// </summary>
    private const int AudioOutPacketCapacity = 392;

    /// <summary>
    /// Capacity of the haptics packet scratch buffer: the four haptics bytes of every whole 8-byte
    /// speaker frame of a maximum-size (392-byte) packet, so 49 frames times two channels.
    /// </summary>
    private const int HapticsPacketCapacity = 196;

    /// <summary>Bytes of one speaker sample frame: four interleaved 16-bit channels.</summary>
    private const int SpeakerFrameBytes = 8;

    /// <summary>Bytes of one extracted haptics sample frame: channels 2 and 3.</summary>
    private const int HapticsFrameBytes = 4;

    /// <summary>Offset of the first haptics byte inside one speaker frame (channels 2 and 3).</summary>
    private const int HapticsOffsetInSpeakerFrame = 4;

    /// <summary>
    /// Serializes the stored state, the metadata and the report counters against every caller of
    /// <see cref="SetInput"/>, <see cref="SetMetaState"/> and the report providers.
    /// </summary>
    private readonly Lock _stateLock = new Lock();

    /// <summary>Logging seam for subscriber faults; <see langword="null"/> disables logging.</summary>
    private readonly IEmulationLogger? _logger;

    /// <summary>UAC1 audio function of the persona: feature-unit state and the microphone PCM ring.</summary>
    private readonly UsbAudioFunction _audioFunction;

    /// <summary>
    /// Reusable speaker packet buffer of the audio OUT lane; <see cref="AudioOutReceived"/> handlers
    /// receive a view over it. Guarded against concurrent ISO packet delivery by the session's packet
    /// order, so no lock is needed.
    /// </summary>
    private readonly byte[] _audioOutScratch = new byte[AudioOutPacketCapacity];

    /// <summary>
    /// Reusable haptics packet buffer of the audio OUT lane; <see cref="RealtimeHapticsReceived"/>
    /// handlers receive a view over it.
    /// </summary>
    private readonly byte[] _hapticsScratch = new byte[HapticsPacketCapacity];

    /// <summary>
    /// Active alternate setting of the speaker streaming interface as last observed through
    /// <see cref="OnInterfaceChanged"/>; reset by <see cref="OnConfigurationChanged"/> without
    /// raising a duplicate <see cref="SpeakerResetReceived"/> for the same transition.
    /// </summary>
    private byte _speakerAlternateSetting;

    /// <summary>
    /// Whether the device serves the Edge persona: the Edge descriptor set and the firmware report's
    /// Edge controller marker; fixed at construction.
    /// </summary>
    private readonly bool _edge;

    /// <summary>Input state of the following reports; guarded by <see cref="_stateLock"/>.</summary>
    private DualSenseInputState _inputState;

    /// <summary>
    /// Controller MAC address in address order, seeded from the identity and replaced by
    /// <see cref="SetMetaState"/>; device-owned (updates clone the caller's array) and read without
    /// further copying. Guarded by <see cref="_stateLock"/> for the reference swap.
    /// </summary>
    private byte[] _macAddress;

    /// <summary>Serial number, seeded from the identity and replaced by <see cref="SetMetaState"/>; guarded by <see cref="_stateLock"/>.</summary>
    private string _serialNumber;

    /// <summary>
    /// Factory sub-command of the last 0x80 command the host wrote, or zero when none was recorded;
    /// the command response 0x81 answers from it. Guarded by <see cref="_stateLock"/>.
    /// </summary>
    private byte _commandSubcommand;

    /// <summary>
    /// Handshake payload (63 bytes) of the last 0x65 write on the Edge persona, or
    /// <see langword="null"/> when the host never wrote one; the handshake report 0x65 echoes the
    /// firmware body in that case. Device-owned (the write stores a copy of the payload). Guarded
    /// by <see cref="_stateLock"/>.
    /// </summary>
    private byte[]? _edgeHandshake;

    /// <summary>Rolling packet counter of the input reports (the reference's SeqNo byte); advanced once per provided report. Guarded by <see cref="_stateLock"/>.</summary>
    private byte _packetCounter;

    /// <summary>
    /// Rolling counter of the first touch point; advanced per report while the first point is
    /// active, so the pair's rolling touch timestamp follows the sample the report describes.
    /// Guarded by <see cref="_stateLock"/>.
    /// </summary>
    private byte _touch1Counter;

    /// <summary>
    /// Rolling counter of the second touch point; advanced per report while only the second point
    /// is active (the pair shares one timestamp, which the first point owns while it is active).
    /// Guarded by <see cref="_stateLock"/>.
    /// </summary>
    private byte _touch2Counter;

    /// <summary>
    /// Initializes the persona device with the descriptors of <paramref name="identity"/> and an
    /// explicitly neutral input state (centered sticks, released D-pad, released buttons and the
    /// identity's battery defaults: percent 0x0A and the Complete power state for the reference
    /// identity), so an untouched device reports neutral rather than the struct defaults (sticks
    /// zero and D-pad up).
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new DualSenseIdentity()</c> for the reference defaults.</param>
    /// <param name="logger">Logging seam for faults raised by report and audio subscribers; <see langword="null"/> disables logging.</param>
    public DualSenseDevice(DualSenseIdentity identity, IEmulationLogger? logger = null)
        : this(identity, false, logger)
    {
    }

    /// <summary>
    /// Initializes the persona device as the Edge variant: the descriptor set is the Edge factory's
    /// (the identity's base product id and product string are superseded exactly as the reference
    /// resolves them from its controller mode) and the firmware report 0x20 carries the Edge
    /// controller marker the reference reads back to classify the controller.
    /// </summary>
    /// <param name="identity">Identity of the persona; every value is served except the Edge product id and product string.</param>
    /// <param name="edge"><see langword="true"/> for the DualSense Edge persona; <see langword="false"/> for the base DualSense.</param>
    /// <param name="logger">Logging seam for faults raised by report and audio subscribers; <see langword="null"/> disables logging.</param>
    public DualSenseDevice(DualSenseIdentity identity, bool edge, IEmulationLogger? logger = null)
        : base(edge ? DualSenseEdgeDescriptorSet.Create(identity) : DualSenseDescriptorSet.Create(identity))
    {
        _inputState = CreateNeutralState(identity);
        _macAddress = (byte[])identity.MacAddress.Clone();
        _serialNumber = identity.SerialNumber;
        _edge = edge;
        _logger = logger;
        _audioFunction = new UsbAudioFunction(AudioFunctionFormat.DualSense, logger);
    }

    /// <summary>
    /// Raised for every recognized host output report (report id 0x02) with the decoded fields,
    /// synchronously on the thread that delivered the payload; unknown or truncated payloads raise
    /// nothing. A subscriber that throws is logged through the device's logger and skipped: the
    /// remaining subscribers still receive the report and the delivering session is not torn down.
    /// The HID class SET_REPORT path and the interrupt OUT path both arrive id-prefixed (the base
    /// engine prepends the wValue id for SET_REPORT). A bare payload is only possible through a
    /// direct <c>OnOutputReport</c> call and is not recognized.
    /// </summary>
    public event Action<DualSenseOutputReport>? OutputReportReceived;

    /// <summary>
    /// Raised for every isochronous OUT packet of the speaker endpoint (0x01) with the packet bytes
    /// verbatim (up to the endpoint's 392-byte maximum packet size): four interleaved 16-bit
    /// little-endian channels at 48 kHz (8 bytes per sample frame, 384 bytes in a nominal
    /// one-millisecond packet). This is the P5 audio-forwarding surface: the persona applies no
    /// feature-unit volume or mute scaling to the stream, and haptics are taken from
    /// <see cref="RealtimeHapticsReceived"/> rather than derived from the front channels here.
    /// The memory is a view over a per-device reusable
    /// scratch buffer and is valid only for the duration of the callback; a subscriber that keeps the
    /// data must copy it. A subscriber that throws is logged through the device's logger and skipped:
    /// the remaining subscribers still receive the packet and the delivering session is not torn down.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? AudioOutReceived;

    /// <summary>
    /// Raised for every speaker packet that carries at least one whole sample frame, with channels 2
    /// and 3 of every whole 8-byte frame extracted as two interleaved 16-bit little-endian channels at
    /// 48 kHz (4 bytes per extracted frame, 192 bytes in a nominal packet). A partial trailing frame
    /// is ignored, and a packet shorter than one whole frame raises nothing. P5 forwards this lane
    /// separately from <see cref="AudioOutReceived"/>; do not derive haptics from the front channels
    /// as well. The memory is a view over
    /// a per-device reusable scratch buffer and is valid only for the duration of the callback; a
    /// subscriber that keeps the data must copy it. A subscriber that throws is logged through the
    /// device's logger and skipped: the remaining subscribers still receive the packet and the
    /// delivering session is not torn down.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? RealtimeHapticsReceived;

    /// <summary>
    /// Raised exactly once per effective alternate-setting change of the speaker streaming interface 1
    /// between 0 and 1, including the return to 0 that SET_CONFIGURATION performs through the base
    /// class's interface hook; a write of the already active setting raises nothing. P5 should drop
    /// and rebuild its speaker pipeline on this signal. A subscriber that
    /// throws is logged through the device's logger and skipped, so it cannot tear down the session.
    /// </summary>
    public event Action? SpeakerResetReceived;

    /// <summary>
    /// Stores the input state the following input reports carry; may be called from any thread. A
    /// report built concurrently carries either the old or the new state, never a mix. The state
    /// carries the battery bytes as well, so the most recent writer of the battery fields wins: a
    /// battery update installed by <see cref="SetMetaState"/> is superseded by a following
    /// <see cref="SetInput"/>, which asserts every field of the state it stores.
    /// </summary>
    /// <param name="state">New input state; every field is reported as-is.</param>
    public void SetInput(in DualSenseInputState state)
    {
        lock (_stateLock)
        {
            _inputState = state;
        }
    }

    /// <summary>
    /// Merge-updates the persona metadata: every field left at its default (null) keeps its current
    /// value, so a partial update only changes what the caller supplies (the reference's documented
    /// <c>SetDualSenseMetaState</c> contract, where a NULL pointer or zero keeps the current value;
    /// the C# surface uses null as the sentinel so a zero battery level stays expressible). The
    /// serial number is served by the command response 0x81, the MAC address by the pairing report
    /// 0x09, and the battery level and charging state by the following input reports. A serial
    /// number left empty keeps the current value. May be called from any thread; the update is
    /// atomic against concurrent report pulls and feature reads.
    /// </summary>
    /// <param name="state">Metadata update; null fields keep their current value.</param>
    public void SetMetaState(in DualSenseMetaState state)
    {
        lock (_stateLock)
        {
            if (!string.IsNullOrEmpty(state.SerialNumber))
            {
                _serialNumber = state.SerialNumber;
            }

            if (state.MacAddress is not null)
            {
                _macAddress = (byte[])state.MacAddress.Clone();
            }

            if (state.BatteryLevel is byte batteryLevel)
            {
                _inputState.BatteryLevel = batteryLevel;
            }

            if (state.ChargingState is DualSenseChargingState chargingState)
            {
                _inputState.PowerState = chargingState;
            }
        }
    }

    /// <summary>
    /// Publishes <paramref name="report"/> as the raw input report <see cref="ProvideInputReport"/>
    /// serves until <see cref="ClearRawInputReport"/> is called: every pull returns the 64 bytes
    /// verbatim (report id, SeqNo, timestamps, touch and battery bytes exactly as supplied) and the
    /// synthetic builder, including its counters, freezes while raw is set. The payload is copied,
    /// so mutating the caller's buffer afterwards does not reach the device. May be called from any
    /// thread; a pull racing the call serves either a fully synthetic report or the raw report,
    /// never a mix.
    /// </summary>
    /// <param name="report">
    /// Raw report bytes with the report id first, like a captured hardware report (the
    /// SetDualSenseRawInputReport contract, behavior oracle); exactly <see cref="DualSenseInputReport.Size"/> bytes.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="report"/> is not exactly <see cref="DualSenseInputReport.Size"/> bytes long.
    /// </exception>
    public void SetRawInputReport(ReadOnlySpan<byte> report)
    {
        if (report.Length != DualSenseInputReport.Size)
        {
            throw new ArgumentException(
                $"A raw DualSense input report is exactly {DualSenseInputReport.Size} bytes long; got {report.Length}.",
                nameof(report));
        }

        SetRawInputReportCore(report);
    }

    /// <summary>
    /// Drops the raw input report published by <see cref="SetRawInputReport"/>: the following pulls
    /// serve synthetic reports from the current input state, with the counters resuming where they
    /// froze. Clearing without a raw report set is a no-op. May be called from any thread.
    /// </summary>
    public void ClearRawInputReport() => ClearRawInputReportCore();

    /// <summary>
    /// Enqueues one whole microphone PCM frame for the ISO IN endpoint 0x82: two interleaved 16-bit
    /// little-endian channels at 48 kHz, exactly 192 bytes (48 one-millisecond sample frames). The
    /// frame is copied into the persona's bounded microphone ring; when the ring is full its oldest
    /// frame is dropped so the newest audio survives. A P5 microphone producer calls this once per
    /// millisecond with an exact frame; with no producer the empty ring yields nominal zero-filled
    /// packets, so the lane stays silent without rate drift. Queued audio is dropped on an effective
    /// microphone alternate-setting change and on SET_CONFIGURATION 0, so a producer restarts on a
    /// frame boundary. May be called from any thread; a concurrent
    /// endpoint read yields either the old or the new ring state, never a torn frame.
    /// </summary>
    /// <param name="frame">Exactly 192 bytes of interleaved 2-channel 16-bit PCM.</param>
    /// <exception cref="ArgumentException"><paramref name="frame"/> is not exactly one microphone frame.</exception>
    public void SetMicrophonePcm(ReadOnlySpan<byte> frame)
    {
        int frameBytes = _audioFunction.Format.MicrophoneFrameBytes;
        if (frame.Length != frameBytes)
        {
            throw new ArgumentException($"A DualSense microphone frame requires exactly {frameBytes} bytes; got {frame.Length}.", nameof(frame));
        }

        _audioFunction.EnqueueMicrophonePcm(frame);
    }

    /// <summary>
    /// Builds the next 64-byte input report 0x01 from the stored state and advances the device's
    /// counters: the first report carries packet counter zero, every report increments it, the
    /// first touch counter advances once per report in which the first point is active, and the
    /// second touch counter advances once per report in which only the second point is active (both
    /// hold otherwise). The counter updates are serialized with the state copy, so concurrent pulls
    /// (the endpoint pacer thread and a control GET_REPORT on the session loop) never emit a
    /// duplicate or skipped counter. A raw input report takes precedence: while one is set, its
    /// bytes are served verbatim and none of the counters advances. A destination shorter than the
    /// report cannot carry it and receives nothing.
    /// </summary>
    /// <param name="destination">Buffer of at least <see cref="DualSenseInputReport.Size"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="DualSenseInputReport.Size"/>), or zero for an undersized destination.</returns>
    public override int ProvideInputReport(Span<byte> destination)
    {
        if (destination.Length < DualSenseInputReport.Size)
        {
            return 0;
        }

        if (TryCopyRawInputReport(destination, out int rawLength))
        {
            return rawLength;
        }

        DualSenseInputState state;
        byte packetCounter;
        byte touch1Counter;
        byte touch2Counter;
        lock (_stateLock)
        {
            state = _inputState;
            packetCounter = _packetCounter;
            _packetCounter = (byte)(_packetCounter + 1);
            touch1Counter = _touch1Counter;
            touch2Counter = _touch2Counter;
            if (state.Touch1Active)
            {
                _touch1Counter = (byte)(_touch1Counter + 1);
            }
            else if (state.Touch2Active)
            {
                _touch2Counter = (byte)(_touch2Counter + 1);
            }
        }

        return DualSenseInputReport.Build(state, packetCounter, touch1Counter, touch2Counter, destination);
    }

    /// <summary>
    /// Serves a feature report through <see cref="DualSenseFeatureReports"/>: the calibration,
    /// pairing, firmware, factory command-response and (on the Edge persona) handshake reports
    /// answer from the device's metadata snapshot, every other report id the persona's descriptors
    /// declare answers its declared length as a zero stub, and an id no descriptor declares stalls,
    /// exactly like the reference and its unfilled controller cache. The snapshot is
    /// taken under the state lock, so a concurrent <see cref="SetMetaState"/>, <see cref="SetInput"/>
    /// or feature write yields either the old or the new value, never a mix.
    /// </summary>
    /// <param name="reportId">Report id of the requested report (wValue low byte).</param>
    /// <param name="destination">Buffer the report is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    protected override int ProvideFeatureReport(byte reportId, Span<byte> destination)
    {
        DualSenseFeatureReportState state;
        lock (_stateLock)
        {
            state = new DualSenseFeatureReportState
            {
                MacAddress = _macAddress,
                SerialNumber = _serialNumber,
                EdgePersona = _edge,
                CommandSubcommand = _commandSubcommand,
                EdgeHandshake = _edgeHandshake
            };
        }

        return DualSenseFeatureReports.Write(reportId, state, destination);
    }

    /// <summary>
    /// Handles a feature report write: a factory command 0x80 carrying at least two payload bytes
    /// records its sub-command (the command response 0x81 answers from it), a full-length Edge
    /// handshake 0x65 stores its 63-byte payload (the handshake report echoes it instead of the
    /// firmware body), and every write is acknowledged because the reference's SET_REPORT path has
    /// no failure branch and accepts every feature write. Only the factory command and the Edge
    /// handshake carry observable state; the reference's own vendor configuration command is not
    /// part of this persona.
    /// </summary>
    /// <param name="reportId">Report id of the written report (wValue low byte).</param>
    /// <param name="data">Payload of the report, without the report id.</param>
    /// <returns>Always <see langword="true"/>: the write is acknowledged.</returns>
    protected override bool OnFeatureReport(byte reportId, ReadOnlySpan<byte> data)
    {
        if (reportId == DualSenseFeatureReports.CommandReportId
            && data.Length >= DualSenseFeatureReports.CommandPayloadLength)
        {
            lock (_stateLock)
            {
                _commandSubcommand = data[0];
            }
        }
        else if (reportId == DualSenseFeatureReports.EdgeHandshakeReportId
                 && _edge
                 && data.Length >= DualSenseFeatureReports.EdgeHandshakePayloadLength)
        {
            lock (_stateLock)
            {
                _edgeHandshake = data[..DualSenseFeatureReports.EdgeHandshakePayloadLength].ToArray();
            }
        }

        return true;
    }

    /// <summary>
    /// Handles one interrupt OUT payload: a recognized output report 0x02 raises
    /// <see cref="OutputReportReceived"/> with the decoded fields, while an unknown report id or a
    /// truncated payload is ignored without throwing. Every subscriber is invoked in isolation: a
    /// throwing one is logged at <see cref="EmulationLogLevel.Error"/> and skipped, so it can neither
    /// tear down the delivering session nor keep the remaining subscribers from receiving the report.
    /// </summary>
    /// <param name="data">Payload of the host-to-device transfer (report id first).</param>
    public override void OnOutputReport(ReadOnlySpan<byte> data)
    {
        if (!DualSenseOutputReport.TryParse(data, out DualSenseOutputReport report))
        {
            return;
        }

        Action<DualSenseOutputReport>? handlers = OutputReportReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DualSenseOutputReport>)handler)(report);
            }
            catch (Exception exception)
            {
                _logger?.Log(EmulationLogLevel.Error, $"An OutputReportReceived handler failed: {exception}");
            }
        }
    }

    /// <summary>
    /// Handles one isochronous OUT packet of the speaker endpoint: the packet bytes (up to the
    /// endpoint's 392-byte maximum) are copied into the device's reusable audio scratch buffer and
    /// raised through <see cref="AudioOutReceived"/>, then the four haptics bytes of every whole
    /// 8-byte sample frame are extracted into the haptics scratch buffer and raised through
    /// <see cref="RealtimeHapticsReceived"/>. A partial trailing frame contributes no haptics, and a
    /// packet shorter than one whole frame raises no haptics event. Packets of every other endpoint
    /// are ignored.
    /// </summary>
    /// <param name="endpointAddress">Endpoint address the packet belongs to (bEndpointAddress).</param>
    /// <param name="packet">Packet bytes: the transfer buffer slice at the packet's offset and length.</param>
    public override void OnIsoPacketReceived(byte endpointAddress, ReadOnlySpan<byte> packet)
    {
        if (endpointAddress != AudioOutEndpointAddress)
        {
            return;
        }

        int audioLength = Math.Min(packet.Length, _audioOutScratch.Length);
        packet[..audioLength].CopyTo(_audioOutScratch);
        RaiseAudioOutReceived(audioLength);
        RaiseRealtimeHapticsReceived(audioLength);
    }

    /// <summary>
    /// Produces the next isochronous IN packet of the microphone endpoint from the persona's audio
    /// function: queued PCM is copied up to the packet length, and an empty ring yields the nominal
    /// 192-byte zero packet (or the whole destination when it is shorter). Every other endpoint
    /// produces nothing.
    /// </summary>
    /// <param name="endpointAddress">Endpoint address the packet belongs to (bEndpointAddress).</param>
    /// <param name="destination">Packet buffer the data is written to.</param>
    /// <returns>The number of bytes written, or zero for an endpoint without an audio lane.</returns>
    public override int ProvideIsoPacket(byte endpointAddress, Span<byte> destination)
    {
        if (endpointAddress != AudioInEndpointAddress)
        {
            return 0;
        }

        return _audioFunction.ReadMicrophonePcm(destination);
    }

    /// <summary>
    /// Tracks the effective alternate-setting changes of the audio streaming interfaces: a speaker
    /// change (interface 1) raises <see cref="SpeakerResetReceived"/> once when the setting really
    /// changed, and a microphone change (interface 2) drops every queued capture byte. The base class
    /// calls this only for an effective change and for the return to 0 that SET_CONFIGURATION issues.
    /// </summary>
    /// <param name="interfaceNumber">Interface number (bInterfaceNumber) that changed.</param>
    /// <param name="alternateSetting">The new active alternate setting (bAlternateSetting).</param>
    protected override void OnInterfaceChanged(byte interfaceNumber, byte alternateSetting)
    {
        if (interfaceNumber == SpeakerInterfaceNumber)
        {
            if (_speakerAlternateSetting == alternateSetting)
            {
                return;
            }

            _speakerAlternateSetting = alternateSetting;
            RaiseSpeakerResetReceived();
        }
        else if (interfaceNumber == MicrophoneInterfaceNumber)
        {
            _audioFunction.ClearMicrophonePcm();
        }
    }

    /// <summary>
    /// Handles SET_CONFIGURATION: selecting the unconfigured state (value 0) drops queued microphone
    /// audio and resets the tracked speaker setting. The base class already returned the interfaces to
    /// alternate setting 0 and raised their interface hooks before this call, so no second
    /// <see cref="SpeakerResetReceived"/> is raised for the same transition.
    /// </summary>
    /// <param name="configurationValue">The new configuration value (0 selects the unconfigured state).</param>
    protected override void OnConfigurationChanged(byte configurationValue)
    {
        if (configurationValue != 0)
        {
            return;
        }

        _speakerAlternateSetting = 0;
        _audioFunction.ClearMicrophonePcm();
    }

    /// <summary>
    /// Routes class requests by interface class: requests addressed to an audio interface (class 0x01)
    /// are handled by the persona's <see cref="UsbAudioFunction"/> (feature-unit mute and volume state),
    /// and every other interface - including the HID interface 3 - keeps the base class's HID handling
    /// unchanged. A request the audio function does not serve stalls.
    /// </summary>
    /// <param name="setup">Setup packet of the transfer.</param>
    /// <param name="outData">Payload of a host-to-device transfer; empty for device-to-host requests.</param>
    /// <param name="response">Buffer for the response of a device-to-host transfer.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    protected override int OnClassRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, Span<byte> response)
    {
        if (Descriptors.TryGetInterface((byte)setup.Index, 0, out UsbInterfaceInfo interfaceInfo)
            && interfaceInfo.Class == AudioInterfaceClass)
        {
            return _audioFunction.HandleClassRequest(setup, outData, response);
        }

        return base.OnClassRequest(setup, outData, response);
    }

    /// <summary>
    /// Raises <see cref="AudioOutReceived"/> with a view over the first
    /// <paramref name="length"/> bytes of the audio scratch buffer, invoking every subscriber in
    /// isolation; a throwing subscriber is logged at <see cref="EmulationLogLevel.Error"/> and skipped.
    /// </summary>
    /// <param name="length">Number of valid packet bytes in the scratch buffer.</param>
    private void RaiseAudioOutReceived(int length)
    {
        Action<ReadOnlyMemory<byte>>? handlers = AudioOutReceived;
        if (handlers is null)
        {
            return;
        }

        ReadOnlyMemory<byte> packet = _audioOutScratch.AsMemory(0, length);
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<ReadOnlyMemory<byte>>)handler)(packet);
            }
            catch (Exception exception)
            {
                _logger?.Log(EmulationLogLevel.Error, $"An AudioOutReceived handler failed: {exception}");
            }
        }
    }

    /// <summary>
    /// Extracts channels 2 and 3 of every whole speaker frame into the haptics scratch buffer and
    /// raises <see cref="RealtimeHapticsReceived"/> with a view over the extracted bytes; a packet
    /// without a whole frame raises nothing. Every subscriber is invoked in isolation and a throwing
    /// subscriber is logged at <see cref="EmulationLogLevel.Error"/> and skipped.
    /// </summary>
    /// <param name="audioLength">Number of valid speaker packet bytes in the audio scratch buffer.</param>
    private void RaiseRealtimeHapticsReceived(int audioLength)
    {
        int hapticsLength = 0;
        for (int offset = 0; offset + SpeakerFrameBytes <= audioLength; offset += SpeakerFrameBytes)
        {
            _audioOutScratch.AsSpan(offset + HapticsOffsetInSpeakerFrame, HapticsFrameBytes)
                .CopyTo(_hapticsScratch.AsSpan(hapticsLength));
            hapticsLength += HapticsFrameBytes;
        }

        if (hapticsLength == 0)
        {
            return;
        }

        Action<ReadOnlyMemory<byte>>? handlers = RealtimeHapticsReceived;
        if (handlers is null)
        {
            return;
        }

        ReadOnlyMemory<byte> haptics = _hapticsScratch.AsMemory(0, hapticsLength);
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<ReadOnlyMemory<byte>>)handler)(haptics);
            }
            catch (Exception exception)
            {
                _logger?.Log(EmulationLogLevel.Error, $"A RealtimeHapticsReceived handler failed: {exception}");
            }
        }
    }

    /// <summary>
    /// Raises <see cref="SpeakerResetReceived"/>, invoking every subscriber in isolation; a throwing
    /// subscriber is logged at <see cref="EmulationLogLevel.Error"/> and skipped.
    /// </summary>
    private void RaiseSpeakerResetReceived()
    {
        Action? handlers = SpeakerResetReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch (Exception exception)
            {
                _logger?.Log(EmulationLogLevel.Error, $"A SpeakerResetReceived handler failed: {exception}");
            }
        }
    }

    /// <summary>
    /// Creates the device's neutral input state: centered sticks, released D-pad, all buttons
    /// released, released triggers and the identity's battery defaults; the touch points, motion
    /// axes and timestamps stay zero.
    /// </summary>
    /// <param name="identity">Identity carrying the battery defaults of the neutral state.</param>
    /// <returns>A neutral input state, the construction default of the device.</returns>
    private static DualSenseInputState CreateNeutralState(DualSenseIdentity identity)
    {
        return new DualSenseInputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualSenseDpadDirection.Released,
            BatteryLevel = identity.BatteryLevel,
            PowerState = identity.ChargingState
        };
    }
}

/// <summary>
/// Runtime metadata update of the DualSense persona, applied by <see cref="DualSenseDevice.SetMetaState"/>
/// with merge semantics: every field left at its default (<see langword="null"/>, or an empty string
/// for the serial number) keeps the device's current value, so a partial update only changes what the
/// caller supplies. The contract mirrors the reference's documented <c>SetDualSenseMetaState</c>
/// behavior (behavior oracle: "fields left at their zero value (NULL/0) keep the current value"),
/// with <see langword="null"/> as the C# "unspecified" sentinel so that a zero battery level stays
/// expressible. The reference's shell-color, board, build-time, voltage and temperature fields are
/// not modelled: this persona encodes no color anywhere (the input report does
/// not carry the shell color and the descriptor set is fixed at construction), the firmware report
/// echoes the reference body and no served byte carries the other fields.
/// </summary>
public readonly struct DualSenseMetaState
{
    /// <summary>Serial number served by the command response 0x81; null or empty keeps the current value.</summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Controller MAC address, six bytes in address order, served reversed by the pairing report
    /// 0x09; null keeps the current value (a supplied array is copied, so later mutation of the
    /// caller's array does not reach the device).
    /// </summary>
    public byte[]? MacAddress { get; init; }

    /// <summary>
    /// Battery capacity nibble (0x0-0xA) of the following input reports; null keeps the current
    /// value. The reference's raw battery byte is split into this level and <see cref="ChargingState"/>.
    /// </summary>
    public byte? BatteryLevel { get; init; }

    /// <summary>Charging state nibble of the following input reports; null keeps the current value.</summary>
    public DualSenseChargingState? ChargingState { get; init; }
}