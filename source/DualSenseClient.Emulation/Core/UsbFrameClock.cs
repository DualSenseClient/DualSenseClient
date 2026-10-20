namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Drives the frame cadence of one USB endpoint: invokes a frame handler once per frame duration
/// through the configured <see cref="TimeProvider"/>. One timer tick is one full-speed USB frame.
/// Ticks never overlap and never queue - while a slow handler is still running, a due tick is
/// skipped - and an exception thrown by the handler is contained so that the next frame still
/// fires. A clock drives one endpoint: start it when the endpoint starts streaming and stop it
/// when the endpoint goes away.
/// </summary>
public sealed class UsbFrameClock : IDisposable
{
    private readonly TimeProvider _timeProvider;

    private readonly TimeSpan _frameDuration;

    private readonly object _gate = new object();

    private ITimer? _timer;

    private Action? _onFrame;

    private bool _isRunning;

    private bool _tickActive;

    /// <summary>
    /// Initializes a clock that fires at <paramref name="frameDuration"/> once <see cref="Start"/>
    /// is called.
    /// </summary>
    /// <param name="timeProvider">Provider that schedules the ticks: system time in production, a manual provider in tests.</param>
    /// <param name="frameDuration">Duration of one USB frame; must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameDuration"/> is not positive.</exception>
    public UsbFrameClock(TimeProvider timeProvider, TimeSpan frameDuration)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (frameDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameDuration), frameDuration, "The frame duration must be positive.");
        }

        _timeProvider = timeProvider;
        _frameDuration = frameDuration;
    }

    /// <summary>Whether the clock is ticking.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _isRunning;
            }
        }
    }

    /// <summary>
    /// Starts ticking: <paramref name="onFrame"/> is invoked once per frame duration, the first
    /// time one frame from now, on a timer thread of the time provider (or on the caller's thread
    /// when a manual provider drains the clock).
    /// </summary>
    /// <param name="onFrame">Handler invoked once per USB frame.</param>
    /// <exception cref="ArgumentNullException"><paramref name="onFrame"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The clock is already running.</exception>
    public void Start(Action onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        lock (_gate)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("The frame clock is already running.");
            }

            _timer = _timeProvider.CreateTimer(HandleTick, null, _frameDuration, _frameDuration);
            _onFrame = onFrame;
            _isRunning = true;
        }
    }

    /// <summary>
    /// Stops ticking and disposes the timer. A tick that already started may still complete, but no
    /// further frame is started; stopping a clock that is not running does nothing.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _isRunning = false;
            _onFrame = null;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Stops the clock and disposes its timer; equivalent to <see cref="Stop"/>.</summary>
    public void Dispose() => Stop();

    /// <summary>
    /// Timer callback: skips the frame when the clock is stopped or the previous frame is still
    /// running, and contains handler exceptions so the cadence survives them.
    /// </summary>
    /// <param name="state">Unused; the clock is captured, not the state object.</param>
    private void HandleTick(object? state)
    {
        lock (_gate)
        {
            if (!_isRunning || _tickActive)
            {
                return;
            }

            _tickActive = true;
        }

        try
        {
            _onFrame?.Invoke();
        }
        catch (Exception)
        {
            // A faulty frame handler must not stop the clock: the next frame still fires.
        }
        finally
        {
            lock (_gate)
            {
                _tickActive = false;
            }
        }
    }
}