using System;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Logging;
using SoundFlow.Abstracts;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Plays a special action's sound file through a controller speaker or a headset in the
/// headset jack, optionally driving the haptic actuators with the audio. Wraps the shared
/// <see cref="DualSenseAudioPlayer"/> (desktop output disabled, the chosen controller
/// output routed), and closes the route when playback stops so the controller does not
/// keep the audio path selected.
/// </summary>
public sealed class DualSenseSpecialActionSoundPlayer : ISpecialActionSoundPlayer
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("SpecialActions");

    /// <summary>
    /// The controller the sound is played to.
    /// </summary>
    private readonly DualSenseDevice _device;

    /// <summary>
    /// The underlying player handling decoding and the Bluetooth/USB audio lanes.
    /// </summary>
    private readonly DualSenseAudioPlayer _player;

    /// <summary>
    /// Creates a player for the given controller.
    /// </summary>
    /// <param name="device">The controller to play through.</param>
    /// <param name="engine">The shared audio engine used to decode files and open render endpoints.</param>
    public DualSenseSpecialActionSoundPlayer(DualSenseDevice device, AudioEngine engine)
    {
        _device = device;
        _player = new DualSenseAudioPlayer(device, new DualSenseAudioEndpointFinder(engine), engine);
    }

    /// <inheritdoc/>
    public void Play(string path, SoundOutputTarget output, byte speakerVolume, bool hapticFeedback, int hapticStrength)
    {
        _player.ApplyOptions(
            desktop: false,
            speaker: output == SoundOutputTarget.Speaker,
            headset: output == SoundOutputTarget.Headset,
            haptics: hapticFeedback,
            speakerVolume,
            hapticStrength / 100f);
        _player.OpenFile(path);
        _player.Play();
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _player.Stop();
        try
        {
            // Close the speaker route so the audio path is not left primed on the controller.
            _device.SetAudioOutput(AudioControl.OutputPathHeadphones, 0x3F, 0x3F);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to reset audio output after sound action: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _player.Dispose();
}