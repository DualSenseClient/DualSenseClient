using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Provides the previous and new name when a profile is renamed.
/// </summary>
public sealed class ProfileRenamedEventArgs : EventArgs
{
    /// <summary>
    /// The profile name before the rename.
    /// </summary>
    public string OldName { get; }

    /// <summary>
    /// The profile name after the rename.
    /// </summary>
    public string NewName { get; }

    /// <summary>
    /// Creates a new rename event args instance.
    /// </summary>
    /// <param name="oldName">The profile name before the rename.</param>
    /// <param name="newName">The profile name after the rename.</param>
    public ProfileRenamedEventArgs(string oldName, string newName)
    {
        OldName = oldName;
        NewName = newName;
    }
}

/// <summary>
/// Display model for editing a single <see cref="Profile"/> in the profile manager.
/// Exposes the lightbar color, microphone LED mode, and player LED layout for editing and
/// persists every change back to the profile file immediately.
/// </summary>
/// <remarks>
/// <para>
/// The player LED layout is stored in the profile as a raw byte mask (bits 0-4 map to
/// LEDs 1-5), while the UI works with individual booleans. The item converts between the
/// two on load and on every change.
/// </para>
/// <para>
/// The name is editable directly: assigning a new name renames the underlying profile via
/// <see cref="ProfileService.RenameProfile"/> (in-memory with a debounced save) and raises
/// <see cref="ProfileRenamed"/> so the owning ViewModel can re-point controller assignments.
/// </para>
/// </remarks>
public sealed partial class ProfileEditorItem : ObservableObject, IDisposable
{
    /// <summary>
    /// Delay between the last edit and the disk save, so rapid changes (e.g. dragging a
    /// slider) are coalesced into a single write once the user releases control.
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Player LED preset masks, mirror-symmetric layouts used by PS5 (unconfirmed):
    /// Player 1 0x04, Player 2 0x0A, Player 3 0x15, Player 4 0x1B, Player 5 0x1F.
    /// </summary>
    private static readonly byte[] PlayerPresetMasks = [0x04, 0x0A, 0x15, 0x1B, 0x1F];

    /// <summary>
    /// The profile service backing persistence for this item.
    /// </summary>
    private readonly ProfileService _profileService;

    /// <summary>
    /// Debounced save timer; each edit restarts it and the save happens only after edits stop.
    /// </summary>
    private readonly DispatcherTimer _saveTimer;

    /// <summary>
    /// Tracks whether a color update is in progress to avoid feedback loops
    /// between <see cref="LightbarColor"/> and the channel properties.
    /// </summary>
    private bool _syncingColor;

    /// <summary>
    /// Tracks whether a player LED preset sync is in progress, so preset and individual
    /// LED changes applied together do not trigger persistence for each intermediate state.
    /// </summary>
    private bool _syncingPreset;

    /// <summary>
    /// Tracks whether the item has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Whether a change has been made but not yet flushed to disk.
    /// </summary>
    private bool _pendingCommit;

    /// <summary>
    /// The profile being edited.
    /// </summary>
    public Profile Profile { get; }

    /// <summary>
    /// Raised after the profile has been renamed, so the owning ViewModel can refresh
    /// any profile name lists (e.g. the controller assignment dropdown).
    /// </summary>
    public event EventHandler<ProfileRenamedEventArgs>? ProfileRenamed;

    /// <summary>
    /// Raised after the profile's lights or name change, so the owning ViewModel can
    /// re-apply the profile to a controller currently using it.
    /// </summary>
    public event EventHandler? ProfileChanged;

    /// <summary>
    /// Gets or sets the profile name. Setting a new name renames the profile (and its
    /// controller bindings) immediately in memory; invalid names revert the bound value.
    /// The rename is persisted by the debounced save.
    /// </summary>
    public string Name
    {
        get
        {
            return Profile.Name;
        }
        set
        {
            if (_disposed)
            {
                return;
            }

            string trimmed = value?.Trim() ?? string.Empty;
            if (string.Equals(Profile.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string oldName = Profile.Name;
            if (_profileService.RenameProfileInMemory(oldName, trimmed))
            {
                OnPropertyChanged();
                ProfileRenamed?.Invoke(this, new ProfileRenamedEventArgs(oldName, trimmed));
                ProfileChanged?.Invoke(this, EventArgs.Empty);
                ScheduleCommit();
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Lightbar red channel (0-255).
    /// </summary>
    [ObservableProperty] private double _ledRed;

    /// <summary>
    /// Lightbar green channel (0-255).
    /// </summary>
    [ObservableProperty] private double _ledGreen;

    /// <summary>
    /// Lightbar blue channel (0-255).
    /// </summary>
    [ObservableProperty] private double _ledBlue;

    /// <summary>
    /// Microphone LED mode: <c>0</c> off, <c>1</c> on, <c>2</c> pulse.
    /// Doubles as the ComboBox selection index.
    /// </summary>
    [ObservableProperty] private int _muteLedMode;

    /// <summary>
    /// Whether player LED 1 (leftmost) is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed1;

    /// <summary>
    /// Whether player LED 2 is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed2;

    /// <summary>
    /// Whether player LED 3 (center) is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed3;

    /// <summary>
    /// Whether player LED 4 is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed4;

    /// <summary>
    /// Whether player LED 5 (rightmost) is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed5;

    /// <summary>
    /// Whether the Player 1 preset (mirror-symmetric mask 0x04) is selected.
    /// </summary>
    [ObservableProperty] private bool _playerPreset1;

    /// <summary>
    /// Whether the Player 2 preset (mirror-symmetric mask 0x0A) is selected.
    /// </summary>
    [ObservableProperty] private bool _playerPreset2;

    /// <summary>
    /// Whether the Player 3 preset (mirror-symmetric mask 0x15) is selected.
    /// </summary>
    [ObservableProperty] private bool _playerPreset3;

    /// <summary>
    /// Whether the Player 4 preset (mirror-symmetric mask 0x1B) is selected.
    /// </summary>
    [ObservableProperty] private bool _playerPreset4;

    /// <summary>
    /// Whether the Player 5 preset (mirror-symmetric mask 0x1F) is selected.
    /// </summary>
    [ObservableProperty] private bool _playerPreset5;

    /// <summary>
    /// Lightbar effect mode as the ComboBox selection index (see <see cref="LightbarMode"/>).
    /// </summary>
    [ObservableProperty] private int _lightbarModeIndex;

    /// <summary>
    /// Lightbar brightness percentage (0-100).
    /// </summary>
    [ObservableProperty] private double _lightbarBrightness;

    /// <summary>
    /// Effect speed percentage (0-100).
    /// </summary>
    [ObservableProperty] private double _lightbarSpeed;

    /// <summary>
    /// Whether the multi-color palette is played in random order.
    /// </summary>
    [ObservableProperty] private bool _lightbarRandomColor;

    /// <summary>
    /// Minimum brightness percentage of the breathing effect (0-100).
    /// </summary>
    [ObservableProperty] private double _breathMin;

    /// <summary>
    /// Maximum brightness percentage of the breathing effect (0-100).
    /// </summary>
    [ObservableProperty] private double _breathMax;

    /// <summary>
    /// Audio reactivity sensitivity percentage (0-100).
    /// </summary>
    [ObservableProperty] private double _audioSensitivity;

    /// <summary>
    /// Whether the player LEDs show the current audio level.
    /// </summary>
    [ObservableProperty] private bool _audioPlayerLeds;

    /// <summary>
    /// Whether the lightbar crossfades through the charge-level colors while charging.
    /// </summary>
    [ObservableProperty] private bool _batteryChargingAnimation;

    /// <summary>
    /// Whether the player LEDs run a chase while charging.
    /// </summary>
    [ObservableProperty] private bool _batteryPlayerLeds;

    /// <summary>
    /// Player LED charging chase speed percentage (0-100).
    /// </summary>
    [ObservableProperty] private double _batteryPlayerLedSpeed;

    /// <summary>
    /// Whether the player LEDs show the battery level as five cumulative gates.
    /// </summary>
    [ObservableProperty] private bool _batteryLevelPlayerLeds;

    /// <summary>
    /// Color shown at low audio levels.
    /// </summary>
    [ObservableProperty] private Color _audioLowColor;

    /// <summary>
    /// Color shown at mid audio levels.
    /// </summary>
    [ObservableProperty] private Color _audioMidColor;

    /// <summary>
    /// Color shown at high audio levels.
    /// </summary>
    [ObservableProperty] private Color _audioHighColor;

    /// <summary>
    /// The palette color shown in the color picker.
    /// </summary>
    [ObservableProperty] private LightbarPaletteItem? _selectedPaletteItem;

    /// <summary>
    /// Editable multi-color palette.
    /// </summary>
    public ObservableCollection<LightbarPaletteItem> Palette { get; } = [];

    /// <summary>
    /// Read-only battery level colors shown while editing the battery mode.
    /// </summary>
    public IReadOnlyList<LightbarColorPreview> BatteryColorPreviews { get; } =
        SpecialActionEffect.DefaultBatteryColors
            .Select(c => new LightbarColorPreview(new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue))))
            .ToList();

    /// <summary>
    /// Whether the single-color mode is selected.
    /// </summary>
    public bool IsSingleColorMode
    {
        get
        {
            return LightbarModeIndex == (int)LightbarMode.SingleColor;
        }
    }

    /// <summary>
    /// Whether the multi-color mode is selected.
    /// </summary>
    public bool IsMultiColorMode
    {
        get
        {
            return LightbarModeIndex == (int)LightbarMode.MultiColor;
        }
    }

    /// <summary>
    /// Whether the breathing mode is selected.
    /// </summary>
    public bool IsBreathingMode
    {
        get
        {
            return LightbarModeIndex == (int)LightbarMode.Breathing;
        }
    }

    /// <summary>
    /// Whether the strobe mode is selected.
    /// </summary>
    public bool IsStrobeMode
    {
        get
        {
            return LightbarModeIndex == (int)LightbarMode.Strobe;
        }
    }

    /// <summary>
    /// Whether the battery mode is selected.
    /// </summary>
    public bool IsBatteryMode
    {
        get
        {
            return LightbarModeIndex == (int)LightbarMode.Battery;
        }
    }

    /// <summary>
    /// Whether the audio-reactive mode is selected.
    /// </summary>
    public bool IsAudioReactiveMode
    {
        get
        {
            return LightbarModeIndex == (int)LightbarMode.AudioReactive;
        }
    }

    /// <summary>
    /// Whether the currently selected mode uses the base lightbar color.
    /// </summary>
    public bool UsesBaseColor
    {
        get
        {
            return IsSingleColorMode || IsBreathingMode || IsStrobeMode;
        }
    }

    /// <summary>
    /// Whether the currently selected mode uses the effect speed.
    /// </summary>
    public bool UsesSpeed
    {
        get
        {
            return IsMultiColorMode || IsBreathingMode || IsStrobeMode;
        }
    }

    /// <summary>
    /// Whether the player LED editor is shown. Battery and audio-reactive modes hide it
    /// while the player LEDs are driven by the effect.
    /// </summary>
    public bool IsPlayerLedsSectionVisible
    {
        get
        {
            return !(IsBatteryMode && BatteryLevelPlayerLeds) &&
                   !(IsAudioReactiveMode && AudioPlayerLeds);
        }
    }

    /// <summary>
    /// Whether a palette color is selected for editing.
    /// </summary>
    public bool HasSelectedPaletteColor
    {
        get
        {
            return SelectedPaletteItem is not null;
        }
    }

    /// <summary>
    /// Brush for the lightbar color preview swatch.
    /// </summary>
    public IBrush LightbarBrush
    {
        get
        {
            return new SolidColorBrush(Color.FromRgb(Channel(LedRed), Channel(LedGreen), Channel(LedBlue)));
        }
    }

    /// <summary>
    /// Lightbar color as a "#RRGGBB" string.
    /// </summary>
    public string ColorHex
    {
        get
        {
            return $"#{Channel(LedRed):X2}{Channel(LedGreen):X2}{Channel(LedBlue):X2}";
        }
    }

    /// <summary>
    /// Lightbar color as an <see cref="Avalonia.Media.Color"/>, bridged two-way
    /// with the channel doubles for binding to <c>ColorView</c>.
    /// </summary>
    public Color LightbarColor
    {
        get
        {
            return Color.FromRgb(Channel(LedRed), Channel(LedGreen), Channel(LedBlue));
        }
        set
        {
            if (_syncingColor)
            {
                return;
            }

            _syncingColor = true;
            try
            {
                LedRed = value.R;
                LedGreen = value.G;
                LedBlue = value.B;
            }
            finally
            {
                _syncingColor = false;
            }

            OnPropertyChanged(nameof(LightbarColor));
        }
    }

    /// <summary>
    /// Creates a new profile editor item wrapping the given profile.
    /// </summary>
    /// <param name="profile">The profile to edit.</param>
    /// <param name="profileService">The profile service used for persistence.</param>
    public ProfileEditorItem(Profile profile, ProfileService profileService)
    {
        Profile = profile;
        _profileService = profileService;

        _ledRed = profile.Lightbar.Red;
        _ledGreen = profile.Lightbar.Green;
        _ledBlue = profile.Lightbar.Blue;
        _muteLedMode = profile.MicLed.Mode;
        _playerLed1 = (profile.PlayerLeds.Mask & 0x01) != 0;
        _playerLed2 = (profile.PlayerLeds.Mask & 0x02) != 0;
        _playerLed3 = (profile.PlayerLeds.Mask & 0x04) != 0;
        _playerLed4 = (profile.PlayerLeds.Mask & 0x08) != 0;
        _playerLed5 = (profile.PlayerLeds.Mask & 0x10) != 0;
        _playerPreset1 = profile.PlayerLeds.Mask == PlayerPresetMasks[0];
        _playerPreset2 = profile.PlayerLeds.Mask == PlayerPresetMasks[1];
        _playerPreset3 = profile.PlayerLeds.Mask == PlayerPresetMasks[2];
        _playerPreset4 = profile.PlayerLeds.Mask == PlayerPresetMasks[3];
        _playerPreset5 = profile.PlayerLeds.Mask == PlayerPresetMasks[4];
        _lightbarModeIndex = Enum.IsDefined(profile.Lightbar.Mode) ? (int)profile.Lightbar.Mode : 0;
        _lightbarBrightness = Math.Clamp(profile.Lightbar.Brightness, 0, 100);
        _lightbarSpeed = Math.Clamp(profile.Lightbar.Speed, 0, 100);
        _lightbarRandomColor = profile.Lightbar.RandomColor;
        _breathMin = Math.Clamp(profile.Lightbar.BreathMin, 0, 100);
        _breathMax = Math.Clamp(profile.Lightbar.BreathMax, 0, 100);
        _audioSensitivity = Math.Clamp(profile.Lightbar.AudioSensitivity, 0, 100);
        _audioPlayerLeds = profile.Lightbar.AudioPlayerLeds;
        _batteryChargingAnimation = profile.Lightbar.BatteryChargingAnimation;
        _batteryPlayerLeds = profile.Lightbar.BatteryPlayerLeds;
        _batteryPlayerLedSpeed = Math.Clamp(profile.Lightbar.BatteryPlayerLedSpeed, 0, 100);
        _batteryLevelPlayerLeds = profile.Lightbar.BatteryLevelPlayerLeds;
        _audioLowColor = ToColor(profile.Lightbar.AudioLowColor);
        _audioMidColor = ToColor(profile.Lightbar.AudioMidColor);
        _audioHighColor = ToColor(profile.Lightbar.AudioHighColor);

        foreach (LightbarColorSetting color in profile.Lightbar.Colors)
        {
            Palette.Add(new LightbarPaletteItem(ToColor(color), _ => Persist()));
        }

        SelectedPaletteItem = Palette.FirstOrDefault();

        _saveTimer = new DispatcherTimer
        {
            Interval = SaveDebounce
        };
        _saveTimer.Tick += (_, _) => CommitPendingChanges();
    }

    /// <summary>
    /// Re-raises the derived color properties and persists the new lightbar color.
    /// </summary>
    partial void OnLedRedChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Re-raises the derived color properties and persists the new lightbar color.
    /// </summary>
    partial void OnLedGreenChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Re-raises the derived color properties and persists the new lightbar color.
    /// </summary>
    partial void OnLedBlueChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Persists the new microphone LED mode.
    /// </summary>
    partial void OnMuteLedModeChanged(int value) => Persist();

    /// <summary>
    /// Persists the new player LED layout.
    /// </summary>
    partial void OnPlayerLed1Changed(bool value) => OnPlayerLedChanged();

    /// <summary>
    /// Persists the new player LED layout.
    /// </summary>
    partial void OnPlayerLed2Changed(bool value) => OnPlayerLedChanged();

    /// <summary>
    /// Persists the new player LED layout.
    /// </summary>
    partial void OnPlayerLed3Changed(bool value) => OnPlayerLedChanged();

    /// <summary>
    /// Persists the new player LED layout.
    /// </summary>
    partial void OnPlayerLed4Changed(bool value) => OnPlayerLedChanged();

    /// <summary>
    /// Persists the new player LED layout.
    /// </summary>
    partial void OnPlayerLed5Changed(bool value) => OnPlayerLedChanged();

    /// <summary>
    /// Applies the Player 1 preset (mask 0x04).
    /// </summary>
    partial void OnPlayerPreset1Changed(bool value) => ApplyPlayerPreset(0, value);

    /// <summary>
    /// Applies the Player 2 preset (mask 0x0A).
    /// </summary>
    partial void OnPlayerPreset2Changed(bool value) => ApplyPlayerPreset(1, value);

    /// <summary>
    /// Applies the Player 3 preset (mask 0x15).
    /// </summary>
    partial void OnPlayerPreset3Changed(bool value) => ApplyPlayerPreset(2, value);

    /// <summary>
    /// Applies the Player 4 preset (mask 0x1B).
    /// </summary>
    partial void OnPlayerPreset4Changed(bool value) => ApplyPlayerPreset(3, value);

    /// <summary>
    /// Applies the Player 5 preset (mask 0x1F).
    /// </summary>
    partial void OnPlayerPreset5Changed(bool value) => ApplyPlayerPreset(4, value);

    /// <summary>
    /// Re-raises the mode-dependent visibility properties and persists the new mode.
    /// </summary>
    partial void OnLightbarModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSingleColorMode));
        OnPropertyChanged(nameof(IsMultiColorMode));
        OnPropertyChanged(nameof(IsBreathingMode));
        OnPropertyChanged(nameof(IsStrobeMode));
        OnPropertyChanged(nameof(IsBatteryMode));
        OnPropertyChanged(nameof(IsAudioReactiveMode));
        OnPropertyChanged(nameof(UsesBaseColor));
        OnPropertyChanged(nameof(UsesSpeed));
        OnPropertyChanged(nameof(IsPlayerLedsSectionVisible));
        Persist();
    }

    /// <summary>
    /// Re-raises the selection-dependent visibility property.
    /// </summary>
    partial void OnSelectedPaletteItemChanged(LightbarPaletteItem? value) =>
        OnPropertyChanged(nameof(HasSelectedPaletteColor));

    /// <summary>
    /// Persists the new lightbar brightness.
    /// </summary>
    partial void OnLightbarBrightnessChanged(double value) => Persist();

    /// <summary>
    /// Persists the new effect speed.
    /// </summary>
    partial void OnLightbarSpeedChanged(double value) => Persist();

    /// <summary>
    /// Persists the new random palette order.
    /// </summary>
    partial void OnLightbarRandomColorChanged(bool value) => Persist();

    /// <summary>
    /// Persists the new breath minimum.
    /// </summary>
    partial void OnBreathMinChanged(double value) => Persist();

    /// <summary>
    /// Persists the new breath maximum.
    /// </summary>
    partial void OnBreathMaxChanged(double value) => Persist();

    /// <summary>
    /// Persists the new audio sensitivity.
    /// </summary>
    partial void OnAudioSensitivityChanged(double value) => Persist();

    /// <summary>
    /// Clears the manual player LED layout when the audio level takes over the player
    /// LEDs, then re-raises the section visibility and persists the setting.
    /// </summary>
    partial void OnAudioPlayerLedsChanged(bool value)
    {
        if (value)
        {
            SetPlayerLedMask(0);
        }

        OnPropertyChanged(nameof(IsPlayerLedsSectionVisible));
        Persist();
    }

    /// <summary>
    /// Persists the new battery lightbar animation setting.
    /// </summary>
    partial void OnBatteryChargingAnimationChanged(bool value) => Persist();

    /// <summary>
    /// Persists the new battery player-LED setting.
    /// </summary>
    partial void OnBatteryPlayerLedsChanged(bool value) => Persist();

    /// <summary>
    /// Persists the new battery player-LED chase speed.
    /// </summary>
    partial void OnBatteryPlayerLedSpeedChanged(double value) => Persist();

    /// <summary>
    /// Clears the manual player LED layout when the battery level takes over the player
    /// LEDs, then re-raises the section visibility and persists the setting.
    /// </summary>
    partial void OnBatteryLevelPlayerLedsChanged(bool value)
    {
        if (value)
        {
            SetPlayerLedMask(0);
        }

        OnPropertyChanged(nameof(IsPlayerLedsSectionVisible));
        Persist();
    }

    /// <summary>
    /// Persists the new low audio color.
    /// </summary>
    partial void OnAudioLowColorChanged(Color value) => Persist();

    /// <summary>
    /// Persists the new mid audio color.
    /// </summary>
    partial void OnAudioMidColorChanged(Color value) => Persist();

    /// <summary>
    /// Persists the new high audio color.
    /// </summary>
    partial void OnAudioHighColorChanged(Color value) => Persist();

    /// <summary>
    /// Handles an individual LED toggle: persists the layout and re-syncs the preset
    /// checked state (a mask matching a preset checks it, any other mask clears them).
    /// Skipped while a preset sync is applying the individual LEDs.
    /// </summary>
    private void OnPlayerLedChanged()
    {
        if (_syncingPreset)
        {
            return;
        }

        Persist();
        SyncPlayerPresetCheckedState();
    }

    /// <summary>
    /// Applies a player LED preset by setting the mirror-symmetric mask. Unchecking a
    /// preset turns those player LEDs off. Preset changes made programmatically during
    /// a sync are ignored.
    /// </summary>
    private void ApplyPlayerPreset(int preset, bool value)
    {
        if (_syncingPreset)
        {
            return;
        }

        SetPlayerLedMask(value ? PlayerPresetMasks[preset] : (byte)0);
    }

    /// <summary>
    /// Sets the player LED mask, updating the individual LED toggles, persisting the
    /// layout, and re-syncing the preset checked state.
    /// </summary>
    private void SetPlayerLedMask(byte mask)
    {
        _syncingPreset = true;
        try
        {
            PlayerLed1 = (mask & 0x01) != 0;
            PlayerLed2 = (mask & 0x02) != 0;
            PlayerLed3 = (mask & 0x04) != 0;
            PlayerLed4 = (mask & 0x08) != 0;
            PlayerLed5 = (mask & 0x10) != 0;
        }
        finally
        {
            _syncingPreset = false;
        }

        Persist();
        SyncPlayerPresetCheckedState();
    }

    /// <summary>
    /// Checks the preset whose mask matches the current LED layout and clears the others.
    /// </summary>
    private void SyncPlayerPresetCheckedState()
    {
        byte mask = ComputePlayerLedMask();
        _syncingPreset = true;
        try
        {
            PlayerPreset1 = mask == PlayerPresetMasks[0];
            PlayerPreset2 = mask == PlayerPresetMasks[1];
            PlayerPreset3 = mask == PlayerPresetMasks[2];
            PlayerPreset4 = mask == PlayerPresetMasks[3];
            PlayerPreset5 = mask == PlayerPresetMasks[4];
        }
        finally
        {
            _syncingPreset = false;
        }
    }

    /// <summary>
    /// Re-raises the derived color properties and persists the new color.
    /// </summary>
    private void NotifyColorChanged()
    {
        if (!_syncingColor)
        {
            OnPropertyChanged(nameof(LightbarColor));
        }

        OnPropertyChanged(nameof(LightbarBrush));
        OnPropertyChanged(nameof(ColorHex));
        Persist();
    }

    /// <summary>
    /// Writes the current UI state back into <see cref="Profile"/> immediately (so the
    /// in-memory profile is always current) and schedules a debounced disk save.
    /// </summary>
    private void Persist()
    {
        if (_disposed)
        {
            return;
        }

        Profile.Lightbar.Red = Channel(LedRed);
        Profile.Lightbar.Green = Channel(LedGreen);
        Profile.Lightbar.Blue = Channel(LedBlue);
        Profile.Lightbar.Mode = (LightbarMode)Math.Clamp(LightbarModeIndex, 0, 5);
        Profile.Lightbar.Brightness = (int)Math.Round(Math.Clamp(LightbarBrightness, 0, 100));
        Profile.Lightbar.Speed = (int)Math.Round(Math.Clamp(LightbarSpeed, 0, 100));
        Profile.Lightbar.RandomColor = LightbarRandomColor;
        int breathMin = (int)Math.Round(Math.Clamp(BreathMin, 0, 100));
        int breathMax = (int)Math.Round(Math.Clamp(BreathMax, 0, 100));
        Profile.Lightbar.BreathMin = Math.Min(breathMin, breathMax);
        Profile.Lightbar.BreathMax = Math.Max(breathMin, breathMax);
        Profile.Lightbar.Colors = Palette.Select(p => ToSetting(p.Color)).ToList();
        Profile.Lightbar.AudioLowColor = ToSetting(AudioLowColor);
        Profile.Lightbar.AudioMidColor = ToSetting(AudioMidColor);
        Profile.Lightbar.AudioHighColor = ToSetting(AudioHighColor);
        Profile.Lightbar.AudioSensitivity = (int)Math.Round(Math.Clamp(AudioSensitivity, 0, 100));
        Profile.Lightbar.AudioPlayerLeds = AudioPlayerLeds;
        Profile.Lightbar.BatteryChargingAnimation = BatteryChargingAnimation;
        Profile.Lightbar.BatteryPlayerLeds = BatteryPlayerLeds;
        Profile.Lightbar.BatteryPlayerLedSpeed = (int)Math.Round(Math.Clamp(BatteryPlayerLedSpeed, 0, 100));
        Profile.Lightbar.BatteryLevelPlayerLeds = BatteryLevelPlayerLeds;
        Profile.MicLed.Mode = (byte)Math.Clamp(MuteLedMode, 0, 2);
        Profile.PlayerLeds.Mask = ComputePlayerLedMask();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
        ScheduleCommit();
    }

    /// <summary>
    /// Restarts the debounce timer so the pending save is delayed until edits stop.
    /// </summary>
    private void ScheduleCommit()
    {
        if (_disposed)
        {
            return;
        }

        _pendingCommit = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Flushes the pending changes to disk once the debounce period elapses.
    /// </summary>
    private void CommitPendingChanges()
    {
        _saveTimer.Stop();

        if (_disposed)
        {
            return;
        }

        _pendingCommit = false;
        _profileService.Save();
    }

    /// <summary>
    /// Builds the player LED byte mask from the five booleans (bit 0 = LED 1, ... bit 4 = LED 5).
    /// </summary>
    private byte ComputePlayerLedMask()
    {
        byte mask = 0;
        if (PlayerLed1)
        {
            mask |= 0x01;
        }

        if (PlayerLed2)
        {
            mask |= 0x02;
        }

        if (PlayerLed3)
        {
            mask |= 0x04;
        }

        if (PlayerLed4)
        {
            mask |= 0x08;
        }

        if (PlayerLed5)
        {
            mask |= 0x10;
        }

        return mask;
    }

    /// <summary>
    /// Converts a slider value to the 0-255 channel byte.
    /// </summary>
    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));

    /// <summary>
    /// Appends the current base color to the multi-color palette and selects it.
    /// </summary>
    [RelayCommand]
    private void AddPaletteColor()
    {
        LightbarPaletteItem item = new LightbarPaletteItem(LightbarColor, _ => Persist());
        Palette.Add(item);
        SelectedPaletteItem = item;
        Persist();
    }

    /// <summary>
    /// Replaces the palette with the six default colors.
    /// </summary>
    [RelayCommand]
    private void ResetPalette()
    {
        Palette.Clear();
        foreach (LightbarColorSetting color in LightbarSettings.CreateDefaultColors())
        {
            Palette.Add(new LightbarPaletteItem(ToColor(color), _ => Persist()));
        }

        SelectedPaletteItem = Palette.FirstOrDefault();
        Persist();
    }

    /// <summary>
    /// Removes the selected palette color and selects the nearest remaining color.
    /// </summary>
    [RelayCommand]
    private void RemoveSelectedPaletteColor()
    {
        if (SelectedPaletteItem is not { } item)
        {
            return;
        }

        int index = Palette.IndexOf(item);
        Palette.Remove(item);
        SelectedPaletteItem = Palette.Count == 0
            ? null
            : Palette[Math.Min(index, Palette.Count - 1)];
        Persist();
    }

    /// <summary>
    /// Converts a stored color to an Avalonia color.
    /// </summary>
    private static Color ToColor(LightbarColorSetting color) =>
        Color.FromRgb(color.Red, color.Green, color.Blue);

    /// <summary>
    /// Converts an Avalonia color to a stored color.
    /// </summary>
    private static LightbarColorSetting ToSetting(Color color) =>
        new LightbarColorSetting
        {
            Red = color.R,
            Green = color.G,
            Blue = color.B
        };

    /// <summary>
    /// Releases the item: stops the debounce timer and flushes any pending changes so
    /// edits made just before disposal are not lost.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Stop();
        if (_pendingCommit)
        {
            _pendingCommit = false;
            _profileService.Save();
        }
    }
}

/// <summary>
/// Display model for one color of the multi-color lightbar palette.
/// </summary>
public sealed partial class LightbarPaletteItem : ObservableObject
{
    /// <summary>
    /// Invoked whenever the color changes, so the owning item can persist.
    /// </summary>
    private readonly Action<LightbarPaletteItem> _onChanged;

    /// <summary>
    /// The palette color.
    /// </summary>
    [ObservableProperty] private Color _color;

    /// <summary>
    /// Creates a new palette color.
    /// </summary>
    /// <param name="color">The initial color.</param>
    /// <param name="onChanged">Callback invoked when the color changes.</param>
    public LightbarPaletteItem(Color color, Action<LightbarPaletteItem> onChanged)
    {
        _color = color;
        _onChanged = onChanged;
    }

    /// <summary>
    /// Red channel (0-255).
    /// </summary>
    public double Red
    {
        get
        {
            return Color.R;
        }
        set
        {
            Color = Color.FromRgb(Channel(value), Color.G, Color.B);
        }
    }

    /// <summary>
    /// Green channel (0-255).
    /// </summary>
    public double Green
    {
        get
        {
            return Color.G;
        }
        set
        {
            Color = Color.FromRgb(Color.R, Channel(value), Color.B);
        }
    }

    /// <summary>
    /// Blue channel (0-255).
    /// </summary>
    public double Blue
    {
        get
        {
            return Color.B;
        }
        set
        {
            Color = Color.FromRgb(Color.R, Color.G, Channel(value));
        }
    }

    /// <summary>
    /// Color as a "#RRGGBB" string.
    /// </summary>
    public string Hex
    {
        get
        {
            return $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
        }
    }

    /// <summary>
    /// Brush for the palette swatch.
    /// </summary>
    public IBrush Brush
    {
        get
        {
            return new SolidColorBrush(Color);
        }
    }

    /// <summary>
    /// Re-raises the channel and display properties and forwards the change.
    /// </summary>
    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Brush));
        _onChanged(this);
    }

    /// <summary>
    /// Converts a slider value to the 0-255 channel byte.
    /// </summary>
    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}

/// <summary>
/// Read-only preview brush for one battery level color.
/// </summary>
public sealed class LightbarColorPreview
{
    /// <summary>
    /// The brush used to paint the preview swatch.
    /// </summary>
    public IBrush Brush { get; }

    /// <summary>
    /// Creates a new battery color preview.
    /// </summary>
    /// <param name="brush">The brush used to paint the preview swatch.</param>
    public LightbarColorPreview(IBrush brush)
    {
        Brush = brush;
    }
}