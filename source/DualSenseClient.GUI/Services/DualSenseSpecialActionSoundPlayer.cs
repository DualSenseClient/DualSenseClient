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
/// output routed), and releases the controller audio route once playback has fully stopped —
/// whether it ends naturally or via <see cref="Stop"/> — so the controller does not keep the
/// audio path selected.
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
    /// Whether to release the audio route once the writer loop exits. Written from the
    /// caller's thread (<see cref="Play"/>/<see cref="Stop"/>/<see cref="Dispose"/>) and read
    /// on the writer thread (<c>PlaybackEnded</c>/<c>WriterExited</c>), so it must be volatile.
    /// </summary>
    private volatile bool _resetRoutePending;

    /// <summary>
    /// Creates a player for the given controller.
    /// </summary>
    /// <param name="device">The controller to play through.</param>
    /// <param name="engine">The shared audio engine used to decode files and open render endpoints.</param>
    public DualSenseSpecialActionSoundPlayer(DualSenseDevice device, AudioEngine engine)
    {
        _device = device;
        _player = new DualSenseAudioPlayer(device, new DualSenseAudioEndpointFinder(engine), engine);
        _player.PlaybackEnded += OnPlaybackEnded;
        _player.WriterExited += OnWriterExited;
    }

    /// <inheritdoc/>
    public void Play(string path, SoundOutputTarget output, byte speakerVolume, bool hapticFeedback, int hapticStrength)
    {
        // A new playback applies its own route, so any pending release from a previous one
        // must not touch this session when the previous writer exits late.
        _resetRoutePending = false;
        _player.ApplyOptions(
            false,
            output == SoundOutputTarget.Speaker,
            output == SoundOutputTarget.Headset,
            hapticFeedback,
            speakerVolume,
            hapticStrength / 100f);
        _player.OpenFile(path);
        _player.Play();
    }

    /// <inheritdoc/>
    public void Stop()
    {
        // Request the release first: a writer that exits between this write and the
        // <see cref="IsWriterActive"/> read will pick the request up via <c>WriterExited</c>.
        // When no writer is (or was) running, none will, so it is released directly here.
        _resetRoutePending = true;
        bool writerActive = _player.IsWriterActive;
        _player.Stop();
        if (!writerActive)
        {
            _resetRoutePending = false;
            ResetAudioRoute();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Request the release before disposing so a winding-down writer still delivers it
        // through <see cref="OnWriterExited"/>; when it already exited, release directly.
        _resetRoutePending = true;
        bool writerActive = _player.IsWriterActive;
        _player.Dispose();
        if (!writerActive)
        {
            _resetRoutePending = false;
            ResetAudioRoute();
        }
    }

    /// <summary>
    /// Requests the route release when the current playback ends on its own.
    /// </summary>
    private void OnPlaybackEnded(object? sender, EventArgs e) => _resetRoutePending = true;

    /// <summary>
    /// Releases the controller audio route once the writer loop has fully exited, ordering
    /// the release report after the last audio frame. Only fires when a release was
    /// requested; a new <see cref="Play"/> clears the pending request so a stale writer
    /// cannot tear down a newer playback.
    /// </summary>
    private void OnWriterExited(object? sender, EventArgs e)
    {
        if (!_resetRoutePending)
        {
            return;
        }

        _resetRoutePending = false;
        ResetAudioRoute();
    }

    /// <summary>
    /// Resets the audio route to the default headphone path, unselecting the speaker so the
    /// controller does not keep the audio path primed.
    /// </summary>
    private void ResetAudioRoute()
    {
        try
        {
            _device.SetAudioOutput(AudioControl.OutputPathHeadphones, 0x3F, 0x3F);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to reset audio output after sound action: {ex.Message}");
        }
    }
}