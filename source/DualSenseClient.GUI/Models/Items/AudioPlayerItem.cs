using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;
using SoundFlow.Abstracts;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display model for the audio player section of the input monitor page. Wraps a
/// <see cref="DualSenseAudioPlayer"/> and exposes the bound state: current file, play
/// state, position/duration, output destination toggles, and volumes.
/// </summary>
/// <remarks>
/// <para>
/// All player events fire on the audio writer thread, so UI-facing properties are
/// updated through coalesced <see cref="Dispatcher.UIThread"/> posts, matching the
/// pattern used by <see cref="InputMonitorItem"/>.
/// </para>
/// <para>
/// The seek slider binds two-way to <see cref="PositionSeconds"/>. Dragging schedules a
/// debounced seek; playback-position ticks write the backing field directly (without the
/// seek setter) so the moving thumb never re-triggers a seek.
/// </para>
/// </remarks>
public sealed partial class AudioPlayerItem : ObservableObject, IDisposable
{
    /// <summary>
    /// Delay after the seek slider stops moving before the jump is applied.
    /// </summary>
    private static readonly TimeSpan SeekDebounceDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Delay after a value slider stops moving before the output options are applied.
    /// </summary>
    private static readonly TimeSpan OptionsDebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("AudioPlayer");

    /// <summary>
    /// The underlying player that decodes, writes, and routes the audio stream.
    /// </summary>
    private readonly DualSenseAudioPlayer _player;

    /// <summary>
    /// Delays the seek until the slider settles, so continuous drags don't seek each tick.
    /// </summary>
    private readonly DispatcherTimer _seekDebounceTimer;

    /// <summary>
    /// Delays applying output options until their sliders settle.
    /// </summary>
    private readonly DispatcherTimer _optionsDebounceTimer;

    /// <summary>
    /// Whether a coalesced UI-thread position update is already queued.
    /// </summary>
    private bool _updateQueued;

    /// <summary>
    /// Tracks whether the item has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Whether a debounced seek is scheduled; while set, live position ticks are skipped
    /// so they cannot fight the user's slider drag.
    /// </summary>
    private bool _seekPending;

    /// <summary>
    /// The user's requested seek target, captured when the slider moves so live position
    /// ticks during the debounce window cannot overwrite it.
    /// </summary>
    private double _seekTargetSeconds;

    /// <summary>
    /// Whether audio is played to the desktop render target (two-way).
    /// </summary>
    private bool _playToDesktop = true;

    /// <summary>
    /// Selected controller output destination: 0 = off, 1 = speaker, 2 = headset.
    /// </summary>
    private int _controllerOutputIndex;

    /// <summary>
    /// Whether the haptic actuators follow the audio (two-way).
    /// </summary>
    private bool _playToHaptics;

    /// <summary>
    /// Controller speaker volume (0-255).
    /// </summary>
    private int _speakerVolume = 0x50;

    /// <summary>
    /// Haptic vibration strength as a percentage (0-200).
    /// </summary>
    private int _hapticStrength = 100;

    /// <summary>
    /// Name of the loaded file, or <c>null</c> when none is loaded.
    /// </summary>
    private string? _fileName;

    /// <summary>
    /// Whether a file is loaded and can be played.
    /// </summary>
    private bool _hasFile;

    /// <summary>
    /// Whether the writer loop is running.
    /// </summary>
    private bool _isPlaying;

    /// <summary>
    /// Playback position in seconds (drives the seek slider).
    /// </summary>
    private double _positionSeconds;

    /// <summary>
    /// Whether a DualSense is wrapped, so controller audio outputs are available.
    /// </summary>
    public bool CanUseControllerOutputs { get; }

    /// <summary>
    /// Whether desktop playback is enabled (two-way).
    /// </summary>
    public bool PlayToDesktop
    {
        get => _playToDesktop;
        set
        {
            if (SetProperty(ref _playToDesktop, value))
            {
                ApplyOptions();
            }
        }
    }

    /// <summary>
    /// Selectable controller output destinations, in <see cref="ControllerOutputIndex"/> order
    /// (off, DualSense speaker, headset).
    /// </summary>
    public ObservableCollection<string> ControllerOutputOptions { get; }

    /// <summary>
    /// Selected entry in <see cref="ControllerOutputOptions"/> (two-way). Because the DualSense
    /// speaker and headset cannot run simultaneously, a single destination is chosen: 0 = off,
    /// 1 = speaker, 2 = headset.
    /// </summary>
    public int ControllerOutputIndex
    {
        get => _controllerOutputIndex;
        set
        {
            if (SetProperty(ref _controllerOutputIndex, Math.Clamp(value, 0, 2)))
            {
                ApplyOptions();
            }
        }
    }

    /// <summary>
    /// Whether the haptic actuators are driven with the audio (two-way).
    /// </summary>
    public bool PlayToHaptics
    {
        get => _playToHaptics;
        set
        {
            if (SetProperty(ref _playToHaptics, value))
            {
                ApplyOptions();
            }
        }
    }

    /// <summary>
    /// Controller speaker volume (0-255, two-way).
    /// </summary>
    public int SpeakerVolume
    {
        get => _speakerVolume;
        set
        {
            if (SetProperty(ref _speakerVolume, Math.Clamp(value, 0, 255)))
            {
                ScheduleApplyOptions();
            }
        }
    }

    /// <summary>
    /// Haptic vibration strength as a percentage (0-200, two-way).
    /// </summary>
    public int HapticStrength
    {
        get => _hapticStrength;
        set
        {
            if (SetProperty(ref _hapticStrength, Math.Clamp(value, 0, 200)))
            {
                ScheduleApplyOptions();
            }
        }
    }

    /// <summary>
    /// Name of the loaded file, or <c>null</c> when none is loaded.
    /// </summary>
    public string? FileName => _fileName;

    /// <summary>
    /// Whether a file is loaded and can be played.
    /// </summary>
    public bool HasFile => _hasFile;

    /// <summary>
    /// Whether the writer loop is running.
    /// </summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>
    /// Total duration of the loaded file in seconds (drives the seek slider maximum).
    /// </summary>
    public double DurationSeconds => _player.Duration.TotalSeconds;

    /// <summary>
    /// Playback position in seconds (two-way; the setter is only hit by user drags).
    /// The requested position is captured immediately so live position ticks during the
    /// debounce window cannot overwrite it.
    /// </summary>
    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            if (SetProperty(ref _positionSeconds, value))
            {
                _seekTargetSeconds = value;
                ScheduleSeek();
            }
        }
    }

    /// <summary>
    /// Playback position formatted as "m:ss", or "-" when nothing is loaded.
    /// </summary>
    public string PositionText => _hasFile ? FormatClock(_player.Position) : "-";

    /// <summary>
    /// Duration formatted as "m:ss", or "-" when nothing is loaded.
    /// </summary>
    public string DurationText => _hasFile ? FormatClock(_player.Duration) : "-";

    /// <summary>
    /// Creates the audio player for the given DualSense, or for desktop-only output when
    /// <paramref name="device"/> is <c>null</c> (non-DualSense controllers or none).
    /// </summary>
    public AudioPlayerItem(DualSenseDevice? device, AudioEngine engine)
    {
        CanUseControllerOutputs = device is not null;
        _player = new DualSenseAudioPlayer(device, new DualSenseAudioEndpointFinder(engine), engine);
        _player.PositionChanged += OnPositionChanged;
        _player.StateChanged += OnStateChanged;
        _player.PlaybackEnded += OnPlaybackEnded;

        ControllerOutputOptions =
        [
            LocalizationService.GetText("InputMonitorPage.Audio.Output.None"),
            LocalizationService.GetText("InputMonitorPage.Audio.Output.Speaker"),
            LocalizationService.GetText("InputMonitorPage.Audio.Output.Headset")
        ];

        _seekDebounceTimer = new DispatcherTimer { Interval = SeekDebounceDelay };
        _seekDebounceTimer.Tick += OnSeekDebounceTick;

        _optionsDebounceTimer = new DispatcherTimer { Interval = OptionsDebounceDelay };
        _optionsDebounceTimer.Tick += OnOptionsDebounceTick;
    }

    /// <summary>
    /// Loads an audio file and starts playback immediately, stopping any current playback.
    /// </summary>
    /// <param name="path">Path of the audio file to open.</param>
    public void OpenFile(string path)
    {
        try
        {
            _player.OpenFile(path);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open audio file '{path}': {ex.Message}");
            return;
        }

        _fileName = Path.GetFileName(path);
        _positionSeconds = 0;
        _hasFile = true;
        ResetSeekState();

        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(DurationText));
        NotifyCommandsChanged();

        _player.Play();
    }

    /// <summary>
    /// Starts or pauses playback.
    /// </summary>
    [RelayCommand]
    private void TogglePlayPause()
    {
        if (!_hasFile)
        {
            return;
        }

        if (_isPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
    }

    /// <summary>
    /// Stops playback and releases the loaded file.
    /// </summary>
    [RelayCommand]
    private void Stop()
    {
        _player.Stop();
        _hasFile = false;
        _fileName = null;
        _positionSeconds = 0;
        ResetSeekState();
        OnPropertyChanged(nameof(HasFile));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(DurationSeconds));
        NotifyCommandsChanged();
    }

    /// <summary>
    /// Whether play/pause is available (a file is loaded).
    /// </summary>
    private bool CanTogglePlayPause() => _hasFile;

    /// <summary>
    /// Whether stop is available (a file is loaded).
    /// </summary>
    private bool CanStop() => _hasFile;

    /// <summary>
    /// Releases the player and its event subscriptions.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PositionChanged -= OnPositionChanged;
        _player.StateChanged -= OnStateChanged;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _seekDebounceTimer.Stop();
        _optionsDebounceTimer.Stop();
        _player.Dispose();
    }

    /// <summary>
    /// Applies the current output selection, volume, and haptic strength to the player.
    /// </summary>
    private void ApplyOptions()
    {
        _player.ApplyOptions(
            _playToDesktop,
            _controllerOutputIndex == 1,
            _controllerOutputIndex == 2,
            _playToHaptics,
            (byte)_speakerVolume,
            _hapticStrength / 100f);
    }

    /// <summary>
    /// Queues a debounced seek using the captured seek target.
    /// </summary>
    private void ScheduleSeek()
    {
        _seekPending = true;
        _seekDebounceTimer.Stop();
        _seekDebounceTimer.Start();
    }

    /// <summary>
    /// Applies the debounced seek once the slider settles.
    /// </summary>
    private void OnSeekDebounceTick(object? sender, EventArgs e)
    {
        _seekDebounceTimer.Stop();
        _seekPending = false;
        _player.Seek(TimeSpan.FromSeconds(_seekTargetSeconds));
    }

    /// <summary>
    /// Queues a debounced <see cref="ApplyOptions"/> so continuous slider drags don't
    /// re-route audio until the user stops adjusting the value.
    /// </summary>
    private void ScheduleApplyOptions()
    {
        _optionsDebounceTimer.Stop();
        _optionsDebounceTimer.Start();
    }

    /// <summary>
    /// Applies the output options once the slider settles.
    /// </summary>
    private void OnOptionsDebounceTick(object? sender, EventArgs e)
    {
        _optionsDebounceTimer.Stop();
        ApplyOptions();
    }

    /// <summary>
    /// Updates <see cref="PositionSeconds"/> and the clock text from the player's writer
    /// thread, coalesced into a single UI-thread update. The backing field is written
    /// directly so live ticks never route through the seek setter. While a debounced seek
    /// is pending the update is skipped entirely so the live position cannot fight the
    /// user's slider drag.
    /// </summary>
    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (_updateQueued || _seekPending)
        {
            return;
        }

        _updateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _updateQueued = false;
            double seconds = _player.Position.TotalSeconds;
            if (_positionSeconds != seconds)
            {
                _positionSeconds = seconds;
                OnPropertyChanged(nameof(PositionSeconds));
            }

            OnPropertyChanged(nameof(PositionText));
        });
    }

    /// <summary>
    /// Mirrors the player's running state and refreshes the transport commands.
    /// </summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isPlaying = _player.IsPlaying;
            OnPropertyChanged(nameof(IsPlaying));
            NotifyCommandsChanged();
        });
    }

    /// <summary>
    /// Clears the playing state when the file reaches its end.
    /// </summary>
    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isPlaying = false;
            OnPropertyChanged(nameof(IsPlaying));
            NotifyCommandsChanged();
        });
    }

    /// <summary>
    /// Re-evaluates the transport command availability.
    /// </summary>
    private void NotifyCommandsChanged()
    {
        TogglePlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Cancels any pending seek and drops its target, so a released file cannot be sought.
    /// </summary>
    private void ResetSeekState()
    {
        _seekDebounceTimer.Stop();
        _seekPending = false;
        _seekTargetSeconds = 0;
    }

    /// <summary>
    /// Formats a duration as "m:ss" (hours are omitted for anything under an hour).
    /// </summary>
    private static string FormatClock(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }
}