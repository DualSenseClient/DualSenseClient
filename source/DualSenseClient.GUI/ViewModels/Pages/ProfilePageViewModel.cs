using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// ViewModel for the profile manager page. Lets the user create, rename, duplicate, delete,
/// and edit controller profiles (lightbar color, microphone LED mode, and player LEDs),
/// assign a profile to the controller selected in the title bar combobox (by its MAC
/// address with a device path fallback), and save the controller's current lights as a
/// new profile.
/// </summary>
/// <remarks>
/// <para>
/// Resolves <see cref="MainViewModel"/> and <see cref="ProfileService"/> from the DI container.
/// Profile edits persist immediately through <see cref="ProfileEditorItem"/>; assigning a
/// profile to the selected controller applies it right away and stores the binding so it is
/// re-applied automatically when the controller connects (see <see cref="MainViewModel"/>).
/// </para>
/// <para>
/// <see cref="Refresh"/> is called from the page's <c>OnLoaded</c> to resynchronize with the
/// current profile service state and controller selection on every navigation. Profiles are
/// loaded from disk once during startup (see <see cref="Controls.AppSplashScreen"/>).
/// </para>
/// </remarks>
public partial class ProfilePageViewModel : ObservableObject
{
    /// <summary>
    /// Base name used for automatically created profiles ("Profile", "Profile 2", ...).
    /// </summary>
    private const string DefaultProfileName = "Profile";

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ProfilePage");

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
    /// Service used for delete confirmations.
    /// </summary>
    private readonly IMessageBoxService _messageBox;

    /// <summary>
    /// Tracks the previous lights item so its subscriptions are released on replacement.
    /// </summary>
    private LightsItem? _previousItem;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public LightsItem? CurrentDevice { get; private set; }

    /// <summary>
    /// Whether a controller is selected and can be customized or assigned a profile.
    /// </summary>
    public bool HasDevice => CurrentDevice is not null;

    /// <summary>
    /// The Bluetooth MAC address of the selected controller, or empty when unavailable.
    /// </summary>
    public string CurrentMac => CurrentDevice?.Controller.PairingInfo?.ClientMac ?? string.Empty;

    /// <summary>
    /// The HID device path of the selected controller, used as a binding fallback when the
    /// MAC address is unavailable.
    /// </summary>
    public string CurrentDevicePath => CurrentDevice?.Controller.Device.Info.Path ?? string.Empty;

    /// <summary>
    /// The name of the profile the selected controller is currently using: the bound
    /// profile, or the default profile when unbound.
    /// </summary>
    private string CurrentUsedProfileName
        => _controllerService.GetBoundProfileName(CurrentMac, CurrentDevicePath) ?? ProfileService.DefaultProfileName;

    /// <summary>
    /// All saved profiles, shown in the profile list and editor.
    /// </summary>
    public ObservableCollection<ProfileEditorItem> Profiles { get; } = [];

    /// <summary>
    /// The profile currently being edited, or <c>null</c> when none exists.
    /// </summary>
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))] [NotifyCanExecuteChangedFor(nameof(DuplicateProfileCommand))]
    private ProfileEditorItem? _selectedProfile;

    /// <summary>
    /// Whether a profile is selected for editing.
    /// </summary>
    public bool HasSelectedProfile => SelectedProfile is not null;

    /// <summary>
    /// Options for the "assigned profile" dropdown: the name of every saved profile.
    /// No selection (<c>-1</c>) means the controller has no bound profile.
    /// </summary>
    public ObservableCollection<string> AssignedProfileOptions { get; } = [];

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
    /// Gets or sets the selected entry in <see cref="AssignedProfileOptions"/>. Setting it
    /// binds the current controller (by MAC, with a device path fallback) to the chosen
    /// profile, applies it immediately, and persists the binding.
    /// </summary>
    public int SelectedAssignedProfileIndex
    {
        get
        {
            if (!HasDevice)
            {
                return -1;
            }

            string? bound = _controllerService.GetBoundProfileName(CurrentMac, CurrentDevicePath);
            if (bound is null)
            {
                return -1;
            }

            for (int i = 0; i < AssignedProfileOptions.Count; i++)
            {
                if (string.Equals(AssignedProfileOptions[i], bound, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }
        set
        {
            if (value < 0 || value >= AssignedProfileOptions.Count || !HasDevice)
            {
                return;
            }

            string? profileName = AssignedProfileOptions[value];
            string? current = _controllerService.GetBoundProfileName(CurrentMac, CurrentDevicePath);
            if (string.Equals(profileName, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _log.Info($"Binding controller {CurrentMac} to profile '{profileName}'");
            _controllerService.SetControllerProfile(CurrentMac, CurrentDevicePath, profileName);
            ApplyBoundProfileToController();
        }
    }

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public ProfilePageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _profileService = App.Services.GetRequiredService<ProfileService>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _messageBox = App.Services.GetRequiredService<IMessageBoxService>();
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
    /// Creates a new, empty profile and selects it for editing.
    /// </summary>
    [RelayCommand]
    private void CreateProfile()
    {
        SelectedProfile = AddProfile(_profileService.CreateProfile(DefaultProfileName));
    }

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

    /// <summary>
    /// Re-applies the profile currently used by the selected controller (the bound profile,
    /// or the default when unbound) to push its current settings back to the controller.
    /// </summary>
    [RelayCommand]
    private void ReapplyProfile()
    {
        ApplyBoundProfileToController();
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
    /// Rebuilds <see cref="CurrentDevice"/> from the shell's selected controller and syncs
    /// the preview color to the profile the controller is using.
    /// Releases the previous item's subscriptions before replacing it.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new LightsItem(selected) : null;
        _previousItem = CurrentDevice;

        if (CurrentDevice is not null)
        {
            Profile? applied = GetCurrentControllerProfile();
            if (applied is not null)
            {
                CurrentDevice.SetPreview(applied.Lightbar.Red, applied.Lightbar.Green, applied.Lightbar.Blue);
            }
        }

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(CurrentMac));
        OnPropertyChanged(nameof(CurrentDevicePath));
        OnPropertyChanged(nameof(SelectedAssignedProfileIndex));
    }

    /// <summary>
    /// Rebuilds the <see cref="Profiles"/> collection and the assignment dropdown from the
    /// current profile service state. Disposes and unsubscribes previous editor items.
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

        BuildAssignedProfileOptions();
        // Prefer selecting a non-default profile so the seeded "Default" entry is only
        // selected when it is the only profile available.
        SelectedProfile = Profiles.FirstOrDefault(p => !string.Equals(p.Name, ProfileService.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                          ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedProfile));
    }

    /// <summary>
    /// Rebuilds <see cref="AssignedProfileOptions"/> from the current profiles, keeping the
    /// current binding selected.
    /// </summary>
    private void BuildAssignedProfileOptions()
    {
        AssignedProfileOptions.Clear();
        foreach (ProfileEditorItem item in Profiles)
        {
            AssignedProfileOptions.Add(item.Name);
        }
        OnPropertyChanged(nameof(SelectedAssignedProfileIndex));
    }

    /// <summary>
    /// Wraps a profile in a new <see cref="ProfileEditorItem"/>, subscribes to its events,
    /// and adds it to <see cref="Profiles"/> and the assignment dropdown.
    /// </summary>
    private ProfileEditorItem AddProfile(Profile profile)
    {
        ProfileEditorItem item = new ProfileEditorItem(profile, _profileService);
        item.ProfileRenamed += OnProfileRenamed;
        item.ProfileChanged += OnProfileChanged;
        Profiles.Add(item);
        BuildAssignedProfileOptions();
        return item;
    }

    /// <summary>
    /// Refreshes the assignment dropdown after a profile is renamed, and re-points the
    /// controller assignments referencing the old name.
    /// </summary>
    private void OnProfileRenamed(object? sender, EventArgs e)
    {
        if (e is ProfileRenamedEventArgs args)
        {
            _controllerService.UpdateProfileName(args.OldName, args.NewName);
        }
        BuildAssignedProfileOptions();
    }

    /// <summary>
    /// Re-applies a profile to the controller when it is the profile the controller is
    /// currently using (bound profile, or the default when unbound), so lightbar edits
    /// take effect immediately on both the controller and the preview card.
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
    /// default when unbound), so an assignment change takes effect immediately.
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
    /// Applies a profile to the selected controller and syncs the preview card color to it.
    /// </summary>
    private void ApplyProfileToController(Profile profile)
    {
        if (CurrentDevice?.Controller.Device is not DualSenseDevice device)
        {
            return;
        }

        _log.Info($"Applying profile '{profile.Name}' to {CurrentMac}");
        device.ApplyProfile(profile);
        CurrentDevice.SetPreview(profile.Lightbar.Red, profile.Lightbar.Green, profile.Lightbar.Blue);
    }
}