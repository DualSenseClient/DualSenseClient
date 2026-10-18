using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Devices.Xbox360;

/// <summary>
/// Persona device of the Xbox 360 wired controller (Microsoft 045E:028E): a vendor-class USB
/// device — not HID, so the base request engine serves the standard requests of USB 2.0 and stalls
/// every class or vendor request — that serves the <see cref="Xbox360DescriptorSet"/> descriptor
/// set and the 20-byte input packet built by <see cref="Xbox360InputReport"/> from the stored input
/// state, and decodes the host's 8-byte rumble packets into <see cref="OutputReportReceived"/>.
/// The packet format carries no report counters, so <see cref="ProvideInputReport"/> serves the
/// stored state byte-for-byte on every pull; the state copy is locked so <see cref="SetInput"/>
/// (any thread) and concurrent pulls (the endpoint pacer thread on the interrupt IN pipe) can
/// never yield a torn packet. The device never throws on host payloads: an unknown packet id, the
/// controller's LED packet or a truncated payload raises no event. The persona keeps the base
/// engine's full-speed export: the USB/IP speed follows the reference's explicit descriptor field
/// (Speed 2 = full speed), whose export corroborates the reference's 4/8 endpoint intervals as
/// full-speed frames. The reference declares the emulated device high speed through its
/// kernel-bus interface, a kernel-domain enumeration fact rather than a wire speed, so
/// <see cref="VirtualUsbDevice.Speed"/> is not overridden.
/// </summary>
public sealed class Xbox360Device : VirtualUsbDevice
{
    /// <summary>
    /// Serializes the stored state against every caller of <see cref="SetInput"/> and
    /// <see cref="ProvideInputReport"/>.
    /// </summary>
    private readonly Lock _stateLock = new Lock();

    /// <summary>Logging seam for subscriber faults; <see langword="null"/> disables logging.</summary>
    private readonly IEmulationLogger? _logger;

    /// <summary>Input state of the following packets; guarded by <see cref="_stateLock"/>.</summary>
    private Xbox360InputState _inputState;

    /// <summary>
    /// Initializes the persona device with the descriptors of <paramref name="identity"/> and the
    /// neutral input state (an all-zero state: centered sticks at zero, released D-pad, released
    /// buttons and both triggers released), so an untouched device reports neutral rather than any
    /// other direction.
    /// </summary>
    /// <param name="identity">Identity of the persona; use <c>new Xbox360Identity()</c> for the reference defaults.</param>
    /// <param name="logger">Logging seam for faults raised by <see cref="OutputReportReceived"/> subscribers; <see langword="null"/> disables logging.</param>
    public Xbox360Device(Xbox360Identity identity, IEmulationLogger? logger = null)
        : base(Xbox360DescriptorSet.Create(identity))
    {
        _inputState = new Xbox360InputState();
        _logger = logger;
    }

    /// <summary>
    /// Raised for every recognized host rumble packet (id 0x00, size 0x08) with the decoded motor
    /// strengths, synchronously on the thread that delivered the payload; unknown packets (the LED
    /// packet), wrong sizes and truncated payloads raise nothing. A subscriber that throws is
    /// logged through the device's logger and skipped: the remaining subscribers still receive the
    /// report and the delivering session is not torn down.
    /// </summary>
    public event Action<Xbox360OutputReport>? OutputReportReceived;

    /// <summary>
    /// Stores the input state the following packets carry; may be called from any thread. A packet
    /// built concurrently carries either the old or the new state, never a mix.
    /// </summary>
    /// <param name="state">New input state; every field is reported as-is.</param>
    public void SetInput(in Xbox360InputState state)
    {
        lock (_stateLock)
        {
            _inputState = state;
        }
    }

    /// <summary>
    /// Builds the next 20-byte input packet from the stored state. The format carries no counters,
    /// so every pull of an unchanged state is byte-identical; the state copy is taken under the
    /// lock, so a concurrent <see cref="SetInput"/> yields either the old or the new packet. A
    /// destination shorter than the packet cannot carry it and receives nothing.
    /// </summary>
    /// <param name="destination">Buffer of at least <see cref="Xbox360InputReport.Size"/> bytes.</param>
    /// <returns>The number of bytes written (<see cref="Xbox360InputReport.Size"/>), or zero for an undersized destination.</returns>
    public override int ProvideInputReport(Span<byte> destination)
    {
        if (destination.Length < Xbox360InputReport.Size)
        {
            return 0;
        }

        Xbox360InputState state;
        lock (_stateLock)
        {
            state = _inputState;
        }

        return Xbox360InputReport.Build(state, destination);
    }

    /// <summary>
    /// Handles one interrupt OUT payload: a recognized rumble packet raises
    /// <see cref="OutputReportReceived"/> with the decoded motor strengths, while the LED packet,
    /// an unknown packet id or a truncated payload is ignored without throwing. Every subscriber
    /// is invoked in isolation: a throwing one is logged at <see cref="EmulationLogLevel.Error"/>
    /// and skipped, so it can neither tear down the delivering session nor keep the remaining
    /// subscribers from receiving the report.
    /// </summary>
    /// <param name="data">Payload of the host-to-device transfer (packet id first).</param>
    public override void OnOutputReport(ReadOnlySpan<byte> data)
    {
        if (!Xbox360OutputReport.TryParse(data, out Xbox360OutputReport report))
        {
            return;
        }

        Action<Xbox360OutputReport>? handlers = OutputReportReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<Xbox360OutputReport>)handler)(report);
            }
            catch (Exception exception)
            {
                _logger?.Log(EmulationLogLevel.Error, $"An OutputReportReceived handler failed: {exception}");
            }
        }
    }
}