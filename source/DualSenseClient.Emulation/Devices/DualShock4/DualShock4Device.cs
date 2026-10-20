using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Persona device of the DualShock 4 (Sony 054C:09CC as <see cref="DualShock4Variant.V2"/>, Sony
/// 054C:05C4 as <see cref="DualShock4Variant.V1"/>): serves the variant's composite
/// <see cref="DualShock4DescriptorSet"/> descriptor set and the 64-byte input report 0x01 built by
/// <see cref="DualShock4InputReport"/> from the stored input state, serves the feature reports
/// 0x02/0x12/0x81/0xA3 through <see cref="DualShock4FeatureReports"/> and stalls the rest, and
/// decodes the host's output reports 0x05 (rumble, lightbar, flash) into
/// <see cref="OutputReportReceived"/>.
/// The composite configuration carries a UAC1 audio function (audio control interface 0, speaker
/// streaming interface 1 with isochronous OUT endpoint 0x01, microphone streaming interface 2 with
/// isochronous IN endpoint 0x82) beside the HID interface 3. The device forwards speaker packets to
/// <see cref="AudioOutReceived"/>, accepts capture PCM through <see cref="SetMicrophonePcm"/> and
/// drains its ring on the ISO IN endpoint, raises <see cref="SpeakerResetReceived"/> when the speaker
/// alternate setting changes, and routes class requests of the audio interfaces to its
/// <see cref="UsbAudioFunction"/>. The DualShock 4 has no realtime-haptics lane, so the device exposes
/// no haptics event.
/// The report shape is variant-independent; only the descriptor set differs. The device owns
/// the report counters: every provided input report increments the six-bit packet counter, and each
/// touch counter advances once per report in which its point is active and holds otherwise. The
/// device never throws on host payloads: an unknown report id or a truncated payload raises no
/// event. The state copy and the counter updates share one lock, so <see cref="SetInput"/> (any
/// thread) and concurrent input-report pulls (the endpoint pacer thread and a control GET_REPORT on
/// the session loop) can never emit a duplicate or skipped counter. A published raw input report
/// (<see cref="SetRawInputReport"/>) takes precedence over the synthetic builder: every pull serves
/// its bytes verbatim and the counters freeze until <see cref="ClearRawInputReport"/> resumes them.
/// </summary>
public sealed class DualShock4Device : VirtualHidDevice
{
    /// <summary>Neutral stick value (center) written to an untouched device's input state.</summary>
    private const byte NeutralStick = 128;

    /// <summary>Neutral battery level: 10 = 100% while discharging (the hardware's capacity band).</summary>
    private const byte NeutralBatteryLevel = 10;

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
    /// (132 bytes; the nominal two-channel packet is 128 bytes and carries four spare bytes).
    /// </summary>
    private const int AudioOutPacketCapacity = 132;

    /// <summary>
    /// Serializes the stored state and the report counters against every caller of
    /// <see cref="SetInput"/> and <see cref="ProvideInputReport"/>.
    /// </summary>
    private readonly Lock _stateLock = new Lock();

    /// <summary>Logging seam for subscriber faults; <see langword="null"/> disables logging.</summary>
    private readonly IEmulationLogger? _logger;

    /// <summary>UAC1 audio function of the persona: feature-unit state and the microphone PCM ring.</summary>
    private readonly UsbAudioFunction _audioFunction;

    /// <summary>Controller MAC address in address order served by feature reports 0x12/0x81; empty when the identity carries none.</summary>
    private readonly byte[] _macAddress;

    /// <summary>
    /// Reusable speaker packet buffer of the audio OUT lane; <see cref="AudioOutReceived"/> handlers
    /// receive a view over it. Guarded against concurrent ISO packet delivery by the session's packet
    /// order, so no lock is needed.
    /// </summary>
    private readonly byte[] _audioOutScratch = new byte[AudioOutPacketCapacity];

    /// <summary>
    /// Active alternate setting of the speaker streaming interface as last observed through
    /// <see cref="OnInterfaceChanged"/>; reset by <see cref="OnConfigurationChanged"/> without
    /// raising a duplicate <see cref="SpeakerResetReceived"/> for the same transition.
    /// </summary>
    private byte _speakerAlternateSetting;

    /// <summary>Input state of the following reports; guarded by <see cref="_stateLock"/>.</summary>
    private DualShock4InputState _inputState;

    /// <summary>Rolling packet counter of the input reports; advanced once per provided report. Guarded by <see cref="_stateLock"/>.</summary>
    private byte _packetCounter;

    /// <summary>Rolling counter of the first touch point; advanced per report while the point is active. Guarded by <see cref="_stateLock"/>.</summary>
    private byte _touch1Counter;

    /// <summary>Rolling counter of the second touch point; advanced per report while the point is active. Guarded by <see cref="_stateLock"/>.</summary>
    private byte _touch2Counter;

    /// <summary>
    /// Initializes the persona device with the descriptors of <paramref name="identity"/> and
    /// <paramref name="variant"/> and an explicitly neutral input state (centered sticks, released
    /// D-pad, full discharging battery), so an untouched device reports neutral rather than the struct
    /// defaults (sticks zero and D-pad up).
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new DualShock4Identity()</c> for the reference defaults.</param>
    /// <param name="logger">Logging seam for faults raised by report and audio subscribers; <see langword="null"/> disables logging.</param>
    /// <param name="variant">Descriptor variant of the persona; the default is the reference v2 (<see cref="DualShock4Variant.V2"/>).</param>
    public DualShock4Device(DualShock4Identity identity, IEmulationLogger? logger = null, DualShock4Variant variant = DualShock4Variant.V2)
        : base(DualShock4DescriptorSet.Create(identity, variant))
    {
        _inputState = CreateNeutralState();
        _logger = logger;
        _audioFunction = new UsbAudioFunction(AudioFunctionFormat.DualShock4, logger);
        _macAddress = identity.MacAddress is { Length: >= DualShock4FeatureReports.MacAddressLength } mac
            ? mac[..DualShock4FeatureReports.MacAddressLength]
            : [];
    }

    /// <summary>
    /// Raised for every recognized host output report (report id 0x05) with the decoded fields,
    /// synchronously on the thread that delivered the payload; unknown or truncated payloads raise
    /// nothing. A subscriber that throws is logged through the device's logger and skipped: the
    /// remaining subscribers still receive the report and the delivering session is not torn down.
    /// The HID class SET_REPORT path and the interrupt OUT path both arrive id-prefixed (the base
    /// engine prepends the wValue id for SET_REPORT). A bare payload is only possible through a
    /// direct <c>OnOutputReport</c> call and is not recognized.
    /// </summary>
    public event Action<DualShock4OutputReport>? OutputReportReceived;

    /// <summary>
    /// Raised for every isochronous OUT packet of the speaker endpoint (0x01) with the packet bytes
    /// verbatim (up to the endpoint's 132-byte maximum packet size): two interleaved 16-bit
    /// little-endian channels at 32 kHz (4 bytes per sample frame, 128 bytes in a nominal
    /// one-millisecond packet). This is the P5 audio-forwarding surface: the persona applies no
    /// feature-unit volume or mute scaling to the stream, and the DualShock 4 has no haptics lane.
    /// The memory is a view over a per-device reusable scratch buffer and is
    /// valid only for the duration of the callback; a subscriber that keeps the data must copy it.
    /// A subscriber that throws is logged through the device's logger and skipped: the remaining
    /// subscribers still receive the packet and the delivering session is not torn down.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? AudioOutReceived;

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
    /// report built concurrently carries either the old or the new state, never a mix.
    /// </summary>
    /// <param name="state">New input state; every field is reported as-is.</param>
    public void SetInput(in DualShock4InputState state)
    {
        lock (_stateLock)
        {
            _inputState = state;
        }
    }

    /// <summary>
    /// Publishes <paramref name="report"/> as the raw input report <see cref="ProvideInputReport"/>
    /// serves until <see cref="ClearRawInputReport"/> is called: every pull returns the 64 bytes
    /// verbatim (report id, packet and touch counters, timestamps, touch and battery bytes exactly
    /// as supplied) and the synthetic builder, including its counters, freezes while raw is set.
    /// The payload is copied, so mutating the caller's buffer afterwards does not reach the device.
    /// May be called from any thread; a pull racing the call serves either a fully synthetic report
    /// or the raw report, never a mix.
    /// </summary>
    /// <param name="report">
    /// Raw report bytes with the report id first, like a captured hardware report (the
    /// SetDS4RawInputReport contract, behavior oracle); exactly <see cref="DualShock4InputReport.Size"/> bytes.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="report"/> is not exactly <see cref="DualShock4InputReport.Size"/> bytes long.
    /// </exception>
    public void SetRawInputReport(ReadOnlySpan<byte> report)
    {
        if (report.Length != DualShock4InputReport.Size)
        {
            throw new ArgumentException(
                $"A raw DualShock 4 input report is exactly {DualShock4InputReport.Size} bytes long; got {report.Length}.",
                nameof(report));
        }

        SetRawInputReportCore(report);
    }

    /// <summary>
    /// Drops the raw input report published by <see cref="SetRawInputReport"/>: the following pulls
    /// serve synthetic reports from the current input state, with the packet and touch counters
    /// resuming where they froze. Clearing without a raw report set is a no-op. May be called from
    /// any thread.
    /// </summary>
    public void ClearRawInputReport() => ClearRawInputReportCore();

    /// <summary>
    /// Enqueues one whole microphone PCM frame for the ISO IN endpoint 0x82: one interleaved 16-bit
    /// little-endian channel at 16 kHz, exactly 32 bytes (16 one-millisecond samples). The frame is
    /// copied into the persona's bounded microphone ring; when the ring is full its oldest frame is
    /// dropped so the newest audio survives. A P5 microphone producer calls this once per millisecond
    /// with an exact frame; with no producer the empty ring yields nominal zero-filled packets, so
    /// the lane stays silent without rate drift. Queued audio is dropped on an effective microphone
    /// alternate-setting change and on SET_CONFIGURATION 0, so a producer restarts on a frame
    /// boundary. May be called from any thread; a concurrent endpoint read yields either the old or
    /// the new ring state, never a torn frame.
    /// </summary>
    /// <param name="frame">Exactly 32 bytes of interleaved 1-channel 16-bit PCM.</param>
    /// <exception cref="ArgumentException"><paramref name="frame"/> is not exactly one microphone frame.</exception>
    public void SetMicrophonePcm(ReadOnlySpan<byte> frame)
    {
        int frameBytes = _audioFunction.Format.MicrophoneFrameBytes;
        if (frame.Length != frameBytes)
        {
            throw new ArgumentException($"A DualShock 4 microphone frame requires exactly {frameBytes} bytes; got {frame.Length}.", nameof(frame));
        }

        _audioFunction.EnqueueMicrophonePcm(frame);
    }

    /// <summary>
    /// Builds the next 64-byte input report 0x01 from the stored state and advances the device's
    /// counters: the first report carries packet counter zero, every report increments it, and a
    /// touch counter advances once per report in which its touch point is active (it holds
    /// otherwise). The counter updates are serialized with the state copy, so concurrent pulls
    /// (the endpoint pacer thread and a control GET_REPORT on the session loop) never emit a
    /// duplicate or skipped counter. A raw input report takes precedence: while one is set, its
    /// bytes are served verbatim and none of the counters advances. A destination shorter than the
    /// report cannot carry it and receives nothing.
    /// </summary>
    /// <param name="destination">Buffer of at least <see cref="DualShock4InputReport.Size"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="DualShock4InputReport.Size"/>), or zero for an undersized destination.</returns>
    public override int ProvideInputReport(Span<byte> destination)
    {
        if (destination.Length < DualShock4InputReport.Size)
        {
            return 0;
        }

        if (TryCopyRawInputReport(destination, out int rawLength))
        {
            return rawLength;
        }

        DualShock4InputState state;
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

            if (state.Touch2Active)
            {
                _touch2Counter = (byte)(_touch2Counter + 1);
            }
        }

        return DualShock4InputReport.Build(state, packetCounter, touch1Counter, touch2Counter, destination);
    }

    /// <summary>
    /// Serves a feature report through <see cref="DualShock4FeatureReports"/>: the calibration
    /// (0x02), pairing (0x12), controller-MAC (0x81) and firmware (0xA3) anchors answer their fixed
    /// bodies, and every other report id - including the declared 0x04 - stalls.
    /// </summary>
    /// <param name="reportId">Report id of the requested report (wValue low byte).</param>
    /// <param name="destination">Buffer the report is written to.</param>
    /// <returns>The number of bytes written, or -1 for STALL.</returns>
    protected override int ProvideFeatureReport(byte reportId, Span<byte> destination) =>
        DualShock4FeatureReports.Write(reportId, _macAddress, destination);

    /// <summary>
    /// Handles one interrupt OUT payload: a recognized output report 0x05 raises
    /// <see cref="OutputReportReceived"/> with the decoded fields, while an unknown report id or a
    /// truncated payload is ignored without throwing. Every subscriber is invoked in isolation: a
    /// throwing one is logged at <see cref="EmulationLogLevel.Error"/> and skipped, so it can neither
    /// tear down the delivering session nor keep the remaining subscribers from receiving the report.
    /// </summary>
    /// <param name="data">Payload of the host-to-device transfer (report id first).</param>
    public override void OnOutputReport(ReadOnlySpan<byte> data)
    {
        if (!DualShock4OutputReport.TryParse(data, out DualShock4OutputReport report))
        {
            return;
        }

        Action<DualShock4OutputReport>? handlers = OutputReportReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<DualShock4OutputReport>)handler)(report);
            }
            catch (Exception exception)
            {
                _logger?.Log(EmulationLogLevel.Error, $"An OutputReportReceived handler failed: {exception}");
            }
        }
    }

    /// <summary>
    /// Handles one isochronous OUT packet of the speaker endpoint: the packet bytes (up to the
    /// endpoint's 132-byte maximum) are copied into the device's reusable audio scratch buffer and
    /// raised through <see cref="AudioOutReceived"/> exactly as received. Packets of every other
    /// endpoint are ignored.
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
    }

    /// <summary>
    /// Produces the next isochronous IN packet of the microphone endpoint from the persona's audio
    /// function: queued PCM is copied up to the packet length, and an empty ring yields the nominal
    /// 32-byte zero packet (or the whole destination when it is shorter). Every other endpoint
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
    /// released, released triggers and a full discharging battery; the touch points and motion axes
    /// stay zero.
    /// </summary>
    /// <returns>A neutral input state, the construction default of the device.</returns>
    private static DualShock4InputState CreateNeutralState()
    {
        return new DualShock4InputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualShock4DpadDirection.Released,
            BatteryLevel = NeutralBatteryLevel
        };
    }
}