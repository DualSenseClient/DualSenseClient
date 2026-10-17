namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Paces the periodic work of one endpoint: invokes a tick handler once per interval through the
/// configured <see cref="TimeProvider"/>. Ticks never overlap and never queue - while a slow tick is
/// still running, a due tick is skipped - and an exception thrown by the handler is contained so
/// that the next tick still fires. One pacer drives one endpoint: create it on import and
/// <see cref="Stop"/> it when the endpoint goes away.
/// </summary>
public sealed class EndpointPacer
{
    private readonly TimeProvider _timeProvider;

    private readonly TimeSpan _interval;

    private readonly object _gate = new object();

    private ITimer? _timer;

    private Action? _onTick;

    private bool _isRunning;

    private bool _tickActive;

    /// <summary>
    /// Initializes a pacer that fires at <paramref name="interval"/> once <see cref="Start"/> is called.
    /// </summary>
    /// <param name="timeProvider">Provider that schedules the ticks: system time in production, a manual provider in tests.</param>
    /// <param name="interval">Time between ticks; must be positive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is not positive.</exception>
    public EndpointPacer(TimeProvider timeProvider, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval must be positive.");
        }

        _timeProvider = timeProvider;
        _interval = interval;
    }

    /// <summary>Whether the pacer is ticking.</summary>
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
    /// Starts ticking: <paramref name="onTick"/> is invoked once per interval, the first time one
    /// interval from now, on a timer thread of the time provider (or on the caller's thread when a
    /// manual provider drains the clock).
    /// </summary>
    /// <param name="onTick">Handler invoked once per tick.</param>
    /// <exception cref="ArgumentNullException"><paramref name="onTick"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The pacer is already running.</exception>
    public void Start(Action onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);
        lock (_gate)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("The pacer is already running.");
            }

            _timer = _timeProvider.CreateTimer(HandleTick, null, _interval, _interval);
            _onTick = onTick;
            _isRunning = true;
        }
    }

    /// <summary>
    /// Stops ticking and disposes the timer. A tick that already started may still complete, but no
    /// further tick is started; stopping a pacer that is not running does nothing.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _isRunning = false;
            _onTick = null;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>
    /// Timer callback: skips the tick when the pacer is stopped or the previous tick is still
    /// running, and contains handler exceptions so the pacing survives them.
    /// </summary>
    /// <param name="state">Unused; the pacer is captured, not the state object.</param>
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
            _onTick?.Invoke();
        }
        catch (Exception)
        {
            // A faulty tick handler must not stop the pacer: the next tick still fires.
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