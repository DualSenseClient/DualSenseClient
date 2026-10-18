using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Persona device of the DualSense (Sony 054C:0CE6, or Sony 054C:0DF2 when constructed as the Edge):
/// serves the persona's descriptor set and the 64-byte input report 0x01 built by
/// <see cref="DualSenseInputReport"/> from the stored input state, decodes the host's output reports
/// 0x02 (rumble, volumes, adaptive-trigger effect blocks, lightbar and LEDs) into
/// <see cref="OutputReportReceived"/>, and answers the feature-report surface of
/// <see cref="DualSenseFeatureReports"/> from the metadata state <see cref="SetMetaState"/> updates.
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

    /// <summary>
    /// Serializes the stored state, the metadata and the report counters against every caller of
    /// <see cref="SetInput"/>, <see cref="SetMetaState"/> and the report providers.
    /// </summary>
    private readonly Lock _stateLock = new Lock();

    /// <summary>Logging seam for subscriber faults; <see langword="null"/> disables logging.</summary>
    private readonly IEmulationLogger? _logger;

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
    /// <param name="logger">Logging seam for faults raised by <see cref="OutputReportReceived"/> subscribers; <see langword="null"/> disables logging.</param>
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
    /// <param name="logger">Logging seam for faults raised by <see cref="OutputReportReceived"/> subscribers; <see langword="null"/> disables logging.</param>
    public DualSenseDevice(DualSenseIdentity identity, bool edge, IEmulationLogger? logger = null)
        : base(edge ? DualSenseEdgeDescriptorSet.Create(identity) : DualSenseDescriptorSet.Create(identity))
    {
        _inputState = CreateNeutralState(identity);
        _macAddress = (byte[])identity.MacAddress.Clone();
        _serialNumber = identity.SerialNumber;
        _edge = edge;
        _logger = logger;
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