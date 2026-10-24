using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// An Emulation-backed virtual DualSense: mirrors the physical controller's input onto a ready
/// <see cref="DualSenseDevice"/> persona and forwards the host's output reports to the physical
/// controller through <see cref="IDualSenseOutputs"/>. It reuses the same
/// <see cref="ButtonMappingTable"/>/<see cref="VirtualInputMapper"/> pipeline as the other
/// personas, so every virtual device presents the same host-visible input and output behaviour. The
/// Edge variant is this same adapter wrapping a persona constructed with <c>edge: true</c>.
/// </summary>
/// <remarks>
/// <para>
/// The adapter owns no device lifecycle: the service creates and seeds the persona (including the
/// ownership MAC through <c>VirtualDeviceFilter.CreateOwnershipMac()</c> and the Edge/variant
/// choice), registers and attaches it through the Emulation server, and later detaches and removes
/// it. <see cref="Dispose"/> only unsubscribes and makes later pushes no-ops; it never
/// detaches or removes the device.
/// </para>
/// <para>
/// The persona's realtime-haptics lane (channels 2 and 3 of the speaker packet) is surfaced through
/// <see cref="RealtimeHapticsReceived"/>: the adapter copies the persona's scratch-buffer view before
/// invoking subscribers, exactly like the physical pad's lane, and never derives haptics from the
/// front-channel audio lane. The forwarder owns the policy that consumes the lane.
/// </para>
/// </remarks>
public sealed class VirtualDualSenseController : VirtualControllerBase
{
    /// <summary>Mask of the battery capacity nibble in the persona's battery byte.</summary>
    private const byte BatteryLevelMask = 0x0F;

    /// <summary>Shift of the power-state nibble in the persona's battery byte.</summary>
    private const int PowerStateShift = 4;

    /// <summary>Bit of the second "use rumble not haptics" selector in valid-flag byte 2 (reference bit 38.3).</summary>
    private const byte UseRumbleNotHaptics2Bit = 0x08;

    /// <summary>The ready persona device; owned by the service, never removed here.</summary>
    private readonly DualSenseDevice _device;

    /// <summary>Whether the physical controller uses the "vibration v2" rumble encoding.</summary>
    private readonly bool _vibrationV2;

    /// <summary>
    /// Whether the initial battery meta state has been pushed from the first received report.
    /// </summary>
    private bool _metaInitialized;

    /// <summary>
    /// Whether a raw passthrough report is currently published on the persona. While set, the
    /// synthetic builder is frozen; pushing synthesized input requires clearing first.
    /// </summary>
    private bool _rawActive;

    /// <summary>Whether the adapter has been disposed; later pushes and deliveries become no-ops.</summary>
    private bool _disposed;

    /// <summary>
    /// The latest battery byte the adapter knows (meta battery), carried into every synthesised
    /// input state because the persona stores the battery as part of that state.
    /// </summary>
    private byte? _batteryRaw;

    /// <summary>
    /// Host mute-light mode last received from the host while the output report allowed the mute
    /// light to change (0 off, 1 on, 2 breathing, reference MuteLight). The persona has no echo
    /// lane of its own, so this cached value serves the host mute-light echo
    /// (<see cref="DualSenseInputState.MicMuted"/>, served report byte 54 bit 2) on every
    /// following input push.
    /// </summary>
    private byte _muteLightMode;

    /// <summary>
    /// Raised after the host's output state (rumble, lightbar, player LEDs, trigger effects) was
    /// forwarded to the physical controller. Subscribers must not block.
    /// </summary>
    public event Action<SetStateData>? OutputStateReceived;

    /// <summary>
    /// Raised with a copy of the persona's realtime haptics: channels 2 and 3 of every whole speaker
    /// frame (192 bytes for a nominal 384-byte packet), separate from the front-channel audio lane.
    /// The persona's event memory is only valid during its callback, so the adapter copies it before
    /// invoking subscribers. Subscribers must not block.
    /// </summary>
    public event Action<byte[]>? RealtimeHapticsReceived;

    /// <summary>
    /// Creates an adapter around a ready Emulation DualSense persona. The caller keeps ownership of
    /// the device's creation, identity/ownership MAC, registration, attachment and removal.
    /// </summary>
    /// <param name="device">Ready persona device; constructed as the standard or Edge variant by the service.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> or <paramref name="outputs"/> is <see langword="null"/>.</exception>
    public VirtualDualSenseController(DualSenseDevice device, IDualSenseOutputs outputs, bool vibrationV2)
        : base(outputs ?? throw new ArgumentNullException(nameof(outputs)))
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _vibrationV2 = vibrationV2;
        _device.OutputReportReceived += OnOutputReport;
        _device.RealtimeHapticsReceived += OnRealtimeHaptics;
    }

    /// <summary>
    /// The persona this adapter mirrors. The service uses it as the access point for the speaker
    /// captures; the adapter never detaches or removes it.
    /// </summary>
    internal DualSenseDevice Persona
    {
        get
        {
            return _device;
        }
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
    /// Whether verbatim raw passthrough is safe: only with the built-in one-to-one mapping.
    /// </summary>
    public bool ShouldUseRaw
    {
        get
        {
            return ShouldUseRawMapping(ButtonMappings);
        }
    }

    /// <summary>
    /// Whether the given mapping table allows verbatim raw passthrough.
    /// </summary>
    /// <param name="table">Mapping table to check; <c>null</c> means the built-in default.</param>
    /// <returns><c>true</c> when the table preserves a one-to-one mapping.</returns>
    public static bool ShouldUseRawMapping(ButtonMappingTable? table)
        => table is null || ReferenceEquals(table, VirtualInputMapper.DualSenseDefaultTable);

    /// <summary>
    /// Publishes one exact 64-byte USB input report to the persona, served verbatim to the host
    /// until a synthesised report follows.
    /// </summary>
    /// <param name="usbRaw64">Exact 64-byte USB report 0x01.</param>
    /// <returns><see langword="true"/> when the persona accepted the report.</returns>
    public bool PushRawInput(byte[] usbRaw64)
    {
        if (_disposed || usbRaw64 is null || usbRaw64.Length != DualSenseRawReport.UsbReportSize || usbRaw64[0] != 0x01)
        {
            return false;
        }

        _device.SetRawInputReport(usbRaw64);
        _rawActive = true;
        return true;
    }

    /// <summary>
    /// Drops the raw passthrough and restores synthesised reports.
    /// </summary>
    public void ClearRawInput()
    {
        if (!_disposed)
        {
            _device.ClearRawInputReport();
        }

        _rawActive = false;
    }

    /// <summary>
    /// Translates physical input to the persona's input state and pushes it. Prefers the verbatim
    /// raw report when the mapping is one-to-one; falls back to the synthesised state for remaps or
    /// invalid raw reports.
    /// </summary>
    /// <param name="report">Latest physical input report.</param>
    public override void PushInput(InputReport report)
    {
        if (_disposed)
        {
            return;
        }

        if (report.UsbRaw64 is { } raw && ShouldUseRaw)
        {
            if (PushRawInput(raw))
            {
                EnsureInitialMeta(report);
                return;
            }
        }

        if (_rawActive)
        {
            ClearRawInput();
        }

        // The persona stores the battery bytes inside the input state, so the meta battery must be
        // known before the state is written. The native wrapper keeps meta and state on separate
        // lanes; the observable result is the same because every state write carries that battery.
        EnsureInitialMeta(report);

        InputState input = report.Input;
        MappedInputResult mapped = (ButtonMappings ?? VirtualInputMapper.DualSenseDefaultTable).Evaluate(input);
        _device.SetInput(TranslateInput(report, mapped, _batteryRaw!.Value, _muteLightMode != 0));
    }

    /// <inheritdoc/>
    public override void PushBattery(BatteryState battery) => PushMeta(battery);

    /// <inheritdoc/>
    public override void PushConnectionStatus(ConnectionStatus status)
    {
        // The persona carries no connection-status field: its USB report's plug/mute byte echoes
        // the host, not the physical pad. Kept as a no-op to satisfy the interface.
    }

    /// <summary>
    /// Pushes the battery meta state: the raw physical byte maps onto the persona's capacity nibble
    /// and power-state nibble, so the served battery byte round-trips unchanged.
    /// </summary>
    /// <param name="battery">Latest physical battery state.</param>
    private void PushMeta(BatteryState battery)
    {
        if (_disposed)
        {
            return;
        }

        _batteryRaw = battery.Raw;
        _device.SetMetaState(new DualSenseMetaState
        {
            BatteryLevel = (byte)(battery.Raw & BatteryLevelMask),
            ChargingState = (DualSenseChargingState)(byte)(battery.Raw >> PowerStateShift)
        });
    }

    /// <summary>
    /// Pushes the battery meta from the first received report so the persona reports real values
    /// from the start.
    /// </summary>
    /// <param name="report">First physical report seen by this adapter.</param>
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
    /// Translates one mapped physical report into the persona's input state. This is the single
    /// translation point of the adapter: every field the persona's input state exposes is mapped
    /// here, the host mute-light echo is served from the adapter's cache (see
    /// <see cref="_muteLightMode"/>), and fields the mapping pipeline does not produce (sensor
    /// timestamp, temperature, trigger stop/status echo, host/device timestamps) stay at the
    /// persona's defaults.
    /// </summary>
    /// <param name="report">Physical report being mirrored.</param>
    /// <param name="mapped">Result of the mode's mapping table.</param>
    /// <param name="batteryRaw">Latest meta battery byte to carry in the state.</param>
    /// <param name="micMuted">Cached host mute-light echo to serve (the persona has no echo lane of its own).</param>
    /// <returns>The state to store on the persona.</returns>
    private static DualSenseInputState TranslateInput(InputReport report, MappedInputResult mapped, byte batteryRaw, bool micMuted)
    {
        InputState input = report.Input;
        DualSenseInputState state = new DualSenseInputState
        {
            // The physical stick bytes are the wire values: VirtualInputMapper.DualSenseStick
            // subtracts 128 for the native signed state and the persona's report builder adds it back.
            LeftStickX = input.LeftStickX,
            LeftStickY = input.LeftStickY,
            RightStickX = input.RightStickX,
            RightStickY = input.RightStickY,
            Dpad = (DualSenseDpadDirection)VirtualInputMapping.DpadToHat(mapped.DPad),
            LeftTrigger = mapped.LeftTrigger,
            RightTrigger = mapped.RightTrigger,
            BatteryLevel = (byte)(batteryRaw & BatteryLevelMask),
            PowerState = (DualSenseChargingState)(byte)(batteryRaw >> PowerStateShift),
            MicMuted = micMuted
        };
        ApplyButtons(ref state, mapped.Buttons);

        TouchpadState touchpad = report.Touchpad;
        state.Touch1X = touchpad.Touch1.X;
        state.Touch1Y = touchpad.Touch1.Y;
        state.Touch1Active = touchpad.Touch1.IsActive;
        state.Touch1Id = touchpad.Touch1.TrackingId;
        state.Touch2X = touchpad.Touch2.X;
        state.Touch2Y = touchpad.Touch2.Y;
        state.Touch2Active = touchpad.Touch2.IsActive;
        state.Touch2Id = touchpad.Touch2.TrackingId;

        MotionState motion = report.Motion;
        state.GyroX = motion.GyroX;
        // The persona orders the motion words X, Z, Y (its GyroZ field is the second wire word).
        // Physical Y goes into that field so the served wire keeps the physical X, Y, Z order.
        state.GyroZ = motion.GyroY;
        state.GyroY = motion.GyroZ;
        state.AccelX = motion.AccelX;
        state.AccelY = motion.AccelY;
        state.AccelZ = motion.AccelZ;
        return state;
    }

    /// <summary>
    /// Translates the mapped <see cref="DualSenseButtons"/> bitmask onto the persona's per-button
    /// flags: the single place the two button encodings meet. Every bit the mapping pipeline can
    /// produce is covered, including the Edge Fn and paddle bits; bits outside the enum are ignored.
    /// </summary>
    /// <param name="state">State receiving the button flags.</param>
    /// <param name="buttons">Mapped button bitmask.</param>
    private static void ApplyButtons(ref DualSenseInputState state, ulong buttons)
    {
        DualSenseButtons mask = (DualSenseButtons)(uint)buttons;
        state.Square = (mask & DualSenseButtons.Square) != 0;
        state.Cross = (mask & DualSenseButtons.Cross) != 0;
        state.Circle = (mask & DualSenseButtons.Circle) != 0;
        state.Triangle = (mask & DualSenseButtons.Triangle) != 0;
        state.L1 = (mask & DualSenseButtons.L1) != 0;
        state.R1 = (mask & DualSenseButtons.R1) != 0;
        state.L2 = (mask & DualSenseButtons.L2) != 0;
        state.R2 = (mask & DualSenseButtons.R2) != 0;
        state.Create = (mask & DualSenseButtons.Create) != 0;
        state.Options = (mask & DualSenseButtons.Options) != 0;
        state.L3 = (mask & DualSenseButtons.L3) != 0;
        state.R3 = (mask & DualSenseButtons.R3) != 0;
        state.Ps = (mask & DualSenseButtons.PS) != 0;
        state.TouchpadPressed = (mask & DualSenseButtons.Touchpad) != 0;
        state.Mute = (mask & DualSenseButtons.MicMute) != 0;
        state.FnLeft = (mask & DualSenseButtons.LeftFunction) != 0;
        state.FnRight = (mask & DualSenseButtons.RightFunction) != 0;
        state.PaddleLeft = (mask & DualSenseButtons.L4) != 0;
        state.PaddleRight = (mask & DualSenseButtons.R4) != 0;
    }

    /// <summary>
    /// Forwards the host's decoded output report to the physical controller: sends the rebuilt
    /// payload on the outputs lane first, then raises <see cref="OutputStateReceived"/>, in that
    /// order. The host mute-light mode is cached first (the persona has no
    /// echo lane of its own), so following input pushes serve it as the mic-mute echo.
    /// </summary>
    /// <param name="report">Decoded host output report 0x02.</param>
    private void OnOutputReport(DualSenseOutputReport report)
    {
        if (_disposed)
        {
            return;
        }

        if (report.MuteLightAllowed)
        {
            _muteLightMode = report.MuteLightMode;
        }

        SetStateData payload = BuildOutputPayload(report, _vibrationV2);
        Outputs.SendOutputState(payload);
        OutputStateReceived?.Invoke(payload);
    }

    /// <summary>
    /// Copies the persona's realtime-haptics view into an array and raises
    /// <see cref="RealtimeHapticsReceived"/>. The copy happens only when a subscriber exists, and
    /// the persona's memory is never retained past the callback.
    /// </summary>
    /// <param name="pcm">Persona view over the extracted rear channels; valid only during this callback.</param>
    private void OnRealtimeHaptics(ReadOnlyMemory<byte> pcm)
    {
        if (_disposed || RealtimeHapticsReceived is not { } handlers)
        {
            return;
        }

        handlers(pcm.ToArray());
    }

    /// <summary>
    /// Rebuilds the physical controller's 47-byte output payload from the persona's decoded host
    /// output report with the same field/offset semantics as the physical pad's output report, then
    /// applies the shared rumble-mode selector translation. The engine's decode exposes the four
    /// defined bits of valid-flag byte 2 and not the unknown byte 40, so those positions are served
    /// as zero; every byte the decode carries rides through unchanged.
    /// </summary>
    /// <param name="report">Decoded host output report 0x02.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    /// <returns>The payload to send to the physical controller.</returns>
    public static SetStateData BuildOutputPayload(DualSenseOutputReport report, bool vibrationV2)
    {
        byte[] bytes = new byte[SetStateData.PayloadSize];
        bytes[0] = (byte)Flags0(report);
        bytes[1] = (byte)Flags1(report);
        bytes[2] = report.RumbleRight;
        bytes[3] = report.RumbleLeft;
        bytes[4] = report.HeadphoneVolume;
        bytes[5] = report.SpeakerVolume;
        bytes[6] = report.MicVolume;
        bytes[7] = report.AudioControl;
        bytes[8] = report.MuteLightMode;
        bytes[9] = report.MuteControl;
        (report.RightTriggerEffect ?? Array.Empty<byte>()).CopyTo(bytes, 10);
        (report.LeftTriggerEffect ?? Array.Empty<byte>()).CopyTo(bytes, 21);
        bytes[32] = (byte)report.HostTimestamp;
        bytes[33] = (byte)(report.HostTimestamp >> 8);
        bytes[34] = (byte)(report.HostTimestamp >> 16);
        bytes[35] = (byte)(report.HostTimestamp >> 24);
        bytes[36] = report.MotorPowerLevel;
        bytes[37] = report.AudioControl2;
        bytes[38] = Flags2(report);
        bytes[39] = report.HapticLowPassFilter ? (byte)0x01 : (byte)0x00;
        // Byte 40 carries no decoded field (the unknown byte); it stays zero.
        bytes[41] = report.LightFadeAnimation;
        bytes[42] = report.LightBrightness;
        bytes[43] = report.PlayerLeds;
        bytes[44] = report.Red;
        bytes[45] = report.Green;
        bytes[46] = report.Blue;
        return ApplyRumbleModeSelector(new SetStateData(bytes, 0), vibrationV2);
    }

    /// <summary>
    /// Applies the rumble-mode selector translation to a built output payload. Games select v1
    /// (flag 0 bit 0) or v2 (flag 2 bit 2) against the virtual device's firmware; the physical pad
    /// may require the other encoding. When the game did not touch rumble at all, all selector bits
    /// are cleared so the pad retains its motors - mirroring real hardware.
    /// </summary>
    /// <param name="payload">Built 47-byte payload to translate; its selector bits are replaced.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    /// <returns>The payload with the translated selector bits.</returns>
    public static SetStateData ApplyRumbleModeSelector(SetStateData payload, bool vibrationV2)
    {
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

    /// <summary>Rebuilds valid-flag byte 0 from the decoded allowance bits.</summary>
    /// <param name="report">Decoded host output report.</param>
    /// <returns>The byte the physical payload carries.</returns>
    private static ValidFlags Flags0(DualSenseOutputReport report)
    {
        ValidFlags flags = ValidFlags.None;
        if (report.RumbleEmulationEnabled)
        {
            flags |= ValidFlags.EnableRumbleEmulation;
        }

        if (report.UseRumbleNotHaptics)
        {
            flags |= ValidFlags.UseRumbleNotHaptics;
        }

        if (report.RightTriggerEffectAllowed)
        {
            flags |= ValidFlags.AllowRightTriggerFfb;
        }

        if (report.LeftTriggerEffectAllowed)
        {
            flags |= ValidFlags.AllowLeftTriggerFfb;
        }

        if (report.HeadphoneVolumeAllowed)
        {
            flags |= ValidFlags.AllowHeadphoneVolume;
        }

        if (report.SpeakerVolumeAllowed)
        {
            flags |= ValidFlags.AllowSpeakerVolume;
        }

        if (report.MicVolumeAllowed)
        {
            flags |= ValidFlags.AllowMicVolume;
        }

        if (report.AudioControlAllowed)
        {
            flags |= ValidFlags.AllowAudioControl;
        }

        return flags;
    }

    /// <summary>Rebuilds valid-flag byte 1 from the decoded allowance bits.</summary>
    /// <param name="report">Decoded host output report.</param>
    /// <returns>The byte the physical payload carries.</returns>
    private static ValidFlags Flags1(DualSenseOutputReport report)
    {
        ValidFlags flags = ValidFlags.None;
        if (report.MuteLightAllowed)
        {
            flags |= ValidFlags.AllowMuteLight;
        }

        if (report.AudioMuteAllowed)
        {
            flags |= ValidFlags.AllowAudioMute;
        }

        if (report.LightbarColorAllowed)
        {
            flags |= ValidFlags.AllowLedColor;
        }

        if (report.ResetLights)
        {
            flags |= ValidFlags.ResetLights;
        }

        if (report.PlayerIndicatorsAllowed)
        {
            flags |= ValidFlags.AllowPlayerIndicators;
        }

        if (report.HapticLowPassFilterAllowed)
        {
            flags |= ValidFlags.AllowHapticLpf;
        }

        if (report.MotorPowerLevelAllowed)
        {
            flags |= ValidFlags.AllowMotorPowerLevel;
        }

        if (report.AudioControl2Allowed)
        {
            flags |= ValidFlags.AllowAudioControl2;
        }

        return flags;
    }

    /// <summary>
    /// Rebuilds valid-flag byte 2 from the decoded bits, including the second rumble-mode selector
    /// that has no <see cref="ValidFlags"/> member.
    /// </summary>
    /// <param name="report">Decoded host output report.</param>
    /// <returns>The byte the physical payload carries.</returns>
    private static byte Flags2(DualSenseOutputReport report)
    {
        ValidFlags flags = ValidFlags.None;
        if (report.LightBrightnessAllowed)
        {
            flags |= ValidFlags.AllowBrightnessChange;
        }

        if (report.LightFadeAllowed)
        {
            flags |= ValidFlags.AllowColorFadeAnim;
        }

        if (report.ImprovedRumbleEmulationEnabled)
        {
            flags |= ValidFlags.EnableImprovedRumbleEmu;
        }

        byte value = (byte)flags;
        if (report.UseRumbleNotHaptics2)
        {
            value |= UseRumbleNotHaptics2Bit;
        }

        return value;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _muteLightMode = 0;
        _device.OutputReportReceived -= OnOutputReport;
        _device.RealtimeHapticsReceived -= OnRealtimeHaptics;
    }
}