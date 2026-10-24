using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// An Emulation-backed virtual DualShock 4: mirrors the physical controller's input onto a ready
/// <see cref="DualShock4Device"/> persona and forwards the host's rumble and lightbar commands to the
/// physical controller through <see cref="IDualSenseOutputs"/>. It reuses the same
/// <see cref="ButtonMappingTable"/>/<see cref="VirtualInputMapper"/> pipeline as the other personas,
/// including the same touch-Y and motion scalers, so every virtual device presents identical
/// host-visible input.
/// </summary>
/// <remarks>
/// <para>
/// The adapter owns no device lifecycle: the service creates and seeds the persona (including the
/// ownership MAC and the descriptor variant), registers and attaches it through the Emulation
/// server, and later detaches and removes it. <see cref="Dispose"/> only unsubscribes and makes
/// later pushes no-ops; it never detaches or removes the device.
/// </para>
/// <para>
/// The DualShock 4 mode has no raw passthrough, so an attached raw USB image is ignored. The DS4
/// has no battery metadata lane: the battery byte travels inside the persona's input state, so an
/// explicit battery push re-stamps the current state immediately (once one exists) and does not
/// mark the meta initialized, so the first input report's battery still wins; after that, later
/// reports never overwrite an explicitly pushed battery. Before the first input push there is no
/// state to re-stamp, so the persona keeps serving its neutral battery until the first report
/// arrives. The host's flash-LED fields have no DualSense equivalent, so a report that only flashes
/// is a no-op.
/// </para>
/// </remarks>
public sealed class VirtualDualShock4Controller : VirtualControllerBase
{
    /// <summary>USB vendor ID of the virtual DualShock 4 (Sony).</summary>
    public const ushort VendorId = 0x054C;

    /// <summary>USB product ID of the first-generation DualShock 4 (CUH-ZCT1).</summary>
    public const ushort ProductIdV1 = 0x05C4;

    /// <summary>USB product ID of the second-generation DualShock 4 (CUH-ZCT2W).</summary>
    public const ushort ProductIdV2 = 0x09CC;

    /// <summary>Mask of the battery capacity nibble in the physical battery byte.</summary>
    private const byte BatteryLevelMask = 0x0F;

    /// <summary>Bit of the DS4 battery/ext byte that carries the cable (charging) state.</summary>
    private const byte ChargingBit = 0x10;

    /// <summary>Lightbar animation always sent with a DS4 lightbar update.</summary>
    private const byte FadeOutAnimation = 0x02;

    /// <summary>The ready persona device; owned by the service, never removed here.</summary>
    private readonly DualShock4Device _device;

    /// <summary>Whether the adapter has been disposed; later pushes and deliveries become no-ops.</summary>
    private bool _disposed;

    /// <summary>
    /// Whether the initial battery byte has been pushed from the first received report.
    /// </summary>
    private bool _metaInitialized;

    /// <summary>
    /// The latest physical battery byte, carried inside every following input state because the
    /// persona has no separate meta lane.
    /// </summary>
    private byte _batteryRaw;

    /// <summary>The last state written to the persona, re-stamped by <see cref="PushMeta"/> so an explicit battery push lands immediately.</summary>
    private DualShock4InputState _lastState;

    /// <summary>Whether an input state has been written yet; before the first push there is no state to re-stamp.</summary>
    private bool _hasState;

    /// <summary>Last host rumble values enabled by the report flags; retained across lightbar-only reports.</summary>
    private byte _rumbleLeft;

    private byte _rumbleRight;

    /// <summary>Last host lightbar color enabled by the report flags; retained across rumble-only reports.</summary>
    private byte _ledRed;

    private byte _ledGreen;
    private byte _ledBlue;

    /// <summary>
    /// Raised after the host's output state (rumble and lightbar) was forwarded to the physical
    /// controller. Subscribers must not block.
    /// </summary>
    public event Action<SetStateData>? OutputStateReceived;

    /// <summary>
    /// Creates an adapter around a ready Emulation DualShock 4 persona. The caller keeps ownership
    /// of the device's creation, identity/ownership MAC, descriptor variant, registration,
    /// attachment and removal.
    /// </summary>
    /// <param name="device">Ready persona device, constructed with the desired descriptor variant by the service.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> or <paramref name="outputs"/> is <see langword="null"/>.</exception>
    public VirtualDualShock4Controller(DualShock4Device device, IDualSenseOutputs outputs)
        : base(outputs ?? throw new ArgumentNullException(nameof(outputs)))
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _device.OutputReportReceived += OnOutputReport;
    }

    /// <summary>
    /// The persona this adapter mirrors. The service uses it as the access point for the speaker
    /// capture; the adapter never detaches or removes it.
    /// </summary>
    internal DualShock4Device Persona
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
            return EmulationMode.DualShock4;
        }
    }

    /// <summary>
    /// Translates physical input to the persona's DualShock 4 input state and pushes it. An attached
    /// raw USB image is ignored: the DualShock 4 mode has no raw passthrough.
    /// </summary>
    /// <param name="report">Latest physical input report.</param>
    public override void PushInput(InputReport report)
    {
        if (_disposed)
        {
            return;
        }

        // The persona stores the battery inside the input state, so the meta battery must be known
        // before the state is written; setting the cached value first yields the same observable
        // result.
        EnsureInitialMeta(report);

        InputState input = report.Input;
        MappedInputResult mapped = (ButtonMappings ?? VirtualInputMapper.DualShock4DefaultTable).Evaluate(input);
        DualShock4InputState state = new DualShock4InputState
        {
            // The physical stick bytes are the wire values: VirtualInputMapper.DualSenseStick
            // subtracts 128 for the native signed state and the persona's builder adds it back.
            LeftStickX = input.LeftStickX,
            LeftStickY = input.LeftStickY,
            RightStickX = input.RightStickX,
            RightStickY = input.RightStickY,
            Dpad = (DualShock4DpadDirection)VirtualInputMapping.DpadToHat(mapped.DPad),
            LeftTrigger = mapped.LeftTrigger,
            RightTrigger = mapped.RightTrigger,
            BatteryLevel = BatteryLevelOf(_batteryRaw),
            Charging = IsCharging(_batteryRaw)
        };
        ApplyButtons(ref state, (DualShock4Buttons)(ushort)mapped.Buttons);

        TouchpadState touchpad = report.Touchpad;
        state.Touch1X = touchpad.Touch1.X;
        state.Touch1Y = VirtualInputMapper.TouchYToDs4(touchpad.Touch1.Y);
        state.Touch1Active = touchpad.Touch1.IsActive;
        state.Touch1Id = touchpad.Touch1.TrackingId;
        state.Touch2X = touchpad.Touch2.X;
        state.Touch2Y = VirtualInputMapper.TouchYToDs4(touchpad.Touch2.Y);
        state.Touch2Active = touchpad.Touch2.IsActive;
        state.Touch2Id = touchpad.Touch2.TrackingId;

        MotionState motion = report.Motion;
        state.GyroPitch = VirtualInputMapper.GyroToDs4(motion.GyroX);
        state.GyroYaw = VirtualInputMapper.GyroToDs4(motion.GyroY);
        state.GyroRoll = VirtualInputMapper.GyroToDs4(motion.GyroZ);
        state.AccelX = VirtualInputMapper.AccelToDs4(motion.AccelX);
        state.AccelY = VirtualInputMapper.AccelToDs4(motion.AccelY);
        state.AccelZ = VirtualInputMapper.AccelToDs4(motion.AccelZ);

        _lastState = state;
        _hasState = true;
        _device.SetInput(state);
    }

    /// <inheritdoc/>
    public override void PushBattery(BatteryState battery) => PushMeta(battery);

    /// <inheritdoc/>
    public override void PushConnectionStatus(ConnectionStatus status)
    {
        // The persona carries no connection-status field; kept as a no-op to satisfy the interface.
    }

    /// <summary>Extracts the capacity nibble of the raw physical battery byte.</summary>
    /// <param name="raw">Raw physical battery byte.</param>
    /// <returns>The value written into the state's battery-level field.</returns>
    private static byte BatteryLevelOf(byte raw) => (byte)(raw & BatteryLevelMask);

    /// <summary>Extracts the cable/charging bit of the raw physical battery byte.</summary>
    /// <param name="raw">Raw physical battery byte.</param>
    /// <returns>Whether the state's charging field is set.</returns>
    private static bool IsCharging(byte raw) => (raw & ChargingBit) != 0;

    /// <summary>
    /// Pushes the battery byte: the state write is the DualShock 4 battery lane, so the raw byte
    /// re-stamps the current state immediately when one exists, and it is carried by every following
    /// input state. This does not mark the meta initialized, so the first input report still wins.
    /// </summary>
    /// <param name="battery">Latest physical battery state.</param>
    private void PushMeta(BatteryState battery)
    {
        if (_disposed)
        {
            return;
        }

        _batteryRaw = battery.Raw;
        if (_hasState)
        {
            _lastState = _lastState with
            {
                BatteryLevel = BatteryLevelOf(_batteryRaw),
                Charging = IsCharging(_batteryRaw)
            };
            _device.SetInput(_lastState);
        }
    }

    /// <summary>
    /// Pushes the battery from the first received report so the persona reports real values from the
    /// start. An explicit <see cref="PushBattery"/> does not set the flag, so the first report's
    /// battery overwrites any pre-input push.
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
    /// Translates the mapped <see cref="DualShock4Buttons"/> bitmask onto the persona's per-button
    /// flags: the single place the two button encodings meet. Every bit the DualShock 4 mapping
    /// pipeline can produce is covered; bits outside the enum are ignored.
    /// </summary>
    /// <param name="state">State receiving the button flags.</param>
    /// <param name="buttons">Mapped button bitmask.</param>
    private static void ApplyButtons(ref DualShock4InputState state, DualShock4Buttons buttons)
    {
        state.Square = (buttons & DualShock4Buttons.Square) != 0;
        state.Cross = (buttons & DualShock4Buttons.Cross) != 0;
        state.Circle = (buttons & DualShock4Buttons.Circle) != 0;
        state.Triangle = (buttons & DualShock4Buttons.Triangle) != 0;
        state.L1 = (buttons & DualShock4Buttons.L1) != 0;
        state.R1 = (buttons & DualShock4Buttons.R1) != 0;
        state.L2 = (buttons & DualShock4Buttons.L2) != 0;
        state.R2 = (buttons & DualShock4Buttons.R2) != 0;
        state.Share = (buttons & DualShock4Buttons.Share) != 0;
        state.Options = (buttons & DualShock4Buttons.Options) != 0;
        state.L3 = (buttons & DualShock4Buttons.L3) != 0;
        state.R3 = (buttons & DualShock4Buttons.R3) != 0;
        state.Ps = (buttons & DualShock4Buttons.PS) != 0;
        state.TouchpadPressed = (buttons & DualShock4Buttons.Touchpad) != 0;
    }

    /// <summary>
    /// Applies the fields the host report's flags enable, filling the rest from the cached values:
    /// sends the rebuilt payload on the outputs lane first, then raises
    /// <see cref="OutputStateReceived"/>, in that order. A report that
    /// enables neither rumble nor lightbar, including a flash-only report, is ignored.
    /// </summary>
    /// <param name="report">Decoded host output report 0x05.</param>
    private void OnOutputReport(DualShock4OutputReport report)
    {
        if (_disposed)
        {
            return;
        }

        // The DualSense has no flash equivalent; a report that only flashes changes nothing.
        if (!report.RumbleEnabled && !report.LightbarEnabled)
        {
            return;
        }

        if (report.RumbleEnabled)
        {
            _rumbleLeft = report.LeftMotor;
            _rumbleRight = report.RightMotor;
            Outputs.SetVibration(_rumbleLeft, _rumbleRight);
        }

        if (report.LightbarEnabled)
        {
            _ledRed = report.Red;
            _ledGreen = report.Green;
            _ledBlue = report.Blue;
        }

        ValidFlags flag1 = ValidFlags.None;
        ValidFlags flag2 = ValidFlags.None;
        if (report.LightbarEnabled)
        {
            flag1 |= ValidFlags.AllowLedColor;
            flag2 = ValidFlags.AllowColorFadeAnim;
        }

        SetStateData payload = new SetStateData
        {
            RumbleLeft = _rumbleLeft,
            RumbleRight = _rumbleRight,
            ValidFlag1 = flag1,
            ValidFlag2 = flag2,
            LightFadeAnimation = report.LightbarEnabled ? FadeOutAnimation : (byte)0,
            LedRed = _ledRed,
            LedGreen = _ledGreen,
            LedBlue = _ledBlue
        };
        Outputs.SendOutputState(payload);
        OutputStateReceived?.Invoke(payload);
    }

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