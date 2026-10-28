namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Smoothed peak level of the forwarded audio, in the 0-1 range, for reactive lighting.
/// </summary>
/// <remarks>
/// <see cref="Process"/> runs on the capture callback while <see cref="Level"/> is read from the
/// effect timer, so the level is stored in a volatile field rather than locked. The level reads
/// zero once no block has been processed for <see cref="IdleSeconds"/>, so a stream that pauses
/// without being stopped decays to the quiet color.
/// </remarks>
public sealed class AudioLevelMeter
{
    private const double AttackSeconds = 0.06;
    private const double ReleaseSeconds = 0.2;
    private const double IdleSeconds = 0.5;

    private readonly TimeProvider _time;

    private volatile float level;

    private long lastProcessTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioLevelMeter"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock used for the idle window; defaults to <see cref="TimeProvider.System"/>.</param>
    public AudioLevelMeter(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the current smoothed level (0-1), or zero when no audio has been processed recently.
    /// </summary>
    public float Level
    {
        get
        {
            return IsStale(Volatile.Read(ref lastProcessTimestamp)) ? 0f : level;
        }
    }

    /// <summary>
    /// Drops the level to zero.
    /// </summary>
    public void Reset()
    {
        level = 0f;
        Volatile.Write(ref lastProcessTimestamp, 0);
    }

    /// <summary>
    /// Returns whether the last processed block is older than <see cref="IdleSeconds"/>.
    /// </summary>
    /// <param name="last">Timestamp of the last processed block, or zero when none was processed.</param>
    /// <returns><see langword="true"/> when the stored level is stale.</returns>
    private bool IsStale(long last) =>
        last != 0 && _time.GetElapsedTime(last).TotalSeconds > IdleSeconds;

    /// <summary>
    /// Folds one block of interleaved stereo samples into the level, rising toward the block's
    /// peak within the attack time and falling back within the release time.
    /// </summary>
    /// <param name="interleavedStereo">Interleaved left/right samples, two floats per frame.</param>
    /// <param name="durationSeconds">Duration of the block, in seconds.</param>
    public void Process(ReadOnlySpan<float> interleavedStereo, double durationSeconds)
    {
        long now = _time.GetTimestamp();
        if (IsStale(Volatile.Read(ref lastProcessTimestamp)))
        {
            level = 0f;
        }

        Volatile.Write(ref lastProcessTimestamp, now);

        float peak = 0f;
        foreach (float sample in interleavedStereo)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        peak = Math.Min(peak, 1f);
        double coefficient = 1 - Math.Exp(-durationSeconds / (peak > level ? AttackSeconds : ReleaseSeconds));
        level = (float)Math.Max(0, level + (peak - level) * coefficient);
    }
}