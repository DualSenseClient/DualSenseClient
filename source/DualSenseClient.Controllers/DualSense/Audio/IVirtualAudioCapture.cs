namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// A virtual controller's host-audio capture feeding a
/// <see cref="DualSenseAudioForwarder"/>: the speaker-endpoint capture of a virtual
/// DualSense or DualShock 4. Exposes the suspension flag the emulation service uses to
/// keep exactly one source feeding the forwarder.
/// </summary>
public interface IVirtualAudioCapture
{
    /// <summary>
    /// Whether captured audio is discarded because another source owns the forwarder.
    /// </summary>
    bool IsSuspended { get; set; }
}