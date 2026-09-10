using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Views;

/// <summary>
/// Borderless popup window used for desktop notifications.
/// Positions itself in the working area of its current screen on open and
/// dismisses when clicked. Slides in from the screen edge matching
/// <see cref="Placement"/> and reverses the animation when closing.
/// Auto-close timing is owned by <see cref="Services.INotificationPopupService"/>.
/// </summary>
public partial class NotificationPopupWindow : Window
{
    /// <summary>
    /// Distance in pixels from the working area edges.
    /// </summary>
    private const int EdgeMargin = 16;

    /// <summary>
    /// How far the popup travels during the slide animation, in pixels.
    /// </summary>
    private const int SlideDistance = 24;

    /// <summary>
    /// Duration of the slide/fade animations.
    /// </summary>
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Guards overlapping animations: a newer animation cancels an older one
    /// (e.g. closing mid-slide-in).
    /// </summary>
    private int _animationVersion;

    /// <summary>
    /// Whether the close animation already ran, guarding against re-entry when
    /// the closing sequence calls <see cref="Window.Close"/> a second time.
    /// </summary>
    private bool _closeConfirmed;

    /// <summary>
    /// Gets or sets where on the screen the popup appears.
    /// Set before showing; applied in <see cref="OnOpened"/>.
    /// </summary>
    public NotificationPosition Placement { get; set; } = NotificationPosition.BottomRight;

    /// <summary>
    /// Gets or sets an optional action invoked when the popup is clicked,
    /// before it closes (e.g. opening a release page).
    /// </summary>
    public Action? ClickAction { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationPopupWindow"/> class.
    /// </summary>
    public NotificationPopupWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        ContentBorder.PointerPressed += OnPointerPressed;
    }

    /// <summary>
    /// Sets the notification title and message text.
    /// Must be called on the UI thread before showing the window.
    /// </summary>
    /// <param name="title">The bold title line.</param>
    /// <param name="message">The descriptive message line.</param>
    public void SetContent(string title, string message)
    {
        TitleBlock.Text = title;
        MessageBlock.Text = message;
    }

    /// <summary>
    /// Positions the window at <see cref="Placement"/> and plays the slide-in
    /// animation. Positioning here (rather than before <see cref="Window.Show"/>)
    /// guarantees screen info is available. The window starts at zero opacity
    /// to avoid flashing at the default position.
    /// </summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        PositionWindow();
        _ = PlayShowAnimationAsync();
    }

    /// <summary>
    /// Plays the reverse slide animation before actually closing, so every
    /// dismissal path (timeout, click, close button) animates out.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;
        try
        {
            await PlayHideAnimationAsync();
        }
        catch
        {
            // Shutting down: close immediately instead of animating.
        }
        finally
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>
    /// Dismisses the popup when the content area is clicked, invoking <see cref="ClickAction"/> first.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ClickAction?.Invoke();
        Close();
    }

    /// <summary>
    /// Dismisses the popup when the close button is clicked.
    /// </summary>
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Moves the window to <see cref="Placement"/> within its current screen's
    /// working area.
    /// </summary>
    private void PositionWindow()
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null && Screens.All.Count > 0)
        {
            screen = Screens.All[0];
        }

        if (screen is null)
        {
            return;
        }

        PixelRect workingArea = screen.WorkingArea;
        int width = (int)Math.Max(Width, 0);
        int height = (int)Math.Max(Height, 0);
        int x = Placement switch
        {
            NotificationPosition.TopLeft or NotificationPosition.MiddleLeft or NotificationPosition.BottomLeft => workingArea.X + EdgeMargin,
            NotificationPosition.TopCenter or NotificationPosition.BottomCenter => workingArea.X + (workingArea.Width - width) / 2,
            _ => workingArea.Right - width - EdgeMargin
        };
        int y = Placement switch
        {
            NotificationPosition.TopLeft or NotificationPosition.TopCenter or NotificationPosition.TopRight => workingArea.Y + EdgeMargin,
            NotificationPosition.MiddleLeft or NotificationPosition.MiddleRight => workingArea.Y + (workingArea.Height - height) / 2,
            _ => workingArea.Bottom - height - EdgeMargin
        };
        Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// Offset the slide animation starts from, pointing away from the screen
    /// edge matching <see cref="Placement"/>: below for bottom positions, above
    /// for top positions, and sideways for middle positions.
    /// </summary>
    private Vector SlideOffset
    {
        get
        {
            return Placement switch
            {
                NotificationPosition.TopLeft or NotificationPosition.TopCenter or NotificationPosition.TopRight => new Vector(0, -SlideDistance),
                NotificationPosition.MiddleLeft => new Vector(-SlideDistance, 0),
                NotificationPosition.MiddleRight => new Vector(SlideDistance, 0),
                _ => new Vector(0, SlideDistance)
            };
        }
    }

    /// <summary>
    /// Slides the popup in from <see cref="SlideOffset"/> while fading in.
    /// Runs on the UI thread.
    /// </summary>
    private async Task PlayShowAnimationAsync()
    {
        int version = ++_animationVersion;
        Vector from = SlideOffset;
        TranslateTransform transform = new TranslateTransform(from.X, from.Y);
        RenderTransform = transform;
        Opacity = 0;

        await Task.WhenAll(
            AnimateOpacity(0, 1, new QuadraticEaseOut(), version),
            AnimateOffset(transform, from, new Vector(0, 0), new QuadraticEaseOut(), version));
    }

    /// <summary>
    /// Slides the popup back out to <see cref="SlideOffset"/> while fading out.
    /// Runs on the UI thread.
    /// </summary>
    private async Task PlayHideAnimationAsync()
    {
        int version = ++_animationVersion;
        TranslateTransform transform = RenderTransform as TranslateTransform ?? new TranslateTransform(0, 0);
        RenderTransform = transform;
        Vector from = new Vector(transform.X, transform.Y);

        await Task.WhenAll(
            AnimateOpacity(Opacity, 0, new QuadraticEaseIn(), version),
            AnimateOffset(transform, from, SlideOffset, new QuadraticEaseIn(), version));
    }

    /// <summary>
    /// Animates the window opacity, aborting when a newer animation starts.
    /// </summary>
    private async Task AnimateOpacity(double from, double to, Easing easing, int version)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < AnimationDuration && version == _animationVersion)
        {
            double progress = Math.Min(1.0, stopwatch.Elapsed.TotalMilliseconds / AnimationDuration.TotalMilliseconds);
            Opacity = from + (to - from) * easing.Ease(progress);
            await Task.Delay(8);
        }

        if (version == _animationVersion)
        {
            Opacity = to;
        }
    }

    /// <summary>
    /// Animates a <see cref="TranslateTransform"/>, aborting when a newer
    /// animation starts.
    /// </summary>
    private async Task AnimateOffset(TranslateTransform transform, Vector from, Vector to, Easing easing, int version)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < AnimationDuration && version == _animationVersion)
        {
            double progress = Math.Min(1.0, stopwatch.Elapsed.TotalMilliseconds / AnimationDuration.TotalMilliseconds);
            double eased = easing.Ease(progress);
            transform.X = from.X + (to.X - from.X) * eased;
            transform.Y = from.Y + (to.Y - from.Y) * eased;
            await Task.Delay(8);
        }

        if (version == _animationVersion)
        {
            transform.X = to.X;
            transform.Y = to.Y;
        }
    }
}