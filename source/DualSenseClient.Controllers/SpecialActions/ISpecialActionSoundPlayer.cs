namespace DualSenseClient.Controllers.SpecialActions;

/// <summary>
/// The device a sound special action is played through.
/// </summary>
public enum SoundOutputTarget
{
    /// <summary>
    /// The controller's built-in speaker.
    /// </summary>
    Speaker,

    /// <summary>
    /// A headset connected to the controller's headset jack.
    /// </summary>
    Headset
}

/// <summary>
/// Plays a sound file through a controller's speaker or headset, optionally driving the
/// haptic actuators with the audio. Implemented in the application layer (needs the shared
/// audio engine and the DualSense render endpoint); the engine only consumes the interface.
/// </summary>
public interface ISpecialActionSoundPlayer : IDisposable
{
    /// <summary>
    /// Opens the file, applies the output options, and starts playback through the
    /// controller. Callers may call this repeatedly; implementations restart the file.
    /// </summary>
    /// <param name="path">Path of the audio file to play.</param>
    /// <param name="output">The device the sound is played through.</param>
    /// <param name="speakerVolume">Controller speaker volume (0-255).</param>
    /// <param name="hapticFeedback">Whether the haptic actuators follow the audio.</param>
    /// <param name="hapticStrength">Haptic strength as a percentage (0-200).</param>
    void Play(string path, SoundOutputTarget output, byte speakerVolume, bool hapticFeedback, int hapticStrength);

    /// <summary>
    /// Stops playback and releases the loaded file.
    /// </summary>
    void Stop();
}