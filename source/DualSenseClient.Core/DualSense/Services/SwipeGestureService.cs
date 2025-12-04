using System.Diagnostics;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Events;
using DualSenseClient.Core.DualSense.Reports;
using DualSenseClient.Core.Logging;

namespace DualSenseClient.Core.DualSense.Services;

/// <summary>
/// Handles swipe gesture recognition on the DualSense touchpad
/// </summary>
public class SwipeGestureService : IDisposable
{
    private DualSenseController? _controller;
    private readonly TouchPoint _initialTouch = new TouchPoint();
    private readonly TouchPoint _currentTouch = new TouchPoint();
    private readonly Stopwatch _swipeTimer = new Stopwatch();
    private bool _isTrackingSwipe = false;
    private bool _isSwipeProcessed = false;

    // Swipe detection parameters - configurable values
    private int _minSwipeDistance = 300; // Minimum distance in touchpad units out of 1920x1080 (rounded)
    private int _maxSwipeDuration = 1000; // Maximum duration in milliseconds
    private double _minSwipeRatio = 1.5; // Minimum ratio between primary and secondary axis to qualify as swipe

    public int MinSwipeDistance
    {
        get => _minSwipeDistance;
        set => _minSwipeDistance = Math.Max(50, value); // Minimum 50 units
    }

    public int MaxSwipeDuration
    {
        get => _maxSwipeDuration;
        set => _maxSwipeDuration = Math.Max(100, value); // Minimum 100ms
    }

    public double MinSwipeRatio
    {
        get => _minSwipeRatio;
        set => _minSwipeRatio = Math.Max(1.0, value); // Minimum ratio of 1.0
    }

    public event EventHandler<SwipeEventArgs>? SwipeDetected;

    public void Initialize(DualSenseController controller)
    {
        _controller = controller;
        
        // Subscribe to touchpad events
        _controller.TouchpadChanged += OnTouchpadChanged;
        
        Logger.Info<SwipeGestureService>($"Swipe gesture service initialized for controller {_controller.Device.DevicePath}");
    }

    private void OnTouchpadChanged(object? sender, TouchpadEventArgs e)
    {
        if (_controller == null)
        {
            return;
        }

        // Handle single touch only for swipe detection
        if (!e.CurrentState.Touch1.IsActive && _isTrackingSwipe)
        {
            // Touch ended - evaluate if it was a swipe
            ProcessSwipeEnd();
        }
        else if (e.CurrentState.Touch1.IsActive && !e.CurrentState.Touch2.IsActive && !_isTrackingSwipe)
        {
            // New single touch - start tracking for swipe
            StartSwipeTracking(e.CurrentState.Touch1);
        }
        else if (_isTrackingSwipe && e.CurrentState.Touch1.IsActive)
        {
            // Continue tracking the ongoing touch
            _currentTouch.X = e.CurrentState.Touch1.X;
            _currentTouch.Y = e.CurrentState.Touch1.Y;
        }
    }

    private void StartSwipeTracking(TouchPoint touchPoint)
    {
        _initialTouch.X = touchPoint.X;
        _initialTouch.Y = touchPoint.Y;
        _initialTouch.IsActive = true;
        _currentTouch.X = touchPoint.X;
        _currentTouch.Y = touchPoint.Y;
        _currentTouch.IsActive = true;
        
        _isTrackingSwipe = true;
        _isSwipeProcessed = false;
        _swipeTimer.Restart();
    }

    private void ProcessSwipeEnd()
    {
        if (!_isTrackingSwipe || _isSwipeProcessed)
        {
            _isTrackingSwipe = false;
            return;
        }

        // Check if enough time has passed
        if (_swipeTimer.ElapsedMilliseconds > _maxSwipeDuration)
        {
            _isTrackingSwipe = false;
            return;
        }

        // Calculate swipe distance
        int deltaX = _currentTouch.X - _initialTouch.X;
        int deltaY = _currentTouch.Y - _initialTouch.Y;
        int absDeltaX = Math.Abs(deltaX);
        int absDeltaY = Math.Abs(deltaY);

        // Check if the swipe distance is significant enough
        if (absDeltaX < _minSwipeDistance && absDeltaY < _minSwipeDistance)
        {
            _isTrackingSwipe = false;
            return;
        }

        // Determine swipe direction based on greater movement axis
        Actions.SwipeDirection? direction = null;

        if (absDeltaX > absDeltaY && absDeltaX > _minSwipeDistance)
        {
            // Horizontal swipe - check if it meets the ratio requirement
            if (absDeltaX >= _minSwipeRatio * absDeltaY)
            {
                direction = deltaX > 0 ? Actions.SwipeDirection.Right : Actions.SwipeDirection.Left;
            }
        }
        else if (absDeltaY > absDeltaX && absDeltaY > _minSwipeDistance)
        {
            // Vertical swipe - check if it meets the ratio requirement
            if (absDeltaY >= _minSwipeRatio * absDeltaX)
            {
                direction = deltaY > 0 ? Actions.SwipeDirection.Down : Actions.SwipeDirection.Up;
            }
        }

        if (direction.HasValue)
        {
            // Trigger the swipe event
            if (_controller != null)
            {
                SwipeDetected?.Invoke(this, new SwipeEventArgs(_controller, direction.Value));
            }
            _isSwipeProcessed = true;
        }

        _isTrackingSwipe = false;
    }

    public void Dispose()
    {
        if (_controller != null)
        {
            _controller.TouchpadChanged -= OnTouchpadChanged;
        }

        _swipeTimer?.Stop();
        Logger.Info<SwipeGestureService>("Swipe gesture service disposed");
    }

    public void ConfigureSwipeParameters(int minSwipeDistance, int maxSwipeDuration, double minSwipeRatio)
    {
        MinSwipeDistance = minSwipeDistance;
        MaxSwipeDuration = maxSwipeDuration;
        MinSwipeRatio = minSwipeRatio;

        Logger.Debug<SwipeGestureService>($"Swipe parameters configured: Distance={MinSwipeDistance}, Duration={MaxSwipeDuration}ms, Ratio={MinSwipeRatio:F2}");
    }
}

