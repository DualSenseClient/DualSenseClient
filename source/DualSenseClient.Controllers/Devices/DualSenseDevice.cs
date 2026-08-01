using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// Concrete controller implementation for the Sony DualSense (PS5) controller.
/// Opens and communicates with the DualSense over USB or Bluetooth via SDL3 HID.
/// </summary>
public class DualSenseDevice : ControllerDevice
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
    /// The background task running the read loop.
    /// </summary>
    private readonly Task _readTask;

    /// <summary>
    /// Previous input report snapshot for change detection. Null until the first
    /// report is received, so no events fire on the initial read.
    /// </summary>
    private InputReport? _previousInputReport;

    /// <inheritdoc/>
    public override ControllerType ControllerType => ControllerType.DualSense;

    /// <summary>
    /// Whether this controller is a DualSense Edge, which has the extra Fn
    /// buttons and back paddles. Base DualSense hardware returns <c>false</c>;
    /// <see cref="DualSenseEdgeDevice"/> overrides this.
    /// </summary>
    public virtual bool IsEdge => false;

    /// <inheritdoc/>
    public override int MaxOutputReportLength => ConnectionType switch
    {
        ConnectionType.Bluetooth => 78,
        ConnectionType.Usb => 63,
        ConnectionType.Unknown => throw new ArgumentOutOfRangeException($"Unknown connection type: {ConnectionType}"),
        _ => throw new ArgumentOutOfRangeException($"Unknown connection type: {ConnectionType}")
    };

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
    /// Current state of input
    /// </summary>
    public InputReport InputReport { get; private set; } = null!;

    /// <summary>
    /// Raised when any input state field changes (sticks, triggers, or buttons).
    /// Fires once per report regardless of how many individual fields changed.
    /// </summary>
    public event EventHandler<InputStateEventArgs>? InputStateChanged;

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
    /// Creates a new DualSense controller wrapper around an already-opened HID device.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseDevice(IHidDevice device, IHidDeviceInfo info) : base(device, info)
    {
        FirmwareInfo = FeatureReader.ReadFirmwareInfo(this);
        PairingInfo = FeatureReader.ReadPairingInfo(this);
        _readTask = Task.Run(() => ReadLoop(_cts.Token));
    }

    /// <summary>
    /// Background loop that continuously reads HID input reports from the controller.
    /// Runs on a background task for the lifetime of the controller connection.
    /// </summary>
    /// <param name="ct">Cancellation token to signal when the loop should stop.</param>
    private async Task ReadLoop(CancellationToken ct)
    {
        _log.Debug("Read Loop Start");
        byte[] buffer = new byte[MaxOutputReportLength];
        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                int result = await ReadInputAsync(buffer, 0, buffer.Length, ct);
                if (result <= 0)
                {
                    _log.Warning($"Read returned {result} bytes, disconnecting");
                    break;
                }

                ProcessInputReport(buffer);
            }
            catch (HidException)
            {
                _log.Error("SDL_hid_read_timeout failed");
                break;
            }
            catch (OperationCanceledException)
            {
                _log.Debug("Read Loop Cancelled");
                break;
            }
            catch (Exception ex)
            {
                _log.LogExceptionDetails(ex);
                break;
            }
        }

        _log.Debug("Read Loop End");
    }

    /// <summary>
    /// Routes a raw HID report to the correct parser based on connection type and report ID.
    /// Strips the protocol header bytes before forwarding to the input parser.
    /// </summary>
    /// <param name="data">Raw HID input report buffer.</param>
    private void ProcessInputReport(byte[] data)
    {
        byte reportId = data[0];
        int offset;
        if (ConnectionType == ConnectionType.Bluetooth)
        {
            switch (reportId)
            {
                case 0x31:
                    offset = 2;
                    break;
                case 0x01:
                    _log.Warning("Controller is in simple Bluetooth state");
                    return;
                default:
                    _log.Warning($"Unknown Bluetooth report ID: 0x{reportId:X2}");
                    return;
            }
        }
        else
        {
            if (reportId != 0x01)
            {
                _log.Warning($"Invalid USB report ID: 0x{reportId:X2} (expected 0x01)");
                return;
            }
            offset = 1;
        }

        InputReport report = new InputReport(data, offset);
        _log.Trace($"Input report 0x{reportId:X2} ({data.Length} byte(s)): {BitConverter.ToString(data, 0, data.Length)}");
        if (_previousInputReport is { } prev)
        {
            DetectChanges(prev, report);
        }
        _previousInputReport = report;
        InputReport = report;
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
            StickMoved?.Invoke(this, new StickEventArgs(StickType.Left, cur.Input.LeftStickX, cur.Input.LeftStickY, prev.Input.LeftStickX, prev.Input.LeftStickY));
        }
        if (prev.Input.RightStickX != cur.Input.RightStickX || prev.Input.RightStickY != cur.Input.RightStickY)
        {
            _log.Trace($"Right stick moved to ({cur.Input.RightStickX}, {cur.Input.RightStickY})");
            StickMoved?.Invoke(this, new StickEventArgs(StickType.Right, cur.Input.RightStickX, cur.Input.RightStickY, prev.Input.RightStickX, prev.Input.RightStickY));
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
}