using System.Runtime.InteropServices;
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
    /// PCM (2ch S16LE @48kHz). Subscribers must not block; the buffer is a copy.
    /// </summary>
    public event Action<byte[]>? RealtimeHapticsReceived;

    /// <summary>
    /// Whether the initial battery meta state has been pushed yet.
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
        LibVIIPER.SetDualSenseOutputCallback(handle, _outputStateCallback);
        LibVIIPER.SetDualSenseRealtimeHapticsCallback(handle, _realtimeHapticsCallback);
        _log.Info($"Virtual DualSense{(edge ? " Edge" : "")} created (handle=0x{handle:X})");
    }

    /// <inheritdoc/>
    public override EmulationMode Mode
    {
        get
        {
            return EmulationMode.DualSense;
        }
    }

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
    public override void PushBattery(BatteryState battery) => PushMeta(battery);

    /// <inheritdoc/>
    public override void PushConnectionStatus(ConnectionStatus status)
    {
        // New libVIIPER DSMetaState has no connection-status field; the virtual
        // USB report carries headset/mic flags from the host, not the physical pad.
        // Kept as a no-op to satisfy the interface.
    }

    /// <summary>
    /// Pushes battery meta state; zero-valued fields keep their
    /// current values on the device side.
    /// </summary>
    private void PushMeta(BatteryState? battery)
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

        if (!LibVIIPER.SetDualSenseMetaState(handle, new[]
            {
                meta
            }))
        {
            _log.Error("Failed to set the virtual DualSense meta state");
        }
    }

    /// <summary>
    /// Pushes the battery meta from the first received report so the
    /// virtual device reports real values from the start.
    /// </summary>
    private void EnsureInitialMeta(InputReport report)
    {
        if (_metaInitialized)
        {
            return;
        }

        _metaInitialized = true;
        PushMeta(report.Battery);
    }

    /// <summary>
    /// Forwards host output (rumble, lightbar, player LEDs, triggers) to the physical
    /// controller. Invoked on the libVIIPER callback thread.
    /// </summary>
    private void OnOutputState(nuint handle, in DSOutputState output)
    {
        SetStateData payload = BuildOutputPayload(output, _vibrationV2);
        Outputs.SendOutputState(payload);
        OutputStateReceived?.Invoke(payload);
    }

    /// <summary>
    /// Rebuilds the physical controller's output payload from the game's flat output
    /// state (the 47-byte USB payload), so every feature the game addresses on the
    /// virtual controller — adaptive triggers, rumble, lightbar, player LEDs, mute LED
    /// and its validity bits, volumes, audio control, motor power reduction, haptic
    /// low-pass filter, brightness/fade — arrives 1:1 over USB or Bluetooth.
    /// Two adjustments are applied:
    /// <list type="bullet">
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
        byte[] bytes = new byte[SetStateData.PayloadSize];
        bytes[0] = output.Flags0;
        bytes[1] = output.Flags1;
        bytes[2] = output.RumbleSmall;
        bytes[3] = output.RumbleLarge;
        bytes[4] = output.VolumeHeadphones;
        bytes[5] = output.VolumeSpeaker;
        bytes[6] = output.VolumeMic;
        bytes[7] = output.AudioControl;
        bytes[8] = output.MuteLightMode;
        bytes[9] = output.MuteControl;
        (output.TriggerRight ?? Array.Empty<byte>()).CopyTo(bytes, 10);
        (output.TriggerLeft ?? Array.Empty<byte>()).CopyTo(bytes, 21);
        bytes[32] = (byte)output.HostTimestamp;
        bytes[33] = (byte)(output.HostTimestamp >> 8);
        bytes[34] = (byte)(output.HostTimestamp >> 16);
        bytes[35] = (byte)(output.HostTimestamp >> 24);
        bytes[36] = output.MotorPower;
        bytes[37] = output.AudioControl2;
        bytes[38] = output.Flags3;
        bytes[39] = output.HapticFilter;
        bytes[40] = output.UnkByte;
        bytes[41] = output.LightFade;
        bytes[42] = output.LightBrightness;
        bytes[43] = output.PlayerLeds;
        bytes[44] = output.LedRed;
        bytes[45] = output.LedGreen;
        bytes[46] = output.LedBlue;

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
    /// Forwards the game's low-latency rear haptics PCM to subscribers. Invoked on
    /// the libVIIPER callback thread; the native buffer is copied before dispatch.
    /// </summary>
    private void OnRealtimeHaptics(nuint handle, IntPtr pcm, nuint length)
    {
        int byteCount = (int)length;
        if (byteCount <= 0)
        {
            return;
        }

        byte[] copy = new byte[byteCount];
        Marshal.Copy(pcm, copy, 0, byteCount);
        RealtimeHapticsReceived?.Invoke(copy);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (DeviceHandle is not { } handle)
        {
            return;
        }

        _log.Info("Removing virtual DualSense device");
        LibVIIPER.SetDualSenseOutputCallback(handle, null);
        LibVIIPER.SetDualSenseRealtimeHapticsCallback(handle, null);
        if (!LibVIIPER.RemoveDualSenseDevice(handle))
        {
            _log.Error("The native library failed to remove the virtual DualSense device");
        }

        DeviceHandle = null;
    }
}