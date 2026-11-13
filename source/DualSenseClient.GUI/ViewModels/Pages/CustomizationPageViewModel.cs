using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Avalonia.Threading;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.Emulation;
using SoundFlow.Abstracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.Profiles;
using DualSenseClient.Core.Models;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the customization page: the global profiles manager (lightbar color,
/// microphone LED mode, and player LEDs), the special actions editor, the device
/// output tests (vibration and adaptive triggers), and the audio forwarding settings.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MainViewModel.SelectedItem"/> like every page ViewModel: special
/// actions are bound to the controller active in the shell, and profile edits apply
/// live to the controller using them.
/// </remarks>
public partial class CustomizationPageViewModel : ObservableObject
{
    /// <summary>
    /// Base name used for automatically created profiles ("Profile", "Profile 2", ...).
    /// </summary>
    private const string DefaultProfileName = "Profile";

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("CustomizationPage");

    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Service used to read and persist profiles.
    /// </summary>
    private readonly ProfileService _profileService;

    private readonly IProfileActivation _profileActivation;

    /// <summary>
    /// Service storing controller info and controller-to-profile assignments.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Service storing the foreground-app auto profile rules referencing profiles by name.
    /// </summary>
    private readonly AutoProfileService _autoProfileService;

    /// <summary>
    /// Service storing the global list of special actions.
    /// </summary>
    private readonly SpecialActionService _specialActionService;

    /// <summary>
    /// Service used for delete confirmations and import/export dialogs.
    /// </summary>
    private readonly IMessageBoxService _messageBox;

    /// <summary>
    /// Tracks the previous device item so its event subscriptions are released on replacement.
    /// </summary>
    private DeviceInfoItem? _previousItem;

    /// <summary>
    /// Tracks the previous monitor item so its event subscriptions are released on replacement.
    /// </summary>
    private InputMonitorItem? _previousMonitorState;

    /// <summary>
    /// Service creating the selected controller's virtual controller and applying its
    /// emulation settings, used to route the forwarding options to the active forwarder.
    /// </summary>
    private readonly IEmulationService _emulation;

    /// <summary>
    /// Service enumerating the applications currently producing audio and resolving the
    /// stored application to a live process.
    /// </summary>
    private readonly AudioApplicationService _audioApplications;

    /// <summary>
    /// Delay between the last dongle configuration edit and the auto-apply, so dragging a
    /// slider coalesces into a single feature-report write.
    /// </summary>
    private const int DongleApplyDebounceMilliseconds = 300;

    /// <summary>
    /// Debounced dongle configuration auto-apply timer.
    /// </summary>
    private readonly DispatcherTimer _dongleApplyTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(DongleApplyDebounceMilliseconds)
    };

    /// <summary>
    /// Serializes dongle configuration reads, applies, saves, and reconnects.
    /// </summary>
    private readonly SemaphoreSlim _dongleIoSync = new SemaphoreSlim(1, 1);

    /// <summary>
    /// The configuration last read from or written to the selected dongle.
    /// </summary>
    private DongleConfig? _lastAppliedDongleConfig;

    /// <summary>
    /// USB-interface configuration captured at the last read or reconnect, used to warn
    /// when a newly applied setting needs the USB device to re-enumerate.
    /// </summary>
    private UsbEffectiveDongleConfig? _usbEffectiveDongleBaseline;

    /// <summary>
    /// Suppresses the editor's change event while a device snapshot is loaded into it.
    /// </summary>
    private bool _suppressDongleConfigChanged;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public DeviceInfoItem? CurrentDevice { get; private set; }

    /// <summary>
    /// The concrete DualSense device of the selected controller, or <c>null</c> for
    /// non-DualSense devices.
    /// </summary>
    private DualSenseDevice? CurrentDualSenseDevice
    {
        get
        {
            return CurrentDevice?.Controller.Device as DualSenseDevice;
        }
    }

    /// <summary>
    /// The live monitor item of the selected controller backing the output tests
    /// (vibration and adaptive triggers), or <c>null</c> when none is selected.
    /// </summary>
    public InputMonitorItem? MonitorState { get; private set; }

    /// <summary>
    /// The adaptive trigger modes offered by the effect pickers. One shared list so
    /// the pickers never rebind a fresh instance: a <c>SelectedItem</c> pushed before
    /// the picker's items resolve is re-selected by identity once they do, instead of
    /// being lost like an index (which left the pickers empty).
    /// </summary>
    public IReadOnlyList<TriggerEffectModeItem> TriggerEffectModes
    {
        get
        {
            return InputMonitorItem.SharedTriggerEffectModes;
        }
    }

    /// <summary>
    /// The left (L2) adaptive trigger effect mode, bound by identity so the picker
    /// keeps its selection across monitor item rebuilds.
    /// </summary>
    public TriggerEffectModeItem LeftTriggerMode
    {
        get
        {
            return TriggerEffectModes[MonitorState?.LeftTriggerModeIndex ?? 0];
        }
        set
        {
            if (value is not null && MonitorState is not null)
            {
                for (int i = 0; i < TriggerEffectModes.Count; i++)
                {
                    if (ReferenceEquals(TriggerEffectModes[i], value))
                    {
                        MonitorState.LeftTriggerModeIndex = i;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The right (R2) adaptive trigger effect mode; see <see cref="LeftTriggerMode"/>.
    /// </summary>
    public TriggerEffectModeItem RightTriggerMode
    {
        get
        {
            return TriggerEffectModes[MonitorState?.RightTriggerModeIndex ?? 0];
        }
        set
        {
            if (value is not null && MonitorState is not null)
            {
                for (int i = 0; i < TriggerEffectModes.Count; i++)
                {
                    if (ReferenceEquals(TriggerEffectModes[i], value))
                    {
                        MonitorState.RightTriggerModeIndex = i;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The dongle backing the selected controller, or <c>null</c> for non-dongle devices.
    /// </summary>
    private DualSenseDongle? CurrentDongleDevice
    {
        get
        {
            return MonitorState?.Controller.Device as DualSenseDongle;
        }
    }

    /// <summary>
    /// Whether a controller is selected.
    /// </summary>
    public bool HasDevice
    {
        get
        {
            return CurrentDevice is not null;
        }
    }

    /// <summary>
    /// Whether the selected controller is connected through a DS5Dongle bridge.
    /// </summary>
    public bool IsDongle
    {
        get
        {
            return CurrentDevice?.IsDongle == true;
        }
    }

    /// <summary>
    /// Whether the dongle configuration editor has loaded and can be shown.
    /// </summary>
    public bool HasDongleConfigEditor
    {
        get
        {
            return DongleConfigEditor is not null;
        }
    }

    /// <summary>
    /// The dongle configuration editor, or <c>null</c> when the selected controller is not
    /// a DS5Dongle or its configuration has not loaded yet.
    /// </summary>
    [ObservableProperty] private DongleConfigItem? _dongleConfigEditor;

    /// <summary>
    /// User-visible state of the last dongle configuration operation.
    /// </summary>
    [ObservableProperty] private string _dongleStatusText = string.Empty;

    /// <summary>
    /// Whether one or more applied settings need a USB reconnect to take effect.
    /// </summary>
    [ObservableProperty] private bool _needsDongleUsbReconnect;

    /// <summary>
    /// Whether an explicit dongle configuration command is in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadDongleConfigCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveDongleConfigToFlashCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectDongleUsbCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetDongleConfigCommand))]
    private bool _isDongleBusy;

    /// <summary>
    /// The Bluetooth MAC address of the selected controller, or empty when unavailable.
    /// </summary>
    public string CurrentMac
    {
        get
        {
            return CurrentDevice?.Controller.PairingInfo?.ClientMac ?? string.Empty;
        }
    }

    /// <summary>
    /// The HID device path of the selected controller, used as a binding fallback when the
    /// MAC address is unavailable.
    /// </summary>
    public string CurrentDevicePath
    {
        get
        {
            return CurrentDevice?.Controller.Device.Info.Path ?? string.Empty;
        }
    }

    /// <summary>
    /// The key of the selected controller, or an empty key when none is selected.
    /// </summary>
    private ControllerKey CurrentKey
    {
        get
        {
            return CurrentDevice is null
                ? ControllerKey.From(null, null)
                : ControllerKeyFactory.From(CurrentDevice.Controller.Device);
        }
    }

    /// <summary>
    /// The name of the profile the selected controller is currently using: the bound
    /// profile, or the default profile when unbound.
    /// </summary>
    private string CurrentUsedProfileName
    {
        get
        {
            return CurrentDevice?.Controller.Device is DualSenseDevice device
                ? _profileActivation.GetActiveProfile(device)?.Name ?? ProfileService.DefaultProfileName
                : ProfileService.DefaultProfileName;
        }
    }

    /// <summary>
    /// Whether the selected controller's hardware supports full player-LED functionality.
    /// Generation 0x04 and above is restricted to Mirrored Only, in which case only the
    /// mirrored player presets are offered and the individual LED toggles are hidden.
    /// </summary>
    public bool HasFullPlayerLedSupport
    {
        get
        {
            return CurrentDevice?.Controller.FirmwareInfo?.IsValid == true
                   && CurrentDevice.Controller.FirmwareInfo.Value.HasFullPlayerLedSupport;
        }
    }

    // ── Vibration ───────────────────────────────────────────────

    /// <summary>
    /// Rumble encoding options for the picker. The list is created once so rebuilding the
    /// page cannot desynchronize the ComboBox selection; Improved (V2) is disabled when
    /// the selected controller's firmware does not support it.
    /// </summary>
    public ObservableCollection<VibrationModeItem> VibrationModes { get; } = [];

    /// <summary>
    /// The selected rumble encoding as an index into <see cref="VibrationModes"/>.
    /// </summary>
    public int VibrationEncodingIndex
    {
        get
        {
            VibrationEncodingMode encoding = GetVibrationSettings().RumbleEncoding;
            for (int i = 0; i < VibrationModes.Count; i++)
            {
                if (VibrationModes[i].Value == encoding)
                {
                    return i;
                }
            }

            return 0;
        }
        set
        {
            if (!HasDevice || value < 0 || value >= VibrationModes.Count)
            {
                return;
            }

            VibrationSettings settings = GetVibrationSettings();
            VibrationEncodingMode encoding = VibrationModes[value].Value;
            if (settings.RumbleEncoding == encoding)
            {
                return;
            }

            settings.RumbleEncoding = encoding;
            PersistVibrationSettings(settings);
            OnPropertyChanged(nameof(VibrationEncodingIndex));
        }
    }

    /// <summary>
    /// Whether the app enforces the motor power reductions (two-way, persisted).
    /// </summary>
    public bool MotorPowerLevelEnabled
    {
        get
        {
            return HasDevice && GetVibrationSettings().MotorPowerLevelEnabled;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            VibrationSettings settings = GetVibrationSettings();
            if (settings.MotorPowerLevelEnabled == value)
            {
                return;
            }

            settings.MotorPowerLevelEnabled = value;
            PersistVibrationSettings(settings);
            OnPropertyChanged(nameof(MotorPowerLevelEnabled));
        }
    }

    /// <summary>
    /// Rumble-motor power reduction (0-15, two-way, persisted).
    /// </summary>
    public int RumblePowerReduction
    {
        get
        {
            return HasDevice ? GetVibrationSettings().RumblePowerReduction : 0;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, VibrationSettings.MaxRumblePowerReduction);
            VibrationSettings settings = GetVibrationSettings();
            if (settings.RumblePowerReduction == clamped)
            {
                return;
            }

            settings.RumblePowerReduction = clamped;
            CurrentDualSenseDevice?.ApplyVibrationSettings(settings);
            ScheduleVibrationSave();
            OnPropertyChanged(nameof(RumblePowerReduction));
        }
    }

    /// <summary>
    /// Trigger-motor power reduction (0-10, two-way, persisted).
    /// </summary>
    public int TriggerPowerReduction
    {
        get
        {
            return HasDevice ? GetVibrationSettings().TriggerPowerReduction : 0;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, VibrationSettings.MaxTriggerPowerReduction);
            VibrationSettings settings = GetVibrationSettings();
            if (settings.TriggerPowerReduction == clamped)
            {
                return;
            }

            settings.TriggerPowerReduction = clamped;
            CurrentDualSenseDevice?.ApplyVibrationSettings(settings);
            ScheduleVibrationSave();
            OnPropertyChanged(nameof(TriggerPowerReduction));
        }
    }

    /// <summary>
    /// Whether the app enforces the haptic low-pass filter. Off leaves the game's value
    /// untouched (two-way, persisted).
    /// </summary>
    public bool HapticLowPassFilter
    {
        get
        {
            return HasDevice && GetVibrationSettings().HapticLowPassFilter;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            VibrationSettings settings = GetVibrationSettings();
            if (settings.HapticLowPassFilter == value)
            {
                return;
            }

            settings.HapticLowPassFilter = value;
            PersistVibrationSettings(settings);
            OnPropertyChanged(nameof(HapticLowPassFilter));
        }
    }

    /// <summary>
    /// Gets the vibration settings stored for the selected controller, or defaults when
    /// none are stored.
    /// </summary>
    private VibrationSettings GetVibrationSettings() => _controllerService.GetVibrationSettings(CurrentKey);

    /// <summary>
    /// Persists the vibration settings and applies them to the live controller.
    /// </summary>
    private void PersistVibrationSettings(VibrationSettings settings)
    {
        _vibrationSaveTimer.Stop();
        _vibrationSavePending = false;
        _controllerService.SaveVibrationSettings(CurrentKey, settings);
        CurrentDualSenseDevice?.ApplyVibrationSettings(settings);
    }

    /// <summary>
    /// Restarts the debounce timer for slider changes, so the live output updates
    /// immediately but the settings file is written once after dragging stops.
    /// </summary>
    private void ScheduleVibrationSave()
    {
        _vibrationSavePending = true;
        _vibrationSaveTimer.Stop();
        _vibrationSaveTimer.Start();
    }

    /// <summary>
    /// Persists a pending vibration settings change after the debounce delay.
    /// </summary>
    private void SaveVibrationDebounced()
    {
        _vibrationSaveTimer.Stop();
        if (!_vibrationSavePending || !HasDevice)
        {
            _vibrationSavePending = false;
            return;
        }

        _vibrationSavePending = false;
        _controllerService.SaveVibrationSettings(CurrentKey, GetVibrationSettings());
    }

    /// <summary>
    /// Flushes a pending vibration settings save immediately, for controller switches and
    /// page navigation.
    /// </summary>
    public void FlushVibrationSettings()
    {
        _vibrationSaveTimer.Stop();
        if (!_vibrationSavePending || !HasDevice)
        {
            _vibrationSavePending = false;
            return;
        }

        _vibrationSavePending = false;
        _controllerService.SaveVibrationSettings(CurrentKey, GetVibrationSettings());
    }

    /// <summary>
    /// Applies the stored vibration settings when the page selection changes.
    /// </summary>
    private void ApplyStoredVibrationSettings() => CurrentDualSenseDevice?.ApplyVibrationSettings(GetVibrationSettings());

    /// <summary>
    /// Rebuilds the rumble encoding picker from the selected controller's capabilities.
    /// </summary>
    private void RebuildVibrationModes()
    {
        bool supportsV2 = CurrentDualSenseDevice?.SupportsVibrationV2 == true;
        NormalizeUnsupportedVibrationEncoding(supportsV2);
        if (VibrationModes.Count == 0)
        {
            VibrationModes.Add(new VibrationModeItem(
                VibrationEncodingMode.Auto,
                LocalizationService.GetText("CustomizationPage.OutputTest.Vibration.Encoding.Auto")));
            VibrationModes.Add(new VibrationModeItem(
                VibrationEncodingMode.Compatible,
                LocalizationService.GetText("CustomizationPage.OutputTest.Vibration.Encoding.Compatible")));
            VibrationModes.Add(new VibrationModeItem(
                VibrationEncodingMode.Improved,
                LocalizationService.GetText("CustomizationPage.OutputTest.Vibration.Encoding.Improved"),
                supportsV2));
            return;
        }

        VibrationModes[0].IsEnabled = true;
        VibrationModes[1].IsEnabled = true;
        VibrationModes[2].IsEnabled = supportsV2;
    }

    /// <summary>
    /// Downgrades a stored Improved (V2) preference when the selected controller cannot
    /// use it, so the picker never shows a disabled item as the active selection. Auto
    /// still resolves to V2 automatically if the controller later supports it.
    /// </summary>
    /// <param name="supportsV2">Whether the selected controller supports Improved (V2).</param>
    private void NormalizeUnsupportedVibrationEncoding(bool supportsV2)
    {
        if (supportsV2)
        {
            return;
        }

        VibrationSettings settings = GetVibrationSettings();
        if (settings.RumbleEncoding != VibrationEncodingMode.Improved)
        {
            return;
        }

        settings.RumbleEncoding = VibrationEncodingMode.Auto;
        _controllerService.SaveVibrationSettings(CurrentKey, settings);
    }

    // ── Profiles ────────────────────────────────────────────────

    /// <summary>
    /// All saved profiles, shown in the profile list and editor.
    /// </summary>
    public ObservableCollection<ProfileEditorItem> Profiles { get; } = [];

    /// <summary>
    /// Microphone LED mode options for the dropdown, in mode order (off, on, pulse).
    /// </summary>
    public ObservableCollection<string> MicLedModes { get; } =
    [
        LocalizationService.GetText("CustomizationPage.MicLed.Mode.Off"),
        LocalizationService.GetText("CustomizationPage.MicLed.Mode.On"),
        LocalizationService.GetText("CustomizationPage.MicLed.Mode.Pulse")
    ];

    /// <summary>
    /// Lightbar mode options for the dropdown, in <see cref="LightbarMode"/> order.
    /// </summary>
    public ObservableCollection<string> LightbarModes { get; } =
    [
        LocalizationService.GetText("CustomizationPage.Lightbar.Mode.SingleColor"),
        LocalizationService.GetText("CustomizationPage.Lightbar.Mode.MultiColor"),
        LocalizationService.GetText("CustomizationPage.Lightbar.Mode.Breathing"),
        LocalizationService.GetText("CustomizationPage.Lightbar.Mode.Strobe"),
        LocalizationService.GetText("CustomizationPage.Lightbar.Mode.Battery"),
        LocalizationService.GetText("CustomizationPage.Lightbar.Mode.AudioReactive")
    ];

    /// <summary>
    /// Dongle polling-rate options for current firmware, in <see cref="DonglePollingRateMode"/> order.
    /// </summary>
    public ObservableCollection<string> DonglePollingRates { get; } = [];

    /// <summary>
    /// Dongle polling-rate options for legacy firmware, whose real-time mode is the third value.
    /// </summary>
    public ObservableCollection<string> DongleLegacyPollingRates { get; } = [];

    /// <summary>
    /// Dongle controller-identity options for the dropdown, in <see cref="DongleControllerMode"/> order.
    /// </summary>
    public ObservableCollection<string> DongleControllerModes { get; } = [];

    /// <summary>
    /// Dongle microphone/speaker route options for the dropdown, in <see cref="DongleAudioDevice"/> order.
    /// </summary>
    public ObservableCollection<string> DongleAudioDevices { get; } = [];

    /// <summary>
    /// Dongle volume-lock options for the dropdown, in <see cref="DongleVolumeLockMode"/> order.
    /// </summary>
    public ObservableCollection<string> DongleVolumeLockModes { get; } = [];

    /// <summary>
    /// Dongle status GPIO pin options for the dropdown, in <see cref="DongleConfigItem.GpioPinValues"/> order.
    /// </summary>
    public ObservableCollection<string> DongleGpioPins { get; } = [];

    /// <summary>
    /// Dongle status GPIO behaviour options for the dropdown, in <see cref="DongleStatusGpioMode"/> order.
    /// </summary>
    public ObservableCollection<string> DongleGpioModes { get; } = [];

    /// <summary>
    /// The profile currently being edited, or <c>null</c> when none exists.
    /// </summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))] [NotifyCanExecuteChangedFor(nameof(DuplicateProfileCommand))]
    private ProfileEditorItem? _selectedProfile;

    /// <summary>
    /// Whether a profile is selected for editing.
    /// </summary>
    public bool HasSelectedProfile
    {
        get
        {
            return SelectedProfile is not null;
        }
    }

    /// <summary>
    /// Creates a new, empty profile and selects it for editing.
    /// </summary>
    [RelayCommand]
    private void CreateProfile() => SelectedProfile = AddProfile(_profileService.CreateProfile(DefaultProfileName));

    /// <summary>
    /// Deletes the selected profile after confirmation, removing any controller bindings
    /// that referenced it. If the selected controller was using the deleted profile, it
    /// falls back to the default profile.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        string name = SelectedProfile.Name;
        bool confirmed = await _messageBox.ShowConfirmationAsync(
            LocalizationService.GetText("CustomizationPage.Delete.Title"),
            LocalizationService.GetText("CustomizationPage.Delete.Message").Replace("{name}", name));
        if (!confirmed)
        {
            return;
        }

        bool controllerWasUsing = HasDevice && string.Equals(CurrentUsedProfileName, name, StringComparison.OrdinalIgnoreCase);
        _profileActivation.DeleteProfile(name);
        _autoProfileService.RemoveProfileReferences(name);
        SelectedProfile = null;
        RebuildProfiles();

        if (controllerWasUsing)
        {
            ApplyBoundProfileToController();
        }
    }

    /// <summary>
    /// Duplicates the selected profile (lightbar color, microphone LED mode, and player LEDs)
    /// under a new name and selects the copy for editing.
    /// </summary>
    [RelayCommand]
    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        Profile? copy = _profileService.DuplicateProfile(SelectedProfile.Name);
        if (copy is not null)
        {
            SelectedProfile = AddProfile(copy);
        }
    }

    // ── Special actions ────────────────────────────────────────

    /// <summary>
    /// The global list of special actions, shown in the special actions section.
    /// </summary>
    public ObservableCollection<SpecialActionItem> SpecialActions { get; } = [];

    /// <summary>
    /// Whether any special actions exist.
    /// </summary>
    public bool HasSpecialActions
    {
        get
        {
            return SpecialActions.Count > 0;
        }
    }

    /// <summary>
    /// The special action currently being edited, or <c>null</c> when none exists.
    /// </summary>
    [ObservableProperty] private SpecialActionItem? _selectedSpecialAction;

    /// <summary>
    /// Creates a new special action, enabled for the controller currently selected on this
    /// page, and adds it to the list for editing. New actions have no button combination
    /// until the user selects one.
    /// </summary>
    [RelayCommand]
    private void AddSpecialAction()
    {
        SpecialAction action = _specialActionService.CreateAction(
            null,
            SpecialActionService.GetControllerId(CurrentKey));
        SpecialActionItem item = AddSpecialActionItem(action);
        SpecialActions.Add(item);
        SelectedSpecialAction = item;
        OnPropertyChanged(nameof(HasSpecialActions));
    }

    /// <summary>
    /// Exports all special actions to the given file and shows an error dialog on failure.
    /// </summary>
    /// <param name="path">The full path of the file to write.</param>
    public async Task ExportSpecialActions(string path)
    {
        try
        {
            _specialActionService.ExportActions(path);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to export special actions to '{path}'", ex);
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("CustomizationPage.SpecialActions.Export.Error.Title"),
                LocalizationService.GetText("CustomizationPage.SpecialActions.Export.Error.Message"));
        }
    }

    /// <summary>
    /// Exports a single special action to the given file and shows an error dialog on failure.
    /// </summary>
    /// <param name="item">The action to export.</param>
    /// <param name="path">The full path of the file to write.</param>
    public async Task ExportSpecialAction(SpecialActionItem item, string path)
    {
        try
        {
            _specialActionService.ExportAction(item.Action.Id, path);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to export special action '{item.Action.Name}' to '{path}'", ex);
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("CustomizationPage.SpecialActions.Export.Error.Title"),
                LocalizationService.GetText("CustomizationPage.SpecialActions.Export.Error.Message"));
        }
    }

    /// <summary>
    /// Imports special actions from the given file, refreshes the list, and reports the
    /// outcome (nothing found or a failure) in a dialog.
    /// </summary>
    /// <param name="path">The full path of the file to read.</param>
    public async Task ImportSpecialActions(string path)
    {
        try
        {
            int count = _specialActionService.ImportActions(path);
            RebuildSpecialActions();
            if (count <= 0)
            {
                await _messageBox.ShowWarningAsync(
                    LocalizationService.GetText("CustomizationPage.SpecialActions.Import.Empty.Title"),
                    LocalizationService.GetText("CustomizationPage.SpecialActions.Import.Empty.Message"));
                return;
            }

            await _messageBox.ShowInfoAsync(
                LocalizationService.GetText("CustomizationPage.SpecialActions.Import.Success.Title"),
                LocalizationService.GetText("CustomizationPage.SpecialActions.Import.Success.Message").Replace("{count}", count.ToString()));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to import special actions from '{path}'", ex);
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("CustomizationPage.SpecialActions.Import.Error.Title"),
                LocalizationService.GetText("CustomizationPage.SpecialActions.Import.Error.Message"));
        }
    }

    /// <summary>
    /// Deletes a special action after its delete button is pressed, and persists the change.
    /// </summary>
    private void OnSpecialActionDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is not SpecialActionItem item)
        {
            return;
        }

        _log.Info($"Deleting special action '{item.Action.Name}'");
        _specialActionService.DeleteAction(item.Action.Id);
        SpecialActions.Remove(item);
        item.Dispose();
        SelectedSpecialAction = SpecialActions.FirstOrDefault();
        OnPropertyChanged(nameof(HasSpecialActions));
    }

    /// <summary>
    /// Rebuilds the <see cref="SpecialActions"/> collection from the current service state,
    /// binding each item to the currently selected controller. Disposes and unsubscribes
    /// previous items first.
    /// </summary>
    private void RebuildSpecialActions()
    {
        foreach (SpecialActionItem item in SpecialActions)
        {
            item.DeleteRequested -= OnSpecialActionDeleteRequested;
            item.Dispose();
        }

        SpecialActions.Clear();

        string? controllerId = SpecialActionService.GetControllerId(CurrentKey);
        foreach (SpecialAction action in _specialActionService.Settings.Actions)
        {
            SpecialActions.Add(AddSpecialActionItem(action, controllerId));
        }

        SelectedSpecialAction = SpecialActions.FirstOrDefault();
        OnPropertyChanged(nameof(HasSpecialActions));
    }

    /// <summary>
    /// Wraps an action in a new <see cref="SpecialActionItem"/> and subscribes to its
    /// delete request. When <paramref name="controllerId"/> is <c>null</c>, the current
    /// controller's identifier from the page selection is used.
    /// </summary>
    private SpecialActionItem AddSpecialActionItem(SpecialAction action, string? controllerId = null)
    {
        SpecialActionItem item = new SpecialActionItem(
            action,
            _specialActionService,
            controllerId ?? SpecialActionService.GetControllerId(CurrentKey),
            HasFullPlayerLedSupport);
        item.DeleteRequested += OnSpecialActionDeleteRequested;
        return item;
    }

    // ── Profile manager internals ───────────────────────────────

    /// <summary>
    /// Re-points the controller assignments and auto profile rules referencing the old
    /// profile name when a profile is renamed.
    /// </summary>
    private void OnProfileRenamed(object? sender, EventArgs e)
    {
        if (e is ProfileRenamedEventArgs args)
        {
            _controllerService.UpdateProfileName(args.OldName, args.NewName);
            _autoProfileService.UpdateProfileName(args.OldName, args.NewName);
        }
    }

    /// <summary>
    /// Re-applies a profile to the controller when it is the profile the controller is
    /// currently using (bound profile, or the default when unbound), so lightbar edits
    /// take effect immediately on the controller.
    /// </summary>
    private void OnProfileChanged(object? sender, EventArgs e)
    {
        if (sender is not ProfileEditorItem item || !HasDevice)
        {
            return;
        }

        if (CurrentDevice?.Controller.Device is not DualSenseDevice device)
        {
            return;
        }

        Profile? applied = _profileActivation.GetActiveProfile(device);
        if (applied is null || !string.Equals(item.Name, applied.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _profileActivation.Activate(device, applied);
    }

    /// <summary>
    /// Applies the profile currently used by the selected controller (bound profile, or the
    /// default when unbound), so an assignment change takes effect immediately. Only the
    /// controller-side light settings are reapplied.
    /// </summary>
    private void ApplyBoundProfileToController()
    {
        if (CurrentDevice?.Controller.Device is not DualSenseDevice device)
        {
            return;
        }

        _log.Info($"Applying the active profile to {CurrentMac}");
        _profileActivation.Activate(device);
    }

    /// <summary>
    /// Rebuilds the <see cref="Profiles"/> collection from the current profile service
    /// state. Disposes and unsubscribes previous editor items first.
    /// </summary>
    private void RebuildProfiles()
    {
        foreach (ProfileEditorItem item in Profiles)
        {
            item.ProfileRenamed -= OnProfileRenamed;
            item.ProfileChanged -= OnProfileChanged;
            item.Dispose();
        }

        Profiles.Clear();

        foreach (Profile profile in _profileService.Settings.Profiles)
        {
            ProfileEditorItem item = new ProfileEditorItem(profile, _profileService);
            item.ProfileRenamed += OnProfileRenamed;
            item.ProfileChanged += OnProfileChanged;
            Profiles.Add(item);
        }

        // Prefer selecting a non-default profile so the seeded "Default" entry is only
        // selected when it is the only profile available.
        SelectedProfile = Profiles.FirstOrDefault(p => !string.Equals(p.Name, ProfileService.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                          ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedProfile));
    }

    /// <summary>
    /// Wraps a profile in a new <see cref="ProfileEditorItem"/>, subscribes to its events,
    /// and adds it to <see cref="Profiles"/>.
    /// </summary>
    private ProfileEditorItem AddProfile(Profile profile)
    {
        ProfileEditorItem item = new ProfileEditorItem(profile, _profileService);
        item.ProfileRenamed += OnProfileRenamed;
        item.ProfileChanged += OnProfileChanged;
        Profiles.Add(item);
        return item;
    }

    // ── Lifecycle ───────────────────────────────────────────────

    // ── Audio forwarding ───────────────────────────────────────

    /// <summary>
    /// Delay between the last forwarding slider change and the controller info save,
    /// so dragging a slider coalesces into a single disk write.
    /// </summary>
    private static readonly TimeSpan EmulationSaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Debounced emulation settings save timer: each slider change restarts it and the
    /// save happens only after changes stop.
    /// </summary>
    private readonly DispatcherTimer _emulationSaveTimer;

    /// <summary>
    /// Debounced vibration settings save timer, so dragging a motor-power slider does not
    /// write <c>controllers.json</c> on every tick.
    /// </summary>
    private readonly DispatcherTimer _vibrationSaveTimer;

    /// <summary>
    /// Whether a debounced vibration settings save is pending.
    /// </summary>
    private bool _vibrationSavePending;

    /// <summary>
    /// The applications currently producing audio, backing the application picker.
    /// </summary>
    private readonly List<AudioApplicationInfo> _forwardAudioAppItems = [];

    /// <summary>
    /// Forwarded audio output options for the dropdown, in
    /// <see cref="EmulationAudioOutput"/> order (auto, speaker, headset).
    /// </summary>
    public ObservableCollection<string> EmulationAudioOutputs { get; } =
    [
        LocalizationService.GetText("EmulationPage.Emulation.AudioOutput.Auto"),
        LocalizationService.GetText("EmulationPage.Emulation.AudioOutput.Speaker"),
        LocalizationService.GetText("EmulationPage.Emulation.AudioOutput.Headset")
    ];

    /// <summary>
    /// Forwarded Bluetooth audio packet modes for the dropdown, in
    /// <see cref="BluetoothAudioPacketMode"/> order (single, double).
    /// </summary>
    public ObservableCollection<string> EmulationAudioPacketModes { get; } =
    [
        LocalizationService.GetText("EmulationPage.Emulation.AudioPacketMode.Single"),
        LocalizationService.GetText("EmulationPage.Emulation.AudioPacketMode.Double")
    ];

    /// <summary>
    /// Forwarded-audio source options for the dropdown, in <see cref="EmulationAudioSource"/>
    /// order (virtual controller endpoint, system output, a single application).
    /// </summary>
    public ObservableCollection<string> ForwardAudioSources { get; } =
    [
        LocalizationService.GetText("EmulationPage.Emulation.AudioSource.Controller"),
        LocalizationService.GetText("EmulationPage.Emulation.AudioSource.SystemOutput"),
        LocalizationService.GetText("EmulationPage.Emulation.AudioSource.Application")
    ];

    /// <summary>
    /// The applications currently producing audio, backing the application picker.
    /// </summary>
    public ObservableCollection<string> ForwardAudioApps { get; } = [];

    /// <summary>
    /// Application-picker modes for the dropdown, in <see cref="EmulationAudioAppMode"/>
    /// order (currently-playing list, executable file picked with the file browser).
    /// </summary>
    public ObservableCollection<string> ForwardAudioAppModes { get; } =
    [
        LocalizationService.GetText("AudioSource.Mode.PlayingApps"),
        LocalizationService.GetText("AudioSource.Mode.ExecutableFile")
    ];

    /// <summary>
    /// The emulation settings stored for the selected controller. The returned instance
    /// is the live stored object; mutating it and calling
    /// <see cref="ControllerInfoService.SaveEmulationSettings"/> persists the change.
    /// </summary>
    private EmulationSettings GetEmulationSettings()
        => _controllerService.GetEmulationSettings(CurrentKey);

    /// <summary>
    /// Whether the selected controller's emulation mode is DualSense, the only mode whose
    /// forwarded haptics can ignore the game's own payload.
    /// </summary>
    public bool IsDualSenseEmulation
    {
        get
        {
            return HasDevice && GetEmulationSettings().Mode == EmulationMode.DualSense;
        }
    }

    /// <summary>
    /// The physical controller output (<see cref="EmulationAudioOutput"/> value) used
    /// when forwarding host audio. Setting it persists the change immediately and
    /// applies it to the active forwarder without recreating the virtual controller.
    /// </summary>
    public int ForwardAudioOutputIndex
    {
        get
        {
            return !HasDevice ? (int)EmulationAudioOutput.Auto : (int)GetEmulationSettings().Forward.AudioOutput;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationAudioOutput output = (EmulationAudioOutput)Math.Clamp(value, (int)EmulationAudioOutput.Auto, (int)EmulationAudioOutput.Headset);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.AudioOutput == output)
            {
                return;
            }

            _log.Debug($"Setting forwarded audio output of {CurrentMac} to {output}");
            settings.Forward.AudioOutput = output;
            _controllerService.SaveEmulationSettings(CurrentKey, settings);
            OnPropertyChanged(nameof(ForwardAudioOutputIndex));
        }
    }

    /// <summary>
    /// The speaker volume applied to the physical controller when forwarding host
    /// audio (0-255, two-way, persisted).
    /// </summary>
    public int ForwardVolume
    {
        get
        {
            return !HasDevice ? 0 : GetEmulationSettings().Forward.Volume;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, 255);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.Volume == clamped)
            {
                return;
            }

            settings.Forward.Volume = clamped;
            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardVolume));
        }
    }

    /// <summary>
    /// The software gain applied to every audio source sent to the selected controller
    /// (1-8, 1 = unchanged, two-way, persisted). Values above 1 are soft-limited.
    /// </summary>
    public double ForwardSoftwareVolumeScale
    {
        get
        {
            return !HasDevice ? 1.0 : GetEmulationSettings().Forward.SoftwareVolumeScale;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            double clamped = Math.Clamp(Math.Round(value, 1, MidpointRounding.AwayFromZero), 1.0, 8.0);
            EmulationSettings settings = GetEmulationSettings();
            if (Math.Abs(settings.Forward.SoftwareVolumeScale - clamped) < 0.0001)
            {
                return;
            }

            settings.Forward.SoftwareVolumeScale = clamped;
            if (CurrentDevice?.Controller.Device is DualSenseDevice device)
            {
                // Apply the gain immediately so the audio player and special actions hear
                // the change before the debounced save reaches the emulation service.
                device.AudioLane.SoftwareGain = (float)clamped;
            }

            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardSoftwareVolumeScale));
        }
    }

    /// <summary>
    /// The controller-side audio buffer length used by forwarded Bluetooth audio
    /// (16-128, two-way, persisted).
    /// </summary>
    public int ForwardAudioBufferLength
    {
        get
        {
            return !HasDevice ? 48 : GetEmulationSettings().Forward.AudioBufferLength;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 16, 128);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.AudioBufferLength == clamped)
            {
                return;
            }

            settings.Forward.AudioBufferLength = clamped;
            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardAudioBufferLength));
        }
    }

    /// <summary>
    /// The Bluetooth audio report shape used when forwarding host audio
    /// (<see cref="BluetoothAudioPacketMode"/> value, two-way, persisted).
    /// </summary>
    public int ForwardAudioPacketModeIndex
    {
        get
        {
            return !HasDevice ? (int)BluetoothAudioPacketMode.Single : (int)GetEmulationSettings().Forward.PacketMode;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            BluetoothAudioPacketMode mode = (BluetoothAudioPacketMode)Math.Clamp(
                value,
                (int)BluetoothAudioPacketMode.Single,
                (int)BluetoothAudioPacketMode.Double);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.PacketMode == mode)
            {
                return;
            }

            _log.Debug($"Setting forwarded audio packet mode of {CurrentMac} to {mode}");
            settings.Forward.PacketMode = mode;
            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardAudioPacketModeIndex));
        }
    }

    /// <summary>
    /// The haptic vibration strength when forwarding host audio, as a percentage
    /// (0-200, two-way, persisted).
    /// </summary>
    public int ForwardHapticStrength
    {
        get
        {
            return !HasDevice ? 0 : GetEmulationSettings().Forward.Haptics;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, 200);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.Haptics == clamped)
            {
                return;
            }

            settings.Forward.Haptics = clamped;
            ScheduleEmulationSave();
            OnPropertyChanged(nameof(ForwardHapticStrength));
        }
    }

    /// <summary>
    /// Whether forwarded haptics ignore the game's own payload and always derive from
    /// the forwarded audio (two-way, persisted). The haptic strength slider always
    /// applies to audio-derived haptics. Only affects DualSense emulation over Bluetooth.
    /// </summary>
    public bool ForceAudioHaptics
    {
        get
        {
            return HasDevice && GetEmulationSettings().Forward.ForceAudioHaptics;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.ForceAudioHaptics == value)
            {
                return;
            }

            _log.Debug($"Setting forced audio-derived haptics of {CurrentMac} to {value}");
            settings.Forward.ForceAudioHaptics = value;
            _controllerService.SaveEmulationSettings(CurrentKey, settings);
            OnPropertyChanged(nameof(ForceAudioHaptics));
        }
    }

    /// <summary>
    /// The forwarded-audio source (<see cref="EmulationAudioSource"/> value) of the
    /// selected controller (two-way, persisted). The direct sources (system output and
    /// application) route straight to the physical controller and need no virtual device;
    /// the chosen application is stored separately and resolved to a process on capture.
    /// </summary>
    public int ForwardAudioSourceIndex
    {
        get
        {
            return !HasDevice ? 0 : (int)GetEmulationSettings().Forward.AudioSource;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationAudioSource source = (EmulationAudioSource)Math.Clamp(value, 0, (int)EmulationAudioSource.Application);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.AudioSource == source)
            {
                return;
            }

            _log.Debug($"Setting forwarded audio source of {CurrentMac} to {source}");
            settings.Forward.AudioSource = source;
            _controllerService.SaveEmulationSettings(CurrentKey, settings);
            OnPropertyChanged(nameof(ForwardAudioSourceIndex));
            OnPropertyChanged(nameof(IsForwardAudioAppVisible));
            OnPropertyChanged(nameof(IsForwardAudioAppListVisible));
            OnPropertyChanged(nameof(IsForwardAudioAppBrowseVisible));
            OnPropertyChanged(nameof(IsForwardAudioAppEmpty));
            if (source == EmulationAudioSource.Application)
            {
                _ = RefreshForwardAudioApps();
            }
            else
            {
                ClearForwardAudioApps();
            }
        }
    }

    /// <summary>
    /// Whether the application picker is shown: the forwarded-audio source is a single
    /// application.
    /// </summary>
    public bool IsForwardAudioAppVisible
    {
        get
        {
            return HasDevice && GetEmulationSettings().Forward.AudioSource == EmulationAudioSource.Application;
        }
    }

    /// <summary>
    /// The captured application as an index into the picker modes
    /// (<see cref="EmulationAudioAppMode"/> value, two-way, persisted). List mode shows
    /// the applications currently playing audio; executable-file mode shows the stored
    /// path with a browse button. The mode only decides how the path is chosen.
    /// </summary>
    public int ForwardAudioAppModeIndex
    {
        get
        {
            return !HasDevice ? 0 : (int)GetEmulationSettings().Forward.AudioAppMode;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationAudioAppMode mode = (EmulationAudioAppMode)Math.Clamp(value, 0, (int)EmulationAudioAppMode.ExecutableFile);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.AudioAppMode == mode)
            {
                return;
            }

            _log.Debug($"Setting forwarded audio application mode of {CurrentMac} to {mode}");
            settings.Forward.AudioAppMode = mode;
            _controllerService.SaveEmulationSettings(CurrentKey, settings);
            OnPropertyChanged(nameof(ForwardAudioAppModeIndex));
            OnPropertyChanged(nameof(IsForwardAudioAppListVisible));
            OnPropertyChanged(nameof(IsForwardAudioAppBrowseVisible));
            OnPropertyChanged(nameof(IsForwardAudioAppEmpty));
            if (mode == EmulationAudioAppMode.PlayingApps)
            {
                _ = RefreshForwardAudioApps();
            }
        }
    }

    /// <summary>
    /// Whether the application picker is shown but has no entries, so the page can
    /// explain that nothing is playing instead of showing an empty dropdown. Only
    /// list mode has entries to be empty.
    /// </summary>
    public bool IsForwardAudioAppEmpty
    {
        get
        {
            return IsForwardAudioAppListVisible && ForwardAudioApps.Count == 0;
        }
    }

    /// <summary>
    /// Whether the list of currently playing applications is shown: the application
    /// source is selected and the picker is in list mode.
    /// </summary>
    public bool IsForwardAudioAppListVisible
    {
        get
        {
            return IsForwardAudioAppVisible && ForwardAudioAppModeIndex == (int)EmulationAudioAppMode.PlayingApps;
        }
    }

    /// <summary>
    /// Whether the executable-file row is shown: the application source is selected and
    /// the picker is in executable-file mode.
    /// </summary>
    public bool IsForwardAudioAppBrowseVisible
    {
        get
        {
            return IsForwardAudioAppVisible && ForwardAudioAppModeIndex == (int)EmulationAudioAppMode.ExecutableFile;
        }
    }

    /// <summary>
    /// The executable path captured by the application source, shown (and browsed to) in
    /// executable-file mode. Empty when nothing has been chosen yet.
    /// </summary>
    public string ForwardAudioAppPath
    {
        get
        {
            return !HasDevice ? string.Empty : GetEmulationSettings().Forward.AudioApp;
        }
    }

    /// <summary>
    /// The selected application in <see cref="ForwardAudioApps"/> as an index, or -1 when
    /// none is selected. Setting it persists the executable path and applies it to the
    /// active forwarder.
    /// </summary>
    public int ForwardAudioAppIndex
    {
        get
        {
            if (!HasDevice)
            {
                return -1;
            }

            string stored = GetEmulationSettings().Forward.AudioApp;
            if (string.IsNullOrEmpty(stored))
            {
                return -1;
            }

            return _forwardAudioAppItems.FindIndex(app =>
                string.Equals(app.ExecutablePath, stored, StringComparison.OrdinalIgnoreCase));
        }
        set
        {
            if (!HasDevice || value < 0 || value >= _forwardAudioAppItems.Count)
            {
                return;
            }

            SetForwardAudioApp(_forwardAudioAppItems[value].ExecutablePath);
        }
    }

    /// <summary>
    /// Re-enumerates the applications currently producing audio and refreshes the app
    /// picker. Enumeration runs off the UI thread; a stored application that is not
    /// currently playing is kept in the list so the selection stays visible. Results for
    /// a controller switched away from meanwhile are discarded.
    /// </summary>
    [RelayCommand]
    private async Task RefreshForwardAudioApps()
    {
        if (!HasDevice)
        {
            return;
        }

        InputMonitorItem? device = MonitorState;
        string stored = GetEmulationSettings().Forward.AudioApp;
        List<AudioApplicationInfo> apps =
            new List<AudioApplicationInfo>(await Task.Run(_audioApplications.GetPlayingApplications));
        if (!ReferenceEquals(MonitorState, device))
        {
            return;
        }

        if (!string.IsNullOrEmpty(stored)
            && !apps.Any(app => string.Equals(app.ExecutablePath, stored, StringComparison.OrdinalIgnoreCase)))
        {
            apps.Insert(0, new AudioApplicationInfo(Path.GetFileNameWithoutExtension(stored), stored, 0));
        }

        _forwardAudioAppItems.Clear();
        _forwardAudioAppItems.AddRange(apps);

        List<string> names = new List<string>();
        foreach (AudioApplicationInfo app in _forwardAudioAppItems)
        {
            names.Add(app.DisplayName);
        }

        SyncForwardAudioApps(names);
        OnPropertyChanged(nameof(ForwardAudioAppIndex));
        OnPropertyChanged(nameof(IsForwardAudioAppEmpty));
    }

    /// <summary>
    /// Empties the app picker while the application source is not selected.
    /// </summary>
    private void ClearForwardAudioApps()
    {
        _forwardAudioAppItems.Clear();
        ForwardAudioApps.Clear();
        OnPropertyChanged(nameof(ForwardAudioAppIndex));
        OnPropertyChanged(nameof(IsForwardAudioAppEmpty));
    }

    /// <summary>
    /// Updates the app picker options in place, mirroring the auto-profile page's option
    /// sync so a bound ComboBox selection survives a refresh.
    /// </summary>
    private void SyncForwardAudioApps(List<string> desired)
    {
        int index = 0;
        while (index < desired.Count && index < ForwardAudioApps.Count)
        {
            if (!string.Equals(ForwardAudioApps[index], desired[index], StringComparison.Ordinal))
            {
                ForwardAudioApps[index] = desired[index];
            }

            index++;
        }

        while (index < desired.Count)
        {
            ForwardAudioApps.Add(desired[index]);
            index++;
        }

        while (ForwardAudioApps.Count > desired.Count)
        {
            ForwardAudioApps.RemoveAt(ForwardAudioApps.Count - 1);
        }
    }

    /// <summary>
    /// Persists the chosen application and applies it to the active forwarder. No-op when
    /// the selection did not change.
    /// </summary>
    private void SetForwardAudioApp(string applicationPath)
    {
        EmulationSettings settings = GetEmulationSettings();
        if (string.Equals(settings.Forward.AudioApp, applicationPath, StringComparison.Ordinal))
        {
            return;
        }

        _log.Debug($"Setting forwarded audio application of {CurrentMac} to '{applicationPath}'");
        settings.Forward.AudioApp = applicationPath;
        _controllerService.SaveEmulationSettings(CurrentKey, settings);
        OnPropertyChanged(nameof(ForwardAudioAppIndex));
        OnPropertyChanged(nameof(ForwardAudioAppPath));
    }

    /// <summary>
    /// Applies an executable picked with the file browser: stores it as the captured
    /// application and refreshes the list, so the choice also appears in list mode.
    /// </summary>
    /// <param name="applicationPath">Full path of the chosen executable.</param>
    public void SelectAudioApplication(string applicationPath)
    {
        if (!HasDevice || string.IsNullOrWhiteSpace(applicationPath))
        {
            return;
        }

        SetForwardAudioApp(applicationPath);
        OnPropertyChanged(nameof(ForwardAudioAppPath));
        _ = RefreshForwardAudioApps();
    }

    /// <summary>
    /// Restarts the debounce timer so the pending emulation settings save is delayed
    /// until slider changes stop, avoiding a disk write per drag step.
    /// </summary>
    private void ScheduleEmulationSave()
    {
        _emulationSaveTimer.Stop();
        _emulationSaveTimer.Start();
    }

    /// <summary>
    /// Flushes the debounced emulation settings save to disk once the debounce period
    /// elapses.
    /// </summary>
    private void SaveEmulationDebounced()
    {
        _emulationSaveTimer.Stop();
        if (!HasDevice)
        {
            return;
        }

        _controllerService.SaveEmulationSettings(CurrentKey, GetEmulationSettings());
    }

    // ── Dongle configuration ───────────────────────────────────

    /// <summary>
    /// Builds the localized dropdown options from the model enums and pin list.
    /// </summary>
    private void BuildDongleOptions()
    {
        DonglePollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.250"));
        DonglePollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.500"));
        DonglePollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.1000"));
        DonglePollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.RealTime"));

        DongleLegacyPollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.250"));
        DongleLegacyPollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.500"));
        DongleLegacyPollingRates.Add(LocalizationService.GetText("CustomizationPage.Dongle.PollingRate.RealTime"));

        DongleControllerModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.ControllerMode.DS5"));
        DongleControllerModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.ControllerMode.DSE"));
        DongleControllerModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.ControllerMode.Auto"));

        DongleAudioDevices.Add(LocalizationService.GetText("CustomizationPage.Dongle.Audio.Auto"));
        DongleAudioDevices.Add(LocalizationService.GetText("CustomizationPage.Dongle.Audio.BuiltIn"));
        DongleAudioDevices.Add(LocalizationService.GetText("CustomizationPage.Dongle.Audio.Headphone"));
        DongleAudioDevices.Add(LocalizationService.GetText("CustomizationPage.Dongle.Audio.Disabled"));

        DongleVolumeLockModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.LockVolume.Off"));
        DongleVolumeLockModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.LockVolume.GameAndSoftware"));
        DongleVolumeLockModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.LockVolume.SystemVolume"));

        foreach (int pin in DongleConfigItem.GpioPinValues)
        {
            DongleGpioPins.Add(pin == DongleConfig.DisabledStatusGpioPin
                ? LocalizationService.GetText("CustomizationPage.Dongle.GpioPin.Disabled")
                : pin.ToString());
        }

        DongleGpioModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.GpioMode.High"));
        DongleGpioModes.Add(LocalizationService.GetText("CustomizationPage.Dongle.GpioMode.Pulse"));
    }

    /// <summary>
    /// Subscribes the editor's change event when the selected dongle's editor is replaced.
    /// </summary>
    partial void OnDongleConfigEditorChanged(DongleConfigItem? oldValue, DongleConfigItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Changed -= OnDongleConfigChanged;
        }

        if (newValue is not null)
        {
            newValue.Changed += OnDongleConfigChanged;
        }

        OnPropertyChanged(nameof(HasDongleConfigEditor));
        NotifyDongleCommands();
    }

    /// <summary>
    /// Schedules a debounced apply when the user edits a dongle setting.
    /// </summary>
    private void OnDongleConfigChanged(object? sender, EventArgs e)
    {
        if (_suppressDongleConfigChanged || DongleConfigEditor is null)
        {
            return;
        }

        SetDongleStatus(DongleOperationStatus.Applying);
        _dongleApplyTimer.Stop();
        _dongleApplyTimer.Start();
    }

    /// <summary>
    /// Loads the selected dongle's configuration and populates the editor.
    /// </summary>
    private async Task LoadDongleConfigAsync()
    {
        DeviceInfoItem? item = CurrentDevice;
        DualSenseDongle? device = CurrentDongleDevice;
        if (item is null || device is null || !item.IsDongle)
        {
            return;
        }

        SetDongleStatus(DongleOperationStatus.Reading);
        await _dongleIoSync.WaitAsync();
        try
        {
            DongleConfig? config = await Task.Run(device.ReadDongleConfig);
            if (!ReferenceEquals(CurrentDevice, item) || !item.IsDongle)
            {
                return;
            }

            if (config is null)
            {
                SetDongleStatus(DongleOperationStatus.Unavailable);
                return;
            }

            LoadDongleSnapshot(config);
            _usbEffectiveDongleBaseline = UsbEffectiveDongleConfig.From(config);
            NeedsDongleUsbReconnect = false;
            SetDongleStatus(DongleOperationStatus.Loaded);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to read the DS5Dongle configuration", ex);
            if (ReferenceEquals(CurrentDevice, item))
            {
                SetDongleStatus(DongleOperationStatus.Failed);
            }
        }
        finally
        {
            _dongleIoSync.Release();
        }
    }

    /// <summary>
    /// Replaces the editor's contents with a device snapshot without triggering an apply.
    /// </summary>
    private void LoadDongleSnapshot(DongleConfig config)
    {
        _lastAppliedDongleConfig = config;
        _suppressDongleConfigChanged = true;
        try
        {
            if (DongleConfigEditor is null)
            {
                DongleConfigEditor = new DongleConfigItem(config);
            }
            else
            {
                DongleConfigEditor.Load(config);
            }
        }
        finally
        {
            _suppressDongleConfigChanged = false;
        }
    }

    /// <summary>
    /// Applies the editor's current values to the dongle and reloads the firmware-validated
    /// snapshot. Returns <c>true</c> when the device already matched or the apply succeeded.
    /// </summary>
    private async Task<bool> ApplyDongleConfigAsync(bool force = false)
    {
        if (IsDongleBusy)
        {
            return false;
        }

        DeviceInfoItem? item = CurrentDevice;
        await _dongleIoSync.WaitAsync();
        try
        {
            return await ApplyDongleConfigCoreAsync(force);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to apply the DS5Dongle configuration", ex);
            if (ReferenceEquals(CurrentDevice, item))
            {
                SetDongleStatus(DongleOperationStatus.Failed);
            }

            return false;
        }
        finally
        {
            _dongleIoSync.Release();
        }
    }

    /// <summary>
    /// Applies the editor's current values while the caller holds <see cref="_dongleIoSync"/>.
    /// </summary>
    private async Task<bool> ApplyDongleConfigCoreAsync(bool force)
    {
        DongleConfigItem? editor = DongleConfigEditor;
        DeviceInfoItem? item = CurrentDevice;
        DualSenseDongle? device = CurrentDongleDevice;
        if (editor is null || item is null || device is null || !item.IsDongle)
        {
            return false;
        }

        DongleConfig requested = editor.ToConfig();
        if (!force
            && _lastAppliedDongleConfig is { } current
            && current.IsEquivalentTo(requested))
        {
            return true;
        }

        int revision = editor.Revision;
        SetDongleStatus(DongleOperationStatus.Applying);
        DongleConfig? applied = await Task.Run(() => device.ApplyDongleConfig(requested));
        if (!ReferenceEquals(DongleConfigEditor, editor) || !ReferenceEquals(CurrentDevice, item))
        {
            return false;
        }

        if (applied is null)
        {
            SetDongleStatus(DongleOperationStatus.Unavailable);
            return false;
        }

        _lastAppliedDongleConfig = applied;
        UpdateDongleReconnectWarning(applied);

        if (editor.Revision != revision)
        {
            // The user edited another field while the write was in flight. Keep the newer
            // editor values and apply them next instead of replacing them with the read-back.
            SetDongleStatus(DongleOperationStatus.Applying);
            _dongleApplyTimer.Stop();
            _dongleApplyTimer.Start();
            return true;
        }

        _suppressDongleConfigChanged = true;
        try
        {
            editor.Load(applied);
        }
        finally
        {
            _suppressDongleConfigChanged = false;
        }

        SetDongleStatus(DongleOperationStatus.Applied);

        if (!applied.IsEquivalentTo(editor.ToConfig()))
        {
            _dongleApplyTimer.Stop();
            _dongleApplyTimer.Start();
        }

        return true;
    }

    /// <summary>
    /// Applies the editor until the firmware read-back matches it. If the user changes
    /// another field while a write is in flight, the newer edit is applied on the next
    /// pass instead of being saved or discarded by a follow-up action.
    /// </summary>
    private async Task<bool> ApplyDongleConfigUntilStableAsync()
    {
        const int MaxAttempts = 3;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (!await ApplyDongleConfigCoreAsync(true))
            {
                return false;
            }

            DongleConfigItem? editor = DongleConfigEditor;
            if (editor is null || _lastAppliedDongleConfig is not { } applied)
            {
                return false;
            }

            if (applied.IsEquivalentTo(editor.ToConfig()))
            {
                return true;
            }
        }

        _log.Warning("DS5Dongle configuration did not stabilize after repeated edits");
        return false;
    }

    /// <summary>
    /// Re-reads the dongle configuration, discarding any pending editor changes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReadDongleConfig))]
    private async Task ReadDongleConfig()
    {
        DeviceInfoItem? item = CurrentDevice;
        DualSenseDongle? device = CurrentDongleDevice;
        if (item is null || device is null)
        {
            return;
        }

        _dongleApplyTimer.Stop();
        IsDongleBusy = true;
        SetDongleStatus(DongleOperationStatus.Reading);
        await _dongleIoSync.WaitAsync();
        try
        {
            DongleConfig? config = await Task.Run(device.ReadDongleConfig);
            if (!ReferenceEquals(CurrentDevice, item))
            {
                return;
            }

            if (config is null)
            {
                SetDongleStatus(DongleOperationStatus.Unavailable);
                return;
            }

            LoadDongleSnapshot(config);
            _usbEffectiveDongleBaseline = UsbEffectiveDongleConfig.From(config);
            NeedsDongleUsbReconnect = false;
            SetDongleStatus(DongleOperationStatus.Loaded);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to read the DS5Dongle configuration", ex);
            if (ReferenceEquals(CurrentDevice, item))
            {
                SetDongleStatus(DongleOperationStatus.Failed);
            }
        }
        finally
        {
            _dongleIoSync.Release();
            IsDongleBusy = false;
        }
    }

    /// <summary>
    /// Persists the current dongle configuration to flash after applying any pending edit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseDongleConfig))]
    private async Task SaveDongleConfigToFlash()
    {
        DeviceInfoItem? item = CurrentDevice;
        DualSenseDongle? device = CurrentDongleDevice;
        if (item is null || device is null)
        {
            return;
        }

        IsDongleBusy = true;
        _dongleApplyTimer.Stop();
        await _dongleIoSync.WaitAsync();
        try
        {
            if (!await ApplyDongleConfigUntilStableAsync() || !ReferenceEquals(CurrentDevice, item))
            {
                return;
            }

            _dongleApplyTimer.Stop();
            SetDongleStatus(DongleOperationStatus.Saving);
            await Task.Run(device.SaveDongleConfigToFlash);
            if (!ReferenceEquals(CurrentDevice, item))
            {
                return;
            }

            SetDongleStatus(DongleOperationStatus.Saved);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save the DS5Dongle configuration to flash", ex);
            if (ReferenceEquals(CurrentDevice, item))
            {
                SetDongleStatus(DongleOperationStatus.Failed);
            }
        }
        finally
        {
            _dongleIoSync.Release();
            IsDongleBusy = false;
        }
    }

    /// <summary>
    /// Applies pending edits and asks the dongle to reconnect its USB device.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseDongleConfig))]
    private async Task ReconnectDongleUsb()
    {
        DeviceInfoItem? item = CurrentDevice;
        DualSenseDongle? device = CurrentDongleDevice;
        if (item is null || device is null)
        {
            return;
        }

        IsDongleBusy = true;
        _dongleApplyTimer.Stop();
        await _dongleIoSync.WaitAsync();
        try
        {
            if (!await ApplyDongleConfigUntilStableAsync() || !ReferenceEquals(CurrentDevice, item))
            {
                return;
            }

            _dongleApplyTimer.Stop();
            SetDongleStatus(DongleOperationStatus.Reconnecting);
            await Task.Run(device.ReconnectDongleUsb);

            if (_lastAppliedDongleConfig is { } applied)
            {
                _usbEffectiveDongleBaseline = UsbEffectiveDongleConfig.From(applied);
            }

            NeedsDongleUsbReconnect = false;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to reconnect the DS5Dongle USB device", ex);
            if (ReferenceEquals(CurrentDevice, item))
            {
                SetDongleStatus(DongleOperationStatus.Failed);
            }
        }
        finally
        {
            _dongleIoSync.Release();
            IsDongleBusy = false;
        }
    }

    /// <summary>
    /// Replaces the dongle configuration with defaults, applies it, and saves it to flash.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseDongleConfig))]
    private async Task ResetDongleConfig()
    {
        DongleConfigItem? editor = DongleConfigEditor;
        DeviceInfoItem? item = CurrentDevice;
        DualSenseDongle? device = CurrentDongleDevice;
        if (editor is null || item is null || device is null)
        {
            return;
        }

        bool confirmed = await _messageBox.ShowConfirmationAsync(
            LocalizationService.GetText("CustomizationPage.Dongle.Reset.Confirm.Title"),
            LocalizationService.GetText("CustomizationPage.Dongle.Reset.Confirm.Message"));
        if (!confirmed)
        {
            return;
        }

        DongleConfig defaults = DongleConfig.CreateDefault(editor.Template.BodyLength) with
        {
            Version = editor.Template.Version,
            SpeakerVolume = editor.Template.SpeakerVolume,
            HeadsetVolume = editor.Template.HeadsetVolume,
            SleepOnDock = editor.Template.SleepOnDock,
            TrailingBytes = editor.Template.TrailingBytes
        };

        IsDongleBusy = true;
        _dongleApplyTimer.Stop();
        await _dongleIoSync.WaitAsync();
        try
        {
            if (!ReferenceEquals(CurrentDevice, item) || !ReferenceEquals(DongleConfigEditor, editor))
            {
                return;
            }

            _suppressDongleConfigChanged = true;
            try
            {
                editor.Load(defaults);
            }
            finally
            {
                _suppressDongleConfigChanged = false;
            }

            if (!await ApplyDongleConfigUntilStableAsync() || !ReferenceEquals(CurrentDevice, item))
            {
                return;
            }

            _dongleApplyTimer.Stop();
            SetDongleStatus(DongleOperationStatus.Saving);
            await Task.Run(device.SaveDongleConfigToFlash);
            if (!ReferenceEquals(CurrentDevice, item))
            {
                return;
            }

            SetDongleStatus(DongleOperationStatus.Reset);
        }
        catch (Exception ex)
        {
            _log.Error("Failed to reset the DS5Dongle configuration", ex);
            if (ReferenceEquals(CurrentDevice, item))
            {
                SetDongleStatus(DongleOperationStatus.Failed);
            }
        }
        finally
        {
            _dongleIoSync.Release();
            IsDongleBusy = false;
        }
    }

    /// <summary>
    /// Updates the reconnect warning by comparing the applied USB-interface settings with
    /// the baseline captured at the last read or reconnect.
    /// </summary>
    private void UpdateDongleReconnectWarning(DongleConfig config)
    {
        if (_usbEffectiveDongleBaseline is not { } baseline)
        {
            NeedsDongleUsbReconnect = false;
            return;
        }

        NeedsDongleUsbReconnect = !baseline.Equals(UsbEffectiveDongleConfig.From(config));
    }

    /// <summary>
    /// Whether the configuration can be read. Read stays available after a failed load so
    /// the user can retry without switching controllers.
    /// </summary>
    private bool CanReadDongleConfig() => IsDongle && !IsDongleBusy;

    /// <summary>
    /// Whether a dongle configuration command that needs a loaded editor can run.
    /// </summary>
    private bool CanUseDongleConfig() => IsDongle && DongleConfigEditor is not null && !IsDongleBusy;

    /// <summary>
    /// Re-notifies the dongle commands after the selected device changes.
    /// </summary>
    private void NotifyDongleCommands()
    {
        ReadDongleConfigCommand.NotifyCanExecuteChanged();
        SaveDongleConfigToFlashCommand.NotifyCanExecuteChanged();
        ReconnectDongleUsbCommand.NotifyCanExecuteChanged();
        ResetDongleConfigCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Publishes the localized status text for a dongle configuration operation.
    /// </summary>
    private void SetDongleStatus(DongleOperationStatus status)
    {
        DongleStatusText = status switch
        {
            DongleOperationStatus.Reading => LocalizationService.GetText("CustomizationPage.Dongle.Status.Reading"),
            DongleOperationStatus.Applying => LocalizationService.GetText("CustomizationPage.Dongle.Status.Applying"),
            DongleOperationStatus.Applied => LocalizationService.GetText("CustomizationPage.Dongle.Status.Applied"),
            DongleOperationStatus.Saving => LocalizationService.GetText("CustomizationPage.Dongle.Status.Saving"),
            DongleOperationStatus.Saved => LocalizationService.GetText("CustomizationPage.Dongle.Status.Saved"),
            DongleOperationStatus.Reconnecting => LocalizationService.GetText("CustomizationPage.Dongle.Status.Reconnecting"),
            DongleOperationStatus.Reset => LocalizationService.GetText("CustomizationPage.Dongle.Status.Reset"),
            DongleOperationStatus.Loaded => LocalizationService.GetText("CustomizationPage.Dongle.Status.Loaded"),
            DongleOperationStatus.Unavailable => LocalizationService.GetText("CustomizationPage.Dongle.Status.Unavailable"),
            _ => LocalizationService.GetText("CustomizationPage.Dongle.Status.Failed")
        };
    }

    /// <summary>
    /// Dongle configuration operations shown in the status line.
    /// </summary>
    private enum DongleOperationStatus
    {
        Reading,
        Applying,
        Applied,
        Saving,
        Saved,
        Reconnecting,
        Reset,
        Loaded,
        Unavailable,
        Failed
    }

    /// <summary>
    /// USB-interface settings that only take effect after the dongle re-enumerates.
    /// </summary>
    private readonly record struct UsbEffectiveDongleConfig(
        DonglePollingRateMode PollingRateMode,
        DongleControllerMode ControllerMode,
        bool EnableUsbSerialNumber,
        bool EnableKeyboard,
        bool EnableWake)
    {
        /// <summary>
        /// Captures the USB-interface subset of a configuration snapshot.
        /// </summary>
        public static UsbEffectiveDongleConfig From(DongleConfig config)
        {
            return new UsbEffectiveDongleConfig(
                config.PollingRateMode,
                config.ControllerMode,
                config.EnableUsbSerialNumber,
                config.EnableKeyboard,
                config.EnableWake);
        }
    }

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public CustomizationPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _profileService = App.Services.GetRequiredService<ProfileService>();
        _profileActivation = App.Services.GetRequiredService<IProfileActivation>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _autoProfileService = App.Services.GetRequiredService<AutoProfileService>();
        _specialActionService = App.Services.GetRequiredService<SpecialActionService>();
        _messageBox = App.Services.GetRequiredService<IMessageBoxService>();
        _emulation = App.Services.GetRequiredService<IEmulationService>();
        _audioApplications = App.Services.GetRequiredService<AudioApplicationService>();

        _emulationSaveTimer = new DispatcherTimer
        {
            Interval = EmulationSaveDebounce
        };
        _emulationSaveTimer.Tick += (_, _) => SaveEmulationDebounced();
        _dongleApplyTimer.Tick += (_, _) =>
        {
            _dongleApplyTimer.Stop();
            _ = ApplyDongleConfigAsync();
        };
        BuildDongleOptions();
        _vibrationSaveTimer = new DispatcherTimer
        {
            Interval = EmulationSaveDebounce
        };
        _vibrationSaveTimer.Tick += (_, _) => SaveVibrationDebounced();
        _controllerService.ControllersChanged += OnControllerInfoChanged;
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        Refresh();
    }

    /// <summary>
    /// Resynchronizes the page with the current profile service state and controller selection.
    /// Called on construction and from the page's <c>OnLoaded</c>.
    /// </summary>
    public void Refresh()
    {
        RebuildProfiles();
        UpdateDevice();
    }

    /// <summary>
    /// Tracks the shell's controller selection.
    /// </summary>
    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedItem))
        {
            UpdateDevice();
        }
    }

    /// <summary>
    /// Re-notifies the forwarding settings when controller info changes elsewhere (e.g.
    /// the emulation mode toggled on the emulation page), so bindings stay current
    /// without re-selecting the controller.
    /// </summary>
    private void OnControllerInfoChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDualSenseEmulation));
        OnPropertyChanged(nameof(ForwardAudioOutputIndex));
        OnPropertyChanged(nameof(ForwardVolume));
        OnPropertyChanged(nameof(ForwardSoftwareVolumeScale));
        OnPropertyChanged(nameof(ForwardAudioBufferLength));
        OnPropertyChanged(nameof(ForwardAudioPacketModeIndex));
        OnPropertyChanged(nameof(ForwardHapticStrength));
        OnPropertyChanged(nameof(ForceAudioHaptics));
        OnPropertyChanged(nameof(ForwardAudioSourceIndex));
        OnPropertyChanged(nameof(VibrationEncodingIndex));
        OnPropertyChanged(nameof(MotorPowerLevelEnabled));
        OnPropertyChanged(nameof(RumblePowerReduction));
        OnPropertyChanged(nameof(TriggerPowerReduction));
        OnPropertyChanged(nameof(HapticLowPassFilter));
    }

    /// <summary>
    /// Rebuilds the device context from the shell's selected controller.
    /// </summary>
    private void UpdateDevice()
    {
        FlushVibrationSettings();

        _dongleApplyTimer.Stop();
        IsDongleBusy = false;
        DongleConfigEditor = null;
        _lastAppliedDongleConfig = null;
        _usbEffectiveDongleBaseline = null;
        NeedsDongleUsbReconnect = false;
        DongleStatusText = string.Empty;

        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new DeviceInfoItem(selected) : null;
        _previousItem = CurrentDevice;

        _previousMonitorState?.Dispose();
        MonitorState = selected is not null ? new InputMonitorItem(selected) : null;
        _previousMonitorState = MonitorState;

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(IsDongle));
        NotifyDongleCommands();
        OnPropertyChanged(nameof(HasFullPlayerLedSupport));
        OnPropertyChanged(nameof(CurrentMac));
        OnPropertyChanged(nameof(CurrentDevicePath));
        OnPropertyChanged(nameof(MonitorState));
        OnPropertyChanged(nameof(LeftTriggerMode));
        OnPropertyChanged(nameof(RightTriggerMode));

        RebuildVibrationModes();
        ApplyStoredVibrationSettings();
        OnPropertyChanged(nameof(VibrationEncodingIndex));
        OnPropertyChanged(nameof(MotorPowerLevelEnabled));
        OnPropertyChanged(nameof(RumblePowerReduction));
        OnPropertyChanged(nameof(TriggerPowerReduction));
        OnPropertyChanged(nameof(HapticLowPassFilter));

        OnPropertyChanged(nameof(IsDualSenseEmulation));
        OnPropertyChanged(nameof(ForwardAudioOutputIndex));
        OnPropertyChanged(nameof(ForwardVolume));
        OnPropertyChanged(nameof(ForwardSoftwareVolumeScale));
        OnPropertyChanged(nameof(ForwardAudioBufferLength));
        OnPropertyChanged(nameof(ForwardAudioPacketModeIndex));
        OnPropertyChanged(nameof(ForwardHapticStrength));
        OnPropertyChanged(nameof(ForceAudioHaptics));
        OnPropertyChanged(nameof(ForwardAudioSourceIndex));
        OnPropertyChanged(nameof(IsForwardAudioAppVisible));
        OnPropertyChanged(nameof(IsForwardAudioAppListVisible));
        OnPropertyChanged(nameof(IsForwardAudioAppBrowseVisible));
        OnPropertyChanged(nameof(IsForwardAudioAppEmpty));
        OnPropertyChanged(nameof(ForwardAudioAppModeIndex));
        OnPropertyChanged(nameof(ForwardAudioAppPath));
        if (IsForwardAudioAppVisible)
        {
            _ = RefreshForwardAudioApps();
        }
        else
        {
            ClearForwardAudioApps();
        }

        RebuildSpecialActions();

        if (IsDongle)
        {
            _ = LoadDongleConfigAsync();
        }
    }
}