using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.GUI.Services;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display model for a single <see cref="SpecialActionButtonItem"/> toggle: one controller
/// button and whether it is part of the combination.
/// </summary>
public sealed partial class SpecialActionButtonItem : ObservableObject
{
    /// <summary>
    /// The button this toggle represents.
    /// </summary>
    public ButtonType Button { get; }

    /// <summary>
    /// The localized button name shown on the toggle.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Invoked with the new state whenever the toggle changes.
    /// </summary>
    private readonly Action<bool> _onChanged;

    /// <summary>
    /// Whether the button is part of the combination.
    /// </summary>
    [ObservableProperty] private bool _isChecked;

    /// <summary>
    /// Creates a new button toggle.
    /// </summary>
    /// <param name="button">The button this toggle represents.</param>
    /// <param name="isChecked">Whether the button is initially part of the combination.</param>
    /// <param name="onChanged">Callback invoked with the new state on change.</param>
    public SpecialActionButtonItem(ButtonType button, bool isChecked, Action<bool> onChanged)
    {
        Button = button;
        DisplayName = GetDisplayName(button);
        _isChecked = isChecked;
        _onChanged = onChanged;
    }

    /// <summary>
    /// The localized display name of a button (e.g. "D-Pad Up", "Left Fn"), falling back
    /// to the enum name when no translation exists.
    /// </summary>
    public static string GetDisplayName(ButtonType button) =>
        LocalizationService.GetText($"ProfilePage.SpecialActions.Button.{button}");

    /// <summary>
    /// Forwards toggle changes to the owning item.
    /// </summary>
    partial void OnIsCheckedChanged(bool value) => _onChanged(value);
}

/// <summary>
/// Display model for one of the 10 charge levels of a battery-level special action:
/// the level entry shown in the level selector and the color edited through the shared
/// color picker.
/// </summary>
public sealed partial class BatteryLevelItem : ObservableObject
{
    /// <summary>
    /// Invoked whenever the color changes, so the owning item can persist.
    /// </summary>
    private readonly Action _onColorChanged;

    /// <summary>
    /// Prevents feedback loops between the color picker and the channel sliders.
    /// </summary>
    private bool _syncingColor;

    /// <summary>
    /// The level index (0 = lowest charge, 9 = full).
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// The charge range label (e.g. "30-39%", "90-100%").
    /// </summary>
    public string DisplayName => Level == 9 ? "90-100%" : $"{Level * 10}-{Level * 10 + 9}%";

    /// <summary>
    /// Red channel of this level's color (0-255).
    /// </summary>
    [ObservableProperty] private double _red;

    /// <summary>
    /// Green channel of this level's color (0-255).
    /// </summary>
    [ObservableProperty] private double _green;

    /// <summary>
    /// Blue channel of this level's color (0-255).
    /// </summary>
    [ObservableProperty] private double _blue;

    /// <summary>
    /// The level color for the color picker. The initial channels are set directly on the
    /// backing fields so the constructor does not raise change notifications.
    /// </summary>
    /// <param name="level">The level index (0-9).</param>
    /// <param name="red">Initial red channel.</param>
    /// <param name="green">Initial green channel.</param>
    /// <param name="blue">Initial blue channel.</param>
    /// <param name="onColorChanged">Callback invoked when the color changes.</param>
    public BatteryLevelItem(int level, byte red, byte green, byte blue, Action onColorChanged)
    {
        Level = level;
        _red = red;
        _green = green;
        _blue = blue;
        _onColorChanged = onColorChanged;
    }

    /// <summary>
    /// The level color for the color picker (kept in sync with the channel sliders).
    /// </summary>
    public Color Color
    {
        get => Color.FromRgb(Channel(Red), Channel(Green), Channel(Blue));
        set
        {
            if (_syncingColor)
            {
                return;
            }

            _syncingColor = true;
            try
            {
                Red = value.R;
                Green = value.G;
                Blue = value.B;
            }
            finally
            {
                _syncingColor = false;
            }

            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(Brush));
        }
    }

    /// <summary>
    /// The level color as a brush, for the swatch.
    /// </summary>
    public IBrush Brush => new SolidColorBrush(Color);

    /// <summary>
    /// Persists the new color.
    /// </summary>
    partial void OnRedChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Persists the new color.
    /// </summary>
    partial void OnGreenChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Persists the new color.
    /// </summary>
    partial void OnBlueChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Re-raises the derived color properties and notifies the owner.
    /// </summary>
    private void NotifyColorChanged()
    {
        if (!_syncingColor)
        {
            OnPropertyChanged(nameof(Color));
        }

        OnPropertyChanged(nameof(Brush));
        _onColorChanged();
    }

    /// <summary>
    /// Converts a slider value to the 0-255 channel byte.
    /// </summary>
    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
}

/// <summary>
/// Display model for editing a single <see cref="SpecialAction"/> on the profile page.
/// Exposes the name, per-controller enablement toggle, button combination, effect toggles
/// (at most one effect per type), and effect parameters, and persists every change back to
/// the special actions file (debounced, mirroring <see cref="ProfileEditorItem"/>).
/// </summary>
/// <remarks>
/// <para>
/// The combination is stored in the action as button names; the item bridges the UI toggles
/// and the name list on load and on every change.
/// </para>
/// <para>
/// Effects are stored in <see cref="SpecialAction.Effects"/>; the parameter properties
/// (lightbar channels, player LEDs, sound) edit the matching effect, which the UI shows or
/// hides depending on which effects are enabled.
/// </para>
/// </remarks>
public sealed partial class SpecialActionItem : ObservableObject, IDisposable
{
    /// <summary>
    /// Number of charge levels of the battery-level effect.
    /// </summary>
    private const int BatteryLevelCount = 10;

    /// <summary>
    /// Delay between the last edit and the disk save, so rapid changes (e.g. dragging a
    /// slider) are coalesced into a single write once the user releases control.
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Player LED preset masks, mirror-symmetric layouts used by PS5 (unconfirmed):
    /// Player 1 0x04, Player 2 0x06, Player 3 0x15, Player 4 0x1B, Player 5 0x1F.
    /// </summary>
    private static readonly byte[] PlayerPresetMasks = [0x04, 0x06, 0x15, 0x1B, 0x1F];

    /// <summary>
    /// The special action service backing persistence for this item.
    /// </summary>
    private readonly SpecialActionService _service;

    /// <summary>
    /// The identifier of the controller this page is currently showing, or <c>null</c> when
    /// no controller is selected.
    /// </summary>
    private readonly string? _controllerId;

    /// <summary>
    /// Debounced save timer; each edit restarts it and the save happens only after edits stop.
    /// </summary>
    private readonly DispatcherTimer _saveTimer;

    /// <summary>
    /// Prevents feedback loops between the lightbar color picker and the channel sliders.
    /// </summary>
    private bool _syncingLightbarColor;

    /// <summary>
    /// Suppresses effect-change handling while <see cref="DisableEffect"/> unchecks
    /// conflicting toggles, so the mutual-exclusion logic does not re-enter itself.
    /// </summary>
    private bool _suppressEffectChanges;

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
    /// Raised when the user requests deletion of this action.
    /// </summary>
    public event EventHandler? DeleteRequested;

    /// <summary>
    /// The action being edited.
    /// </summary>
    public SpecialAction Action { get; }

    /// <summary>
    /// One toggle per controller button, checked when the button is part of the combination.
    /// </summary>
    public ObservableCollection<SpecialActionButtonItem> Buttons { get; } = [];

    /// <summary>
    /// The selected buttons of the combination, joined with " + " (or a placeholder when
    /// none are selected), shown in the special actions list to identify the action. When a
    /// touchpad gesture is selected, the gesture name is shown instead.
    /// </summary>
    public string ComboSummary
    {
        get
        {
            if (HasGesture)
            {
                return GestureOptions[SelectedGestureIndex];
            }

            string[] names = Buttons.Where(b => b.IsChecked).Select(b => b.DisplayName).ToArray();
            return names.Length > 0
                ? string.Join(" + ", names)
                : LocalizationService.GetText("ProfilePage.SpecialActions.Combo.None");
        }
    }

    /// <summary>
    /// Whether the action is triggered by a touchpad gesture instead of a button combination.
    /// </summary>
    public bool HasGesture => !string.IsNullOrEmpty(TouchpadGesture);

    /// <summary>
    /// Whether the current controller's hardware supports full player-LED functionality.
    /// When <c>false</c>, only the mirrored player presets are offered for the
    /// set-player-LEDs effect and the individual LED toggles are hidden.
    /// </summary>
    public bool HasFullPlayerLedSupport { get; }

    /// <summary>
    /// The trigger type options, in selection order (0 = button combination, 1 = touchpad
    /// gesture). Only one trigger can be selected per action.
    /// </summary>
    public ObservableCollection<string> TriggerOptions { get; } =
    [
        LocalizationService.GetText("ProfilePage.SpecialActions.Trigger.Buttons"),
        LocalizationService.GetText("ProfilePage.SpecialActions.Trigger.Touchpad")
    ];

    /// <summary>
    /// The selected entry in <see cref="TriggerOptions"/>, bridged to
    /// <see cref="TouchpadGesture"/>: the touchpad trigger stores the current swipe
    /// direction, the button trigger clears it.
    /// </summary>
    public int SelectedTriggerIndex
    {
        get => HasGesture ? 1 : 0;
        set => TouchpadGesture = value == 1 ? Gesture : null;
    }

    /// <summary>
    /// The touchpad gesture that triggers the action, one of <see cref="TouchpadGestures"/>,
    /// or <c>null</c> for a button combination. Changing it persists immediately.
    /// </summary>
    [ObservableProperty] private string? _touchpadGesture;

    /// <summary>
    /// The swipe direction selected for the touchpad trigger, one of
    /// <see cref="TouchpadGestures"/>. Kept separate from <see cref="TouchpadGesture"/> so
    /// the chosen direction is preserved when the trigger is switched back to a button
    /// combination.
    /// </summary>
    [ObservableProperty] private string _gesture = TouchpadGestures.SwipeUp;

    /// <summary>
    /// The swipe direction options, in selection order (up, down, left, right).
    /// </summary>
    public ObservableCollection<string> GestureOptions { get; } =
    [
        LocalizationService.GetText("ProfilePage.SpecialActions.Gesture.SwipeUp"),
        LocalizationService.GetText("ProfilePage.SpecialActions.Gesture.SwipeDown"),
        LocalizationService.GetText("ProfilePage.SpecialActions.Gesture.SwipeLeft"),
        LocalizationService.GetText("ProfilePage.SpecialActions.Gesture.SwipeRight")
    ];

    /// <summary>
    /// The selected entry in <see cref="GestureOptions"/>, bridged to <see cref="Gesture"/>.
    /// </summary>
    public int SelectedGestureIndex
    {
        get
        {
            if (string.Equals(Gesture, TouchpadGestures.SwipeDown, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(Gesture, TouchpadGestures.SwipeLeft, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(Gesture, TouchpadGestures.SwipeRight, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            return 0;
        }
        set => Gesture = value switch
        {
            1 => TouchpadGestures.SwipeDown,
            2 => TouchpadGestures.SwipeLeft,
            3 => TouchpadGestures.SwipeRight,
            _ => TouchpadGestures.SwipeUp
        };
    }

    /// <summary>
    /// The name shown in the list. Renaming updates the action in memory and schedules a save.
    /// </summary>
    public string Name
    {
        get => Action.Name;
        set
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed) || string.Equals(Action.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged();
                return;
            }

            Action.Name = trimmed;
            OnPropertyChanged();
            Persist();
        }
    }

    /// <summary>
    /// Whether the action is enabled for the controller currently shown on the page.
    /// Toggling persists immediately (a dedicated service call, like the profile assignment).
    /// </summary>
    [ObservableProperty] private bool _isEnabledForThisController;

    /// <summary>
    /// Whether the disconnect effect is part of the action. A type can appear at most once,
    /// so the toggle both enables the effect and enforces the no-duplicates rule.
    /// </summary>
    [ObservableProperty] private bool _effectDisconnect;

    /// <summary>
    /// Whether the set-lightbar-color effect is part of the action.
    /// </summary>
    [ObservableProperty] private bool _effectLightbar;

    /// <summary>
    /// Whether the set-player-LEDs effect is part of the action.
    /// </summary>
    [ObservableProperty] private bool _effectPlayerLeds;

    /// <summary>
    /// Whether the play-sound effect is part of the action.
    /// </summary>
    [ObservableProperty] private bool _effectSound;

    /// <summary>
    /// Whether the show-battery-level effect is part of the action. It cannot be combined
    /// with the set-lightbar-color or set-player-LEDs effects: enabling it disables those,
    /// and enabling either of those disables it.
    /// </summary>
    [ObservableProperty] private bool _effectBattery;

    /// <summary>
    /// Lightbar red channel (0-255), used by the set-lightbar-color effect.
    /// </summary>
    [ObservableProperty] private double _ledRed;

    /// <summary>
    /// Lightbar green channel (0-255), used by the set-lightbar-color effect.
    /// </summary>
    [ObservableProperty] private double _ledGreen;

    /// <summary>
    /// Lightbar blue channel (0-255), used by the set-lightbar-color effect.
    /// </summary>
    [ObservableProperty] private double _ledBlue = 255;

    /// <summary>
    /// Whether player LED 1 (leftmost) is part of the layout, used by the set-player-LEDs effect.
    /// </summary>
    [ObservableProperty] private bool _playerLed1;

    /// <summary>
    /// Whether player LED 2 is part of the layout, used by the set-player-LEDs effect.
    /// </summary>
    [ObservableProperty] private bool _playerLed2;

    /// <summary>
    /// Whether player LED 3 (center) is part of the layout, used by the set-player-LEDs effect.
    /// </summary>
    [ObservableProperty] private bool _playerLed3;

    /// <summary>
    /// Whether player LED 4 is part of the layout, used by the set-player-LEDs effect.
    /// </summary>
    [ObservableProperty] private bool _playerLed4;

    /// <summary>
    /// Whether player LED 5 (rightmost) is part of the layout, used by the set-player-LEDs effect.
    /// </summary>
    [ObservableProperty] private bool _playerLed5;

    /// <summary>
    /// Whether the Player 1 preset (mirror-symmetric mask 0x04) is selected.
    /// </summary>
    [ObservableProperty] private bool _playerPreset1;

    /// <summary>
    /// Whether the Player 2 preset (mirror-symmetric mask 0x06) is selected.
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
    /// How long the combination must be held (in seconds, 0-10) before the action fires.
    /// Bridged to <see cref="SpecialAction.HoldTimeMs"/> on load and persist.
    /// </summary>
    [ObservableProperty] private double _holdTimeSeconds;

    /// <summary>
    /// Whether the action's effects apply only while the combination is held (light effects
    /// revert and a sound effect stops on release) instead of staying applied.
    /// </summary>
    [ObservableProperty] private bool _applyWhileHeld;

    /// <summary>
    /// How long the light effects stay applied (in seconds, 0-60) before the bound profile
    /// is restored. <c>0</c> keeps them applied. Mutually exclusive with
    /// <see cref="ApplyWhileHeld"/>. Bridged to <see cref="SpecialAction.DurationMs"/> on
    /// load and persist.
    /// </summary>
    [ObservableProperty] private double _durationSeconds;

    /// <summary>
    /// The audio file played by the play-sound effect.
    /// </summary>
    public string? SoundPath
    {
        get => Effect(SpecialActionTypes.PlaySound)?.Sound.Path;
        set
        {
            SpecialActionEffect? effect = Effect(SpecialActionTypes.PlaySound);
            string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (effect is null || string.Equals(effect.Sound.Path, normalized, StringComparison.Ordinal))
            {
                OnPropertyChanged();
                return;
            }

            effect.Sound.Path = normalized;
            OnPropertyChanged();
            Persist();
        }
    }

    /// <summary>
    /// Controller speaker volume (0-255), used by the play-sound effect.
    /// </summary>
    [ObservableProperty] private int _soundVolume = 0x50;

    /// <summary>
    /// The device the play-sound effect plays through, one of <see cref="SoundOutputDevices"/>.
    /// </summary>
    [ObservableProperty] private string _soundOutputDevice = SoundOutputDevices.Speaker;

    /// <summary>
    /// The audio output device options for the play-sound effect, in selection order
    /// (index 0 = speaker, 1 = headset).
    /// </summary>
    public ObservableCollection<string> SoundOutputOptions { get; } =
    [
        LocalizationService.GetText("ProfilePage.SpecialActions.Sound.Output.Speaker"),
        LocalizationService.GetText("ProfilePage.SpecialActions.Sound.Output.Headset")
    ];

    /// <summary>
    /// The selected entry in <see cref="SoundOutputOptions"/>.
    /// </summary>
    public int SelectedSoundOutputIndex
    {
        get => string.Equals(SoundOutputDevice, SoundOutputDevices.Headset, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        set => SoundOutputDevice = value == 1 ? SoundOutputDevices.Headset : SoundOutputDevices.Speaker;
    }

    /// <summary>
    /// Whether the sound drives the controller's haptic actuators.
    /// </summary>
    [ObservableProperty] private bool _hapticFeedback;

    /// <summary>
    /// Haptic vibration strength as a percentage (0-200).
    /// </summary>
    [ObservableProperty] private int _hapticStrength = 100;

    /// <summary>
    /// The 10 charge levels of the show-battery-level effect (level 0 = lowest charge).
    /// </summary>
    public ObservableCollection<BatteryLevelItem> BatteryLevels { get; } = [];

    /// <summary>
    /// The level whose color the shared picker is currently editing.
    /// </summary>
    [ObservableProperty] private int _selectedBatteryLevel;

    /// <summary>
    /// Red channel of the selected battery level's color (0-255).
    /// </summary>
    public double SelectedBatteryRed
    {
        get => SelectedBatteryLevelItem.Red;
        set => SelectedBatteryLevelItem.Red = value;
    }

    /// <summary>
    /// Green channel of the selected battery level's color (0-255).
    /// </summary>
    public double SelectedBatteryGreen
    {
        get => SelectedBatteryLevelItem.Green;
        set => SelectedBatteryLevelItem.Green = value;
    }

    /// <summary>
    /// Blue channel of the selected battery level's color (0-255).
    /// </summary>
    public double SelectedBatteryBlue
    {
        get => SelectedBatteryLevelItem.Blue;
        set => SelectedBatteryLevelItem.Blue = value;
    }

    /// <summary>
    /// The selected battery level's color, for the shared color picker.
    /// </summary>
    public Color SelectedBatteryColor
    {
        get => SelectedBatteryLevelItem.Color;
        set => SelectedBatteryLevelItem.Color = value;
    }

    /// <summary>
    /// The level whose color the shared picker is editing (never out of range).
    /// </summary>
    private BatteryLevelItem SelectedBatteryLevelItem => BatteryLevels[Math.Clamp(SelectedBatteryLevel, 0, BatteryLevels.Count - 1)];

    /// <summary>
    /// Whether the action has the set-lightbar-color effect enabled.
    /// </summary>
    public bool IsColorAction => Effect(SpecialActionTypes.SetLightbarColor)?.Enabled ?? false;

    /// <summary>
    /// Whether the action has the set-player-LEDs effect enabled.
    /// </summary>
    public bool IsPlayerLedsAction => Effect(SpecialActionTypes.SetPlayerLeds)?.Enabled ?? false;

    /// <summary>
    /// Whether the action has the play-sound effect enabled.
    /// </summary>
    public bool IsSoundAction => Effect(SpecialActionTypes.PlaySound)?.Enabled ?? false;

    /// <summary>
    /// Whether the action has the show-battery-level effect enabled.
    /// </summary>
    public bool IsBatteryAction => Effect(SpecialActionTypes.ShowBatteryLevel)?.Enabled ?? false;

    /// <summary>
    /// Whether the apply-while-held toggle is relevant: light and sound effects support it,
    /// but touchpad-triggered actions never use it.
    /// </summary>
    public bool IsApplyWhileHeldVisible => !HasGesture && (IsColorAction || IsPlayerLedsAction || IsBatteryAction || IsSoundAction);

    /// <summary>
    /// Whether the duration field is relevant: the light effects support the timed restore.
    /// </summary>
    public bool IsDurationVisible => IsColorAction || IsPlayerLedsAction || IsBatteryAction;

    /// <summary>
    /// Whether the haptic strength slider is shown (sound effects with haptics enabled).
    /// </summary>
    public bool IsHapticVisible => IsSoundAction && HapticFeedback;

    /// <summary>
    /// The lightbar color of the set-lightbar-color effect, kept in sync with the channel
    /// sliders. The two-way guard prevents feedback between the color picker and the sliders.
    /// </summary>
    public Color LightbarColor
    {
        get => Color.FromRgb(Channel(LedRed), Channel(LedGreen), Channel(LedBlue));
        set
        {
            if (_syncingLightbarColor)
            {
                return;
            }

            _syncingLightbarColor = true;
            try
            {
                LedRed = value.R;
                LedGreen = value.G;
                LedBlue = value.B;
            }
            finally
            {
                _syncingLightbarColor = false;
            }

            OnPropertyChanged(nameof(LightbarColor));
        }
    }

    /// <summary>
    /// Creates a new item wrapping the given action.
    /// </summary>
    /// <param name="action">The action to edit.</param>
    /// <param name="service">The special action service used for persistence.</param>
    /// <param name="controllerId">The identifier of the controller shown on the page, or <c>null</c>.</param>
    /// <param name="hasFullPlayerLedSupport">
    /// Whether the current controller's hardware supports full player-LED functionality.
    /// Generation 0x04 and above is restricted to Mirrored Only, in which case only the
    /// mirrored player presets are offered and the individual LED toggles are hidden.
    /// </param>
    public SpecialActionItem(SpecialAction action, SpecialActionService service, string? controllerId, bool hasFullPlayerLedSupport = false)
    {
        Action = action;
        _service = service;
        _controllerId = controllerId;
        HasFullPlayerLedSupport = hasFullPlayerLedSupport;

        _effectDisconnect = Effect(SpecialActionTypes.Disconnect)?.Enabled ?? false;
        _effectLightbar = Effect(SpecialActionTypes.SetLightbarColor)?.Enabled ?? false;
        _effectPlayerLeds = Effect(SpecialActionTypes.SetPlayerLeds)?.Enabled ?? false;
        _effectSound = Effect(SpecialActionTypes.PlaySound)?.Enabled ?? false;
        _effectBattery = Effect(SpecialActionTypes.ShowBatteryLevel)?.Enabled ?? false;
        _isEnabledForThisController = SpecialActionService.IsEnabledFor(action, controllerId);
        _ledRed = Effect(SpecialActionTypes.SetLightbarColor)?.Lightbar.Red ?? 0;
        _ledGreen = Effect(SpecialActionTypes.SetLightbarColor)?.Lightbar.Green ?? 0;
        _ledBlue = Effect(SpecialActionTypes.SetLightbarColor)?.Lightbar.Blue ?? 255;
        byte ledMask = Effect(SpecialActionTypes.SetPlayerLeds)?.PlayerLeds.Mask ?? 0;
        _playerLed1 = (ledMask & 0x01) != 0;
        _playerLed2 = (ledMask & 0x02) != 0;
        _playerLed3 = (ledMask & 0x04) != 0;
        _playerLed4 = (ledMask & 0x08) != 0;
        _playerLed5 = (ledMask & 0x10) != 0;
        _playerPreset1 = ledMask == PlayerPresetMasks[0];
        _playerPreset2 = ledMask == PlayerPresetMasks[1];
        _playerPreset3 = ledMask == PlayerPresetMasks[2];
        _playerPreset4 = ledMask == PlayerPresetMasks[3];
        _playerPreset5 = ledMask == PlayerPresetMasks[4];
        _holdTimeSeconds = action.HoldTimeMs / 1000.0;
        _applyWhileHeld = action.ApplyWhileHeld;
        _durationSeconds = action.DurationMs / 1000.0;
        _gesture = NormalizeGesture(action.TouchpadGesture) ?? TouchpadGestures.SwipeUp;
        _touchpadGesture = NormalizeGesture(action.TouchpadGesture);
        _soundVolume = Effect(SpecialActionTypes.PlaySound)?.Sound.Volume ?? 0x50;
        _soundOutputDevice = Effect(SpecialActionTypes.PlaySound)?.Sound.Output ?? SoundOutputDevices.Speaker;
        _hapticFeedback = Effect(SpecialActionTypes.PlaySound)?.Haptics.Feedback ?? false;
        _hapticStrength = Effect(SpecialActionTypes.PlaySound)?.Haptics.Strength ?? 100;
        _selectedBatteryLevel = 0;

        IReadOnlyList<BatteryLevelColor>? customColors = Effect(SpecialActionTypes.ShowBatteryLevel)?.BatteryColors;
        for (int i = 0; i < BatteryLevelCount; i++)
        {
            BatteryLevelColor color = i < (customColors?.Count ?? 0)
                ? customColors![i]
                : SpecialActionEffect.DefaultBatteryColors[i];
            BatteryLevels.Add(new BatteryLevelItem(
                i,
                color.Red,
                color.Green,
                color.Blue,
                OnBatteryLevelColorChanged));
        }

        foreach (ButtonType button in Enum.GetValues<ButtonType>())
        {
            Buttons.Add(new SpecialActionButtonItem(
                button,
                action.Buttons.Contains(button.ToString(), StringComparer.OrdinalIgnoreCase),
                _ =>
                {
                    Persist();
                    OnPropertyChanged(nameof(ComboSummary));
                }));
        }

        _saveTimer = new DispatcherTimer
        {
            Interval = SaveDebounce
        };
        _saveTimer.Tick += (_, _) => CommitPendingChanges();
    }

    /// <summary>
    /// Re-raises the selected battery level's color properties after the level selector
    /// changed, so the picker and the sliders show the newly selected level's color.
    /// </summary>
    partial void OnSelectedBatteryLevelChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedBatteryRed));
        OnPropertyChanged(nameof(SelectedBatteryGreen));
        OnPropertyChanged(nameof(SelectedBatteryBlue));
        OnPropertyChanged(nameof(SelectedBatteryColor));
    }

    /// <summary>
    /// Re-raises the selected battery level's color properties after its color changed
    /// (whether via the picker, the sliders, or a reset), so every editor stays in sync,
    /// then persists. The level item's own properties are not what the UI binds to, so the
    /// proxies must be re-raised explicitly.
    /// </summary>
    private void OnBatteryLevelColorChanged()
    {
        OnPropertyChanged(nameof(SelectedBatteryRed));
        OnPropertyChanged(nameof(SelectedBatteryGreen));
        OnPropertyChanged(nameof(SelectedBatteryBlue));
        OnPropertyChanged(nameof(SelectedBatteryColor));
        Persist();
    }

    /// <summary>
    /// Toggles the action's enablement for the current controller, persisting immediately.
    /// </summary>
    partial void OnIsEnabledForThisControllerChanged(bool value)
    {
        if (_disposed || _controllerId is null)
        {
            return;
        }

        _service.SetEnabledForController(Action.Id, _controllerId, value);
    }

    /// <summary>
    /// Re-raises the derived properties (trigger selector, combo summary, gesture
    /// visibility) and persists the new gesture. Touchpad-triggered actions never use
    /// apply-while-held, so the toggle is cleared and hidden.
    /// </summary>
    partial void OnTouchpadGestureChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ApplyWhileHeld = false;
        }

        OnPropertyChanged(nameof(HasGesture));
        OnPropertyChanged(nameof(ComboSummary));
        OnPropertyChanged(nameof(SelectedGestureIndex));
        OnPropertyChanged(nameof(SelectedTriggerIndex));
        OnPropertyChanged(nameof(IsApplyWhileHeldVisible));
        Persist();
    }

    /// <summary>
    /// Pushes a changed swipe direction into <see cref="TouchpadGesture"/> while the
    /// touchpad trigger is active so it persists.
    /// </summary>
    partial void OnGestureChanged(string value)
    {
        if (HasGesture)
        {
            TouchpadGesture = value;
        }
    }

    /// <summary>
    /// Adds or removes the disconnect effect.
    /// </summary>
    partial void OnEffectDisconnectChanged(bool value)
    {
        if (!_suppressEffectChanges)
        {
            SetEffect(SpecialActionTypes.Disconnect, value);
        }
    }

    /// <summary>
    /// Adds or removes the set-lightbar-color effect.
    /// </summary>
    partial void OnEffectLightbarChanged(bool value)
    {
        if (!_suppressEffectChanges)
        {
            SetEffect(SpecialActionTypes.SetLightbarColor, value);
        }
    }

    /// <summary>
    /// Adds or removes the set-player-LEDs effect.
    /// </summary>
    partial void OnEffectPlayerLedsChanged(bool value)
    {
        if (!_suppressEffectChanges)
        {
            SetEffect(SpecialActionTypes.SetPlayerLeds, value);
        }
    }

    /// <summary>
    /// Adds or removes the play-sound effect.
    /// </summary>
    partial void OnEffectSoundChanged(bool value)
    {
        if (!_suppressEffectChanges)
        {
            SetEffect(SpecialActionTypes.PlaySound, value);
        }
    }

    /// <summary>
    /// Adds or removes the show-battery-level effect.
    /// </summary>
    partial void OnEffectBatteryChanged(bool value)
    {
        if (!_suppressEffectChanges)
        {
            SetEffect(SpecialActionTypes.ShowBatteryLevel, value);
        }
    }

    /// <summary>
    /// Adds, enables, or disables an effect of the given type (a type can appear at most
    /// once), then refreshes the parameter section visibility and persists. An effect that
    /// is turned off stays in the list with its parameters, so turning it back on restores
    /// the previous configuration. The show-battery-level effect conflicts with the
    /// light-changing effects (set-lightbar-color and set-player-LEDs): enabling one
    /// disables the other(s) so the lightbar can never be claimed twice.
    /// </summary>
    private void SetEffect(string type, bool enabled)
    {
        SpecialActionEffect? existing = Effect(type);
        if (existing is null)
        {
            if (!enabled)
            {
                return;
            }

            Action.Effects.Add(new SpecialActionEffect
            {
                Type = type
            });
        }
        else
        {
            existing.Enabled = enabled;
        }

        if (enabled)
        {
            if (type == SpecialActionTypes.ShowBatteryLevel)
            {
                DisableEffect(SpecialActionTypes.SetLightbarColor);
                DisableEffect(SpecialActionTypes.SetPlayerLeds);
            }
            else if (type is SpecialActionTypes.SetLightbarColor or SpecialActionTypes.SetPlayerLeds)
            {
                DisableEffect(SpecialActionTypes.ShowBatteryLevel);
            }
        }

        RefreshEffectVisibility();
        Persist();
    }

    /// <summary>
    /// Disables an effect of the given type, keeping its parameters for a later re-enable,
    /// and unchecks its toggle without re-entering <see cref="SetEffect"/>.
    /// </summary>
    private void DisableEffect(string type)
    {
        SpecialActionEffect? existing = Effect(type);
        if (existing is not null)
        {
            existing.Enabled = false;
        }

        _suppressEffectChanges = true;
        try
        {
            switch (type)
            {
                case SpecialActionTypes.SetLightbarColor:
                    EffectLightbar = false;
                    break;
                case SpecialActionTypes.SetPlayerLeds:
                    EffectPlayerLeds = false;
                    break;
                case SpecialActionTypes.ShowBatteryLevel:
                    EffectBattery = false;
                    break;
            }
        }
        finally
        {
            _suppressEffectChanges = false;
        }
    }

    /// <summary>
    /// Re-raises all effect-derived visibility properties.
    /// </summary>
    private void RefreshEffectVisibility()
    {
        OnPropertyChanged(nameof(IsColorAction));
        OnPropertyChanged(nameof(IsPlayerLedsAction));
        OnPropertyChanged(nameof(IsSoundAction));
        OnPropertyChanged(nameof(IsBatteryAction));
        OnPropertyChanged(nameof(IsApplyWhileHeldVisible));
        OnPropertyChanged(nameof(IsDurationVisible));
        OnPropertyChanged(nameof(IsHapticVisible));
    }

    /// <summary>
    /// Persists the new hold duration.
    /// </summary>
    partial void OnHoldTimeSecondsChanged(double value) => Persist();

    /// <summary>
    /// Persists the new apply-while-held setting. A checked toggle clears the duration so
    /// the modes stay mutually exclusive (while held wins over timed).
    /// </summary>
    partial void OnApplyWhileHeldChanged(bool value)
    {
        if (value)
        {
            DurationSeconds = 0;
        }

        Persist();
    }

    /// <summary>
    /// Persists the new duration. A duration above zero unchecks apply-while-held so the
    /// modes stay mutually exclusive (while held wins over timed).
    /// </summary>
    partial void OnDurationSecondsChanged(double value)
    {
        if (value > 0)
        {
            ApplyWhileHeld = false;
        }

        Persist();
    }

    /// <summary>
    /// Persists the new speaker volume.
    /// </summary>
    partial void OnSoundVolumeChanged(int value) => Persist();

    /// <summary>
    /// Persists the new output device and re-raises the selector index so a programmatic
    /// change keeps the dropdown in sync.
    /// </summary>
    partial void OnSoundOutputDeviceChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedSoundOutputIndex));
        Persist();
    }

    /// <summary>
    /// Toggles haptics for sound effects, re-raises the strength slider visibility, and persists.
    /// </summary>
    partial void OnHapticFeedbackChanged(bool value)
    {
        OnPropertyChanged(nameof(IsHapticVisible));
        Persist();
    }

    /// <summary>
    /// Persists the new haptic strength.
    /// </summary>
    partial void OnHapticStrengthChanged(int value) => Persist();

    /// <summary>
    /// Sets the sound file from the page's file picker.
    /// </summary>
    /// <param name="path">Path of the selected audio file.</param>
    public void SetSoundFile(string path) => SoundPath = path;

    /// <summary>
    /// Persists the new lightbar color and re-raises the picker color.
    /// </summary>
    partial void OnLedRedChanged(double value) => NotifyLightbarColorChanged();

    /// <summary>
    /// Persists the new lightbar color and re-raises the picker color.
    /// </summary>
    partial void OnLedGreenChanged(double value) => NotifyLightbarColorChanged();

    /// <summary>
    /// Persists the new lightbar color and re-raises the picker color.
    /// </summary>
    partial void OnLedBlueChanged(double value) => NotifyLightbarColorChanged();

    /// <summary>
    /// Persists a lightbar color change, re-raising the picker color only when the change
    /// did not originate from the picker itself.
    /// </summary>
    private void NotifyLightbarColorChanged()
    {
        if (!_syncingLightbarColor)
        {
            OnPropertyChanged(nameof(LightbarColor));
        }

        Persist();
    }

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
    /// Applies the Player 2 preset (mask 0x06).
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
    /// Requests deletion of this action from the owning page.
    /// </summary>
    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Resets all 10 battery level colors to the defaults.
    /// </summary>
    [RelayCommand]
    private void ResetBatteryColors()
    {
        for (int i = 0; i < BatteryLevels.Count; i++)
        {
            BatteryLevelColor defaults = SpecialActionEffect.DefaultBatteryColors[i];
            BatteryLevels[i].Red = defaults.Red;
            BatteryLevels[i].Green = defaults.Green;
            BatteryLevels[i].Blue = defaults.Blue;
        }

        Persist();
    }

    /// <summary>
    /// Writes the current UI state back into <see cref="Action"/> immediately (so the
    /// in-memory action is always current) and schedules a debounced disk save. Effect
    /// parameters are written into the matching effect only when it is enabled.
    /// </summary>
    private void Persist()
    {
        if (_disposed)
        {
            return;
        }

        Action.Buttons = Buttons.Where(b => b.IsChecked).Select(b => b.Button.ToString()).ToList();
        Action.TouchpadGesture = string.IsNullOrWhiteSpace(TouchpadGesture) ? null : TouchpadGesture;
        Action.HoldTimeMs = (int)Math.Round(Math.Clamp(HoldTimeSeconds, 0, SpecialActionEngine.MaxHoldTimeMs / 1000.0) * 1000);
        Action.ApplyWhileHeld = ApplyWhileHeld;
        Action.DurationMs = (int)Math.Round(Math.Clamp(DurationSeconds, 0, SpecialActionEngine.MaxDurationMs / 1000.0) * 1000);

        SpecialActionEffect? color = Effect(SpecialActionTypes.SetLightbarColor);
        if (color is not null)
        {
            color.Lightbar.Red = Channel(LedRed);
            color.Lightbar.Green = Channel(LedGreen);
            color.Lightbar.Blue = Channel(LedBlue);
        }

        SpecialActionEffect? playerLeds = Effect(SpecialActionTypes.SetPlayerLeds);
        if (playerLeds is not null)
        {
            playerLeds.PlayerLeds.Mask = ComputePlayerLedMask();
        }

        SpecialActionEffect? sound = Effect(SpecialActionTypes.PlaySound);
        if (sound is not null)
        {
            sound.Sound.Path = string.IsNullOrWhiteSpace(SoundPath) ? null : SoundPath.Trim();
            sound.Sound.Volume = (byte)Math.Clamp(SoundVolume, 0, 255);
            sound.Sound.Output = SoundOutputDevice;
            sound.Haptics.Feedback = HapticFeedback;
            sound.Haptics.Strength = Math.Clamp(HapticStrength, 0, 200);
        }

        SpecialActionEffect? battery = Effect(SpecialActionTypes.ShowBatteryLevel);
        if (battery is not null)
        {
            battery.BatteryColors = BatteryLevels
                .Select(l => new BatteryLevelColor
                {
                    Red = Channel(l.Red),
                    Green = Channel(l.Green),
                    Blue = Channel(l.Blue)
                })
                .ToList();
        }

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
        _service.Save();
    }

    /// <summary>
    /// The action's effect of the given type, or <c>null</c> when it has none.
    /// </summary>
    private SpecialActionEffect? Effect(string type) => Action.Effects.FirstOrDefault(e => string.Equals(e.Type, type, StringComparison.Ordinal));

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
    /// Normalizes a stored gesture to its canonical <see cref="TouchpadGestures"/> constant
    /// (case-insensitive), or <c>null</c> when none or an unknown value is stored.
    /// </summary>
    private static string? NormalizeGesture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value switch
        {
            var gesture when string.Equals(gesture, TouchpadGestures.SwipeUp, StringComparison.OrdinalIgnoreCase) => TouchpadGestures.SwipeUp,
            var gesture when string.Equals(gesture, TouchpadGestures.SwipeDown, StringComparison.OrdinalIgnoreCase) => TouchpadGestures.SwipeDown,
            var gesture when string.Equals(gesture, TouchpadGestures.SwipeLeft, StringComparison.OrdinalIgnoreCase) => TouchpadGestures.SwipeLeft,
            var gesture when string.Equals(gesture, TouchpadGestures.SwipeRight, StringComparison.OrdinalIgnoreCase) => TouchpadGestures.SwipeRight,
            _ => null
        };
    }

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
            _service.Save();
        }
    }
}