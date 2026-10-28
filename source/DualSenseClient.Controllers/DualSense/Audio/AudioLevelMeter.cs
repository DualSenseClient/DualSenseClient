namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Smoothed peak level of the forwarded audio, in the 0-1 range, for reactive lighting.
/// </summary>
/// <remarks>
/// <see cref="Process"/> runs on the capture callback while <see cref="Level"/> is read from the
/// effect timer, so the level is stored in a volatile field rather than locked.
/// </remarks>
public sealed class AudioLevelMeter
{
    private const double AttackSeconds = 0.06;
    private const double ReleaseSeconds = 0.2;

    private volatile float level;

    /// <summary>
    /// Gets the current smoothed level (0-1).
    /// </summary>
    public float Level
    {
        get
        {
            return level;
        }
    }

    /// <summary>
    /// Drops the level to zero.
    /// </summary>
    public void Reset() => level = 0f;

    /// <summary>
    /// Folds one block of interleaved stereo samples into the level, rising toward the block's
    /// peak within the attack time and falling back within the release time.
    /// </summary>
    /// <param name="interleavedStereo">Interleaved left/right samples, two floats per frame.</param>
    /// <param name="durationSeconds">Duration of the block, in seconds.</param>
    public void Process(ReadOnlySpan<float> interleavedStereo, double durationSeconds)
    {
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