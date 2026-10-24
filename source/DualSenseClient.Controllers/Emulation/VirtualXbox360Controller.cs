using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation.Devices.Xbox360;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// An Emulation-backed virtual Xbox 360 controller: mirrors the physical controller's input to the
/// host as an XInput device and forwards the host's rumble commands to the physical controller.
/// It reuses the same <see cref="ButtonMappingTable"/>/<see cref="VirtualInputMapper"/> pipeline as
/// the other personas, including the two XInput axis helpers. The virtual 360 has no D-pad hat: the
/// D-pad directions are reported as plain button-word bits.
/// </summary>
/// <remarks>
/// <para>
/// The adapter owns no device lifecycle: the service creates and seeds the persona (including the
/// ownership MAC), registers and attaches it through the Emulation server, and later detaches and
/// removes it. <see cref="Dispose"/> only unsubscribes and makes later pushes no-ops; it never
/// detaches or removes the device.
/// </para>
/// <para>
/// The Xbox 360 mode has neither raw passthrough nor battery/connection state, so the inherited
/// no-ops are kept.
/// </para>
/// </remarks>
public sealed class VirtualXbox360Controller : VirtualControllerBase
{
    /// <summary>Mask of the four D-pad bits in the XInput button word.</summary>
    private const uint DPadMask = 0x000F;

    /// <summary>The ready persona device; owned by the service, never removed here.</summary>
    private readonly Xbox360Device _device;

    /// <summary>Whether the adapter has been disposed; later pushes and deliveries become no-ops.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates an adapter around a ready Emulation Xbox 360 persona. The caller keeps ownership of
    /// the device's creation, identity/ownership MAC, registration, attachment and removal.
    /// </summary>
    /// <param name="device">Ready persona device.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> or <paramref name="outputs"/> is <see langword="null"/>.</exception>
    public VirtualXbox360Controller(Xbox360Device device, IDualSenseOutputs outputs)
        : base(outputs ?? throw new ArgumentNullException(nameof(outputs)))
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _device.OutputReportReceived += OnOutputReport;
    }

    /// <inheritdoc/>
    public override EmulationMode Mode
    {
        get
        {
            return EmulationMode.Xbox360;
        }
    }

    /// <summary>
    /// Translates physical input to the persona's XInput state and pushes it. The physical Y axis is
    /// inverted (XInput treats up as positive). An attached raw USB image is ignored: the Xbox 360
    /// mode has no raw passthrough.
    /// </summary>
    /// <param name="report">Latest physical input report.</param>
    public override void PushInput(InputReport report)
    {
        if (_disposed)
        {
            return;
        }

        InputState input = report.Input;
        MappedInputResult mapped = (ButtonMappings ?? VirtualInputMapper.Xbox360DefaultTable).Evaluate(input);
        uint buttons = (uint)mapped.Buttons;
        Xbox360InputState state = new Xbox360InputState
        {
            Dpad = (Xbox360DpadDirection)(buttons & DPadMask),
            A = (buttons & (uint)Xbox360Buttons.A) != 0,
            B = (buttons & (uint)Xbox360Buttons.B) != 0,
            X = (buttons & (uint)Xbox360Buttons.X) != 0,
            Y = (buttons & (uint)Xbox360Buttons.Y) != 0,
            LeftShoulder = (buttons & (uint)Xbox360Buttons.LeftShoulder) != 0,
            RightShoulder = (buttons & (uint)Xbox360Buttons.RightShoulder) != 0,
            Back = (buttons & (uint)Xbox360Buttons.Back) != 0,
            Start = (buttons & (uint)Xbox360Buttons.Start) != 0,
            L3 = (buttons & (uint)Xbox360Buttons.LeftThumb) != 0,
            R3 = (buttons & (uint)Xbox360Buttons.RightThumb) != 0,
            Guide = (buttons & (uint)Xbox360Buttons.Guide) != 0,
            LeftTrigger = mapped.LeftTrigger,
            RightTrigger = mapped.RightTrigger,
            LeftStickX = VirtualInputMapper.X360Axis(input.LeftStickX),
            LeftStickY = VirtualInputMapper.X360AxisInverted(input.LeftStickY),
            RightStickX = VirtualInputMapper.X360Axis(input.RightStickX),
            RightStickY = VirtualInputMapper.X360AxisInverted(input.RightStickY)
        };

        _device.SetInput(state);
    }

    /// <summary>
    /// Forwards the host's rumble packet to the physical controller. The XInput left motor is the
    /// low-frequency motor and maps to the DualSense left motor; the right motor maps to the right
    /// motor.
    /// </summary>
    /// <param name="report">Decoded host rumble packet.</param>
    private void OnOutputReport(Xbox360OutputReport report) => Outputs.SetVibration(report.LeftMotor, report.RightMotor);

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.OutputReportReceived -= OnOutputReport;
    }
}