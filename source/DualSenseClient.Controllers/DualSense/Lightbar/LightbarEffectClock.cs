using System.Diagnostics;

namespace DualSenseClient.Controllers.DualSense.Lightbar;

/// <summary>
/// Elapsed-time source for lightbar effects that freezes while the effect is suspended.
/// </summary>
/// <remarks>Callers serialize access; this class does not lock.</remarks>
public sealed class LightbarEffectClock
{
    private long _startTicks;
    private long _pausedTicks;
    private long? _pauseStartTicks;

    /// <summary>
    /// Gets or sets whether elapsed time is frozen.
    /// </summary>
    public bool Suspended { get; set; }

    /// <summary>
    /// Restarts the clock from the given timestamp.
    /// </summary>
    /// <param name="nowTicks">The current <see cref="Stopwatch"/> timestamp.</param>
    public void Reset(long nowTicks)
    {
        _startTicks = nowTicks;
        _pausedTicks = 0;
        _pauseStartTicks = null;
    }

    /// <summary>
    /// Gets how long the effect has been running, in seconds, excluding suspended time.
    /// </summary>
    /// <param name="nowTicks">The current <see cref="Stopwatch"/> timestamp.</param>
    /// <returns>The running time in seconds.</returns>
    public double GetElapsedSeconds(long nowTicks)
    {
        if (Suspended)
        {
            _pauseStartTicks ??= nowTicks;
        }
        else if (_pauseStartTicks is long pauseStart)
        {
            _pausedTicks += nowTicks - pauseStart;
            _pauseStartTicks = null;
        }

        long endTicks = _pauseStartTicks ?? nowTicks;

        return (endTicks - _startTicks - _pausedTicks) / (double)Stopwatch.Frequency;
    }
}