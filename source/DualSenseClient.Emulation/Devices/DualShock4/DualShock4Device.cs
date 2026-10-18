using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Persona device of the DualShock 4 (Sony 054C:09CC as <see cref="DualShock4Variant.V2"/>, Sony
/// 054C:05C4 as <see cref="DualShock4Variant.V1"/>): serves the variant's
/// <see cref="DualShock4DescriptorSet"/> descriptor set and the 64-byte input report 0x01 built by
/// <see cref="DualShock4InputReport"/> from the stored input state, serves the feature reports
/// 0x02/0x12/0x81/0xA3 through <see cref="DualShock4FeatureReports"/> and stalls the rest, and
/// decodes the host's output reports 0x05 (rumble, lightbar, flash) into
/// <see cref="OutputReportReceived"/>. The report shape is variant-independent; only the descriptor
/// set differs. The device owns
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

    /// <summary>
    /// Serializes the stored state and the report counters against every caller of
    /// <see cref="SetInput"/> and <see cref="ProvideInputReport"/>.
    /// </summary>
    private readonly Lock _stateLock = new Lock();

    /// <summary>Logging seam for subscriber faults; <see langword="null"/> disables logging.</summary>
    private readonly IEmulationLogger? _logger;

    /// <summary>Controller MAC address in address order served by feature reports 0x12/0x81; empty when the identity carries none.</summary>
    private readonly byte[] _macAddress;
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
    /// <param name="logger">Logging seam for faults raised by <see cref="OutputReportReceived"/> subscribers; <see langword="null"/> disables logging.</param>
    /// <param name="variant">Descriptor variant of the persona; the default is the reference v2 (<see cref="DualShock4Variant.V2"/>).</param>
    public DualShock4Device(DualShock4Identity identity, IEmulationLogger? logger = null, DualShock4Variant variant = DualShock4Variant.V2)
        : base(DualShock4DescriptorSet.Create(identity, variant))
    {
        _inputState = CreateNeutralState();
        _logger = logger;
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