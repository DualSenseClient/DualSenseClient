using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;
using DualSenseClient.VIIPER.DualShock4;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// A virtual DualShock 4 controller mirroring the physical DualSense input
/// (including touchpad and motion) to the host, and forwarding the host's rumble
/// and lightbar commands to the physical controller.
/// </summary>
public sealed class VirtualDualShock4Controller : VirtualControllerBase
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("VirtualDualShock4");

    /// <summary>
    /// USB vendor ID presented by the virtual DualShock 4 device (Sony).
    /// </summary>
    public const ushort VendorId = 0x054C;

    /// <summary>
    /// USB product ID of the first-generation DualShock 4 (CUH-ZCT1W).
    /// </summary>
    public const ushort ProductIdV1 = 0x05C4;

    /// <summary>
    /// USB product ID of the second-generation DualShock 4 (CUH-ZCT2W), the libVIIPER default.
    /// </summary>
    public const ushort ProductIdV2 = 0x09CC;

    /// <summary>
    /// Keeps the native output callback delegate alive for the lifetime of the device.
    /// </summary>
    private readonly DS4OutputCallback _outputCallback;

    /// <summary>
    /// Raised on the libVIIPER callback thread after the host's output state (rumble,
    /// lightbar, player LEDs) was forwarded to the physical controller. Subscribers
    /// must not block.
    /// </summary>
    public event Action<SetStateData>? OutputStateReceived;

    /// <summary>
    /// Creates and attaches a virtual DualShock 4 device on the given USB bus.
    /// </summary>
    /// <param name="serverHandle">The USB server hosting the device.</param>
    /// <param name="busId">The bus to attach the device to.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="variant">The hardware generation the virtual device presents
    /// (<see cref="DualShock4Variant.V2"/> by default).</param>
    public VirtualDualShock4Controller(nuint serverHandle, uint busId, IDualSenseOutputs outputs, DualShock4Variant variant = DualShock4Variant.V2) :
        base(outputs)
    {
        ushort productId = variant == DualShock4Variant.V1 ? ProductIdV1 : ProductIdV2;
        _outputCallback = OnOutput;
        if (!LibVIIPER.CreateDS4Device(serverHandle, out nuint handle, busId, true, VendorId, productId, null))
        {
            _log.Error("Failed to create the virtual DualShock 4 device");
            return;
        }

        DeviceHandle = handle;
        LibVIIPER.SetDS4OutputCallback(handle, _outputCallback);
        _log.Info($"Virtual DualShock 4 {variant} created (handle=0x{handle:X}, VID=0x{VendorId:X4}, PID=0x{productId:X4})");
    }

    /// <inheritdoc/>
    public override EmulationMode Mode => EmulationMode.DualShock4;

    /// <summary>
    /// Translates physical input to the virtual DualShock 4 input state and pushes it.
    /// </summary>
    public override void PushInput(InputReport report)
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }

        InputState input = report.Input;
        MappedInputResult mapped = (ButtonMappings ?? VirtualInputMapper.DualShock4DefaultTable).Evaluate(input);
        DS4DeviceState state = new DS4DeviceState
        {
            LX = VirtualInputMapper.DualSenseStick(input.LeftStickX),
            LY = VirtualInputMapper.DualSenseStick(input.LeftStickY),
            RX = VirtualInputMapper.DualSenseStick(input.RightStickX),
            RY = VirtualInputMapper.DualSenseStick(input.RightStickY),
            Buttons = (ushort)mapped.Buttons,
            DPad = (byte)mapped.DPad,
            L2 = mapped.LeftTrigger,
            R2 = mapped.RightTrigger
        };

        TouchpadState touchpad = report.Touchpad;
        state.Touch1X = touchpad.Touch1.X;
        state.Touch1Y = touchpad.Touch1.Y;
        state.Touch1Active = touchpad.Touch1.IsActive ? (byte)1 : (byte)0;
        state.Touch2X = touchpad.Touch2.X;
        state.Touch2Y = touchpad.Touch2.Y;
        state.Touch2Active = touchpad.Touch2.IsActive ? (byte)1 : (byte)0;

        MotionState motion = report.Motion;
        state.GyroX = VirtualInputMapper.GyroToDs4(motion.GyroX);
        state.GyroY = VirtualInputMapper.GyroToDs4(motion.GyroY);
        state.GyroZ = VirtualInputMapper.GyroToDs4(motion.GyroZ);
        state.AccelX = VirtualInputMapper.AccelToDs4(motion.AccelX);
        state.AccelY = VirtualInputMapper.AccelToDs4(motion.AccelY);
        state.AccelZ = VirtualInputMapper.AccelToDs4(motion.AccelZ);

        if (!LibVIIPER.SetDS4DeviceState(handle, state))
        {
            _log.Error("Failed to set the virtual DualShock 4 device state");
        }
    }

    /// <summary>
    /// Forwards host rumble and lightbar commands to the physical controller.
    /// The DS4 flash-LED feature has no DualSense equivalent and is ignored.
    /// Invoked on the libVIIPER callback thread.
    /// </summary>
    private void OnOutput(nuint handle, byte rumbleSmall, byte rumbleLarge, byte ledRed, byte ledGreen, byte ledBlue, byte flashOn, byte flashOff)
    {
        Outputs.SetVibration(rumbleLarge, rumbleSmall);

        // The rumble bytes ride the payload too: while the Bluetooth audio lane is
        // open the pad ignores standalone output reports, and the audio forwarder
        // embeds this state into the combined reports (synthesizing the rumble into
        // the haptics PCM) instead.
        SetStateData payload = new SetStateData
        {
            RumbleLeft = rumbleLarge,
            RumbleRight = rumbleSmall,
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            ValidFlag2 = ValidFlags.AllowColorFadeAnim,
            LightFadeAnimation = 0x02,
            LedRed = ledRed,
            LedGreen = ledGreen,
            LedBlue = ledBlue
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

        _log.Info("Removing virtual DualShock 4 device");
        LibVIIPER.SetDS4OutputCallback(handle, null);
        if (!LibVIIPER.RemoveDS4Device(handle))
        {
            _log.Error("The native library failed to remove the virtual DualShock 4 device");
        }

        DeviceHandle = null;
    }
}