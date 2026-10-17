namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> test double: the clock stands still until
/// <see cref="Advance"/> is called, and timer callbacks run synchronously on the caller's thread
/// when the clock reaches their due time. The system-oriented members that tests do not need fall
/// back to the base implementation.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new object();

    private readonly List<ManualTimer> _timers = new List<ManualTimer>();

    private DateTimeOffset _utcNow = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private long _nextSequence;

    /// <summary>Gets the timestamp frequency: one tick per 100 ns, matching <see cref="DateTimeOffset.Ticks"/>.</summary>
    public override long TimestampFrequency
    {
        get
        {
            return TimeSpan.TicksPerSecond;
        }
    }

    /// <summary>
    /// Number of timers the provider currently tracks; a test hook for asserting that no scheduler
    /// entry leaks, e.g. that every endpoint pacer disposed its timer on session teardown.
    /// </summary>
    public int TimerCount
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }

    /// <summary>Gets the clock time, which starts at 2000-01-01T00:00:00Z and only moves on <see cref="Advance"/>.</summary>
    /// <returns>The current clock time in UTC.</returns>
    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _utcNow;
        }
    }

    /// <summary>Gets the clock time in <see cref="TimestampFrequency"/> units.</summary>
    /// <returns>The current clock time as 100 ns ticks.</returns>
    public override long GetTimestamp()
    {
        lock (_lock)
        {
            return _utcNow.Ticks;
        }
    }

    /// <summary>
    /// Advances the clock by <paramref name="delta"/> and invokes every timer whose due time is
    /// reached, in due-time order (creation order breaks ties), synchronously on the calling thread.
    /// A periodic timer is rescheduled by its period before each invocation, so one Advance drains
    /// every occurrence it covers; a one-shot timer fires at most once. Timers that a callback
    /// creates while already due participate in the same drain.
    /// </summary>
    /// <param name="delta">Time to advance the clock by; must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delta"/> is negative.</exception>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta, "Time cannot be advanced by a negative amount.");
        }

        DateTimeOffset target;
        lock (_lock)
        {
            target = _utcNow + delta;
        }

        while (true)
        {
            ManualTimer? nextTimer;
            lock (_lock)
            {
                nextTimer = FindNextDueTimer(target);
                if (nextTimer is null)
                {
                    break;
                }

                DateTimeOffset dueTime = nextTimer.DueTime!.Value;
                if (dueTime > _utcNow)
                {
                    _utcNow = dueTime;
                }

                nextTimer.Reschedule();
            }

            nextTimer.Invoke();
        }

        lock (_lock)
        {
            if (_utcNow < target)
            {
                _utcNow = target;
            }
        }
    }

    /// <summary>
    /// Creates a timer tracked by this provider. The timer fires when the clock reaches its due time
    /// during an <see cref="Advance"/>; an infinite due time creates a disarmed timer and an infinite
    /// period creates a one-shot timer.
    /// </summary>
    /// <param name="callback">Callback invoked per firing on the thread that calls <see cref="Advance"/>.</param>
    /// <param name="state">State passed to <paramref name="callback"/>.</param>
    /// <param name="dueTime">Delay from now until the first firing, or <see cref="Timeout.InfiniteTimeSpan"/> to create a disarmed timer.</param>
    /// <param name="period">Delay between the firings of a periodic timer, or <see cref="Timeout.InfiniteTimeSpan"/> for a one-shot timer.</param>
    /// <returns>The timer handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dueTime"/> or <paramref name="period"/> is invalid.</exception>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ValidateTimerTimes(dueTime, period);

        lock (_lock)
        {
            DateTimeOffset? firstDueTime = dueTime == Timeout.InfiniteTimeSpan ? null : _utcNow + dueTime;
            TimeSpan? recurrence = period == Timeout.InfiniteTimeSpan ? null : period;
            ManualTimer timer = new ManualTimer(this, _nextSequence++, callback, state, firstDueTime, recurrence);
            _timers.Add(timer);
            return timer;
        }
    }

    private static void ValidateTimerTimes(TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan && dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "The due time must not be negative.");
        }

        if (period != Timeout.InfiniteTimeSpan && period <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "The period must be positive or infinite.");
        }
    }

    /// <summary>
    /// Returns the timer with the earliest due time at or before <paramref name="target"/>, breaking
    /// ties by creation order. Callers hold the provider lock.
    /// </summary>
    /// <param name="target">Inclusive upper bound of the due times that count as due.</param>
    /// <returns>The timer to fire next; <see langword="null"/> when no timer is due.</returns>
    private ManualTimer? FindNextDueTimer(DateTimeOffset target)
    {
        ManualTimer? next = null;
        DateTimeOffset nextDueTime = default;
        foreach (ManualTimer timer in _timers)
        {
            DateTimeOffset? dueTimeOrNull = timer.DueTime;
            if (dueTimeOrNull is null)
            {
                continue;
            }

            DateTimeOffset dueTime = dueTimeOrNull.Value;
            if (dueTime > target)
            {
                continue;
            }

            if (next is null || dueTime < nextDueTime || (dueTime == nextDueTime && timer.Sequence < next.Sequence))
            {
                next = timer;
                nextDueTime = dueTime;
            }
        }

        return next;
    }

    /// <summary>Timer handle of <see cref="ManualTimeProvider"/>; every field is guarded by the provider lock.</summary>
    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _provider;

        private readonly TimerCallback _callback;

        private readonly object? _state;

        private bool _disposed;

        private DateTimeOffset? _dueTime;

        private TimeSpan? _period;

        internal ManualTimer(
            ManualTimeProvider provider,
            long sequence,
            TimerCallback callback,
            object? state,
            DateTimeOffset? dueTime,
            TimeSpan? period)
        {
            _provider = provider;
            Sequence = sequence;
            _callback = callback;
            _state = state;
            _dueTime = dueTime;
            _period = period;
        }

        /// <summary>Creation order of the timer: breaks due-time ties in favour of the older timer.</summary>
        internal long Sequence { get; }

        /// <summary>Absolute due time, or <see langword="null"/> while the timer is disarmed.</summary>
        internal DateTimeOffset? DueTime
        {
            get
            {
                return _dueTime;
            }
        }

        /// <summary>Rearms a periodic timer after a firing and disarms a one-shot timer.</summary>
        internal void Reschedule()
        {
            if (_period is TimeSpan period && _dueTime is DateTimeOffset dueTime)
            {
                _dueTime = dueTime + period;
            }
            else
            {
                _dueTime = null;
            }
        }

        /// <summary>Invokes the user callback outside the provider lock, on the draining thread.</summary>
        internal void Invoke() => _callback(_state);

        /// <summary>
        /// Replaces the due time (relative to the current clock) and the period of the timer.
        /// </summary>
        /// <param name="dueTime">Delay from now until the next firing, or <see cref="Timeout.InfiniteTimeSpan"/> to disarm the timer.</param>
        /// <param name="period">Delay between the firings, or <see cref="Timeout.InfiniteTimeSpan"/> for a one-shot timer.</param>
        /// <returns><see langword="true"/> when the change was applied; <see langword="false"/> when the timer is disposed.</returns>
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ValidateTimerTimes(dueTime, period);
            lock (_provider._lock)
            {
                if (_disposed)
                {
                    return false;
                }

                _dueTime = dueTime == Timeout.InfiniteTimeSpan ? null : _provider._utcNow + dueTime;
                _period = period == Timeout.InfiniteTimeSpan ? null : period;
                return true;
            }
        }

        /// <summary>Removes the timer from its provider: it never fires again and later changes fail.</summary>
        public void Dispose()
        {
            lock (_provider._lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _provider._timers.Remove(this);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}