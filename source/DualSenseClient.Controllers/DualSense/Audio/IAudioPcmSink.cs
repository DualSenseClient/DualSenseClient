namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// A consumer of captured host audio from a virtual controller's speaker endpoint.
/// </summary>
public interface IAudioPcmSink
{
    /// <summary>
    /// Consumes one block of interleaved 48 kHz stereo float PCM. Called synchronously from the
    /// capture callback; the span is only valid for the duration of the call.
    /// </summary>
    /// <param name="stereo">Interleaved left/right samples, two floats per frame.</param>
    void FeedPcm(ReadOnlySpan<float> stereo);

    /// <summary>
    /// Discards buffered audio at a stream-generation barrier: the host reopened or
    /// re-alternated the speaker interface, so buffered PCM belongs to a stale stream.
    /// </summary>
    void Flush();
}