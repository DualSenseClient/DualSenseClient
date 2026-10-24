namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// A host-audio capture feeding a <see cref="DualSenseAudioForwarder"/> directly
/// with 48 kHz interleaved stereo float PCM, without a virtual controller in between:
/// the system-output loopback of the default render device or the process loopback of a
/// single application. Exactly one direct capture owns the forwarder at a time.
/// </summary>
public interface IForwardedAudioCapture
{
    /// <summary>
    /// Whether the capture is currently running.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Opens the capture and starts feeding the forwarder. Returns <c>false</c> when the
    /// platform, the machine or the target does not allow capturing, leaving the capture
    /// stopped. Safe to call repeatedly; a running capture is left untouched.
    /// </summary>
    bool Start();

    /// <summary>
    /// Stops the capture and releases its resources. Safe to call repeatedly.
    /// </summary>
    void Stop();
}