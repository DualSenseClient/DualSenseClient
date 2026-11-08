using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Controllers.TouchpadMouse;

/// <summary>
/// OS-free gesture state machine that turns DualSense touchpad reports into relative pointer
/// movement through an <see cref="IMouseInjector"/>. The owning engine serializes calls; this
/// type is not thread-safe. Work per report is allocation-free so it can run on the controller
/// read thread.
/// </summary>
public sealed class TouchpadMouseProcessor
{
    /// <summary>Accumulated movement below this many touchpad units is treated as jitter.</summary>
    private const double JitterThresholdUnits = 2.0;

    /// <summary>Weight retained by the previous velocity estimate on each emitted move.</summary>
    private const double VelocityRetention = 0.7;

    /// <summary>Weight applied to the instantaneous velocity on each emitted move.</summary>
    private const double VelocityWeight = 0.3;

    /// <summary>Velocity in touchpad units per millisecond at which acceleration saturates.</summary>
    private const double MaxAccelerationVelocity = 6.0;

    /// <summary>Extra gain added at the maximum velocity.</summary>
    private const double MaxAccelerationGain = 0.5;

    /// <summary>Shortest report interval used for velocity, in milliseconds.</summary>
    private const long MinDeltaMs = 1;

    /// <summary>Longest report interval used for velocity, in milliseconds.</summary>
    private const long MaxDeltaMs = 100;

    /// <summary>Longest touch duration, in milliseconds, that can still count as a tap.</summary>
    private const long TapMaxDurationMs = 200;

    /// <summary>Largest cumulative centroid travel, in touchpad units, that can still count as a tap.</summary>
    private const double TapMaxTravelUnits = 50;

    /// <summary>Longest delay, in milliseconds, between the first and second finger of a two-finger tap.</summary>
    private const long TwoFingerTapWindowMs = 150;

    /// <summary>Cumulative two-finger travel, in touchpad units, that starts scrolling.</summary>
    private const double ScrollStartTravelUnits = 15.0;

    /// <summary>
    /// Wheel units emitted per touchpad unit of travel at a scroll speed of 1.0. The pad is
    /// 1080 units tall and Windows counts 120 units per wheel notch, so the 1200/1080 ratio
    /// emits ten notches over a full-height slide (one notch per 108 units of travel).
    /// </summary>
    private const double ScrollWheelScale = 1200.0 / 1080.0;

    /// <summary>Injects the computed pointer movement.</summary>
    private readonly IMouseInjector _injector;

    /// <summary>Options applied to subsequent reports.</summary>
    private TouchpadMouseOptions _options;

    /// <summary>Whether a previous report has initialized the gesture state.</summary>
    private bool _hasState;

    /// <summary>The previous touchpad sample, used to skip idle reports.</summary>
    private TouchpadState _lastTouchpad;

    /// <summary>The previous touchpad button level.</summary>
    private bool _padPressed;

    /// <summary>Number of active points in the previous sample.</summary>
    private int _activeCount;

    /// <summary>Tracking identifier of the first slot in the previous sample, or 0 when inactive.</summary>
    private byte _trackingId1;

    /// <summary>Tracking identifier of the second slot in the previous sample, or 0 when inactive.</summary>
    private byte _trackingId2;

    /// <summary>
    /// Tracking identifier of the finger that stays anchored while two fingers drag, or 0
    /// when no two-finger touch is active.
    /// </summary>
    private byte _anchorTrackingId;

    /// <summary>
    /// Tracking identifier of the second finger, which moves the pointer while the physical
    /// pad button is held, or 0 when no two-finger touch is active.
    /// </summary>
    private byte _dragTrackingId;

    /// <summary>Horizontal position of the drag finger in the previous sample.</summary>
    private double _lastDragX;

    /// <summary>Vertical position of the drag finger in the previous sample.</summary>
    private double _lastDragY;

    /// <summary>Horizontal centroid of the previous sample.</summary>
    private double _lastCentroidX;

    /// <summary>Vertical centroid of the previous sample.</summary>
    private double _lastCentroidY;

    /// <summary>Reference timestamp for the next velocity calculation, in milliseconds.</summary>
    private long _lastTimestampMs;

    /// <summary>Horizontal movement accumulated since the last emitted move (jitter gate).</summary>
    private double _pendingRawX;

    /// <summary>Vertical movement accumulated since the last emitted move (jitter gate).</summary>
    private double _pendingRawY;

    /// <summary>Sub-unit horizontal pointer movement carried into the next report.</summary>
    private double _moveResidualX;

    /// <summary>Sub-unit vertical pointer movement carried into the next report.</summary>
    private double _moveResidualY;

    /// <summary>Smoothed centroid velocity in touchpad units per millisecond.</summary>
    private double _velocity;

    /// <summary>Timestamp of the report that started the current touch, in milliseconds.</summary>
    private long _touchStartMs;

    /// <summary>Cumulative centroid travel during the current touch, in touchpad units.</summary>
    private double _touchTravel;

    /// <summary>Whether the current touch has ever had two fingers down.</summary>
    private bool _sawTwoFingers;

    /// <summary>Whether the current touch can still resolve as a two-finger tap.</summary>
    private bool _twoFingerTapEligible;

    /// <summary>Whether tap resolution is suppressed for the current touch.</summary>
    private bool _suppressTap;

    /// <summary>Whether the current touch began while the physical pad button was held.</summary>
    private bool _touchStartedWhilePressed;

    /// <summary>Whether a physical click happened during the current touch.</summary>
    private bool _physicalClickDuringTouch;

    /// <summary>Whether the current touch has scrolled and must not also right-click.</summary>
    private bool _scrollActive;

    /// <summary>Sub-unit vertical wheel delta carried into the next report.</summary>
    private double _scrollResidualY;

    /// <summary>Sub-unit horizontal wheel delta carried into the next report.</summary>
    private double _scrollResidualX;

    /// <summary>Whether this processor is holding the left mouse button down.</summary>
    private bool _leftHeld;

    /// <summary>
    /// Creates a processor that injects pointer movement through the given injector.
    /// </summary>
    /// <param name="injector">The injector that receives pointer events.</param>
    public TouchpadMouseProcessor(IMouseInjector injector)
    {
        ArgumentNullException.ThrowIfNull(injector);
        _injector = injector;
        _options = new TouchpadMouseOptions(false, 1.0, 1.0, false);
    }

    /// <summary>
    /// Replaces the options used by subsequent reports.
    /// </summary>
    /// <param name="options">The new options.</param>
    public void UpdateOptions(TouchpadMouseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Clears all gesture state so the next report initializes as if the processor were new.
    /// Releases the left button first when this processor injected the matching down.
    /// </summary>
    public void Reset()
    {
        if (_leftHeld)
        {
            _injector.LeftUp();
            _leftHeld = false;
        }

        _hasState = false;
        _lastTouchpad = default;
        _padPressed = false;
        _activeCount = 0;
        _trackingId1 = 0;
        _trackingId2 = 0;
        _lastCentroidX = 0;
        _lastCentroidY = 0;
        _lastTimestampMs = 0;
        _pendingRawX = 0;
        _pendingRawY = 0;
        _moveResidualX = 0;
        _moveResidualY = 0;
        _velocity = 0;
        EndTouch();
    }

    /// <summary>
    /// Processes one input report and injects any pointer movement it produces.
    /// </summary>
    /// <param name="touchpad">The touchpad sample carried by the report.</param>
    /// <param name="padPressed">Whether the physical touchpad button is held.</param>
    /// <param name="timestampMs">The report timestamp in milliseconds.</param>
    public void Process(TouchpadState touchpad, bool padPressed, long timestampMs)
    {
        if (_hasState && touchpad == _lastTouchpad && padPressed == _padPressed)
        {
            _lastTimestampMs = timestampMs;
            return;
        }

        int activeCount = 0;
        byte trackingId1 = 0;
        byte trackingId2 = 0;
        double sumX = 0;
        double sumY = 0;

        TouchPoint touch1 = touchpad.Touch1;
        if (touch1.IsActive)
        {
            trackingId1 = touch1.TrackingId;
            sumX += touch1.X;
            sumY += touch1.Y;
            activeCount++;
        }

        TouchPoint touch2 = touchpad.Touch2;
        if (touch2.IsActive)
        {
            trackingId2 = touch2.TrackingId;
            sumX += touch2.X;
            sumY += touch2.Y;
            activeCount++;
        }

        double centroidX = activeCount > 0 ? sumX / activeCount : 0;
        double centroidY = activeCount > 0 ? sumY / activeCount : 0;

        if (!_hasState)
        {
            _hasState = true;
            _lastTouchpad = touchpad;
            _padPressed = padPressed;

            // A first report with fingers already down means the processor attached (or was
            // re-enabled) in the middle of a touch, so its lift must not synthesize a click.
            if (activeCount > 0)
            {
                StartTouch(activeCount, timestampMs);
                _suppressTap = true;
            }

            Rebase(activeCount, trackingId1, trackingId2, centroidX, centroidY, timestampMs, touch1, touch2);
            return;
        }

        bool padPressedChanged = padPressed != _padPressed;
        bool fingerCountChanged = activeCount != _activeCount;
        bool trackingIdChanged = trackingId1 != _trackingId1 || trackingId2 != _trackingId2;

        _lastTouchpad = touchpad;
        _padPressed = padPressed;

        if (padPressedChanged)
        {
            ApplyPadButton(padPressed, activeCount);
        }

        if (fingerCountChanged || trackingIdChanged)
        {
            if (_activeCount == 0 && activeCount > 0)
            {
                StartTouch(activeCount, timestampMs);
            }
            else if (_activeCount > 0 && activeCount == 0)
            {
                ResolveTap(timestampMs);
                EndTouch();
            }
            else if (_activeCount == 1 && activeCount == 2)
            {
                BeginSecondFinger(timestampMs);
            }
            else if (activeCount > 0 && activeCount == _activeCount)
            {
                // The same number of fingers with different tracking IDs is a new contact,
                // not a continuation of the touch that began the tap.
                _suppressTap = true;
                _twoFingerTapEligible = false;
            }

            Rebase(activeCount, trackingId1, trackingId2, centroidX, centroidY, timestampMs, touch1, touch2);
            return;
        }

        if (padPressedChanged)
        {
            // Switching between scrolling and dragging changes which reference drives the
            // pointer, so that report only rebases and never emits a jump.
            Rebase(activeCount, trackingId1, trackingId2, centroidX, centroidY, timestampMs, touch1, touch2);
            return;
        }

        if (activeCount == 0)
        {
            return;
        }

        double rawDx;
        double rawDy;
        if (activeCount == 2 && _padPressed)
        {
            GetDragPoint(touch1, touch2, out double dragX, out double dragY);
            rawDx = dragX - _lastDragX;
            rawDy = dragY - _lastDragY;
            _lastDragX = dragX;
            _lastDragY = dragY;
        }
        else
        {
            rawDx = centroidX - _lastCentroidX;
            rawDy = centroidY - _lastCentroidY;
            _lastCentroidX = centroidX;
            _lastCentroidY = centroidY;
        }

        _pendingRawX += rawDx;
        _pendingRawY += rawDy;

        if (Math.Abs(_pendingRawX) < JitterThresholdUnits && Math.Abs(_pendingRawY) < JitterThresholdUnits)
        {
            return;
        }

        double dx = _pendingRawX;
        double dy = _pendingRawY;
        _pendingRawX = 0;
        _pendingRawY = 0;

        // Count travel from the gated delta: resting ±1 jitter cancels inside the gate, so it
        // must not consume the tap budget or latch scrolling without emitting any motion.
        _touchTravel += Math.Sqrt(dx * dx + dy * dy);

        if (activeCount == 2 && !_padPressed && _touchTravel > ScrollStartTravelUnits)
        {
            _scrollActive = true;
        }

        // The jitter gate holds small deltas until they reach the threshold. Advancing the
        // reference timestamp only when a gated move is emitted keeps the elapsed time and
        // the accumulated displacement describing the same interval, which keeps velocity
        // (and therefore acceleration) steady at high report rates.
        long deltaMs = Math.Clamp(timestampMs - _lastTimestampMs, MinDeltaMs, MaxDeltaMs);
        _lastTimestampMs = timestampMs;

        if (activeCount == 2)
        {
            if (_padPressed)
            {
                // With the physical button held, the second finger drives the pointer while
                // the first finger anchors the touch, so the drag does not also scroll.
                ApplyPointer(dx, dy, deltaMs);
            }
            else if (_scrollActive)
            {
                ApplyScroll(dx, dy);
            }

            return;
        }

        if (activeCount != 1)
        {
            return;
        }

        ApplyPointer(dx, dy, deltaMs);
    }

    /// <summary>
    /// Emits pointer movement for the given gated delta, applying the configured speed and
    /// the mild velocity-based acceleration.
    /// </summary>
    /// <param name="dx">Horizontal movement since the previous emitted report.</param>
    /// <param name="dy">Vertical movement since the previous emitted report.</param>
    /// <param name="deltaMs">Time since the previous emitted report, in milliseconds.</param>
    private void ApplyPointer(double dx, double dy, long deltaMs)
    {
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double instantaneousVelocity = distance / deltaMs;
        _velocity = VelocityRetention * _velocity + VelocityWeight * instantaneousVelocity;

        double speedFactor = Math.Clamp(_velocity / MaxAccelerationVelocity, 0, 1);
        double gain = _options.PointerSpeed * (1 + MaxAccelerationGain * speedFactor);

        _moveResidualX += dx * gain;
        _moveResidualY += dy * gain;

        int moveX = (int)Math.Truncate(_moveResidualX);
        int moveY = (int)Math.Truncate(_moveResidualY);
        if (moveX != 0 || moveY != 0)
        {
            _moveResidualX -= moveX;
            _moveResidualY -= moveY;
            _injector.Move(moveX, moveY);
        }
    }

    /// <summary>
    /// Applies a change in the physical touchpad button level. A rising edge injects the left
    /// button down and suppresses tap resolution for the current touch, so its lift cannot add
    /// a second click; movement while the button is held drives the pointer, giving
    /// press-and-drag. A falling edge releases the button only when the matching down was
    /// accepted by the host.
    /// </summary>
    /// <param name="padPressed">Whether the button is pressed in the current report.</param>
    /// <param name="activeCount">Number of active points in the current report.</param>
    private void ApplyPadButton(bool padPressed, int activeCount)
    {
        if (padPressed)
        {
            if (_injector.LeftDown())
            {
                _leftHeld = true;
            }

            // A press suppresses tap resolution for the current touch and cancels any
            // pending two-finger tap; dragging comes from movement while held.
            _twoFingerTapEligible = false;
            _scrollActive = false;
            _scrollResidualY = 0;
            _scrollResidualX = 0;
            if (_activeCount > 0 || activeCount > 0)
            {
                _physicalClickDuringTouch = true;
            }

            return;
        }

        if (_leftHeld)
        {
            _injector.LeftUp();
            _leftHeld = false;
        }
    }

    /// <summary>
    /// Accumulates two-finger movement into whole wheel deltas, carrying the sub-unit remainder
    /// into the next report so slow scrolling is not lost.
    /// </summary>
    /// <param name="dx">Horizontal centroid movement since the previous emitted report.</param>
    /// <param name="dy">Vertical centroid movement since the previous emitted report.</param>
    private void ApplyScroll(double dx, double dy)
    {
        double scale = ScrollWheelScale * _options.ScrollSpeed;

        _scrollResidualY += (_options.NaturalScrolling ? dy : -dy) * scale;
        _scrollResidualX += (_options.NaturalScrolling ? -dx : dx) * scale;

        int wheelDelta = (int)Math.Truncate(_scrollResidualY);
        if (wheelDelta != 0)
        {
            _scrollResidualY -= wheelDelta;
            _injector.Wheel(wheelDelta);
        }

        int horizontalDelta = (int)Math.Truncate(_scrollResidualX);
        if (horizontalDelta != 0)
        {
            _scrollResidualX -= horizontalDelta;
            _injector.HorizontalWheel(horizontalDelta);
        }
    }

    /// <summary>
    /// Starts tracking the tap state of a touch that began in the current report.
    /// </summary>
    /// <param name="activeCount">Number of active points in the report.</param>
    /// <param name="timestampMs">Timestamp of the report in milliseconds.</param>
    private void StartTouch(int activeCount, long timestampMs)
    {
        _touchStartMs = timestampMs;
        _touchTravel = 0;
        _sawTwoFingers = activeCount == 2;
        _twoFingerTapEligible = activeCount == 2 && !_padPressed;
        _suppressTap = false;
        _touchStartedWhilePressed = _padPressed;
        _physicalClickDuringTouch = false;
        _scrollActive = false;
    }

    /// <summary>
    /// Marks the current touch as a two-finger tap candidate when the second finger lands soon
    /// enough after the first and neither has travelled beyond the tap limit.
    /// </summary>
    /// <param name="timestampMs">Timestamp of the report in milliseconds.</param>
    private void BeginSecondFinger(long timestampMs)
    {
        _sawTwoFingers = true;
        _twoFingerTapEligible = timestampMs - _touchStartMs <= TwoFingerTapWindowMs
                                && _touchTravel <= TapMaxTravelUnits
                                && !_scrollActive
                                && !_padPressed;
    }

    /// <summary>
    /// Emits a click when the touch that just ended satisfies every tap limit and has not been
    /// disqualified by movement, timing, a physical press, a scroll, or a tracking change.
    /// </summary>
    /// <param name="timestampMs">Timestamp of the lift report in milliseconds.</param>
    private void ResolveTap(long timestampMs)
    {
        if (_suppressTap
            || _touchStartedWhilePressed
            || _physicalClickDuringTouch
            || _scrollActive
            || _leftHeld)
        {
            return;
        }

        if (timestampMs - _touchStartMs > TapMaxDurationMs || _touchTravel > TapMaxTravelUnits)
        {
            return;
        }

        if (_sawTwoFingers)
        {
            if (_twoFingerTapEligible && _injector.RightDown())
            {
                _injector.RightUp();
            }

            return;
        }

        if (_injector.LeftDown())
        {
            _injector.LeftUp();
        }
    }

    /// <summary>
    /// Clears the tap state of the touch that just ended.
    /// </summary>
    private void EndTouch()
    {
        _touchStartMs = 0;
        _touchTravel = 0;
        _sawTwoFingers = false;
        _twoFingerTapEligible = false;
        _suppressTap = false;
        _touchStartedWhilePressed = false;
        _physicalClickDuringTouch = false;
        _scrollActive = false;
        _scrollResidualY = 0;
        _scrollResidualX = 0;
        _anchorTrackingId = 0;
        _dragTrackingId = 0;
        _lastDragX = 0;
        _lastDragY = 0;
    }

    /// <summary>
    /// Resets the movement reference to the given sample so finger-count and tracking-ID
    /// changes never produce a pointer jump.
    /// </summary>
    /// <param name="activeCount">Number of active points in the new sample.</param>
    /// <param name="trackingId1">Tracking identifier of the first slot, or 0 when inactive.</param>
    /// <param name="trackingId2">Tracking identifier of the second slot, or 0 when inactive.</param>
    /// <param name="centroidX">Horizontal centroid of the new sample.</param>
    /// <param name="centroidY">Vertical centroid of the new sample.</param>
    /// <param name="timestampMs">Timestamp of the new sample in milliseconds.</param>
    /// <param name="touch1">The first touch point of the new sample.</param>
    /// <param name="touch2">The second touch point of the new sample.</param>
    private void Rebase(
        int activeCount,
        byte trackingId1,
        byte trackingId2,
        double centroidX,
        double centroidY,
        long timestampMs,
        TouchPoint touch1,
        TouchPoint touch2)
    {
        _activeCount = activeCount;
        _trackingId1 = trackingId1;
        _trackingId2 = trackingId2;
        _lastCentroidX = centroidX;
        _lastCentroidY = centroidY;
        _lastTimestampMs = timestampMs;
        _velocity = 0;
        _pendingRawX = 0;
        _pendingRawY = 0;
        _scrollResidualY = 0;
        _scrollResidualX = 0;

        if (activeCount == 0)
        {
            _anchorTrackingId = 0;
            _dragTrackingId = 0;
            _lastDragX = 0;
            _lastDragY = 0;
        }
        else if (activeCount == 1)
        {
            _anchorTrackingId = trackingId1 != 0 ? trackingId1 : trackingId2;
            _dragTrackingId = 0;
            _lastDragX = 0;
            _lastDragY = 0;
        }
        else
        {
            // Keep the finger that was already down as the anchor and use the other finger
            // as the drag pointer. A fresh two-finger contact (or a tracking-ID change) has
            // no previous anchor, so the first slot becomes the anchor.
            if (_anchorTrackingId != trackingId1 && _anchorTrackingId != trackingId2)
            {
                _anchorTrackingId = trackingId1;
            }

            _dragTrackingId = _anchorTrackingId == trackingId1 ? trackingId2 : trackingId1;
            GetDragPoint(touch1, touch2, out _lastDragX, out _lastDragY);
        }
    }

    /// <summary>
    /// Resolves the position of the drag finger: the second finger when it is active, or the
    /// remaining finger when only one is down.
    /// </summary>
    /// <param name="touch1">The first touch point of the sample.</param>
    /// <param name="touch2">The second touch point of the sample.</param>
    /// <param name="x">Receives the drag finger's horizontal position.</param>
    /// <param name="y">Receives the drag finger's vertical position.</param>
    private void GetDragPoint(TouchPoint touch1, TouchPoint touch2, out double x, out double y)
    {
        if (touch1.IsActive && touch1.TrackingId == _dragTrackingId)
        {
            x = touch1.X;
            y = touch1.Y;
            return;
        }

        if (touch2.IsActive && touch2.TrackingId == _dragTrackingId)
        {
            x = touch2.X;
            y = touch2.Y;
            return;
        }

        if (touch2.IsActive)
        {
            x = touch2.X;
            y = touch2.Y;
            return;
        }

        x = touch1.X;
        y = touch1.Y;
    }
}