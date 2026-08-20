using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DualSenseClient.GUI.Controls;

/// <summary>
/// Reusable DualSense controller visualization that renders the "no analog stick and
/// triggers" base image for the selected skin with the button, trigger, stick, and touch
/// overlay sprites driven live by an <see cref="IControllerMonitorState"/>.
/// </summary>
/// <remarks>
/// <para>
/// The layout is sprite-based: positions are given as offsets from the base image center in
/// 1467x816 asset pixels, scaled down by <see cref="Scale"/> (0.45 by default). The triggers
/// sit behind the base (visible through its holes), everything else on top.
/// </para>
/// <para>
/// The control owns its visuals and updates them directly on <see cref="INotifyPropertyChanged"/>
/// notifications from <see cref="State"/> instead of using XAML bindings, so it can track
/// high-frequency input updates. The same control can be hosted by any window, including a
/// future always-on-top overlay.
/// </para>
/// </remarks>
public sealed class DualSenseControllerView : Canvas
{
    /// <summary>
    /// Default display scale applied to the 1467x816 asset-space layout.
    /// </summary>
    private const double DefaultScale = 0.45;

    /// <summary>
    /// Asset-space width of the base controller image.
    /// </summary>
    private const double BaseWidth = 1467;

    /// <summary>
    /// Asset-space height of the base controller image.
    /// </summary>
    private const double BaseHeight = 816;

    /// <summary>
    /// Asset-space center of the base controller image.
    /// </summary>
    private const double BaseCenterX = BaseWidth / 2;

    /// <summary>
    /// Asset-space center of the base controller image.
    /// </summary>
    private const double BaseCenterY = BaseHeight / 2;

    /// <summary>
    /// Maximum stick sprite travel from its resting center (asset px).
    /// </summary>
    private const double StickTravel = 21.5;

    /// <summary>
    /// Maximum trigger sprite travel from its resting position (asset px).
    /// </summary>
    private const double TriggerTravel = 18.8;

    /// <summary>
    /// Width of the touchpad surface on the base image (asset px).
    /// </summary>
    private const double TouchSurfaceWidth = 617;

    /// <summary>
    /// Height of the touchpad surface on the base image (asset px).
    /// </summary>
    private const double TouchSurfaceHeight = 317;

    /// <summary>
    /// Asset-space center offset of the touchpad from the base image center.
    /// </summary>
    private const double TouchpadCenterOffsetY = -95.5;

    /// <summary>
    /// Size of the touch indicator dot sprite (asset px).
    /// </summary>
    private const double TouchDotSize = 67;

    /// <summary>
    /// Font size of the touch coordinate labels.
    /// </summary>
    private const double TouchLabelFontSize = 12;

    /// <summary>
    /// Gap between the touch dot and its coordinate label (asset px).
    /// </summary>
    private const double TouchLabelOffsetY = 4;

    /// <summary>
    /// Minimum distance kept between a tag label and the canvas edges (asset px).
    /// </summary>
    private const double TouchLabelMargin = 8;

    /// <summary>
    /// Center offsets of the trigger value tags: to the left of L2 and to the right of R2,
    /// vertically aligned with the triggers.
    /// </summary>
    private const double L2LabelCenterOffsetX = -600;

    private const double L2LabelCenterOffsetY = -330.1;
    private const double R2LabelCenterOffsetX = 626.5;
    private const double R2LabelCenterOffsetY = -333.1;

    /// <summary>
    /// Center offsets of the stick value tags, below the left and right sticks.
    /// </summary>
    private const double L3LabelCenterOffsetX = -240.7;

    private const double L3LabelCenterOffsetY = 360;
    private const double R3LabelCenterOffsetX = 242.0;
    private const double R3LabelCenterOffsetY = 360;

    /// <summary>
    /// Left edge of the touchpad surface on the base image (asset px).
    /// </summary>
    private const double TouchSurfaceLeft = BaseCenterX - TouchSurfaceWidth / 2;

    /// <summary>
    /// Top edge of the touchpad surface on the base image (asset px).
    /// </summary>
    private const double TouchSurfaceTop = BaseCenterY + TouchpadCenterOffsetY - TouchSurfaceHeight / 2;

    /// <summary>
    /// Right edge of the touchpad surface on the base image (asset px).
    /// </summary>
    private const double TouchSurfaceRight = BaseCenterX + TouchSurfaceWidth / 2;

    /// <summary>
    /// Bottom edge of the touchpad surface on the base image (asset px).
    /// </summary>
    private const double TouchSurfaceBottom = BaseCenterY + TouchSurfaceHeight / 2;

    /// <summary>
    /// Asset-space center offset of the microphone LED glow from the base image center
    /// (matches the mute button position).
    /// </summary>
    private const double MicLedCenterOffsetX = 4.0;

    /// <summary>
    /// Asset-space center offset of the microphone LED glow from the base image center
    /// (matches the mute button position).
    /// </summary>
    private const double MicLedCenterOffsetY = 225.9;

    /// <summary>
    /// Size of the microphone LED glow sprite (asset px), covering the mute button.
    /// </summary>
    private const double MicLedWidth = 87;

    /// <summary>
    /// Size of the microphone LED glow sprite (asset px), covering the mute button.
    /// </summary>
    private const double MicLedHeight = 28;

    /// <summary>
    /// Lowest opacity reached while the microphone LED is pulsing.
    /// </summary>
    private const double MicLedPulseMinOpacity = 0.15;

    /// <summary>
    /// Full pulse period of the microphone LED breathing animation.
    /// </summary>
    private static readonly TimeSpan MicLedPulsePeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Animation step interval of the microphone LED pulse.
    /// </summary>
    private static readonly TimeSpan MicLedPulseTick = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Mute LED mode meaning "steady on" (see the DualSense mute LED protocol).
    /// </summary>
    private const int MicLedModeOn = 1;

    /// <summary>
    /// Mute LED mode meaning "pulsing" (see the DualSense mute LED protocol).
    /// </summary>
    private const int MicLedModePulse = 2;

    /// <summary>
    /// The controller state displayed by this view.
    /// </summary>
    public static readonly StyledProperty<IControllerMonitorState?> StateProperty =
        AvaloniaProperty.Register<DualSenseControllerView, IControllerMonitorState?>(nameof(State));

    /// <summary>
    /// The skin name whose base image and sprite set are rendered.
    /// </summary>
    public static readonly StyledProperty<string> SkinNameProperty =
        AvaloniaProperty.Register<DualSenseControllerView, string>(nameof(SkinName), string.Empty);

    /// <summary>
    /// Display scale applied to the 1467x816 asset-space layout.
    /// </summary>
    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<DualSenseControllerView, double>(nameof(Scale), DefaultScale);

    /// <summary>
    /// Whether stick, trigger, and touch movement is shown on the visualization.
    /// </summary>
    public static readonly StyledProperty<bool> ShowMovementProperty =
        AvaloniaProperty.Register<DualSenseControllerView, bool>(nameof(ShowMovement), true);

    /// <summary>
    /// Whether pressed button states are shown on the visualization.
    /// </summary>
    public static readonly StyledProperty<bool> ShowButtonPressesProperty =
        AvaloniaProperty.Register<DualSenseControllerView, bool>(nameof(ShowButtonPresses), true);

    /// <summary>
    /// Whether the lightbar color, player LEDs, and microphone LED are shown.
    /// </summary>
    public static readonly StyledProperty<bool> ShowLightbarLedsProperty =
        AvaloniaProperty.Register<DualSenseControllerView, bool>(nameof(ShowLightbarLeds), true);

    /// <summary>
    /// Whether the value/coordinate tag labels are shown.
    /// </summary>
    public static readonly StyledProperty<bool> ShowStatsProperty =
        AvaloniaProperty.Register<DualSenseControllerView, bool>(nameof(ShowStats), true);

    /// <summary>
    /// The controller state displayed by this view.
    /// </summary>
    public IControllerMonitorState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// The skin name whose base image and sprite set are rendered.
    /// </summary>
    public string SkinName
    {
        get => GetValue(SkinNameProperty);
        set => SetValue(SkinNameProperty, value);
    }

    /// <summary>
    /// Display scale applied to the 1467x816 asset-space layout.
    /// </summary>
    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    /// <summary>
    /// Whether stick, trigger, and touch movement is shown on the visualization.
    /// </summary>
    public bool ShowMovement
    {
        get => GetValue(ShowMovementProperty);
        set => SetValue(ShowMovementProperty, value);
    }

    /// <summary>
    /// Whether pressed button states are shown on the visualization.
    /// </summary>
    public bool ShowButtonPresses
    {
        get => GetValue(ShowButtonPressesProperty);
        set => SetValue(ShowButtonPressesProperty, value);
    }

    /// <summary>
    /// Whether the lightbar color, player LEDs, and microphone LED are shown.
    /// </summary>
    public bool ShowLightbarLeds
    {
        get => GetValue(ShowLightbarLedsProperty);
        set => SetValue(ShowLightbarLedsProperty, value);
    }

    /// <summary>
    /// Whether the value/coordinate tag labels are shown.
    /// </summary>
    public bool ShowStats
    {
        get => GetValue(ShowStatsProperty);
        set => SetValue(ShowStatsProperty, value);
    }

    /// <summary>
    /// The state currently subscribed to, so the previous subscription can be released.
    /// </summary>
    private IControllerMonitorState? _state;

    /// <summary>
    /// The scale the visuals were last built with.
    /// </summary>
    private double _scale = DefaultScale;

    /// <summary>
    /// The service loading the monitor base images and overlay sprites.
    /// </summary>
    private readonly ControllerIllustrationService _illustrations;

    /// <summary>
    /// The left trigger sprite (slides down while the trigger is pulled).
    /// </summary>
    private Image? _leftTrigger;

    /// <summary>
    /// The base controller image, re-tinted live when the lightbar color or player LED
    /// layout changes.
    /// </summary>
    private Image? _baseImage;

    /// <summary>
    /// The lightbar color and player LED layout the base image was last tinted with.
    /// </summary>
    private (byte Red, byte Green, byte Blue, byte Leds)? _baseState;

    /// <summary>
    /// The microphone LED glow sprite, animated by opacity for the steady and pulse modes.
    /// </summary>
    private Image? _micLed;

    /// <summary>
    /// Drives the microphone LED pulse animation.
    /// </summary>
    private DispatcherTimer? _micLedPulseTimer;

    /// <summary>
    /// Start time of the current pulse animation phase.
    /// </summary>
    private DateTime _micLedPulseStart;

    /// <summary>
    /// The last mute LED mode applied to the microphone LED sprite.
    /// </summary>
    private int _micLedMode = -1;

    /// <summary>
    /// The left trigger pressed overlay.
    /// </summary>
    private Image? _leftTriggerActive;

    /// <summary>
    /// The right trigger sprite (slides down while the trigger is pulled).
    /// </summary>
    private Image? _rightTrigger;

    /// <summary>
    /// The right trigger pressed overlay.
    /// </summary>
    private Image? _rightTriggerActive;

    /// <summary>
    /// The left stick sprite (moves with the stick).
    /// </summary>
    private Image? _leftStick;

    /// <summary>
    /// The right stick sprite (moves with the stick).
    /// </summary>
    private Image? _rightStick;

    /// <summary>
    /// Touch point 1 indicator sprite.
    /// </summary>
    private Image? _touch1;

    /// <summary>
    /// Touch point 2 indicator sprite.
    /// </summary>
    private Image? _touch2;

    /// <summary>
    /// Coordinate label shown under touch point 1 while it is active.
    /// </summary>
    private TextBlock? _touch1Label;

    /// <summary>
    /// Coordinate label shown under touch point 2 while it is active.
    /// </summary>
    private TextBlock? _touch2Label;

    /// <summary>
    /// Value tag shown below the left trigger.
    /// </summary>
    private TextBlock? _l2Label;

    /// <summary>
    /// Value tag shown below the right trigger.
    /// </summary>
    private TextBlock? _r2Label;

    /// <summary>
    /// Value tag shown below the left stick.
    /// </summary>
    private TextBlock? _l3Label;

    /// <summary>
    /// Value tag shown below the right stick.
    /// </summary>
    private TextBlock? _r3Label;

    /// <summary>
    /// All static overlay sprites (pressed-driven visibility).
    /// </summary>
    private readonly List<OverlaySprite> _overlays = new();

    /// <summary>
    /// Creates a controller view and resolves the illustration service from DI.
    /// </summary>
    public DualSenseControllerView()
    {
        _illustrations = App.Services.GetRequiredService<ControllerIllustrationService>();
        Rebuild();
    }

    static DualSenseControllerView()
    {
        StateProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.Rebuild());
        SkinNameProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.Rebuild());
        ScaleProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.Rebuild());
        ShowMovementProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.OnDisplayOptionsChanged());
        ShowButtonPressesProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.OnDisplayOptionsChanged());
        ShowLightbarLedsProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.OnDisplayOptionsChanged());
        ShowStatsProperty.Changed.AddClassHandler<DualSenseControllerView>((view, _) => view.OnDisplayOptionsChanged());
    }

    /// <summary>
    /// Releases the state subscription when the view leaves the visual tree.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachState();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Rebuilds the full sprite tree for the current skin, state, and scale.
    /// </summary>
    private void Rebuild()
    {
        DetachState();
        Children.Clear();
        _overlays.Clear();
        _baseImage = null;
        _baseState = null;
        StopMicLedPulse();
        _micLed = null;
        _micLedMode = -1;
        _leftTrigger = _leftTriggerActive = _rightTrigger = _rightTriggerActive = null;
        _leftStick = _rightStick = _touch1 = _touch2 = null;

        IControllerMonitorState? state = State;
        string skin = SkinName ?? string.Empty;
        double scale = _scale = Scale;

        Width = BaseWidth * scale;
        Height = BaseHeight * scale;

        if (state is null)
        {
            return;
        }

        AddSprite(skin, "L2", -462.6, -330.1, 200, 152, out _leftTrigger);
        AddSprite(skin, "L2-Active", -462.6, -330.1, 200, 152, out _leftTriggerActive);
        AddSprite(skin, "R2", 464.0, -333.1, 202, 149, out _rightTrigger);
        AddSprite(skin, "R2-Active", 464.0, -333.1, 202, 149, out _rightTriggerActive);

        (byte red, byte green, byte blue, byte leds) color = BaseState(state);
        _baseState = color;
        _baseImage = AddImage(_illustrations.GetMonitorBase(skin, color.red, color.green, color.blue, color.leds), 0, 0, BaseWidth, BaseHeight);

        AddOverlay(skin, "L1-Active", -471.9, -260.8, 221, 130, s => s.L1);
        AddOverlay(skin, "R1-Active", 471.9, -262.2, 221, 130, s => s.R1);
        AddOverlay(skin, "Triangle", 476.0, -71.3, 112, 105, s => s.Triangle);
        AddOverlay(skin, "Square", 363.0, 22.9, 110, 99, s => s.Square);
        AddOverlay(skin, "Cross", 470.6, 112.9, 112, 95, s => s.Cross);
        AddOverlay(skin, "Circle", 583.5, 18.8, 110, 105, s => s.Circle);
        AddOverlay(skin, "D-PAD_Up", -473.3, -37.6, 93, 108, s => s.DPadUp);
        AddOverlay(skin, "D-PAD_Down", -473.3, 80.7, 93, 104, s => s.DPadDown);
        AddOverlay(skin, "D-PAD_Left", -545.9, 21.5, 114, 87, s => s.DPadLeft);
        AddOverlay(skin, "D-PAD_Right", -400.7, 21.5, 114, 87, s => s.DPadRight);
        AddOverlay(skin, "Home_Button", 2.7, 166.7, 97, 54, s => s.PS);
        _micLed = AddImage(_illustrations.GetMicLedSprite(), CenteredX(MicLedCenterOffsetX, MicLedWidth, 0, _scale), CenteredY(MicLedCenterOffsetY, MicLedHeight, 0, _scale), MicLedWidth, MicLedHeight);
        AddOverlay(skin, "Mute_Button", 4.0, 225.9, 75, 16, s => s.Mute);
        AddOverlay(skin, "Option_Button", 359.0, -121.0, 55, 74, s => s.Options);
        AddOverlay(skin, "Create_Button", -359.0, -121.0, 52, 71, s => s.Create);
        AddOverlay(skin, "AnalogStick_Click", -238.0, 195.0, 196, 171, s => s.L3);
        AddOverlay(skin, "AnalogStick_Click", 242.0, 195.0, 196, 171, s => s.R3);

        AddSprite(skin, "LeftAnalogStick", -240.7, 227.2, 173, 147, out _leftStick);
        AddSprite(skin, "RightAnalogStick", 242.0, 228.6, 175, 148, out _rightStick);

        AddOverlay(skin, "Touchpad-Click", 0, TouchpadCenterOffsetY, 617, 317, s => s.TouchPad);
        AddSprite(skin, "Touchpad_Touch", 0, 0, TouchDotSize, TouchDotSize, out _touch1);
        AddSprite(skin, "Touchpad_Touch", 0, 0, TouchDotSize, TouchDotSize, out _touch2);
        _touch1Label = CreateTagLabel();
        _touch2Label = CreateTagLabel();
        _l2Label = CreateTagLabel();
        _r2Label = CreateTagLabel();
        _l3Label = CreateTagLabel();
        _r3Label = CreateTagLabel();
        _l2Label.IsVisible = true;
        _r2Label.IsVisible = true;
        _l3Label.IsVisible = true;
        _r3Label.IsVisible = true;

        _state = state;
        state.PropertyChanged += OnStatePropertyChanged;
        UpdatePositions();
    }

    /// <summary>
    /// Repositions and re-validates every sprite from the state's latest values.
    /// </summary>
    private void UpdatePositions()
    {
        IControllerMonitorState? state = _state;
        double scale = _scale;

        if (state is null)
        {
            return;
        }

        if (_baseImage is { } baseImage)
        {
            (byte red, byte green, byte blue, byte leds) color = ShowLightbarLeds ? BaseState(state) : ((byte)0, (byte)0, (byte)0, (byte)0);
            if (_baseState is not { } current || current != color)
            {
                _baseState = color;
                baseImage.Source = _illustrations.GetMonitorBase(SkinName, color.red, color.green, color.blue, color.leds);
            }
        }

        if (_micLed is { } micLed)
        {
            if (ShowLightbarLeds && state.MuteLedMode != _micLedMode)
            {
                _micLedMode = state.MuteLedMode;
                if (state.MuteLedMode == MicLedModeOn)
                {
                    StopMicLedPulse();
                    micLed.Opacity = 1;
                }
                else if (state.MuteLedMode == MicLedModePulse)
                {
                    StartMicLedPulse(micLed);
                }
                else
                {
                    StopMicLedPulse();
                    micLed.Opacity = 0;
                }
            }
            else if (!ShowLightbarLeds && _micLedMode != -1)
            {
                StopMicLedPulse();
                micLed.Opacity = 0;
                _micLedMode = -1;
            }
        }

        foreach (OverlaySprite overlay in _overlays)
        {
            overlay.Image.IsVisible = ShowButtonPresses && overlay.Pressed(state);
        }

        if (_leftStick is { } leftStick && ShowMovement)
        {
            Canvas.SetLeft(leftStick, CenteredX(-240.7, 173, StickOffset(state.LeftStickX), scale));
            Canvas.SetTop(leftStick, CenteredY(227.2, 147, StickOffset(state.LeftStickY), scale));
        }

        if (_rightStick is { } rightStick && ShowMovement)
        {
            Canvas.SetLeft(rightStick, CenteredX(242.0, 175, StickOffset(state.RightStickX), scale));
            Canvas.SetTop(rightStick, CenteredY(228.6, 148, StickOffset(state.RightStickY), scale));
        }

        if (_leftTrigger is { } leftTrigger && ShowMovement)
        {
            double top = CenteredY(-330.1, 152, 0, scale) + (state.L2 / 255.0) * TriggerTravel * scale;
            Canvas.SetTop(leftTrigger, top);
            if (_leftTriggerActive is { } leftTriggerActive)
            {
                Canvas.SetTop(leftTriggerActive, top);
                leftTriggerActive.IsVisible = ShowButtonPresses && state.L2Click;
            }
        }

        if (_rightTrigger is { } rightTrigger && ShowMovement)
        {
            double top = CenteredY(-333.1, 149, 0, scale) + (state.R2 / 255.0) * TriggerTravel * scale;
            Canvas.SetTop(rightTrigger, top);
            if (_rightTriggerActive is { } rightTriggerActive)
            {
                Canvas.SetTop(rightTriggerActive, top);
                rightTriggerActive.IsVisible = ShowButtonPresses && state.R2Click;
            }
        }

        if (_l2Label is { } l2Label)
        {
            l2Label.IsVisible = ShowStats;
            if (ShowStats)
            {
                l2Label.Text = $"L2: {state.L2}";
                PositionTagLabel(l2Label, L2LabelCenterOffsetX, L2LabelCenterOffsetY, scale);
            }
        }

        if (_r2Label is { } r2Label)
        {
            r2Label.IsVisible = ShowStats;
            if (ShowStats)
            {
                r2Label.Text = $"R2: {state.R2}";
                PositionTagLabel(r2Label, R2LabelCenterOffsetX, R2LabelCenterOffsetY, scale);
            }
        }

        if (_l3Label is { } l3Label)
        {
            l3Label.IsVisible = ShowStats;
            if (ShowStats)
            {
                l3Label.Text = $"L3  X: {state.LeftStickX}  Y: {state.LeftStickY}";
                PositionTagLabel(l3Label, L3LabelCenterOffsetX, L3LabelCenterOffsetY, scale);
            }
        }

        if (_r3Label is { } r3Label)
        {
            r3Label.IsVisible = ShowStats;
            if (ShowStats)
            {
                r3Label.Text = $"R3  X: {state.RightStickX}  Y: {state.RightStickY}";
                PositionTagLabel(r3Label, R3LabelCenterOffsetX, R3LabelCenterOffsetY, scale);
            }
        }

        PositionTouchDot(_touch1, _touch1Label, 1, ShowMovement && state.Touch1Active, state.Touch1X, state.Touch1Y, scale);
        PositionTouchDot(_touch2, _touch2Label, 2, ShowMovement && state.Touch2Active, state.Touch2X, state.Touch2Y, scale);
    }

    /// <summary>
    /// Re-applies the display options (movement, button presses, lights, stats) to the
    /// current visuals without rebuilding them.
    /// </summary>
    private void OnDisplayOptionsChanged()
    {
        if (_state is not null)
        {
            UpdatePositions();
        }
    }

    /// <summary>
    /// Creates a small tag label (white text on a semi-transparent dark pill).
    /// </summary>
    private TextBlock CreateTagLabel()
    {
        var label = new TextBlock
        {
            FontSize = TouchLabelFontSize,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            Padding = new Thickness(4, 1),
            IsHitTestVisible = false,
            IsVisible = false
        };
        Children.Add(label);
        return label;
    }

    /// <summary>
    /// Centers a tag label on the given asset-space offset, keeping it inside the canvas.
    /// </summary>
    private void PositionTagLabel(TextBlock label, double centerOffsetX, double centerOffsetY, double scale)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = label.DesiredSize.Width;
        double height = label.DesiredSize.Height;
        double left = Math.Clamp((BaseCenterX + centerOffsetX) * scale - width / 2, 0, Math.Max(0, (BaseWidth - TouchLabelMargin) * scale - width));
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, (BaseCenterY + centerOffsetY) * scale - height / 2);
    }

    /// <summary>
    /// Positions a touch indicator sprite at the mapped touch coordinates, clamped inside
    /// the touchpad surface, and shows its coordinate label just below the dot.
    /// </summary>
    private void PositionTouchDot(Image? dot, TextBlock? label, int index, bool active, int x, int y, double scale)
    {
        if (dot is null)
        {
            return;
        }

        dot.IsVisible = active;
        if (!active)
        {
            if (label is not null)
            {
                label.IsVisible = false;
            }

            return;
        }

        double half = TouchDotSize / 2;
        double centerX = Math.Clamp(TouchSurfaceLeft + (x / 1919.0) * TouchSurfaceWidth, TouchSurfaceLeft + half, TouchSurfaceRight - half);
        double centerY = Math.Clamp(TouchSurfaceTop + (y / 1079.0) * TouchSurfaceHeight, TouchSurfaceTop + half, TouchSurfaceBottom - half);
        Canvas.SetLeft(dot, (centerX - half) * scale);
        Canvas.SetTop(dot, (centerY - half) * scale);

        if (label is null)
        {
            return;
        }

        label.IsVisible = ShowStats;
        if (!ShowStats)
        {
            return;
        }

        label.Text = $"Touch {index}: {x}, {y}";
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelWidth = label.DesiredSize.Width;
        double left = Math.Clamp(centerX * scale - labelWidth / 2, 0, Math.Max(0, (BaseWidth - TouchLabelMargin) * scale - labelWidth));
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, (centerY + half + TouchLabelOffsetY) * scale);
    }

    /// <summary>
    /// Maps an 8-bit axis value to a signed travel offset.
    /// </summary>
    private static double StickOffset(int value) => ((value - 128) / 128.0) * StickTravel;

    /// <summary>
    /// Reads the state's lightbar RGB as byte channels, clamped to the valid range.
    /// </summary>
    private static (byte Red, byte Green, byte Blue) LightbarColor(IControllerMonitorState state)
        => ((byte)Math.Clamp(state.LightbarRed, 0, 255),
            (byte)Math.Clamp(state.LightbarGreen, 0, 255),
            (byte)Math.Clamp(state.LightbarBlue, 0, 255));

    /// <summary>
    /// Reads the state's lightbar RGB and player LED layout as bytes, clamped to the valid
    /// ranges.
    /// </summary>
    private static (byte Red, byte Green, byte Blue, byte Leds) BaseState(IControllerMonitorState state)
        => (LightbarColor(state).Red, LightbarColor(state).Green, LightbarColor(state).Blue,
            (byte)Math.Clamp(state.PlayerLeds, 0, 31));

    /// <summary>
    /// Left position of a sprite centered at the given asset-space offset, plus a travel offset.
    /// </summary>
    private double CenteredX(double offsetX, double width, double travelX, double scale)
        => (BaseCenterX + offsetX - width / 2 + travelX) * scale;

    /// <summary>
    /// Top position of a sprite centered at the given asset-space offset, plus a travel offset.
    /// </summary>
    private double CenteredY(double offsetY, double height, double travelY, double scale)
        => (BaseCenterY + offsetY - height / 2 + travelY) * scale;

    /// <summary>
    /// Adds a full-size sprite image to the canvas and exposes it through <paramref name="image"/>.
    /// </summary>
    private void AddSprite(string skin, string name, double offsetX, double offsetY, double width, double height, out Image? image)
    {
        image = AddImage(_illustrations.GetSprite(skin, name), CenteredX(offsetX, width, 0, _scale), CenteredY(offsetY, height, 0, _scale), width, height);
    }

    /// <summary>
    /// Adds a pressed-driven overlay sprite to the canvas and tracks it for updates.
    /// </summary>
    private void AddOverlay(string skin, string name, double offsetX, double offsetY, double width, double height, Func<IControllerMonitorState, bool> pressed)
    {
        Image image = AddImage(_illustrations.GetSprite(skin, name), CenteredX(offsetX, width, 0, _scale), CenteredY(offsetY, height, 0, _scale), width, height);
        _overlays.Add(new OverlaySprite(image, pressed));
        image.IsVisible = false;
    }

    /// <summary>
    /// Creates an image child at the given scaled position and size.
    /// </summary>
    private Image AddImage(Bitmap? bitmap, double left, double top, double width, double height)
    {
        var image = new Image
        {
            Source = bitmap,
            Width = width * _scale,
            Height = height * _scale,
            Stretch = Avalonia.Media.Stretch.Fill
        };
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        Children.Add(image);
        return image;
    }

    /// <summary>
    /// Unsubscribes from the current state's notifications.
    /// </summary>
    private void DetachState()
    {
        if (_state is not null)
        {
            _state.PropertyChanged -= OnStatePropertyChanged;
            _state = null;
        }
    }

    /// <summary>
    /// Starts the breathing animation of the microphone LED, resetting its phase.
    /// </summary>
    private void StartMicLedPulse(Image micLed)
    {
        if (_micLedPulseTimer is null)
        {
            _micLedPulseTimer = new DispatcherTimer { Interval = MicLedPulseTick };
            _micLedPulseTimer.Tick += OnMicLedPulseTick;
        }

        _micLedPulseStart = DateTime.UtcNow;
        _micLedPulseTimer.Start();
    }

    /// <summary>
    /// Steps the microphone LED pulse opacity toward the current animation phase.
    /// </summary>
    private void OnMicLedPulseTick(object? sender, EventArgs e)
    {
        if (_micLed is null)
        {
            return;
        }

        double elapsed = (DateTime.UtcNow - _micLedPulseStart).TotalSeconds;
        double phase = 2 * Math.PI * elapsed / MicLedPulsePeriod.TotalSeconds;
        _micLed.Opacity = MicLedPulseMinOpacity + (1 - MicLedPulseMinOpacity) * (0.5 + 0.5 * Math.Sin(phase));
    }

    /// <summary>
    /// Stops the microphone LED pulse animation.
    /// </summary>
    private void StopMicLedPulse()
    {
        if (_micLedPulseTimer is { } timer)
        {
            timer.Stop();
        }
    }

    /// <summary>
    /// Repositions the dynamic sprites whenever the live state reports an update.
    /// </summary>
    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdatePositions();
        }
        else
        {
            Dispatcher.UIThread.Post(UpdatePositions);
        }
    }

    /// <summary>
    /// A static overlay sprite paired with the pressed predicate driving its visibility.
    /// </summary>
    private sealed class OverlaySprite(Image image, Func<IControllerMonitorState, bool> pressed)
    {
        public Image Image { get; } = image;

        public Func<IControllerMonitorState, bool> Pressed { get; } = pressed;
    }
}
