using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.GUI.Services;
using DualSenseClient.Hid;
using SoundFlow.Abstracts;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display model for the input monitor page. Wraps a <see cref="ControllerItem"/> and
/// exposes the controller's live button, stick, trigger, motion, and touchpad state
/// bound by the UI. Values read as "-" until the first input report is received.
/// </summary>
/// <remarks>
/// <para>
/// The item subscribes to the controller's <see cref="DualSenseDevice.InputStateChanged"/>,
/// <see cref="DualSenseDevice.MotionChanged"/>, and <see cref="DualSenseDevice.TouchpadChanged"/>
/// events and re-raises <see cref="ObservableObject.PropertyChanged"/> so bound UI values update
/// as reports arrive.
/// </para>
/// <para>
/// Event handlers fire on the device read-loop thread, so updates are marshaled to the UI thread
/// via <see cref="Dispatcher.UIThread"/>. Updates are coalesced: at most one UI-thread update is
/// queued at a time, and it reads the latest snapshot when it runs, so rapid report delivery
/// never floods the dispatcher.
/// </para>
/// </remarks>
public sealed partial class InputMonitorItem : ObservableObject, IControllerMonitorState, IDisposable
{
    /// <summary>
    /// Placeholder rendered when a value is missing or unreadable.
    /// </summary>
    private const string Unavailable = "-";

    /// <summary>
    /// Default PS-blue lightbar color shown before the controller has received any
    /// color output report.
    /// </summary>
    private const int DefaultLightbarRed = 0;

    private const int DefaultLightbarGreen = 87;
    private const int DefaultLightbarBlue = 255;

    /// <summary>
    /// All property names re-raised on each live update.
    /// </summary>
    private static readonly string[] _updateProperties =
    [
        nameof(HasReport),
        nameof(PollingRateHz),
        nameof(Cross), nameof(Circle), nameof(Square), nameof(Triangle),
        nameof(DPadUp), nameof(DPadDown), nameof(DPadLeft), nameof(DPadRight),
        nameof(L1), nameof(R1), nameof(L2Click), nameof(R2Click),
        nameof(L3), nameof(R3), nameof(Create), nameof(Options),
        nameof(PS), nameof(TouchPad), nameof(Mute),
        nameof(FnL), nameof(FnR), nameof(L4), nameof(R4),
        nameof(LeftStickX), nameof(LeftStickY),
        nameof(RightStickX), nameof(RightStickY),
        nameof(L2), nameof(R2),
        nameof(GyroX), nameof(GyroY), nameof(GyroZ),
        nameof(AccelX), nameof(AccelY), nameof(AccelZ),
        nameof(MotionSamples),
        nameof(Touch1Active), nameof(Touch1State), nameof(Touch1Position),
        nameof(Touch1X), nameof(Touch1Y),
        nameof(Touch2Active), nameof(Touch2State), nameof(Touch2Position),
        nameof(Touch2X), nameof(Touch2Y),
        nameof(LightbarRed), nameof(LightbarGreen), nameof(LightbarBlue),
        nameof(PlayerLeds), nameof(MuteLedMode)
    ];

    /// <summary>
    /// The concrete controller the live state is read from, or <c>null</c> for
    /// non-DualSense devices or when the device is not reachable.
    /// </summary>
    private readonly DualSenseDevice? _device;

    /// <summary>
    /// Latest input snapshot (buttons, sticks, triggers), or <c>null</c> before the
    /// first report is received.
    /// </summary>
    private InputState? _input;

    /// <summary>
    /// Latest motion snapshot (gyro, accel, temperature), or <c>null</c> before the
    /// first report is received.
    /// </summary>
    private MotionState? _motion;

    /// <summary>
    /// Maximum number of motion samples retained for the live graphs.
    /// </summary>
    private const int MotionSampleLimit = 400;

    /// <summary>
    /// Rolling buffer of recent motion samples (oldest first). Only touched on the UI
    /// thread: samples are appended inside the coalesced update, so it is safe for the
    /// graph controls to enumerate it while it is being mutated.
    /// </summary>
    private readonly List<MotionState> _motionSamples = new List<MotionState>(MotionSampleLimit);

    /// <summary>
    /// Set when a new motion report arrived and is waiting to be appended to
    /// <see cref="_motionSamples"/> on the UI thread.
    /// </summary>
    private bool _motionSamplePending;

    /// <summary>
    /// Latest touchpad snapshot, or <c>null</c> before the first report is received.
    /// </summary>
    private TouchpadState? _touchpad;

    /// <summary>
    /// Whether at least one input report has been received.
    /// </summary>
    private bool _hasReport;

    /// <summary>
    /// Whether a UI-thread update is already queued, so rapid events coalesce.
    /// </summary>
    private bool _updateQueued;

    /// <summary>
    /// Tracks whether the event subscriptions have been released.
    /// </summary>
    private bool _disposed;

    // ── Output Test ───────────────────────────────────────────

    /// <summary>
    /// Whether the left rumble motor is enabled.
    /// </summary>
    private bool _leftMotorEnabled;

    /// <summary>
    /// Left rumble motor strength (0-255).
    /// </summary>
    private int _leftMotorStrength;

    /// <summary>
    /// Whether the right rumble motor is enabled.
    /// </summary>
    private bool _rightMotorEnabled;

    /// <summary>
    /// Right rumble motor strength (0-255).
    /// </summary>
    private int _rightMotorStrength;

    /// <summary>
    /// Index into <see cref="TriggerEffectModes"/>: the left (L2) adaptive trigger effect mode.
    /// </summary>
    private int _leftTriggerModeIndex;

    /// <summary>
    /// Left (L2) trigger resistance/effect force (0-255).
    /// </summary>
    private int _leftTriggerForce = 100;

    /// <summary>
    /// Left (L2) trigger effect start position (0-255).
    /// </summary>
    private int _leftTriggerStart;

    /// <summary>
    /// Left (L2) trigger effect end position (0-255).
    /// </summary>
    private int _leftTriggerEnd = 255;

    /// <summary>
    /// Left (L2) automatic mode effect frequency (0-15).
    /// </summary>
    private int _leftTriggerFrequency = 5;

    /// <summary>
    /// Index into <see cref="TriggerEffectModes"/>: the right (R2) adaptive trigger effect mode.
    /// </summary>
    private int _rightTriggerModeIndex;

    /// <summary>
    /// Right (R2) trigger resistance/effect force (0-255).
    /// </summary>
    private int _rightTriggerForce = 100;

    /// <summary>
    /// Right (R2) trigger effect start position (0-255).
    /// </summary>
    private int _rightTriggerStart;

    /// <summary>
    /// Right (R2) trigger effect end position (0-255).
    /// </summary>
    private int _rightTriggerEnd = 255;

    /// <summary>
    /// Right (R2) automatic mode effect frequency (0-15).
    /// </summary>
    private int _rightTriggerFrequency = 5;

    /// <summary>
    /// Delay after a slider stops changing before its value is applied to the controller,
    /// so dragging a slider does not spam output reports.
    /// </summary>
    private static readonly TimeSpan OutputDebounceDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Runs pending vibration/trigger updates after the slider debounce delay elapses.
    /// </summary>
    private readonly DispatcherTimer? _outputDebounceTimer;

    /// <summary>
    /// Interval at which the polling-rate display is refreshed. The rate settles on the
    /// device side independently of input changes, so it cannot ride the change events.
    /// </summary>
    private static readonly TimeSpan PollingRateRefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Periodically re-raises <see cref="PollingRateHz"/> while a device is attached.
    /// </summary>
    private readonly DispatcherTimer? _pollingRateTimer;

    /// <summary>
    /// Whether a debounced vibration update is waiting to be applied.
    /// </summary>
    private bool _vibrationPending;

    /// <summary>
    /// Whether a debounced trigger effect update is waiting to be applied.
    /// </summary>
    private bool _triggersPending;

    /// <summary>
    /// The controller item being displayed.
    /// </summary>
    public ControllerItem Controller { get; }

    /// <summary>
    /// The audio player for the wrapped controller, created when an audio engine is
    /// available; <c>null</c> for hosts that only display live state (e.g. the device
    /// info page).
    /// </summary>
    public AudioPlayerItem? Audio { get; }

    /// <summary>
    /// The adaptive trigger modes offered by the effect pickers (index 0 is Off).
    /// </summary>
    public IReadOnlyList<TriggerEffectModeItem> TriggerEffectModes { get; }

    /// <summary>
    /// Whether the left rumble motor is enabled (two-way). Applied to the controller on change.
    /// </summary>
    public bool LeftMotorEnabled
    {
        get
        {
            return _leftMotorEnabled;
        }
        set
        {
            if (SetProperty(ref _leftMotorEnabled, value))
            {
                ApplyVibration();
            }
        }
    }

    /// <summary>
    /// Left rumble motor strength (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int LeftMotorStrength
    {
        get
        {
            return _leftMotorStrength;
        }
        set
        {
            if (SetProperty(ref _leftMotorStrength, value))
            {
                ScheduleVibrationUpdate();
            }
        }
    }

    /// <summary>
    /// Whether the right rumble motor is enabled (two-way). Applied to the controller on change.
    /// </summary>
    public bool RightMotorEnabled
    {
        get
        {
            return _rightMotorEnabled;
        }
        set
        {
            if (SetProperty(ref _rightMotorEnabled, value))
            {
                ApplyVibration();
            }
        }
    }

    /// <summary>
    /// Right rumble motor strength (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int RightMotorStrength
    {
        get
        {
            return _rightMotorStrength;
        }
        set
        {
            if (SetProperty(ref _rightMotorStrength, value))
            {
                ScheduleVibrationUpdate();
            }
        }
    }

    /// <summary>
    /// Left (L2) trigger effect mode (two-way, index into <see cref="TriggerEffectModes"/>,
    /// default 0 = Off). Applied to the controller on change.
    /// </summary>
    public int LeftTriggerModeIndex
    {
        get
        {
            return _leftTriggerModeIndex;
        }
        set
        {
            int clamped = Math.Clamp(value, 0, TriggerEffectModes.Count - 1);
            if (SetProperty(ref _leftTriggerModeIndex, clamped))
            {
                ApplyTriggerEffects();
                NotifyLeftTriggerVisibilities();
            }
        }
    }

    /// <summary>
    /// Left (L2) trigger force (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int LeftTriggerForce
    {
        get
        {
            return _leftTriggerForce;
        }
        set
        {
            if (SetProperty(ref _leftTriggerForce, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Left (L2) trigger effect start position (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int LeftTriggerStart
    {
        get
        {
            return _leftTriggerStart;
        }
        set
        {
            if (SetProperty(ref _leftTriggerStart, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Left (L2) trigger effect end position (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int LeftTriggerEnd
    {
        get
        {
            return _leftTriggerEnd;
        }
        set
        {
            if (SetProperty(ref _leftTriggerEnd, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Left (L2) automatic mode effect frequency (0-15). Applied to the controller once the slider settles.
    /// </summary>
    public int LeftTriggerFrequency
    {
        get
        {
            return _leftTriggerFrequency;
        }
        set
        {
            if (SetProperty(ref _leftTriggerFrequency, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Right (R2) trigger effect mode (two-way, index into <see cref="TriggerEffectModes"/>,
    /// default 0 = Off). Applied to the controller on change.
    /// </summary>
    public int RightTriggerModeIndex
    {
        get
        {
            return _rightTriggerModeIndex;
        }
        set
        {
            int clamped = Math.Clamp(value, 0, TriggerEffectModes.Count - 1);
            if (SetProperty(ref _rightTriggerModeIndex, clamped))
            {
                ApplyTriggerEffects();
                NotifyRightTriggerVisibilities();
            }
        }
    }

    /// <summary>
    /// Right (R2) trigger force (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int RightTriggerForce
    {
        get
        {
            return _rightTriggerForce;
        }
        set
        {
            if (SetProperty(ref _rightTriggerForce, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Right (R2) trigger effect start position (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int RightTriggerStart
    {
        get
        {
            return _rightTriggerStart;
        }
        set
        {
            if (SetProperty(ref _rightTriggerStart, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Right (R2) trigger effect end position (0-255). Applied to the controller once the slider settles.
    /// </summary>
    public int RightTriggerEnd
    {
        get
        {
            return _rightTriggerEnd;
        }
        set
        {
            if (SetProperty(ref _rightTriggerEnd, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Right (R2) automatic mode effect frequency (0-15). Applied to the controller once the slider settles.
    /// </summary>
    public int RightTriggerFrequency
    {
        get
        {
            return _rightTriggerFrequency;
        }
        set
        {
            if (SetProperty(ref _rightTriggerFrequency, value))
            {
                ScheduleTriggerUpdate();
            }
        }
    }

    /// <summary>
    /// Whether any left (L2) trigger effect parameter is applicable, i.e. a mode other than Off is selected.
    /// </summary>
    public bool LeftTriggerParametersVisible
    {
        get
        {
            return SelectedLeftTriggerMode != TriggerEffectType.Off;
        }
    }

    /// <summary>
    /// Whether the left (L2) start-position slider applies to the selected mode.
    /// </summary>
    public bool LeftTriggerStartVisible
    {
        get
        {
            return LeftTriggerParametersVisible;
        }
    }

    /// <summary>
    /// Whether the left (L2) end-position slider applies to the selected mode (Trigger mode only).
    /// </summary>
    public bool LeftTriggerEndVisible
    {
        get
        {
            return SelectedLeftTriggerMode == TriggerEffectType.Trigger;
        }
    }

    /// <summary>
    /// Whether the left (L2) force slider applies to the selected mode.
    /// </summary>
    public bool LeftTriggerForceVisible
    {
        get
        {
            return LeftTriggerParametersVisible;
        }
    }

    /// <summary>
    /// Whether the left (L2) frequency slider applies to the selected mode (Automatic mode only).
    /// </summary>
    public bool LeftTriggerFrequencyVisible
    {
        get
        {
            return SelectedLeftTriggerMode == TriggerEffectType.Automatic;
        }
    }

    /// <summary>
    /// Whether any right (R2) trigger effect parameter is applicable, i.e. a mode other than Off is selected.
    /// </summary>
    public bool RightTriggerParametersVisible
    {
        get
        {
            return SelectedRightTriggerMode != TriggerEffectType.Off;
        }
    }

    /// <summary>
    /// Whether the right (R2) start-position slider applies to the selected mode.
    /// </summary>
    public bool RightTriggerStartVisible
    {
        get
        {
            return RightTriggerParametersVisible;
        }
    }

    /// <summary>
    /// Whether the right (R2) end-position slider applies to the selected mode (Trigger mode only).
    /// </summary>
    public bool RightTriggerEndVisible
    {
        get
        {
            return SelectedRightTriggerMode == TriggerEffectType.Trigger;
        }
    }

    /// <summary>
    /// Whether the right (R2) force slider applies to the selected mode.
    /// </summary>
    public bool RightTriggerForceVisible
    {
        get
        {
            return RightTriggerParametersVisible;
        }
    }

    /// <summary>
    /// Whether the right (R2) frequency slider applies to the selected mode (Automatic mode only).
    /// </summary>
    public bool RightTriggerFrequencyVisible
    {
        get
        {
            return SelectedRightTriggerMode == TriggerEffectType.Automatic;
        }
    }

    /// <summary>
    /// Human-readable product name.
    /// </summary>
    public string DisplayName
    {
        get
        {
            return Controller.DisplayName;
        }
    }

    /// <summary>
    /// Physical transport (USB / Bluetooth).
    /// </summary>
    public ConnectionType ConnectionType
    {
        get
        {
            return Controller.ConnectionType;
        }
    }

    /// <summary>
    /// Whether at least one input report has been received, so live values are available.
    /// </summary>
    public bool HasReport
    {
        get
        {
            return _hasReport;
        }
    }

    // ── Buttons ────────────────────────────────────────────────

    /// <summary>
    /// Whether the Cross (X) face button is currently pressed.
    /// </summary>
    public bool Cross
    {
        get
        {
            return _input?.Cross ?? false;
        }
    }

    /// <summary>
    /// Whether the Circle (O) face button is currently pressed.
    /// </summary>
    public bool Circle
    {
        get
        {
            return _input?.Circle ?? false;
        }
    }

    /// <summary>
    /// Whether the Square ([]) face button is currently pressed.
    /// </summary>
    public bool Square
    {
        get
        {
            return _input?.Square ?? false;
        }
    }

    /// <summary>
    /// Whether the Triangle (^) face button is currently pressed.
    /// </summary>
    public bool Triangle
    {
        get
        {
            return _input?.Triangle ?? false;
        }
    }

    /// <summary>
    /// Whether the D-pad up direction is currently pressed.
    /// </summary>
    public bool DPadUp
    {
        get
        {
            return _input?.DPadUp ?? false;
        }
    }

    /// <summary>
    /// Whether the D-pad down direction is currently pressed.
    /// </summary>
    public bool DPadDown
    {
        get
        {
            return _input?.DPadDown ?? false;
        }
    }

    /// <summary>
    /// Whether the D-pad left direction is currently pressed.
    /// </summary>
    public bool DPadLeft
    {
        get
        {
            return _input?.DPadLeft ?? false;
        }
    }

    /// <summary>
    /// Whether the D-pad right direction is currently pressed.
    /// </summary>
    public bool DPadRight
    {
        get
        {
            return _input?.DPadRight ?? false;
        }
    }

    /// <summary>
    /// Whether the left shoulder button is currently pressed.
    /// </summary>
    public bool L1
    {
        get
        {
            return _input?.L1 ?? false;
        }
    }

    /// <summary>
    /// Whether the right shoulder button is currently pressed.
    /// </summary>
    public bool R1
    {
        get
        {
            return _input?.R1 ?? false;
        }
    }

    /// <summary>
    /// Whether the left trigger click is currently pressed.
    /// </summary>
    public bool L2Click
    {
        get
        {
            return _input?.L2Click ?? false;
        }
    }

    /// <summary>
    /// Whether the right trigger click is currently pressed.
    /// </summary>
    public bool R2Click
    {
        get
        {
            return _input?.R2Click ?? false;
        }
    }

    /// <summary>
    /// Whether the left stick is currently pressed down (L3).
    /// </summary>
    public bool L3
    {
        get
        {
            return _input?.L3 ?? false;
        }
    }

    /// <summary>
    /// Whether the right stick is currently pressed down (R3).
    /// </summary>
    public bool R3
    {
        get
        {
            return _input?.R3 ?? false;
        }
    }

    /// <summary>
    /// Whether the Create button is currently pressed.
    /// </summary>
    public bool Create
    {
        get
        {
            return _input?.Create ?? false;
        }
    }

    /// <summary>
    /// Whether the Options button is currently pressed.
    /// </summary>
    public bool Options
    {
        get
        {
            return _input?.Options ?? false;
        }
    }

    /// <summary>
    /// Whether the PlayStation button is currently pressed.
    /// </summary>
    public bool PS
    {
        get
        {
            return _input?.PS ?? false;
        }
    }

    /// <summary>
    /// Whether the touchpad click is currently pressed.
    /// </summary>
    public bool TouchPad
    {
        get
        {
            return _input?.TouchPad ?? false;
        }
    }

    /// <summary>
    /// Whether the mute button is currently pressed.
    /// </summary>
    public bool Mute
    {
        get
        {
            return _input?.Mute ?? false;
        }
    }

    /// <summary>
    /// Whether the connected controller is a DualSense Edge (has the Fn buttons
    /// and back paddles). Drives visibility of Edge-only UI.
    /// </summary>
    public bool IsEdge
    {
        get
        {
            return _device?.IsEdge ?? false;
        }
    }

    /// <summary>
    /// Whether the left Edge function button is currently pressed.
    /// </summary>
    public bool FnL
    {
        get
        {
            return _input?.EdgeFunctionLeft ?? false;
        }
    }

    /// <summary>
    /// Whether the right Edge function button is currently pressed.
    /// </summary>
    public bool FnR
    {
        get
        {
            return _input?.EdgeFunctionRight ?? false;
        }
    }

    /// <summary>
    /// Whether the left Edge paddle is currently pressed.
    /// </summary>
    public bool L4
    {
        get
        {
            return _input?.EdgePaddleLeft ?? false;
        }
    }

    /// <summary>
    /// Whether the right Edge paddle is currently pressed.
    /// </summary>
    public bool R4
    {
        get
        {
            return _input?.EdgePaddleRight ?? false;
        }
    }

    // ── Sticks ────────────────────────────────────────────────

    /// <summary>
    /// Left stick horizontal position (0-255, center is 128).
    /// </summary>
    public int LeftStickX
    {
        get
        {
            return _input?.LeftStickX ?? 128;
        }
    }

    /// <summary>
    /// Left stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    public int LeftStickY
    {
        get
        {
            return _input?.LeftStickY ?? 128;
        }
    }

    /// <summary>
    /// Right stick horizontal position (0-255, center is 128).
    /// </summary>
    public int RightStickX
    {
        get
        {
            return _input?.RightStickX ?? 128;
        }
    }

    /// <summary>
    /// Right stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    public int RightStickY
    {
        get
        {
            return _input?.RightStickY ?? 128;
        }
    }

    // ── Triggers ──────────────────────────────────────────────

    /// <summary>
    /// Left analog trigger value (0-255, released to fully pressed).
    /// </summary>
    public int L2
    {
        get
        {
            return _input?.L2 ?? 0;
        }
    }

    /// <summary>
    /// Right analog trigger value (0-255, released to fully pressed).
    /// </summary>
    public int R2
    {
        get
        {
            return _input?.R2 ?? 0;
        }
    }

    // ── Motion ────────────────────────────────────────────────

    /// <summary>
    /// Gyroscope X-axis / pitch (angular velocity, 16.384 LSB/dps), or "-" when unavailable.
    /// </summary>
    public string GyroX
    {
        get
        {
            return _motion is { } motion ? motion.GyroX.ToString() : Unavailable;
        }
    }

    /// <summary>
    /// Gyroscope Y-axis / yaw (angular velocity, 16.384 LSB/dps), or "-" when unavailable.
    /// </summary>
    public string GyroY
    {
        get
        {
            return _motion is { } motion ? motion.GyroY.ToString() : Unavailable;
        }
    }

    /// <summary>
    /// Gyroscope Z-axis / roll (angular velocity, 16.384 LSB/dps), or "-" when unavailable.
    /// </summary>
    public string GyroZ
    {
        get
        {
            return _motion is { } motion ? motion.GyroZ.ToString() : Unavailable;
        }
    }

    /// <summary>
    /// Accelerometer X-axis (linear acceleration, 8192 LSB/g), or "-" when unavailable.
    /// </summary>
    public string AccelX
    {
        get
        {
            return _motion is { } motion ? motion.AccelX.ToString() : Unavailable;
        }
    }

    /// <summary>
    /// Accelerometer Y-axis (linear acceleration, 8192 LSB/g), or "-" when unavailable.
    /// </summary>
    public string AccelY
    {
        get
        {
            return _motion is { } motion ? motion.AccelY.ToString() : Unavailable;
        }
    }

    /// <summary>
    /// Accelerometer Z-axis (linear acceleration, 8192 LSB/g), or "-" when unavailable.
    /// </summary>
    public string AccelZ
    {
        get
        {
            return _motion is { } motion ? motion.AccelZ.ToString() : Unavailable;
        }
    }

    /// <summary>
    /// Rolling buffer of recent motion samples (oldest first) for the motion graphs.
    /// </summary>
    public IReadOnlyList<MotionState> MotionSamples
    {
        get
        {
            return _motionSamples;
        }
    }

    // ── Touchpad ──────────────────────────────────────────────

    /// <summary>
    /// Whether a finger is currently detected at touch point 1.
    /// </summary>
    public bool Touch1Active
    {
        get
        {
            return _touchpad?.Touch1.IsActive ?? false;
        }
    }

    /// <summary>
    /// Touch point 1 horizontal position (0-1919), or 0 when no finger is detected.
    /// </summary>
    public int Touch1X
    {
        get
        {
            return _touchpad?.Touch1 is { } p && p.IsActive ? p.X : 0;
        }
    }

    /// <summary>
    /// Touch point 1 vertical position (0-1079), or 0 when no finger is detected.
    /// </summary>
    public int Touch1Y
    {
        get
        {
            return _touchpad?.Touch1 is { } p && p.IsActive ? p.Y : 0;
        }
    }

    /// <summary>
    /// Localized active/inactive text for touch point 1.
    /// </summary>
    public string Touch1State
    {
        get
        {
            return ActiveText(Touch1Active);
        }
    }

    /// <summary>
    /// Touch point 1 position as "x, y", or "-" when no finger is detected.
    /// </summary>
    public string Touch1Position
    {
        get
        {
            return TouchPosition(_touchpad?.Touch1);
        }
    }

    /// <summary>
    /// Whether a finger is currently detected at touch point 2.
    /// </summary>
    public bool Touch2Active
    {
        get
        {
            return _touchpad?.Touch2.IsActive ?? false;
        }
    }

    /// <summary>
    /// Touch point 2 horizontal position (0-1919), or 0 when no finger is detected.
    /// </summary>
    public int Touch2X
    {
        get
        {
            return _touchpad?.Touch2 is { } p && p.IsActive ? p.X : 0;
        }
    }

    /// <summary>
    /// Touch point 2 vertical position (0-1079), or 0 when no finger is detected.
    /// </summary>
    public int Touch2Y
    {
        get
        {
            return _touchpad?.Touch2 is { } p && p.IsActive ? p.Y : 0;
        }
    }

    /// <summary>
    /// Localized active/inactive text for touch point 2.
    /// </summary>
    public string Touch2State
    {
        get
        {
            return ActiveText(Touch2Active);
        }
    }

    /// <summary>
    /// Touch point 2 position as "x, y", or "-" when no finger is detected.
    /// </summary>
    public string Touch2Position
    {
        get
        {
            return TouchPosition(_touchpad?.Touch2);
        }
    }

    // ── Lightbar ──────────────────────────────────────────────

    /// <summary>
    /// Current lightbar red channel (0-255), or the default PS blue when no
    /// DualSense device is present.
    /// </summary>
    public int LightbarRed
    {
        get
        {
            return _device?.CurrentLightbarColor.Red ?? DefaultLightbarRed;
        }
    }

    /// <summary>
    /// Current lightbar green channel (0-255), or the default PS blue when no
    /// DualSense device is present.
    /// </summary>
    public int LightbarGreen
    {
        get
        {
            return _device?.CurrentLightbarColor.Green ?? DefaultLightbarGreen;
        }
    }

    /// <summary>
    /// Current lightbar blue channel (0-255), or the default PS blue when no
    /// DualSense device is present.
    /// </summary>
    public int LightbarBlue
    {
        get
        {
            return _device?.CurrentLightbarColor.Blue ?? DefaultLightbarBlue;
        }
    }

    /// <summary>
    /// Current player LED layout as a bitmask (bit 0 = LED 1, leftmost), or 0 when no
    /// DualSense device is present.
    /// </summary>
    public int PlayerLeds
    {
        get
        {
            return _device?.CurrentPlayerLeds ?? 0;
        }
    }

    /// <summary>
    /// Current mute LED mode (0 = off, 1 = on, 2 = pulse), or 0 when no DualSense device
    /// is present.
    /// </summary>
    public int MuteLedMode
    {
        get
        {
            return _device?.CurrentMuteLedMode ?? 0;
        }
    }

    // ── Report ────────────────────────────────────────────────

    /// <summary>
    /// Measured controller polling rate in Hz, or 0 before it can be measured.
    /// </summary>
    public int PollingRateHz
    {
        get
        {
            return _device?.PollingRateHz ?? 0;
        }
    }

    /// <summary>
    /// Creates a new input monitor item for the given controller and subscribes to its
    /// input events.
    /// </summary>
    /// <param name="controller">The controller item to display.</param>
    /// <param name="engine">The shared audio engine used by the audio player.</param>
    public InputMonitorItem(ControllerItem controller, AudioEngine engine)
        : this(controller, new AudioPlayerItem(controller.Device as DualSenseDevice, engine))
    {
    }

    /// <summary>
    /// Creates a new input monitor item for the given controller without an audio player,
    /// for hosts that only display live state (e.g. the device info page).
    /// </summary>
    /// <param name="controller">The controller item to display.</param>
    public InputMonitorItem(ControllerItem controller)
        : this(controller, (AudioPlayerItem?)null)
    {
    }

    /// <summary>
    /// Creates a new input monitor item for the given controller, optionally with an
    /// audio player, and subscribes to its input events.
    /// </summary>
    private InputMonitorItem(ControllerItem controller, AudioPlayerItem? audio)
    {
        Controller = controller;
        _device = controller.Device as DualSenseDevice;
        Audio = audio;

        TriggerEffectModes =
        [
            new TriggerEffectModeItem(TriggerEffectType.Off, GetText("InputMonitorPage.OutputTest.Triggers.Mode.Off")),
            new TriggerEffectModeItem(TriggerEffectType.Resistance, GetText("InputMonitorPage.OutputTest.Triggers.Mode.Resistance")),
            new TriggerEffectModeItem(TriggerEffectType.Trigger, GetText("InputMonitorPage.OutputTest.Triggers.Mode.Trigger")),
            new TriggerEffectModeItem(TriggerEffectType.Automatic, GetText("InputMonitorPage.OutputTest.Triggers.Mode.Automatic"))
        ];
        _leftTriggerModeIndex = 0;
        _rightTriggerModeIndex = 0;

        _outputDebounceTimer = new DispatcherTimer
        {
            Interval = OutputDebounceDelay
        };
        _outputDebounceTimer.Tick += OnOutputDebounceTick;

        if (_device?.InputReport is { } report)
        {
            _input = report.Input;
            _motion = report.Motion;
            _touchpad = report.Touchpad;
            _hasReport = true;
        }

        if (_device is not null)
        {
            _device.InputStateChanged += OnInputStateChanged;
            _device.MotionChanged += OnMotionChanged;
            _device.TouchpadChanged += OnTouchpadChanged;
            _device.LightbarColorChanged += OnLightbarColorChanged;
            _device.PlayerLedsChanged += OnLightbarColorChanged;
            _device.MuteLedModeChanged += OnLightbarColorChanged;

            _pollingRateTimer = new DispatcherTimer(PollingRateRefreshInterval, DispatcherPriority.Background,
                (_, _) => OnPropertyChanged(nameof(PollingRateHz)));
            _pollingRateTimer.Start();
        }
    }

    /// <summary>
    /// Unsubscribes from the controller's input events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollingRateTimer?.Stop();
        StopOutputDebounce();
        ResetTestOutputs();
        Audio?.Dispose();
        if (_device is not null)
        {
            _device.InputStateChanged -= OnInputStateChanged;
            _device.MotionChanged -= OnMotionChanged;
            _device.TouchpadChanged -= OnTouchpadChanged;
            _device.LightbarColorChanged -= OnLightbarColorChanged;
            _device.PlayerLedsChanged -= OnLightbarColorChanged;
            _device.MuteLedModeChanged -= OnLightbarColorChanged;
        }
    }

    /// <summary>
    /// Marks a vibration update as pending and restarts the output debounce timer.
    /// </summary>
    private void ScheduleVibrationUpdate()
    {
        _vibrationPending = true;
        RestartOutputDebounce();
    }

    /// <summary>
    /// Marks a trigger effect update as pending and restarts the output debounce timer.
    /// </summary>
    private void ScheduleTriggerUpdate()
    {
        _triggersPending = true;
        RestartOutputDebounce();
    }

    /// <summary>
    /// Restarts the debounce timer so the pending updates are applied only once the
    /// slider value has been stable for <see cref="OutputDebounceDelay"/>.
    /// </summary>
    private void RestartOutputDebounce()
    {
        _outputDebounceTimer?.Stop();
        _outputDebounceTimer?.Start();
    }

    /// <summary>
    /// Stops the debounce timer and discards any pending updates.
    /// </summary>
    private void StopOutputDebounce()
    {
        _outputDebounceTimer?.Stop();
        _vibrationPending = false;
        _triggersPending = false;
    }

    /// <summary>
    /// Applies any pending debounced updates when the debounce delay elapses.
    /// </summary>
    private void OnOutputDebounceTick(object? sender, EventArgs e)
    {
        _outputDebounceTimer?.Stop();
        if (_vibrationPending)
        {
            _vibrationPending = false;
            ApplyVibration();
        }

        if (_triggersPending)
        {
            _triggersPending = false;
            ApplyTriggerEffects();
        }
    }

    /// <summary>
    /// Sends the current per-motor vibration state to the controller, turning each motor off
    /// when its channel is disabled. No-op when no DualSense device is wrapped.
    /// </summary>
    private void ApplyVibration()
    {
        if (_device is null)
        {
            return;
        }

        byte left = LeftMotorEnabled ? (byte)LeftMotorStrength : (byte)0;
        byte right = RightMotorEnabled ? (byte)RightMotorStrength : (byte)0;
        _device.SetVibration(left, right);
    }

    /// <summary>
    /// Builds the adaptive trigger effect block for the given mode and parameters.
    /// </summary>
    private static TriggerEffectBlock BuildTriggerEffect(TriggerEffectType mode, byte start, byte end, byte force, byte frequency)
    {
        return mode switch
        {
            TriggerEffectType.Resistance => TriggerEffectBuilder.Resistance(start, force),
            TriggerEffectType.Trigger => TriggerEffectBuilder.Trigger(start, end, force),
            TriggerEffectType.Automatic => TriggerEffectBuilder.Automatic(frequency, force, start),
            _ => TriggerEffectBuilder.Off()
        };
    }

    /// <summary>
    /// Sends the current per-trigger effect state to the controller, turning each trigger off
    /// when its mode is Off. No-op when no DualSense device is wrapped.
    /// </summary>
    private void ApplyTriggerEffects()
    {
        if (_device is null)
        {
            return;
        }

        TriggerEffectBlock left = BuildTriggerEffect(
            SelectedLeftTriggerMode, (byte)LeftTriggerStart, (byte)LeftTriggerEnd,
            (byte)LeftTriggerForce, (byte)LeftTriggerFrequency);
        TriggerEffectBlock right = BuildTriggerEffect(
            SelectedRightTriggerMode, (byte)RightTriggerStart, (byte)RightTriggerEnd,
            (byte)RightTriggerForce, (byte)RightTriggerFrequency);
        _device.SetTriggerEffects(left, right);
    }

    /// <summary>
    /// Turns off the vibration motors and adaptive trigger effects on the controller and
    /// resets the output-test state so the bound UI reflects the powered-down outputs.
    /// </summary>
    public void ResetTestOutputs()
    {
        StopOutputDebounce();
        _device?.ResetOutputs();

        SetProperty(ref _leftMotorEnabled, false, nameof(LeftMotorEnabled));
        SetProperty(ref _rightMotorEnabled, false, nameof(RightMotorEnabled));
        SetProperty(ref _leftMotorStrength, 0, nameof(LeftMotorStrength));
        SetProperty(ref _rightMotorStrength, 0, nameof(RightMotorStrength));
        if (SetProperty(ref _leftTriggerModeIndex, 0, nameof(LeftTriggerModeIndex)))
        {
            NotifyLeftTriggerVisibilities();
        }

        if (SetProperty(ref _rightTriggerModeIndex, 0, nameof(RightTriggerModeIndex)))
        {
            NotifyRightTriggerVisibilities();
        }
    }

    /// <summary>
    /// The trigger effect mode currently selected in the left (L2) picker.
    /// </summary>
    private TriggerEffectType SelectedLeftTriggerMode
    {
        get
        {
            return TriggerEffectModes[LeftTriggerModeIndex].Value;
        }
    }

    /// <summary>
    /// The trigger effect mode currently selected in the right (R2) picker.
    /// </summary>
    private TriggerEffectType SelectedRightTriggerMode
    {
        get
        {
            return TriggerEffectModes[RightTriggerModeIndex].Value;
        }
    }

    /// <summary>
    /// Re-raises the left (L2) trigger parameter visibility properties after its mode changes.
    /// </summary>
    private void NotifyLeftTriggerVisibilities()
    {
        OnPropertyChanged(nameof(LeftTriggerParametersVisible));
        OnPropertyChanged(nameof(LeftTriggerStartVisible));
        OnPropertyChanged(nameof(LeftTriggerEndVisible));
        OnPropertyChanged(nameof(LeftTriggerForceVisible));
        OnPropertyChanged(nameof(LeftTriggerFrequencyVisible));
    }

    /// <summary>
    /// Re-raises the right (R2) trigger parameter visibility properties after its mode changes.
    /// </summary>
    private void NotifyRightTriggerVisibilities()
    {
        OnPropertyChanged(nameof(RightTriggerParametersVisible));
        OnPropertyChanged(nameof(RightTriggerStartVisible));
        OnPropertyChanged(nameof(RightTriggerEndVisible));
        OnPropertyChanged(nameof(RightTriggerForceVisible));
        OnPropertyChanged(nameof(RightTriggerFrequencyVisible));
    }

    /// <summary>
    /// Caches the latest button/stick/trigger snapshot and queues a UI-thread update.
    /// </summary>
    private void OnInputStateChanged(object? sender, InputStateEventArgs e)
    {
        _input = e.CurrentState;
        _hasReport = true;
        QueueUpdate();
    }

    /// <summary>
    /// Caches the latest motion snapshot and queues a UI-thread update.
    /// </summary>
    private void OnMotionChanged(object? sender, MotionEventArgs e)
    {
        _motion = e.CurrentState;
        _hasReport = true;
        _motionSamplePending = true;
        QueueUpdate();
    }

    /// <summary>
    /// Caches the latest touchpad snapshot and queues a UI-thread update.
    /// </summary>
    private void OnTouchpadChanged(object? sender, TouchpadEventArgs e)
    {
        _touchpad = e.CurrentState;
        _hasReport = true;
        QueueUpdate();
    }

    /// <summary>
    /// Queues a UI-thread update so the lightbar color properties re-read the
    /// device's latest color.
    /// </summary>
    private void OnLightbarColorChanged(object? sender, EventArgs e) => QueueUpdate();

    /// <summary>
    /// Queues a single coalesced UI-thread update that refreshes every bound property
    /// from the latest cached snapshots.
    /// </summary>
    private void QueueUpdate()
    {
        if (_updateQueued)
        {
            return;
        }

        _updateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _updateQueued = false;

            if (_motionSamplePending)
            {
                _motionSamplePending = false;
                if (_motion is { } motion)
                {
                    _motionSamples.Add(motion);
                    if (_motionSamples.Count > MotionSampleLimit)
                    {
                        _motionSamples.RemoveRange(0, _motionSamples.Count - MotionSampleLimit);
                    }
                }
            }

            foreach (string property in _updateProperties)
            {
                OnPropertyChanged(property);
            }
        });
    }

    /// <summary>
    /// Formats a touch point as "x, y", or "-" when no finger is detected.
    /// </summary>
    private static string TouchPosition(TouchPoint? point) => point is { } p && p.IsActive ? $"{p.X}, {p.Y}" : Unavailable;

    /// <summary>
    /// Localized "Active"/"Inactive" text for a touch point.
    /// </summary>
    private static string ActiveText(bool active) => active
        ? GetText("InputMonitorPage.Common.Active")
        : GetText("InputMonitorPage.Common.Inactive");

    /// <summary>
    /// Gets a localized string.
    /// </summary>
    private static string GetText(string key) => LocalizationService.GetText(key);
}