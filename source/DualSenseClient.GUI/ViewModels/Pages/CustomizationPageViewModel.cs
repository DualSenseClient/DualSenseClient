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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.Devices;
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
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public DeviceInfoItem? CurrentDevice { get; private set; }

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
    /// The concrete DualSense device of the selected controller, or <c>null</c> for
    /// non-DualSense devices.
    /// </summary>
    private DualSenseDevice? CurrentDualSenseDevice
    {
        get
        {
            return MonitorState?.Controller.Device as DualSenseDevice;
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
    /// The name of the profile the selected controller is currently using: the bound
    /// profile, or the default profile when unbound.
    /// </summary>
    private string CurrentUsedProfileName
    {
        get
        {
            return _controllerService.GetBoundProfileName(CurrentMac, CurrentDevicePath) ?? ProfileService.DefaultProfileName;
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
        LocalizationService.GetText("ProfilePage.MicLed.Mode.Off"),
        LocalizationService.GetText("ProfilePage.MicLed.Mode.On"),
        LocalizationService.GetText("ProfilePage.MicLed.Mode.Pulse")
    ];

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
            LocalizationService.GetText("ProfilePage.Delete.Title"),
            LocalizationService.GetText("ProfilePage.Delete.Message").Replace("{name}", name));
        if (!confirmed)
        {
            return;
        }

        bool controllerWasUsing = HasDevice && string.Equals(CurrentUsedProfileName, name, StringComparison.OrdinalIgnoreCase);
        _profileService.DeleteProfile(name);
        _controllerService.RemoveProfileReferences(name);
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
            SpecialActionService.GetControllerId(CurrentMac, CurrentDevicePath));
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
            _log.Error($"Failed to export special actions: {ex.Message}");
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("ProfilePage.SpecialActions.Export.Error.Title"),
                LocalizationService.GetText("ProfilePage.SpecialActions.Export.Error.Message"));
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
            _log.Error($"Failed to export special action '{item.Action.Name}': {ex.Message}");
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("ProfilePage.SpecialActions.Export.Error.Title"),
                LocalizationService.GetText("ProfilePage.SpecialActions.Export.Error.Message"));
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
                    LocalizationService.GetText("ProfilePage.SpecialActions.Import.Empty.Title"),
                    LocalizationService.GetText("ProfilePage.SpecialActions.Import.Empty.Message"));
                return;
            }

            await _messageBox.ShowInfoAsync(
                LocalizationService.GetText("ProfilePage.SpecialActions.Import.Success.Title"),
                LocalizationService.GetText("ProfilePage.SpecialActions.Import.Success.Message").Replace("{count}", count.ToString()));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to import special actions: {ex.Message}");
            await _messageBox.ShowErrorAsync(
                LocalizationService.GetText("ProfilePage.SpecialActions.Import.Error.Title"),
                LocalizationService.GetText("ProfilePage.SpecialActions.Import.Error.Message"));
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

        string? controllerId = SpecialActionService.GetControllerId(CurrentMac, CurrentDevicePath);
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
            controllerId ?? SpecialActionService.GetControllerId(CurrentMac, CurrentDevicePath),
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

        string applied = CurrentUsedProfileName;
        if (!string.Equals(item.Name, applied, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Profile? profile = GetCurrentControllerProfile();
        if (profile is not null)
        {
            ApplyProfileToController(profile);
        }
    }

    /// <summary>
    /// Applies the profile currently used by the selected controller (bound profile, or the
    /// default when unbound), so an assignment change takes effect immediately. Only the
    /// controller-side light settings are reapplied.
    /// </summary>
    private void ApplyBoundProfileToController()
    {
        if (!HasDevice)
        {
            return;
        }

        Profile? profile = GetCurrentControllerProfile();
        if (profile is not null)
        {
            ApplyProfileToController(profile);
        }
    }

    /// <summary>
    /// Gets the profile the selected controller is currently using: the bound profile, or
    /// the default profile when unbound or the bound profile no longer exists.
    /// </summary>
    private Profile? GetCurrentControllerProfile()
    {
        string? bound = _controllerService.GetBoundProfileName(CurrentMac, CurrentDevicePath);
        return _profileService.GetProfile(bound ?? ProfileService.DefaultProfileName);
    }

    /// <summary>
    /// Applies a profile to the selected controller.
    /// </summary>
    private void ApplyProfileToController(Profile profile)
    {
        if (CurrentDevice?.Controller.Device is not DualSenseDevice device)
        {
            return;
        }

        _log.Info($"Applying profile '{profile.Name}' to {CurrentMac}");
        device.ApplyProfile(profile);
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
    /// The applications currently producing audio, backing the application picker.
    /// </summary>
    private readonly List<AudioApplicationInfo> _forwardAudioAppItems = [];

    /// <summary>
    /// Serializes background audio-source applies: each queued apply waits for the
    /// previous one, so rapid source or application changes reach the emulation service
    /// in the order the UI made them.
    /// </summary>
    private Task _audioSourceApply = Task.CompletedTask;

    /// <summary>
    /// Forwarded audio output options for the dropdown, in
    /// <see cref="EmulationAudioOutput"/> order (speaker, headset).
    /// </summary>
    public ObservableCollection<string> EmulationAudioOutputs { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioOutput.Speaker"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioOutput.Headset")
    ];

    /// <summary>
    /// Forwarded-audio source options for the dropdown, in <see cref="EmulationAudioSource"/>
    /// order (virtual controller endpoint, system output, a single application).
    /// </summary>
    public ObservableCollection<string> ForwardAudioSources { get; } =
    [
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioSource.Controller"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioSource.SystemOutput"),
        LocalizationService.GetText("VirtualControllerPage.Emulation.AudioSource.Application")
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
        => _controllerService.GetEmulationSettings(CurrentMac, CurrentDevicePath);

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
            return !HasDevice ? 0 : (int)GetEmulationSettings().Forward.AudioOutput;
        }
        set
        {
            if (!HasDevice)
            {
                return;
            }

            EmulationAudioOutput output = (EmulationAudioOutput)Math.Clamp(value, 0, (int)EmulationAudioOutput.Headset);
            EmulationSettings settings = GetEmulationSettings();
            if (settings.Forward.AudioOutput == output)
            {
                return;
            }

            _log.Info($"Setting forwarded audio output of {CurrentMac} to {output}");
            settings.Forward.AudioOutput = output;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
            OnPropertyChanged(nameof(ForwardAudioOutputIndex));
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingAudioOutput(device, output == EmulationAudioOutput.Headset);
            }
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
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingAudioOptions(device, (byte)clamped, settings.Forward.Haptics / 100f);
            }
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
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingAudioOptions(device, (byte)settings.Forward.Volume, clamped / 100f);
            }
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

            _log.Info($"Setting audio-only haptics of {CurrentMac} to {value}");
            settings.Forward.ForceAudioHaptics = value;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
            OnPropertyChanged(nameof(ForceAudioHaptics));
            if (CurrentDualSenseDevice is { } device)
            {
                _emulation.SetForwardingHapticsSource(device, value);
            }
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

            _log.Info($"Setting forwarded audio source of {CurrentMac} to {source}");
            settings.Forward.AudioSource = source;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
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

            if (CurrentDualSenseDevice is { } device)
            {
                ApplyForwardAudioSource(device, source, settings.Forward.AudioApp);
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

            _log.Info($"Setting forwarded audio application mode of {CurrentMac} to {mode}");
            settings.Forward.AudioAppMode = mode;
            _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
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
    /// Applies a forwarded-audio source change off the UI thread: resolving the stored
    /// application can scan every running process and starting the capture can wait
    /// seconds for activation.
    /// </summary>
    private void ApplyForwardAudioSource(DualSenseDevice device, EmulationAudioSource source, string audioApp)
    {
        _audioSourceApply = _audioSourceApply.ContinueWith(
            _ =>
            {
                try
                {
                    _emulation.SetForwardingAudioSource(device, source, audioApp);
                }
                catch (Exception ex)
                {
                    _log.Error($"Applying the forwarded audio source failed: {ex.Message}");
                }
            },
            TaskScheduler.Default);
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

        _log.Info($"Setting forwarded audio application of {CurrentMac} to '{applicationPath}'");
        settings.Forward.AudioApp = applicationPath;
        _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, settings);
        OnPropertyChanged(nameof(ForwardAudioAppIndex));
        OnPropertyChanged(nameof(ForwardAudioAppPath));
        if (CurrentDualSenseDevice is { } device)
        {
            ApplyForwardAudioSource(device, settings.Forward.AudioSource, applicationPath);
        }
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

        _controllerService.SaveEmulationSettings(CurrentMac, CurrentDevicePath, GetEmulationSettings());
    }

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public CustomizationPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _profileService = App.Services.GetRequiredService<ProfileService>();
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
        OnPropertyChanged(nameof(ForwardHapticStrength));
        OnPropertyChanged(nameof(ForceAudioHaptics));
        OnPropertyChanged(nameof(ForwardAudioSourceIndex));
    }

    /// <summary>
    /// Rebuilds the device context from the shell's selected controller.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new DeviceInfoItem(selected) : null;
        _previousItem = CurrentDevice;

        _previousMonitorState?.Dispose();
        MonitorState = selected is not null ? new InputMonitorItem(selected) : null;
        _previousMonitorState = MonitorState;

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(HasFullPlayerLedSupport));
        OnPropertyChanged(nameof(CurrentMac));
        OnPropertyChanged(nameof(CurrentDevicePath));
        OnPropertyChanged(nameof(MonitorState));
        OnPropertyChanged(nameof(LeftTriggerMode));
        OnPropertyChanged(nameof(RightTriggerMode));

        OnPropertyChanged(nameof(IsDualSenseEmulation));
        OnPropertyChanged(nameof(ForwardAudioOutputIndex));
        OnPropertyChanged(nameof(ForwardVolume));
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
    }
}