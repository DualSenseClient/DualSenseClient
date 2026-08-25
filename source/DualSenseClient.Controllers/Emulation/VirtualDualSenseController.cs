using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
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
    /// Keeps the native realtime-haptics callback delegate alive for the lifetime of the device.
    /// </summary>
    private readonly DSRealtimeHapticsCallback _realtimeHapticsCallback;

    /// <summary>
    /// Raised on the libVIIPER callback thread after the game's output state (rumble,
    /// lightbar, player LEDs, trigger effects) was forwarded to the physical controller.
    /// Subscribers must not block.
    /// </summary>
    public event Action<SetStateData>? OutputStateReceived;

    /// <summary>
    /// Raised on the libVIIPER callback thread with the game's low-latency rear haptics
    /// payload (the 398-byte combined Bluetooth report), after it was forwarded to the
    /// physical controller. Subscribers must not block.
    /// </summary>
    public event Action<DSOutputState>? RealtimeHapticsReceived;

    /// <summary>
    /// Whether the initial battery/connection meta state has been pushed yet.
    /// </summary>
    private bool _metaInitialized;

    /// <summary>
    /// Whether the physical controller uses the "vibration v2" rumble encoding.
    /// </summary>
    private readonly bool _vibrationV2;

    /// <summary>
    /// Whether the virtual device presents a DualSense Edge instead of the standard DualSense.
    /// </summary>
    private readonly bool _edge;

    /// <summary>
    /// Creates and attaches a virtual DualSense device on the given USB bus.
    /// </summary>
    /// <param name="serverHandle">The USB server hosting the device.</param>
    /// <param name="busId">The bus to attach the device to.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    /// <param name="edge">True to create a DualSense Edge instead of the standard DualSense.</param>
    public VirtualDualSenseController(nuint serverHandle, uint busId, IDualSenseOutputs outputs, bool vibrationV2, bool edge = false) : base(outputs)
    {
        _vibrationV2 = vibrationV2;
        _edge = edge;
        _outputStateCallback = OnOutputState;
        _realtimeHapticsCallback = OnRealtimeHaptics;
        bool created;
        nuint handle;
        // Stamp the ownership MAC so the app's own scanner can tell this virtual device
        // apart from real hardware (see VirtualDeviceFilter).
        DSMetaState meta = new DSMetaState
        {
            MACAddress = VirtualDeviceFilter.CreateOwnershipMac()
        };
        if (edge)
        {
            created = LibVIIPER.CreateDualSenseEdgeDevice(serverHandle, out handle, busId, true, 0, 0, [meta]);
        }
        else
        {
            created = LibVIIPER.CreateDualSenseDevice(serverHandle, out handle, busId, true, 0, 0, [meta]);
        }

        if (!created)
        {
            _log.Error("Failed to create the virtual DualSense device");
            return;
        }

        DeviceHandle = handle;
        LibVIIPER.SetDualSenseOutputStateCallback(handle, _outputStateCallback);
        LibVIIPER.SetDualSenseRealtimeHapticsCallback(handle, _realtimeHapticsCallback);
        _log.Info($"Virtual DualSense{(edge ? " Edge" : "")} created (handle=0x{handle:X})");
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
        MappedInputResult mapped = (ButtonMappings ?? VirtualInputMapper.DualSenseDefaultTable).Evaluate(input);
        DSDeviceState state = new DSDeviceState
        {
            LX = VirtualInputMapper.DualSenseStick(input.LeftStickX),
            LY = VirtualInputMapper.DualSenseStick(input.LeftStickY),
            RX = VirtualInputMapper.DualSenseStick(input.RightStickX),
            RY = VirtualInputMapper.DualSenseStick(input.RightStickY),
            Buttons = (uint)mapped.Buttons,
            DPad = (byte)mapped.DPad,
            L2 = mapped.LeftTrigger,
            R2 = mapped.RightTrigger
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

        if (!LibVIIPER.SetDualSenseMetaState(handle, new[]
            {
                meta
            }))
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
        SetStateData payload = BuildOutputPayload(output, _vibrationV2);
        Outputs.SendOutputState(payload);
        OutputStateReceived?.Invoke(payload);
    }

    /// <summary>
    /// Rebuilds the physical controller's output payload from the game's exact USB
    /// output report (the 47 bytes following report ID 0x02), so every feature the game
    /// addresses on the virtual controller — adaptive triggers, rumble, lightbar,
    /// player LEDs, mute LED and its validity bits, volumes, audio control, motor power
    /// reduction, haptic low-pass filter, brightness/fade — arrives 1:1 over USB or
    /// Bluetooth. Two adjustments are applied:
    /// <list type="bullet">
    /// <item>The motor bytes always carry libVIIPER's retained decoded values, so
    /// partial reports that do not mention rumble still publish the last requested
    /// magnitudes to subscribers (the audio forwarder's rumble synthesis relies on
    /// this). The pad-side selector bits below decide whether they are applied.</item>
    /// <item>The rumble-mode selector bits are translated between the encodings:
    /// games select v1 (flag 0 bit 0) or v2 (flag 2 bit 2) against the virtual
    /// device's firmware; the physical pad may require the other encoding. When the
    /// game did not touch rumble at all, all selector bits are cleared so the pad
    /// retains its motors — mirroring real hardware.</item>
    /// </list>
    /// The trigger FFB allow bits pass through exactly as the game wrote them: with a
    /// bit set the pad applies the block riding the report; with a bit clear it retains
    /// its effect — the same semantics a real DualSense connected directly to the game
    /// would exhibit.
    /// </summary>
    public static SetStateData BuildOutputPayload(DSOutputState output, bool vibrationV2)
    {
        byte[] raw = output.RawOutputReport;
        if (raw is not { Length: 48 } || raw[0] != 0x02)
        {
            return BuildFallbackPayload(output);
        }

        byte[] bytes = new byte[SetStateData.PayloadSize];
        Buffer.BlockCopy(raw, 1, bytes, 0, SetStateData.PayloadSize);

        // Retained magnitudes for subscribers; gated pad-side by the selector bits.
        bytes[2] = output.RumbleSmall;
        bytes[3] = output.RumbleLarge;

        SetStateData payload = new SetStateData(bytes, 0);
        bool rumbleSelected = (payload.ValidFlag0 & ValidFlags.EnableRumbleEmulation) != 0
                              || (payload.ValidFlag2 & ValidFlags.EnableImprovedRumbleEmu) != 0;
        ValidFlags flag0 = payload.ValidFlag0;
        ValidFlags flag2 = payload.ValidFlag2;
        if (rumbleSelected)
        {
            flag0 |= ValidFlags.UseRumbleNotHaptics;
            if (vibrationV2)
            {
                flag0 &= ~ValidFlags.EnableRumbleEmulation;
                flag2 |= ValidFlags.EnableImprovedRumbleEmu;
            }
            else
            {
                flag0 |= ValidFlags.EnableRumbleEmulation;
                flag2 &= ~ValidFlags.EnableImprovedRumbleEmu;
            }
        }
        else
        {
            flag0 &= ~(ValidFlags.UseRumbleNotHaptics | ValidFlags.EnableRumbleEmulation);
            flag2 &= ~ValidFlags.EnableImprovedRumbleEmu;
        }

        return payload with
        {
            ValidFlag0 = flag0,
            ValidFlag2 = flag2
        };
    }

    /// <summary>
    /// Defensive fallback for callbacks whose raw report is missing or malformed:
    /// reconstructs the previously supported subset from the decoded fields.
    /// </summary>
    private static SetStateData BuildFallbackPayload(DSOutputState output)
    {
        return new SetStateData
        {
            ValidFlag0 = ValidFlags.UseRumbleNotHaptics
                         | ValidFlags.AllowRightTriggerFfb
                         | ValidFlags.AllowLeftTriggerFfb,
            ValidFlag1 = ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators | ValidFlags.AllowMuteLight,
            RumbleLeft = output.RumbleLarge,
            RumbleRight = output.RumbleSmall,
            MuteLedMode = output.MicLed,
            PlayerLeds = (PlayerLedMask)output.PlayerLeds,
            LedRed = output.LedRed,
            LedGreen = output.LedGreen,
            LedBlue = output.LedBlue
        };
    }

    /// <summary>
    /// Forwards the game's low-latency rear haptics payload to subscribers. Invoked on
    /// the libVIIPER callback thread.
    /// </summary>
    private void OnRealtimeHaptics(nuint handle, DSOutputState output)
    {
        RealtimeHapticsReceived?.Invoke(output);
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
        LibVIIPER.SetDualSenseRealtimeHapticsCallback(handle, null);
        if (!LibVIIPER.RemoveDualSenseDevice(handle))
        {
            _log.Error("The native library failed to remove the virtual DualSense device");
        }

        DeviceHandle = null;
    }
}