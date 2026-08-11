using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// A virtual DualSense controller. The full device (HID gamepad + audio interfaces)
/// is created so games can also use the haptics and audio endpoints; forwarding the
/// audio streams to the physical controller is handled in a later milestone.
/// </summary>
public sealed class VirtualDualSenseController : VirtualControllerBase
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("VirtualDualSense");

    /// <summary>
    /// Keeps the native output state callback delegate alive for the lifetime of the device.
    /// </summary>
    private readonly DSOutputStateCallback _outputStateCallback;

    /// <summary>
    /// Raised on the libVIIPER callback thread after the game's output state (rumble,
    /// lightbar, player LEDs, trigger effects) was forwarded to the physical controller.
    /// Subscribers must not block.
    /// </summary>
    public event Action<SetStateData>? OutputStateReceived;

    /// <summary>
    /// Whether the initial battery/connection meta state has been pushed yet.
    /// </summary>
    private bool _metaInitialized;

    /// <summary>
    /// Whether the physical controller uses the "vibration v2" rumble encoding.
    /// </summary>
    private readonly bool _vibrationV2;

    /// <summary>
    /// Creates and attaches a virtual DualSense device on the given USB bus.
    /// </summary>
    /// <param name="serverHandle">The USB server hosting the device.</param>
    /// <param name="busId">The bus to attach the device to.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    public VirtualDualSenseController(nuint serverHandle, uint busId, IDualSenseOutputs outputs, bool vibrationV2) : base(outputs)
    {
        _vibrationV2 = vibrationV2;
        _outputStateCallback = OnOutputState;
        if (!LibVIIPER.CreateDualSenseDevice(serverHandle, out nuint handle, busId, true, 0, 0, null))
        {
            _log.Error("Failed to create the virtual DualSense device");
            return;
        }

        DeviceHandle = handle;
        LibVIIPER.SetDualSenseOutputStateCallback(handle, _outputStateCallback);
        _log.Info($"Virtual DualSense created (handle=0x{handle:X})");
    }

    /// <inheritdoc/>
    public override EmulationMode Mode => EmulationMode.DualSense;

    /// <summary>
    /// Translates physical input to the virtual DualSense input state and pushes it.
    /// </summary>
    public override void PushInput(InputReport report)
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }

        InputState input = report.Input;
        DSDeviceState state = new DSDeviceState
        {
            LX = VirtualInputMapper.DualSenseStick(input.LeftStickX),
            LY = VirtualInputMapper.DualSenseStick(input.LeftStickY),
            RX = VirtualInputMapper.DualSenseStick(input.RightStickX),
            RY = VirtualInputMapper.DualSenseStick(input.RightStickY),
            Buttons = (uint)VirtualInputMapper.ToDualSenseButtons(input),
            DPad = (byte)VirtualInputMapper.ToDualSenseDPad(input),
            L2 = input.L2,
            R2 = input.R2
        };

        TouchpadState touchpad = report.Touchpad;
        state.Touch1X = touchpad.Touch1.X;
        state.Touch1Y = touchpad.Touch1.Y;
        state.Touch1Active = touchpad.Touch1.IsActive ? (byte)1 : (byte)0;
        state.Touch1Tracking = touchpad.Touch1.TrackingId;
        state.Touch2X = touchpad.Touch2.X;
        state.Touch2Y = touchpad.Touch2.Y;
        state.Touch2Active = touchpad.Touch2.IsActive ? (byte)1 : (byte)0;
        state.Touch2Tracking = touchpad.Touch2.TrackingId;

        MotionState motion = report.Motion;
        state.GyroX = motion.GyroX;
        state.GyroY = motion.GyroY;
        state.GyroZ = motion.GyroZ;
        state.AccelX = motion.AccelX;
        state.AccelY = motion.AccelY;
        state.AccelZ = motion.AccelZ;

        if (!LibVIIPER.SetDualSenseDeviceState(handle, state))
        {
            _log.Error("Failed to set the virtual DualSense device state");
        }

        EnsureInitialMeta(report);
    }

    /// <inheritdoc/>
    public override void PushBattery(BatteryState battery) => PushMeta(battery, null);

    /// <inheritdoc/>
    public override void PushConnectionStatus(ConnectionStatus status) => PushMeta(null, status);

    /// <summary>
    /// Pushes battery and/or connection meta state; zero-valued fields keep their
    /// current values on the device side.
    /// </summary>
    private void PushMeta(BatteryState? battery, ConnectionStatus? status)
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }

        DSMetaState meta = new DSMetaState();
        if (battery is { } b)
        {
            meta.BatteryStatus = b.Raw;
        }
        if (status is { } c)
        {
            meta.ConnectionStatus = c.Raw;
        }

        if (!LibVIIPER.SetDualSenseMetaState(handle, new[] { meta }))
        {
            _log.Error("Failed to set the virtual DualSense meta state");
        }
    }

    /// <summary>
    /// Pushes the battery/connection meta from the first received report so the
    /// virtual device reports real values from the start.
    /// </summary>
    private void EnsureInitialMeta(InputReport report)
    {
        if (_metaInitialized)
        {
            return;
        }
        _metaInitialized = true;
        PushMeta(report.Battery, report.Connection);
    }

    /// <summary>
    /// Forwards host output (rumble, lightbar, player LEDs, triggers) to the physical
    /// controller. Invoked on the libVIIPER callback thread.
    /// </summary>
    private void OnOutputState(nuint handle, DSOutputState output)
    {
        byte[] rawReport = output.RawOutputReport is { Length: 48 } raw ? raw : [];

        SetStateData payload = new SetStateData
        {
            ValidFlag0 = ValidFlags.UseRumbleNotHaptics
                         | (_vibrationV2 ? ValidFlags.None : ValidFlags.EnableRumbleEmulation)
                         // The trigger blocks always ride this report, so their enable
                         // bits stay set: with a bit cleared the pad ignores the block
                         // and retains the previous effect, leaving a trigger stuck
                         // after the game disables it (mirrors dualsensectl).
                         | ValidFlags.AllowRightTriggerFfb
                         | ValidFlags.AllowLeftTriggerFfb,
            ValidFlag2 = _vibrationV2 ? ValidFlags.EnableImprovedRumbleEmu : ValidFlags.None,
            RumbleLeft = output.RumbleLarge,
            RumbleRight = output.RumbleSmall,
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators | ValidFlags.AllowMuteLight,
            MuteLedMode = output.MicLed,
            LightFadeAnimation = 0x02,
            LightBrightness = output.LightbarBrightness,
            PlayerLeds = (PlayerLedMask)output.PlayerLeds,
            LedRed = output.LedRed,
            LedGreen = output.LedGreen,
            LedBlue = output.LedBlue,
            R2TriggerEffect = rawReport.Length == 48 ? new TriggerEffectBlock(rawReport, 11) : TriggerEffectBuilder.Off(),
            L2TriggerEffect = rawReport.Length == 48 ? new TriggerEffectBlock(rawReport, 22) : TriggerEffectBuilder.Off()
        };

        Outputs.SendOutputState(payload);
        OutputStateReceived?.Invoke(payload);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }
        _log.Info("Removing virtual DualSense device");
        LibVIIPER.SetDualSenseOutputStateCallback(handle, null);
        if (!LibVIIPER.RemoveDualSenseDevice(handle))
        {
            _log.Error("The native library failed to remove the virtual DualSense device");
        }
        DeviceHandle = null;
    }
}