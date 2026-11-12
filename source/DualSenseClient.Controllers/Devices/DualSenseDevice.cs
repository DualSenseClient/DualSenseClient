using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Lightbar;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Controllers.DualSense.Utilities;
using DualSenseClient.Core.Models;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// Concrete controller implementation for the Sony DualSense (PS5) controller.
/// Opens and communicates with the DualSense over USB or Bluetooth via HIDAPI.
/// </summary>
public class DualSenseDevice : ControllerDevice, ILightSink, IControllerAudioSink
{
    // Fields
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseDevice");

    /// <summary>
    /// Cancellation token source used to stop the background read loop.
    /// </summary>
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    /// <summary>
    /// The dedicated background thread running the read loop. A real thread (rather
    /// than <c>Task.Run</c>) keeps per-read overhead off the ThreadPool: reads are
    /// long-running blocking calls arriving 250-1000 times per second.
    /// </summary>
    private readonly Thread _readThread;

    /// <summary>
    /// Previous input report snapshot for change detection. Null until the first
    /// report is received, so no events fire on the initial read.
    /// </summary>
    private InputReport? _previousInputReport;

    /// <summary>
    /// Bluetooth output report sequence tag; only the low nibble is transmitted.
    /// </summary>
    private byte _outputSequence;

    /// <summary>
    /// Serializes output-state sends and guards the Bluetooth dedupe state.
    /// </summary>
    private readonly Lock _outputSync = new Lock();

    /// <summary>
    /// Guards against starting the input read loop more than once.
    /// </summary>
    private int _readLoopStarted;

    /// <summary>
    /// Controller model reported by this device.
    /// </summary>
    private readonly ControllerType _controllerType;

    /// <summary>
    /// The last payload written over Bluetooth, used to skip duplicate reports: games
    /// tend to rewrite an unchanged state every frame, and each suppressed report
    /// frees interrupt-channel bandwidth for input (lower latency) on Bluetooth.
    /// </summary>
    private readonly byte[] _lastBluetoothPayload = new byte[SetStateData.PayloadSize];

    /// <summary>
    /// Whether <see cref="_lastBluetoothPayload"/> holds a successfully sent payload.
    /// </summary>
    private bool _hasLastBluetoothPayload;

    /// <summary>
    /// Builds the Bluetooth-only audio (<c>0x35</c>) and haptics (<c>0x32</c>) reports.
    /// The rolling sequence and packet counters live here, so the builder must only be
    /// touched by the audio writer thread (guarded by <see cref="_audioWriteLock"/>).
    /// </summary>
    private readonly BluetoothAudioReportBuilder _bluetoothAudioReports = new BluetoothAudioReportBuilder();

    /// <summary>
    /// Serializes writes of the Bluetooth audio/haptics report stream.
    /// </summary>
    private readonly object _audioWriteLock = new object();

    /// <summary>
    /// Owns the precedence between the game output, profile lights, effects, manual edits
    /// and special-action overrides.
    /// </summary>
    private readonly LightingAuthority _lighting;

    /// <summary>
    /// Arbitrates which source drives the controller's audio reports.
    /// </summary>
    private readonly ControllerAudioLane _audioLane;

    /// <summary>
    /// Active manual (Output Test) rumble state encoded as <c>(left &lt;&lt; 8) | right</c>,
    /// or -1 when no manual vibration is held. It is merged into every outgoing output
    /// report because the pad stops the classic motors as soon as a report without the
    /// rumble-selection bits arrives (for example the profile's animated lightbar frames).
    /// </summary>
    private int _manualRumble = -1;

    /// <summary>
    /// Runs the profile's animated lightbar effect.
    /// </summary>
    private readonly LightbarEffectController _lightbarEffect;

    /// <summary>
    /// Whether effects must stop sending frames (e.g. while the settings UI previews).
    /// </summary>
    private volatile bool _lightbarEffectSuspended;

    /// <summary>
    /// Last raw battery byte, or -1 before the first input report.
    /// </summary>
    private volatile int _batteryRaw = -1;

    /// <summary>
    /// 1 once <see cref="Dispose"/> has run, guarding against double disposal.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Whether this controller has been disposed.
    /// </summary>
    public bool IsDisposed
    {
        get
        {
            return Volatile.Read(ref _disposed) != 0;
        }
    }

    /// <summary>
    /// 1 once the read loop observed the input link drop. Output writes are dropped from
    /// then on: the HID handle is gone even though <see cref="Dispose"/> has not run yet.
    /// </summary>
    private int _linkLost;

    /// <summary>
    /// Whether the controller's input link dropped.
    /// </summary>
    private bool IsLinkLost
    {
        get
        {
            return Volatile.Read(ref _linkLost) != 0;
        }
    }

    /// <inheritdoc/>
    public override ControllerType ControllerType
    {
        get
        {
            return _controllerType;
        }
    }

    /// <summary>
    /// Whether this controller is a DualSense Edge, which has the extra Fn
    /// buttons and back paddles.
    /// </summary>
    public bool IsEdge
    {
        get
        {
            return _controllerType == ControllerType.DualSenseEdge;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Probed with feature report 0x09: a live control transfer that never
    /// consumes queued input reports, unlike the base input-read probe.
    /// </remarks>
    public override bool IsConnected
    {
        get
        {
            if (IsLinkLost)
            {
                return false;
            }

            try
            {
                return GetFeatureReport(0x09, 20).Length > 0;
            }
            catch (HidException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Whether this controller uses the "vibration v2" rumble encoding: firmware update
    /// version >= 2.21 for a base DualSense, always for a DualSense Edge. Mirrors the
    /// kernel's <c>dualsense_use_vibration_v2</c> gate. When <c>false</c>, the v1
    /// encoding (flag 0 bit 0) is used instead.
    /// </summary>
    public bool UsesVibrationV2
    {
        get
        {
            return IsEdge ||
                   (FirmwareInfo?.IsValid == true && FirmwareInfo.Value.UpdateVersionValue >= ((2 << 8) | 21));
        }
    }

    /// <inheritdoc/>
    public override int MaxOutputReportLength
    {
        get
        {
            return ConnectionType switch
            {
                ConnectionType.Bluetooth => 78,
                ConnectionType.Usb => IsEdge ? 64 : 48,
                ConnectionType.Unknown => throw new ArgumentOutOfRangeException($"Unknown connection type: {ConnectionType}"),
                _ => throw new ArgumentOutOfRangeException($"Unknown connection type: {ConnectionType}")
            };
        }
    }

    /// <summary>
    /// Motion-sensor calibration read from feature report 0x05 on connect.
    /// Null when the report could not be read.
    /// </summary>
    public CalibrationInfo? CalibrationInfo { get; private set; }

    /// <summary>
    /// Firmware and hardware information read from feature report 0x20 on connect.
    /// Null when the report could not be read.
    /// </summary>
    public FirmwareInfo? FirmwareInfo { get; private set; }

    /// <summary>
    /// Pairing information read from feature report 0x09 on connect.
    /// Null when the report could not be read.
    /// </summary>
    public PairingInfo? PairingInfo { get; private set; }

    /// <summary>
    /// The normalized identity used to key settings and Auto-profile rules.
    /// </summary>
    public ControllerKey Key
    {
        get
        {
            return ControllerKeyFactory.From(this);
        }
    }

    /// <inheritdoc/>
    protected override string? BluetoothMacAddress
    {
        get
        {
            return PairingInfo?.ClientMac;
        }
    }

    /// <summary>
    /// Current state of input, or null before the first report is received.
    /// </summary>
    public InputReport? InputReport { get; private set; }

    /// <summary>
    /// Raised when any input state field changes (sticks, triggers, or buttons).
    /// Fires once per report regardless of how many individual fields changed.
    /// </summary>
    public event EventHandler<InputStateEventArgs>? InputStateChanged;

    /// <summary>
    /// Raised once for every received input report, including the first one and
    /// reports in which nothing changed. Carries the parsed report so consumers
    /// always see the latest motion, touchpad, battery, and connection data even
    /// when the button/stick bytes are unchanged. Fires on the read thread.
    /// </summary>
    public event EventHandler<InputReport>? InputReportReceived;

    /// <summary>
    /// Raised when a button transitions from released to pressed.
    /// </summary>
    public event EventHandler<ButtonEventArgs>? ButtonPressed;

    /// <summary>
    /// Raised when a button transitions from pressed to released.
    /// </summary>
    public event EventHandler<ButtonEventArgs>? ButtonReleased;

    /// <summary>
    /// Raised when either analog stick changes position.
    /// </summary>
    public event EventHandler<StickEventArgs>? StickMoved;

    /// <summary>
    /// Raised when either analog trigger changes value.
    /// </summary>
    public event EventHandler<TriggerEventArgs>? TriggerMoved;

    /// <summary>
    /// Raised when battery level or power state changes.
    /// </summary>
    public event EventHandler<BatteryStateEventArgs>? BatteryStateChanged;

    /// <summary>
    /// Raised when headphone, mic, or USB connection status changes.
    /// </summary>
    public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;

    /// <summary>
    /// Raised when gyroscope or accelerometer data changes.
    /// </summary>
    public event EventHandler<MotionEventArgs>? MotionChanged;

    /// <summary>
    /// Raised when touchpad touch state changes.
    /// </summary>
    public event EventHandler<TouchpadEventArgs>? TouchpadChanged;

    /// <summary>
    /// Raised when the lightbar color is (re)sent to the controller via an output
    /// report carrying the LED color flag. Fires on the sender's thread.
    /// </summary>
    public event EventHandler? LightbarColorChanged;

    /// <summary>
    /// Raised when the player LED layout is (re)sent to the controller via an output
    /// report carrying the player indicator flag. Fires on the sender's thread.
    /// </summary>
    public event EventHandler? PlayerLedsChanged;

    /// <summary>
    /// Raised when the mute LED mode is (re)sent to the controller via an output report
    /// carrying the mute light flag. Fires on the sender's thread.
    /// </summary>
    public event EventHandler? MuteLedModeChanged;

    /// <summary>
    /// The last lightbar color sent to the controller, or the controller's default
    /// PS-blue (0, 87, 255) before any color output report has been sent.
    /// </summary>
    public (byte Red, byte Green, byte Blue) CurrentLightbarColor { get; private set; } = (0, 87, 255);

    /// <summary>
    /// The last player LED layout sent to the controller (bits 0-4, LED 1 = leftmost),
    /// or 0 before any player indicator output report has been sent.
    /// </summary>
    public byte CurrentPlayerLeds { get; private set; }

    /// <summary>
    /// The last mute LED mode sent to the controller (0 = off, 1 = on, 2 = pulse),
    /// or 0 before any mute light output report has been sent.
    /// </summary>
    public byte CurrentMuteLedMode { get; private set; }

    /// <summary>
    /// Gets or sets whether the game owns the lightbar. While true, animated effects keep
    /// their clock running but send no frames.
    /// </summary>
    public bool LightbarPassthroughEnabled
    {
        get
        {
            return _lighting.LightbarPassthroughEnabled;
        }
        set
        {
            _lighting.LightbarPassthroughEnabled = value;
            if (!value)
            {
                _lightbarEffect.RequestImmediateFrame();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the game owns the player LEDs. While true, effects report no
    /// player LED mask.
    /// </summary>
    public bool PlayerLedPassthroughEnabled
    {
        get
        {
            return _lighting.PlayerLedPassthroughEnabled;
        }
        set
        {
            _lighting.PlayerLedPassthroughEnabled = value;
            if (!value)
            {
                _lightbarEffect.RequestImmediateFrame();
            }
        }
    }

    /// <summary>
    /// Gets whether the applied profile forces its lightbar, player LEDs, and microphone
    /// LED over the virtual controller's forwarded lighting.
    /// </summary>
    public bool ProfileLightsOverrideEnabled
    {
        get
        {
            return _lighting.ProfileForcesLights;
        }
    }

    /// <summary>
    /// The controller's lighting authority, through which every light owner submits and
    /// commits state.
    /// </summary>
    public LightingAuthority Lighting
    {
        get
        {
            return _lighting;
        }
    }

    /// <summary>
    /// The controller's shared audio lane, which arbitrates the Forwarder, player, and
    /// Special Action sounds.
    /// </summary>
    public ControllerAudioLane AudioLane
    {
        get
        {
            return _audioLane;
        }
    }

    /// <summary>
    /// Gets or sets whether effect frames are suppressed, e.g. while a settings preview
    /// writes directly to the lightbar.
    /// </summary>
    public bool LightbarEffectSuspended
    {
        get
        {
            return _lightbarEffectSuspended;
        }
        set
        {
            _lightbarEffectSuspended = value;
            if (!value)
            {
                _lightbarEffect.RequestImmediateFrame();
            }
        }
    }

    /// <summary>
    /// Gets the battery state from the last input report, or null before the first report.
    /// </summary>
    public BatteryState? CurrentBatteryState
    {
        get
        {
            int raw = _batteryRaw;
            return raw < 0 ? null : new BatteryState((byte)raw);
        }
    }

    /// <summary>
    /// Gets or sets the provider of the current audio level (0-1) for audio-reactive lighting.
    /// </summary>
    public Func<float>? AudioLevelProvider { get; set; }

    /// <summary>
    /// Creates a new DualSense controller wrapper around an already-opened HID device.
    /// Profiles are not applied here; the owning application applies a profile later via
    /// <see cref="ApplyProfile"/> once the device is connected.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseDevice(IHidDevice device, IHidDeviceInfo info)
        : this(device, info, ControllerType.DualSense, true)
    {
    }

    /// <summary>
    /// Creates a DualSense controller wrapper with an explicit controller model.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    /// <param name="controllerType">The DualSense model to report.</param>
    public DualSenseDevice(IHidDevice device, IHidDeviceInfo info, ControllerType controllerType)
        : this(device, info, controllerType, true)
    {
    }

    /// <summary>
    /// Creates a DualSense wrapper without necessarily starting the input read loop.
    /// Derived types use this to finish their own initialization before input reports
    /// are consumed.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    /// <param name="controllerType">The DualSense model to report.</param>
    /// <param name="startReading">Whether to start the background input read loop.</param>
    protected DualSenseDevice(
        IHidDevice device,
        IHidDeviceInfo info,
        ControllerType controllerType,
        bool startReading) : base(device, info)
    {
        if (controllerType is not (ControllerType.DualSense or ControllerType.DualSenseEdge))
        {
            throw new ArgumentOutOfRangeException(
                nameof(controllerType),
                controllerType,
                "Only DualSense controller models are supported.");
        }

        _controllerType = controllerType;

        // Probe order follows the Linux driver: pairing MAC, firmware/update version,
        // then calibration. The calibration GET is also what moves a Bluetooth
        // controller from the minimal 0x01 report to the full 0x31 report.
        PairingInfo = FeatureReader.ReadPairingInfo(this);
        FirmwareInfo = FeatureReader.ReadFirmwareInfo(this);
        CalibrationInfo = FeatureReader.ReadCalibration(this);

        _lighting = new LightingAuthority(this);
        _audioLane = new ControllerAudioLane(this);
        _lightbarEffect = new LightbarEffectController(this);
        _readThread = new Thread(() => ReadLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "DualSense Input Reader",
            Priority = ThreadPriority.AboveNormal
        };

        if (startReading)
        {
            StartReadLoop();
        }
    }

    /// <summary>
    /// Starts the background input read loop. Calling this more than once has no effect.
    /// </summary>
    protected void StartReadLoop()
    {
        if (Interlocked.Exchange(ref _readLoopStarted, 1) != 0)
        {
            return;
        }

        _readThread.Start();
    }

    /// <summary>
    /// Background loop that continuously reads HID input reports from the controller.
    /// Runs on a dedicated background thread for the lifetime of the controller
    /// connection, blocking on each read instead of yielding to the ThreadPool.
    /// </summary>
    /// <param name="ct">Cancellation token to signal when the loop should stop.</param>
    private void ReadLoop(CancellationToken ct)
    {
        _log.Debug($"Read loop started for {Info.ProductName} ({ConnectionType})");
        // Size by input, not output: USB 0x01 is 64B while the USB output report is
        // 48B (DualSense) or 64B (Edge); BT 0x31 fills 78B. A 63B buffer would truncate
        // the USB AES tail needed for verbatim raw passthrough.
        byte[] buffer = new byte[Math.Max(MaxOutputReportLength, 78)];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int result = ReadInput(buffer, 0, buffer.Length, 25);

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                // Timeout (0) just means no report arrived in time; a dead link
                // surfaces as an HidException below, so keep waiting instead of
                // treating idleness as a disconnect. Yield briefly: a blocking read
                // (like the -1 timeout above) never returns 0 spuriously, so this only
                // triggers for non-blocking transports and test doubles - without the
                // yield, those would spin this above-normal thread at 100% CPU.
                if (result == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                ProcessInputReport(buffer, result);
            }
            catch (HidException ex)
            {
                // A failed read is the normal symptom of the controller being
                // unplugged or the link dropping, so log it as a disconnect
                // rather than an error.
                _log.Warning($"Input link lost for {Info.ProductName} ({ConnectionType}); read loop stopping", ex);
                break;
            }
            catch (Exception ex)
            {
                _log.Error($"Controller read loop failed for {Info.ProductName}", ex);
                break;
            }
        }

        if (!ct.IsCancellationRequested)
        {
            // The loop only exits on a read failure unless disposal canceled it. The
            // handle is gone, so drop output writes until the scanner disposes the
            // device instead of retrying every audio tick against a dead link.
            Volatile.Write(ref _linkLost, 1);
        }

        _log.Debug($"Read loop ended for {Info.ProductName} (cancelled={ct.IsCancellationRequested}, linkLost={IsLinkLost})");
    }

    /// <summary>
    /// Routes a raw HID report to the correct parser based on connection type and report ID.
    /// Strips the protocol header bytes before forwarding to the input parser.
    /// Attaches the verbatim 64B USB report for raw passthrough when available.
    /// </summary>
    /// <param name="data">Raw HID input report buffer.</param>
    /// <param name="bytesRead">Valid bytes in <paramref name="data"/>.</param>
    internal void ProcessInputReport(byte[] data, int bytesRead)
    {
        byte reportId = data[0];
        int offset;
        if (ConnectionType == ConnectionType.Bluetooth)
        {
            switch (reportId)
            {
                case 0x31:
                    if (bytesRead < 2 + 63)
                    {
                        _log.Warning($"{Info.ProductName}: dropping truncated Bluetooth input report: {bytesRead} byte(s), expected at least {2 + 63}");
                        return;
                    }

                    // The full Bluetooth report is CRC-signed over the HIDP 0xA1 header
                    // and report bytes 0-73; reject a corrupt report instead of feeding
                    // stale or mangled state to the application.
                    if (bytesRead >= 78 && !HasValidBluetoothInputCrc(data))
                    {
                        _log.Warning($"{Info.ProductName}: dropping Bluetooth input report with an invalid CRC ({bytesRead} byte(s))");
                        return;
                    }

                    if ((data[1] & 0x01) == 0)
                    {
                        // Bit 0 is HasHID: a 0x31 packet without it carries microphone
                        // data (HasMic), not controller state.
                        return;
                    }

                    offset = 2;
                    break;
                case 0x01:
                    _log.Warning(
                        $"{Info.ProductName}: controller is in simple Bluetooth mode; input is ignored until feature report 0x05 moves it to the full 0x31 report");
                    return;
                default:
                    _log.Warning($"{Info.ProductName}: unknown Bluetooth report ID 0x{reportId:X2} ({bytesRead} byte(s))");
                    return;
            }
        }
        else
        {
            if (reportId != 0x01 || bytesRead < 1 + 63)
            {
                _log.Warning(
                    $"{Info.ProductName}: invalid USB input report: ID 0x{reportId:X2}, {bytesRead} byte(s); expected ID 0x01 with at least {1 + 63} bytes");
                return;
            }

            offset = 1;
        }

        byte[]? usbRaw = null;
        DualSenseRawReport.TryBuildUsbRaw64(data, bytesRead, ConnectionType, out usbRaw);
        InputReport report = new InputReport(data, offset, usbRaw);
        if (_log.IsEnabled(LogLevel.Trace))
        {
            _log.Trace($"Input report 0x{reportId:X2} ({bytesRead} byte(s)): {BitConverter.ToString(data, 0, bytesRead)}");
        }

        if (_previousInputReport is { } prev)
        {
            DetectChanges(prev, report);
        }

        _previousInputReport = report;
        InputReport = report;
        _batteryRaw = report.Battery.Raw;
        TrackPollingRate();
        InputReportReceived?.Invoke(this, report);
    }

    /// <summary>
    /// Validates the CRC32 of a Bluetooth input report 0x31. The checksum covers the
    /// HIDP 0xA1 header plus report bytes 0-73 and is stored little-endian at 74.
    /// </summary>
    /// <param name="report">Wire report starting with report ID 0x31.</param>
    private static bool HasValidBluetoothInputCrc(byte[] report)
    {
        uint expected = (uint)(report[74]
                               | (report[75] << 8)
                               | (report[76] << 16)
                               | (report[77] << 24));
        uint actual = DualSenseCRC32.Compute(report, 0, 74, DualSenseCRC32.InputReportSeed);
        return expected == actual;
    }

    /// <inheritdoc/>
    void ILightSink.Send(SetStateData payload) => SendOutputState(payload);

    /// <inheritdoc/>
    ConnectionType IControllerAudioSink.ConnectionType
    {
        get
        {
            return ConnectionType;
        }
    }

    /// <inheritdoc/>
    bool IControllerAudioSink.HeadphonesConnected
    {
        get
        {
            return InputReport?.Connection.Headphone == true;
        }
    }

    /// <inheritdoc/>
    void IControllerAudioSink.SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume) =>
        SetAudioOutput(outputControl, speakerVolume, headphoneVolume);

    /// <inheritdoc/>
    void IControllerAudioSink.ResetBluetoothAudioStream() => ResetBluetoothAudioStream();

    /// <inheritdoc/>
    void IControllerAudioSink.SendBluetoothAudioPrime(SetStateData state) =>
        SendBluetoothAudioPrime(Lighting.Resolve(state));

    /// <inheritdoc/>
    void IControllerAudioSink.SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame,
        ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route) =>
        SendBluetoothAudioAndHaptics(Lighting.Resolve(state), opusFrame, hapticsPcm, route);

    /// <inheritdoc/>
    void IControllerAudioSink.SendBluetoothAudioAndHapticsDouble(ReadOnlySpan<byte> opusFrames, ReadOnlySpan<byte> hapticsPcm,
        BluetoothAudioRoute route) => SendBluetoothAudioAndHapticsDouble(opusFrames, hapticsPcm, route);

    /// <inheritdoc/>
    void IControllerAudioSink.SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route) =>
        SendBluetoothAudio(opusFrame, route);

    /// <inheritdoc/>
    void IControllerAudioSink.SendBluetoothHaptics(ReadOnlySpan<byte> hapticsPcm) =>
        SendBluetoothHaptics(hapticsPcm);

    /// <summary>
    /// Sends output state (rumble, lightbar, player LEDs, trigger effects) to the
    /// controller, framed for the active transport (USB report ID 0x02 or Bluetooth
    /// report ID 0x31 with a rolling sequence tag and CRC32). Over Bluetooth a payload
    /// byte-identical to the previously sent one is skipped: the controller retains its
    /// output state, so resending unchanged state only consumes interrupt-channel
    /// bandwidth.
    /// </summary>
    /// <param name="payload">The output state to send.</param>
    public void SendOutputState(SetStateData payload)
    {
        if (IsDisposed || IsLinkLost)
        {
            return;
        }

        int manualRumble = Volatile.Read(ref _manualRumble);
        if (manualRumble >= 0)
        {
            payload = ApplyRumbleEncoding(
                payload,
                (byte)(manualRumble >> 8),
                (byte)(manualRumble & 0xFF),
                UsesVibrationV2);
        }

        if ((payload.ValidFlag1 & ValidFlags.AllowLedColor) != 0
            && (payload.LedRed, payload.LedGreen, payload.LedBlue) != CurrentLightbarColor)
        {
            CurrentLightbarColor = (payload.LedRed, payload.LedGreen, payload.LedBlue);
            LightbarColorChanged?.Invoke(this, EventArgs.Empty);
        }

        if ((payload.ValidFlag1 & ValidFlags.AllowPlayerIndicators) != 0)
        {
            byte leds = (byte)((byte)payload.PlayerLeds & (byte)PlayerLedMask.All);
            if (leds != CurrentPlayerLeds)
            {
                CurrentPlayerLeds = leds;
                PlayerLedsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if ((payload.ValidFlag1 & ValidFlags.AllowMuteLight) != 0)
        {
            byte mode = (byte)(payload.MuteLedMode & 0x03);
            if (mode != CurrentMuteLedMode)
            {
                CurrentMuteLedMode = mode;
                MuteLedModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        lock (_outputSync)
        {
            bool bluetooth = ConnectionType == ConnectionType.Bluetooth;
            if (!bluetooth || !IsDuplicateBluetoothPayload(payload))
            {
                OutputReport report = bluetooth
                    ? OutputReport.ForBluetooth(payload, (byte)(_outputSequence & 0x0F))
                    : OutputReport.ForUsb(payload, IsEdge ? 63 : SetStateData.PayloadSize);
                _outputSequence = (byte)((_outputSequence + 1) & 0x0F);

                if (_log.IsEnabled(LogLevel.Trace))
                {
                    _log.Trace($"Sending output report 0x{report.Raw[0]:X2} ({report.Length} byte(s))");
                }

                SendOutput(report.Raw, 0, report.Length);

                // Recorded only after a successful write so a failed send is retried
                // on the next identical payload instead of being deduped away.
                if (bluetooth)
                {
                    RememberBluetoothPayload(payload);
                }
            }
            else
            {
                _log.Trace("Skipping duplicate Bluetooth output report");
            }
        }
    }

    /// <summary>
    /// Reports whether the given payload is byte-identical to the previously sent
    /// Bluetooth payload. Caller must hold <see cref="_outputSync"/>.
    /// </summary>
    private bool IsDuplicateBluetoothPayload(SetStateData payload) => _hasLastBluetoothPayload && payload.AsSpan().SequenceEqual(_lastBluetoothPayload);

    /// <summary>
    /// Stores the given payload as the last successfully sent Bluetooth payload.
    /// Caller must hold <see cref="_outputSync"/>.
    /// </summary>
    private void RememberBluetoothPayload(SetStateData payload)
    {
        payload.CopyTo(_lastBluetoothPayload, 0);
        _hasLastBluetoothPayload = true;
    }

    /// <summary>
    /// Applies the given profile (lightbar color, microphone LED mode, and player LEDs)
    /// to the controller immediately.
    /// </summary>
    /// <param name="profile">The profile to apply.</param>
    public void ApplyProfile(Profile profile)
    {
        LightbarSettings lightbar = profile.Lightbar.Clone();
        (byte baseRed, byte baseGreen, byte baseBlue) =
            LightbarEffectEvaluator.ApplyBrightness(lightbar.Red, lightbar.Green, lightbar.Blue, lightbar.Brightness);

        // The profile owns the static lights until another owner supersedes it. A manual
        // edit is a direct user action, so applying a profile ends that manual hold.
        _lighting.Clear(LightingOwner.Manual);
        _lighting.Submit(LightingOwner.Profile, new LightingSnapshot(
            (baseRed, baseGreen, baseBlue),
            (PlayerLedMask)profile.PlayerLeds.Mask,
            profile.MicLed.Mode,
            profile.OverrideVirtualLights));

        // Start the effect before committing the profile so its first frame is part of the
        // first resolved state and an animated profile never flashes its fallback color.
        _lightbarEffect.Apply(lightbar);

        _log.Debug(
            $"Applying profile '{profile.Name}' to {Info.ProductName}: {lightbar.Mode} RGB({baseRed}, {baseGreen}, {baseBlue}), mic LED {profile.MicLed.Mode}, player LEDs {profile.PlayerLeds.Mask}");

        try
        {
            _lighting.Commit();
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to apply profile '{profile.Name}' to {Info.ProductName}", ex);
        }
    }

    /// <summary>
    /// Stops the profile's animated lightbar effect, leaving the current frame on screen.
    /// Drops the stored profile lights so a manual edit is not overwritten by the audio lane.
    /// </summary>
    public void StopLightbarAnimation()
    {
        _lightbarEffect.Stop();
        _lighting.Clear(LightingOwner.Profile);
    }

    /// <summary>
    /// Gets the current lightbar effect frame for consumers that merge effect colors.
    /// </summary>
    /// <param name="frame">The evaluated frame when an effect is running.</param>
    /// <returns><c>true</c> when a frame is available.</returns>
    public bool TryGetLightbarEffectFrame(out LightbarEffectFrame frame) =>
        _lightbarEffect.TryGetFrame(out frame);

    /// <summary>
    /// Sets the classic (DS4-style) rumble motors. Sends a minimal output report carrying
    /// only the rumble flags and motor values, so no other controller state (lightbar,
    /// trigger effects, audio) is disturbed.
    /// </summary>
    /// <remarks>
    /// Follows the hid-playstation driver's encoding: HAPTICS_SELECT is always set, and
    /// either flag 0 bit 0 (v1) or flag 2 bit 2 (v2) selects the rumble encoding
    /// depending on <see cref="UsesVibrationV2"/>. A strength of 0 turns that motor off.
    /// </remarks>
    /// <param name="left">Left (low-frequency) motor strength (0-255).</param>
    /// <param name="right">Right (high-frequency) motor strength (0-255).</param>
    public void SetVibration(byte left, byte right)
    {
        if (IsDisposed || IsLinkLost)
        {
            return;
        }

        SetStateData payload = ApplyRumbleEncoding(new SetStateData(), left, right, UsesVibrationV2);

        try
        {
            SendOutputState(payload);
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to set vibration on {Info.ProductName} (left={left}, right={right})", ex);
        }
    }

    /// <summary>
    /// Applies the classic rumble selection bits and motor values to an output payload.
    /// The selection bits must accompany every report while classic rumble is active: a
    /// report without them sends the pad back to its haptics path and stops the motors.
    /// </summary>
    /// <param name="payload">Payload to extend; its other fields and flags are preserved.</param>
    /// <param name="left">Left (low-frequency) motor strength (0-255).</param>
    /// <param name="right">Right (high-frequency) motor strength (0-255).</param>
    /// <param name="vibrationV2">Whether the controller uses the improved rumble encoding.</param>
    /// <returns>The payload with the rumble bits and motor values applied.</returns>
    private static SetStateData ApplyRumbleEncoding(SetStateData payload, byte left, byte right, bool vibrationV2)
    {
        ValidFlags flag0 = payload.ValidFlag0 | ValidFlags.UseRumbleNotHaptics;
        ValidFlags flag2 = payload.ValidFlag2;
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

        return payload with
        {
            ValidFlag0 = flag0,
            ValidFlag2 = flag2,
            RumbleLeft = left,
            RumbleRight = right
        };
    }

    /// <summary>
    /// Sets the classic rumble motors for manual (Output Test) vibration. While either
    /// motor is on, the manual claim preempts every audio owner so the pad stops
    /// receiving audio reports and accepts the classic rumble report; releasing both
    /// motors returns the lane to the previous owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Bluetooth audio stream puts the pad into its haptics path, where the motor
    /// bytes of a <c>0x31</c> report are ignored. Forwarded game rumble therefore rides
    /// the haptics PCM instead. Manual testing still needs the classic encoding, so it
    /// takes the lane rather than synthesizing haptics frames.
    /// </para>
    /// <para>
    /// The motor state is also kept in <see cref="_manualRumble"/> and merged into every
    /// outgoing output report while it is set. A single classic rumble report is not
    /// enough: the next report without the rumble-selection bits (such as the profile's
    /// lightbar effect frames) sends the pad back to haptics and stops the motors.
    /// </para>
    /// </remarks>
    /// <param name="left">Left (low-frequency) motor strength (0-255).</param>
    /// <param name="right">Right (high-frequency) motor strength (0-255).</param>
    public void SetManualVibration(byte left, byte right)
    {
        if (left != 0 || right != 0)
        {
            Volatile.Write(ref _manualRumble, (left << 8) | right);
            _audioLane.TryEnter(this, AudioPriority.ManualRumble);
        }
        else
        {
            Volatile.Write(ref _manualRumble, -1);
        }

        SetVibration(left, right);

        if (left == 0 && right == 0)
        {
            _audioLane.Leave(this);
        }
    }

    /// <summary>
    /// Applies adaptive trigger effects to both triggers. Pass <see cref="TriggerEffectBuilder.Off"/>
    /// to clear an effect.
    /// </summary>
    /// <param name="left">Effect block for the L2 (left) trigger.</param>
    /// <param name="right">Effect block for the R2 (right) trigger.</param>
    public void SetTriggerEffects(TriggerEffectBlock left, TriggerEffectBlock right)
    {
        SetStateData payload = new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowLeftTriggerFfb | ValidFlags.AllowRightTriggerFfb,
            L2TriggerEffect = left,
            R2TriggerEffect = right
        };

        try
        {
            SendOutputState(payload);
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to set trigger effects on {Info.ProductName}", ex);
        }
    }

    /// <summary>
    /// Turns off the rumble motors and clears the adaptive trigger effects.
    /// </summary>
    public void ResetOutputs()
    {
        SetManualVibration(0, 0);
        SetTriggerEffects(TriggerEffectBuilder.Off(), TriggerEffectBuilder.Off());
    }

    /// <summary>
    /// Controller-side audio buffer length written into the Bluetooth audio control
    /// blocks (16-128). Applies to the next generated report.
    /// </summary>
    public byte BluetoothAudioBufferLength
    {
        get
        {
            lock (_audioWriteLock)
            {
                return _bluetoothAudioReports.AudioBufferLength;
            }
        }
        set
        {
            lock (_audioWriteLock)
            {
                _bluetoothAudioReports.AudioBufferLength = value;
            }
        }
    }

    /// <summary>
    /// Restarts the Bluetooth audio/haptics report sequence and packet counters.
    /// Call this before starting a stream.
    /// </summary>
    public void ResetBluetoothAudioStream()
    {
        lock (_audioWriteLock)
        {
            _bluetoothAudioReports.Reset();
        }
    }

    /// <summary>
    /// Sends the report <c>0x32</c> init-prime that opens the Bluetooth audio/haptics
    /// stream. The stream must be primed before sending audio or haptics reports.
    /// </summary>
    /// <param name="state">The output state embedded in the prime (routing/volume).</param>
    public void SendBluetoothAudioPrime(SetStateData state)
    {
        EnsureBluetoothAudio();
        lock (_audioWriteLock)
        {
            TrySendAudioReport(_bluetoothAudioReports.BuildInitPrime(state));
        }
    }

    /// <summary>
    /// Sends a report <c>0x32</c> voice-coil haptics frame (64 bytes of s8 stereo PCM).
    /// </summary>
    /// <param name="hapticsPcm">64 bytes of interleaved s8 stereo material.</param>
    public void SendBluetoothHaptics(ReadOnlySpan<byte> hapticsPcm)
    {
        EnsureBluetoothAudio();
        lock (_audioWriteLock)
        {
            TrySendAudioReport(_bluetoothAudioReports.BuildHapticsReport(hapticsPcm));
        }
    }

    /// <summary>
    /// Sends a combined report <c>0x36</c> carrying the output state, one 200-byte Opus
    /// frame and the 64-byte voice-coil haptics frame in a single report. Audio and
    /// haptics must share one report per tick; interleaving a separate <c>0x32</c>
    /// haptics report with the <c>0x35</c> audio lane breaks the controller's audio
    /// stream.
    /// </summary>
    /// <param name="state">The 47-byte output state embedded in the report.</param>
    /// <param name="opusFrame">A 200-byte Opus frame.</param>
    /// <param name="hapticsPcm">64 bytes of interleaved s8 stereo material.</param>
    /// <param name="route">Which output the audio frame targets.</param>
    public void SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route)
    {
        EnsureBluetoothAudio();
        lock (_audioWriteLock)
        {
            TrySendAudioReport(_bluetoothAudioReports.BuildCombinedReport(state, opusFrame, hapticsPcm, route));
        }
    }

    /// <summary>
    /// Sends a double combined report <c>0x39</c> carrying two 200-byte Opus frames and
    /// two 64-byte voice-coil haptics frames in a single report. This variant carries no
    /// output-state block; use <see cref="SendBluetoothAudioAndHaptics"/> when state must
    /// be embedded.
    /// </summary>
    /// <param name="opusFrames">Two contiguous 200-byte Opus frames.</param>
    /// <param name="hapticsPcm">128 bytes of interleaved s8 stereo material (two frames).</param>
    /// <param name="route">Which output both audio frames target.</param>
    public void SendBluetoothAudioAndHapticsDouble(ReadOnlySpan<byte> opusFrames, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route)
    {
        EnsureBluetoothAudio();
        lock (_audioWriteLock)
        {
            TrySendAudioReport(_bluetoothAudioReports.BuildDoubleCombinedReport(opusFrames, hapticsPcm, route));
        }
    }

    /// <summary>
    /// Sends a report <c>0x35</c> speaker/headset audio frame (one 200-byte Opus frame).
    /// </summary>
    /// <param name="opusFrame">A 200-byte Opus frame.</param>
    /// <param name="route">Which output the frame targets.</param>
    public void SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route)
    {
        EnsureBluetoothAudio();
        lock (_audioWriteLock)
        {
            TrySendAudioReport(_bluetoothAudioReports.BuildAudioReport(opusFrame, route));
        }
    }

    /// <summary>
    /// Routes audio output and applies speaker/headphone volume over either transport.
    /// Uses the output-state report (USB <c>0x02</c> / Bluetooth <c>0x31</c>), which is
    /// the authoritative switch for which destination is actually driven.
    /// </summary>
    /// <param name="outputControl">Output path selection: speaker, headphones, or both.</param>
    /// <param name="speakerVolume">Speaker volume (0-255; the controller accepts roughly 0x3D-0x64).</param>
    /// <param name="headphoneVolume">Headphone volume (0-255).</param>
    /// <param name="preampGain">Speaker preamp gain, bits [2:0] of audio_control2 (<c>0x2</c> = +6 dB).</param>
    public void SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume, byte preampGain = 0x02)
    {
        SetStateData payload = new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowSpeakerVolume | ValidFlags.AllowHeadphoneVolume | ValidFlags.AllowAudioControl,
            ValidFlag1 = ValidFlags.AllowAudioControl2,
            SpeakerVolume = speakerVolume,
            HeadphoneVolume = headphoneVolume,
            AudioControl = outputControl,
            AudioControl2 = preampGain
        };

        try
        {
            SendOutputState(payload);
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to set audio output on {Info.ProductName}", ex);
        }
    }

    /// <summary>
    /// Throws if the current transport cannot carry the <c>0x35</c>/<c>0x32</c> reports.
    /// </summary>
    private void EnsureBluetoothAudio()
    {
        if (ConnectionType != ConnectionType.Bluetooth)
        {
            throw new InvalidOperationException("The 0x35/0x32 audio reports require a Bluetooth connection.");
        }
    }

    /// <summary>
    /// Writes an audio/haptics report, logging rather than throwing on HID failures so a
    /// dropped link stops the stream instead of crashing the writer thread.
    /// </summary>
    private void TrySendAudioReport(byte[] report)
    {
        if (IsDisposed || IsLinkLost)
        {
            return;
        }

        try
        {
            if (_log.IsEnabled(LogLevel.Trace))
            {
                _log.Trace($"Sending audio report 0x{report[0]:X2} ({report.Length} byte(s)): {BitConverter.ToString(report, 0, report.Length)}");
            }

            // Audio and state reports share the interrupt channel; serialize them on the
            // same lock so an output-state report can never interleave with an audio report.
            lock (_outputSync)
            {
                SendOutput(report, 0, report.Length);
            }
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to send audio report 0x{report[0]:X2} to {Info.ProductName}", ex);
        }
    }

    /// <summary>
    /// Compares the current and previous input reports and fires events for
    /// any detected changes (buttons, sticks, triggers, battery, connection,
    /// motion, touchpad).
    /// </summary>
    /// <param name="prev">Previous report snapshot.</param>
    /// <param name="cur">Current report.</param>
    private void DetectChanges(InputReport prev, InputReport cur)
    {
        // Button press/release
        CheckButton(prev.Input, cur.Input, ButtonType.Cross, static s => s.Cross);
        CheckButton(prev.Input, cur.Input, ButtonType.Circle, static s => s.Circle);
        CheckButton(prev.Input, cur.Input, ButtonType.Square, static s => s.Square);
        CheckButton(prev.Input, cur.Input, ButtonType.Triangle, static s => s.Triangle);
        CheckButton(prev.Input, cur.Input, ButtonType.DPadUp, static s => s.DPadUp);
        CheckButton(prev.Input, cur.Input, ButtonType.DPadDown, static s => s.DPadDown);
        CheckButton(prev.Input, cur.Input, ButtonType.DPadLeft, static s => s.DPadLeft);
        CheckButton(prev.Input, cur.Input, ButtonType.DPadRight, static s => s.DPadRight);
        CheckButton(prev.Input, cur.Input, ButtonType.L1, static s => s.L1);
        CheckButton(prev.Input, cur.Input, ButtonType.R1, static s => s.R1);
        CheckButton(prev.Input, cur.Input, ButtonType.L2, static s => s.L2Click);
        CheckButton(prev.Input, cur.Input, ButtonType.R2, static s => s.R2Click);
        CheckButton(prev.Input, cur.Input, ButtonType.L3, static s => s.L3);
        CheckButton(prev.Input, cur.Input, ButtonType.R3, static s => s.R3);
        CheckButton(prev.Input, cur.Input, ButtonType.Create, static s => s.Create);
        CheckButton(prev.Input, cur.Input, ButtonType.Options, static s => s.Options);
        CheckButton(prev.Input, cur.Input, ButtonType.PS, static s => s.PS);
        CheckButton(prev.Input, cur.Input, ButtonType.TouchPad, static s => s.TouchPad);
        CheckButton(prev.Input, cur.Input, ButtonType.Mute, static s => s.Mute);
        CheckButton(prev.Input, cur.Input, ButtonType.Edge_LeftFunction, static s => s.EdgeFunctionLeft);
        CheckButton(prev.Input, cur.Input, ButtonType.Edge_RightFunction, static s => s.EdgeFunctionRight);
        CheckButton(prev.Input, cur.Input, ButtonType.Edge_LeftPaddle, static s => s.EdgePaddleLeft);
        CheckButton(prev.Input, cur.Input, ButtonType.Edge_RightPaddle, static s => s.EdgePaddleRight);

        // Stick movement
        if (prev.Input.LeftStickX != cur.Input.LeftStickX || prev.Input.LeftStickY != cur.Input.LeftStickY)
        {
            _log.Trace($"Left stick moved to ({cur.Input.LeftStickX}, {cur.Input.LeftStickY})");
            StickMoved?.Invoke(this,
                new StickEventArgs(StickType.Left, cur.Input.LeftStickX, cur.Input.LeftStickY, prev.Input.LeftStickX, prev.Input.LeftStickY));
        }

        if (prev.Input.RightStickX != cur.Input.RightStickX || prev.Input.RightStickY != cur.Input.RightStickY)
        {
            _log.Trace($"Right stick moved to ({cur.Input.RightStickX}, {cur.Input.RightStickY})");
            StickMoved?.Invoke(this,
                new StickEventArgs(StickType.Right, cur.Input.RightStickX, cur.Input.RightStickY, prev.Input.RightStickX, prev.Input.RightStickY));
        }

        // Trigger movement
        if (prev.Input.L2 != cur.Input.L2)
        {
            _log.Trace($"L2 trigger moved to {cur.Input.L2}");
            TriggerMoved?.Invoke(this, new TriggerEventArgs(TriggerType.L2, cur.Input.L2, prev.Input.L2));
        }

        if (prev.Input.R2 != cur.Input.R2)
        {
            _log.Trace($"R2 trigger moved to {cur.Input.R2}");
            TriggerMoved?.Invoke(this, new TriggerEventArgs(TriggerType.R2, cur.Input.R2, prev.Input.R2));
        }

        // Full-state events (invoke on any change)
        if (prev.Battery != cur.Battery)
        {
            _log.Trace($"Battery changed from {prev.Battery.DisplayPercentage}% to {cur.Battery.DisplayPercentage}% (power state: {cur.Battery.PowerState})");
            BatteryStateChanged?.Invoke(this, new BatteryStateEventArgs(cur.Battery, prev.Battery));
        }

        if (prev.Connection != cur.Connection)
        {
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(cur.Connection, prev.Connection));
        }

        if (prev.Input != cur.Input)
        {
            InputStateChanged?.Invoke(this, new InputStateEventArgs(cur.Input, prev.Input));
        }

        if (prev.Motion != cur.Motion)
        {
            MotionChanged?.Invoke(this, new MotionEventArgs(cur.Motion, prev.Motion));
        }

        if (prev.Touchpad != cur.Touchpad)
        {
            TouchpadChanged?.Invoke(this, new TouchpadEventArgs(cur.Touchpad, prev.Touchpad));
        }
    }

    /// <summary>
    /// Fires ButtonPressed or ButtonReleased based on the transition detected by
    /// the selector predicate.
    /// </summary>
    private void CheckButton(InputState prev, InputState cur, ButtonType button, Func<InputState, bool> selector)
    {
        bool wasPressed = selector(prev);
        bool isPressed = selector(cur);
        if (wasPressed == isPressed)
        {
            return;
        }

        if (isPressed)
        {
            _log.Trace($"{button} pressed");
            ButtonPressed?.Invoke(this, new ButtonEventArgs(button));
        }
        else
        {
            _log.Trace($"{button} released");
            ButtonReleased?.Invoke(this, new ButtonEventArgs(button));
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        // SetVibration is guarded by IsDisposed, so switch the motors off before
        // claiming disposal. A link already gone makes this a no-op.
        try
        {
            SetVibration(0, 0);
        }
        catch (Exception ex)
        {
            // The link may already be gone; the motors will stop when it drops.
            // Keep the reason at Debug so a disposal bug is still diagnosable.
            _log.Debug($"Stopping vibration during dispose failed for {Info.ProductName}", ex);
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lightbarEffect.Dispose();

        _cts.Cancel();

        // Join BEFORE closing the handle: hid_close while the read thread is
        // blocked in hid_read_timeout corrupts the pending native I/O and AVs.
        if (!_readThread.Join(TimeSpan.FromSeconds(2)))
        {
            _log.Error($"Read loop for {Info.ProductName} did not stop within 2 s; closing the HID handle anyway");
        }

        base.Dispose();

        _cts.Dispose();
    }
}