using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// A virtual Xbox 360 controller mirroring the physical DualSense input to the host
/// as an XInput device, and forwarding the host's rumble commands to the physical
/// controller. The virtual 360 has no D-pad hat: the D-pad directions are reported
/// as plain buttons.
/// </summary>
public sealed class VirtualXbox360Controller : VirtualControllerBase
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("VirtualXbox360");

    /// <summary>
    /// Keeps the native rumble callback delegate alive for the lifetime of the device.
    /// </summary>
    private readonly Xbox360RumbleCallback _rumbleCallback;

    /// <summary>
    /// Creates and attaches a virtual Xbox 360 device on the given USB bus.
    /// </summary>
    /// <param name="serverHandle">The USB server hosting the device.</param>
    /// <param name="busId">The bus to attach the device to.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    public VirtualXbox360Controller(nuint serverHandle, uint busId, IDualSenseOutputs outputs) : base(outputs)
    {
        _rumbleCallback = OnRumble;
        if (!LibVIIPER.CreateXbox360Device(serverHandle, out nuint handle, busId, true, 0, 0, 0x01))
        {
            _log.Error("Failed to create the virtual Xbox 360 device");
            return;
        }

        DeviceHandle = handle;
        LibVIIPER.SetXbox360RumbleCallback(handle, _rumbleCallback);
        _log.Info($"Virtual Xbox 360 created (handle=0x{handle:X})");
    }

    /// <inheritdoc/>
    public override EmulationMode Mode => EmulationMode.Xbox360;

    /// <summary>
    /// Translates physical input to the virtual Xbox 360 input state and pushes it.
    /// The physical Y axis is inverted (XInput treats up as positive).
    /// </summary>
    public override void PushInput(InputReport report)
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }

        InputState input = report.Input;
        Xbox360DeviceState state = new Xbox360DeviceState
        {
            Buttons = (uint)VirtualInputMapper.ToXbox360Buttons(input),
            LT = input.L2,
            RT = input.R2,
            LX = VirtualInputMapper.X360Axis(input.LeftStickX),
            LY = VirtualInputMapper.X360AxisInverted(input.LeftStickY),
            RX = VirtualInputMapper.X360Axis(input.RightStickX),
            RY = VirtualInputMapper.X360AxisInverted(input.RightStickY),
            Reserved = new byte[6]
        };

        if (!LibVIIPER.SetXbox360DeviceState(handle, state))
        {
            _log.Error("Failed to set the virtual Xbox 360 device state");
        }
    }

    /// <summary>
    /// Forwards host rumble commands to the physical controller.
    /// The XInput left motor is the low-frequency motor and maps to the DualSense
    /// left motor; the right motor maps to the right motor. Invoked on the
    /// libVIIPER callback thread.
    /// </summary>
    private void OnRumble(nuint handle, byte leftMotor, byte rightMotor)
    {
        Outputs.SetVibration(leftMotor, rightMotor);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }
        _log.Info("Removing virtual Xbox 360 device");
        LibVIIPER.SetXbox360RumbleCallback(handle, null);
        if (!LibVIIPER.RemoveXbox360Device(handle))
        {
            _log.Error("The native library failed to remove the virtual Xbox 360 device");
        }
        DeviceHandle = null;
    }
}