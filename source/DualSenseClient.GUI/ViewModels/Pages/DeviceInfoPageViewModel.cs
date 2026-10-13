using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.GUI.Models.Items;
using DualSenseClient.GUI.Services;
using DualSenseClient.HidHide;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.ViewModels.Pages;

/// <summary>
/// ViewModel for the device info page: the controller's illustration, its live status
/// (polling rate, connection, battery), identification (name, skin, MAC), controller
/// hiding, firmware information, and the assigned profile.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MainViewModel.SelectedItem"/> like every page ViewModel so the
/// page always shows the controller active in the shell. Firmware info is read once
/// when the controller connects; <see cref="RefreshCommand"/> re-reads it on demand.
/// </remarks>
public partial class DeviceInfoPageViewModel : ObservableObject
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DeviceInfoPage");

    /// <summary>
    /// The shell ViewModel owning the controller selection.
    /// </summary>
    private readonly MainViewModel _mainViewModel;

    /// <summary>
    /// Service storing persistent controller info, used to read and rename the custom
    /// display name and the illustration skin of the selected controller.
    /// </summary>
    private readonly ControllerInfoService _controllerService;

    /// <summary>
    /// Service providing the embedded controller illustration skins and their bitmaps.
    /// </summary>
    private readonly ControllerIllustrationService _illustrationService;

    /// <summary>
    /// Service used to read and persist profiles, backing the assigned-profile picker.
    /// </summary>
    private readonly ProfileService _profileService;

    /// <summary>
    /// Platform backend for hiding physical controllers from other applications
    /// (HidHide driver on Windows, other backends on other platforms).
    /// </summary>
    private readonly IControllerHidingService _hiding;

    /// <summary>
    /// The controller currently shown on this page, or <c>null</c> when none is selected.
    /// </summary>
    public DeviceInfoItem? CurrentDevice { get; private set; }

    /// <summary>
    /// Live controller state driving the illustration, or <c>null</c> when no controller
    /// is selected.
    /// </summary>
    public InputMonitorItem? MonitorState { get; private set; }

    /// <summary>
    /// Tracks the previous device item so its event subscriptions are released on replacement.
    /// </summary>
    private DeviceInfoItem? _previousItem;

    /// <summary>
    /// Tracks the previous monitor state so its event subscriptions are released on replacement.
    /// </summary>
    private InputMonitorItem? _previousMonitorState;

    /// <summary>
    /// Tracks the previous lights item so its subscriptions are released on replacement.
    /// </summary>
    private LightsItem? _previousLights;

    /// <summary>
    /// Live light state of the selected controller, previewing the color of the profile
    /// the controller is using.
    /// </summary>
    public LightsItem? Lights { get; private set; }

    /// <summary>
    /// The user-visible controller name (custom name, or product name when none was set).
    /// Editable: assigning a new name renames the controller in
    /// <see cref="ControllerInfoService"/> and persists it.
    /// </summary>
    public string ControllerName
    {
        get
        {
            return _controllerName;
        }
        set
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length > ControllerInfoService.MaxNameLength)
            {
                trimmed = trimmed[..ControllerInfoService.MaxNameLength];
            }

            if (string.Equals(trimmed, _controllerName, StringComparison.OrdinalIgnoreCase))
            {
                OnPropertyChanged();
                return;
            }

            _controllerName = trimmed;
            if (CurrentDevice is not null && !string.IsNullOrEmpty(trimmed))
            {
                bool renamed = _controllerService.RenameController(CurrentMac, CurrentDevicePath, trimmed);
                if (renamed)
                {
                    _log.Info($"Renamed controller '{CurrentMac}' to '{trimmed}'");
                }
                else
                {
                    _controllerName = _controllerService.GetDisplayName(CurrentMac, CurrentDevicePath, CurrentDevice.DisplayName);
                }
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Backing field for <see cref="ControllerName"/>.
    /// </summary>
    private string _controllerName = string.Empty;

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
    /// The HID device path of the selected controller.
    /// </summary>
    public string CurrentDevicePath
    {
        get
        {
            return CurrentDevice?.Controller.Device.Info.Path ?? string.Empty;
        }
    }

    /// <summary>
    /// Whether the battery indicator shows the percentage text (instead of the icon).
    /// Clicking the shown element toggles to the other.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowBatteryIcon))]
    private bool showBatteryPercentage;

    /// <summary>
    /// Whether the battery indicator shows the icon (instead of the percentage text).
    /// </summary>
    public bool ShowBatteryIcon
    {
        get
        {
            return !ShowBatteryPercentage;
        }
    }

    /// <summary>
    /// Switches the battery indicator between the percentage text and the icon.
    /// </summary>
    [RelayCommand]
    private void ToggleBatteryDisplay() => ShowBatteryPercentage = !ShowBatteryPercentage;

    /// <summary>
    /// Whether the value/coordinate tag labels are shown on the illustration.
    /// </summary>
    [ObservableProperty] private bool _showStats;

    // ── Controller illustration ────────────────────────────────

    /// <summary>
    /// The available controller illustration skins, in display order.
    /// </summary>
    public ObservableCollection<string> Skins { get; } = [];

    /// <summary>
    /// The illustration skin rendered by the controller visualization, or empty when no
    /// controller is selected.
    /// </summary>
    public string SkinName
    {
        get
        {
            return Skins.Count > 0 ? Skins[Math.Clamp(_skinIndex, 0, Skins.Count - 1)] : string.Empty;
        }
    }

    /// <summary>
    /// Backing field for <see cref="SkinIndex"/>.
    /// </summary>
    private int _skinIndex;

    /// <summary>
    /// The index of the selected controller's illustration skin in <see cref="Skins"/>.
    /// Setting it stores the skin per controller in <see cref="ControllerInfoService"/>
    /// and refreshes the illustration.
    /// </summary>
    public int SkinIndex
    {
        get
        {
            return _skinIndex;
        }
        set
        {
            if (value < 0 || value >= Skins.Count || value == _skinIndex)
            {
                return;
            }

            string skin = Skins[value];
            _log.Info($"Setting illustration skin of {CurrentMac} to '{skin}'");
            _skinIndex = value;
            _controllerService.SetSkin(CurrentMac, CurrentDevicePath, skin);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SkinName));
        }
    }

    /// <summary>
    /// Whether a controller is selected and its info can be displayed.
    /// </summary>
    public bool HasDevice
    {
        get
        {
            return CurrentDevice is not null;
        }
    }

    // ── Profile assignment ─────────────────────────────────────

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
    /// Options for the "assigned profile" dropdown: every saved profile with
    /// its lightbar color swatch.
    /// </summary>
    public ObservableCollection<AssignedProfileOption> AssignedProfileOptions { get; } = [];

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
                if (string.Equals(AssignedProfileOptions[i].Name, bound, StringComparison.OrdinalIgnoreCase))
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

            string? profileName = AssignedProfileOptions[value].Name;
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
    /// Re-applies the profile currently used by the selected controller (the bound profile,
    /// or the default when unbound) to push its current settings back to the controller.
    /// </summary>
    [RelayCommand]
    private void ReapplyProfile() => ApplyBoundProfileToController();

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
        Lights?.SetPreview(profile.Lightbar.Red, profile.Lightbar.Green, profile.Lightbar.Blue);
    }

    /// <summary>
    /// Rebuilds <see cref="AssignedProfileOptions"/> from the current profiles, keeping the
    /// current binding selected.
    /// </summary>
    private void BuildAssignedProfileOptions()
    {
        AssignedProfileOptions.Clear();
        foreach (Profile profile in _profileService.Settings.Profiles)
        {
            AssignedProfileOptions.Add(new AssignedProfileOption(
                profile.Name, profile.Lightbar.Red, profile.Lightbar.Green, profile.Lightbar.Blue));
        }

        OnPropertyChanged(nameof(SelectedAssignedProfileIndex));
    }

    // ── Controller hiding ───────────────────────────────────────

    /// <summary>
    /// Whether the hiding backend is installed and operational on this system.
    /// </summary>
    public bool HidingAvailable { get; private set; }

    /// <summary>
    /// Whether the selected controller is hidden from other applications. Setting it
    /// hides or unhides the controller, managing the backend's global hiding state.
    /// </summary>
    public bool IsControllerHidden
    {
        get
        {
            if (!HidingAvailable || !TryGetCurrentInstanceId(out string instanceId))
            {
                return false;
            }

            return _hiding.IsControllerHidden(instanceId);
        }
        set
        {
            if (!HidingAvailable || !TryGetCurrentInstanceId(out string instanceId))
            {
                return;
            }

            _hiding.SetControllerHidden(instanceId, value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the controller hiding row is offered: the platform supports it and a
    /// controller is selected.
    /// </summary>
    public bool IsHidingRowVisible
    {
        get
        {
            return HasDevice && IsHidingVisible;
        }
    }

    /// <summary>
    /// Whether controller hiding is offered at all, i.e. the platform supports it.
    /// </summary>
    public bool IsHidingVisible
    {
        get
        {
            return OperatingSystem.IsWindows();
        }
    }

    /// <summary>
    /// Row description for the hide toggle; replaced with an explanation when the
    /// hiding backend is missing on a supported platform.
    /// </summary>
    public string HidingDescription
    {
        get
        {
            if (!HidingAvailable)
            {
                return LocalizationService.GetText("DeviceInfoPage.Hiding.Description.NotInstalled");
            }

            return LocalizationService.GetText("DeviceInfoPage.Hiding.HideController.Description");
        }
    }

    /// <summary>
    /// Whether the selected controller can be hidden, i.e. the driver is available and
    /// its HID path can be resolved to a device instance ID.
    /// </summary>
    public bool CanHideController
    {
        get
        {
            return HidingAvailable && TryGetCurrentInstanceId(out _);
        }
    }

    /// <summary>
    /// Tries to resolve the selected controller's HID device path to the device
    /// instance ID used by HidHide.
    /// </summary>
    private bool TryGetCurrentInstanceId(out string instanceId)
    {
        instanceId = string.Empty;
        if (CurrentDevice is null || !_hiding.TryGetInstanceId(CurrentDevicePath, out string id))
        {
            _log.Debug($"Could not resolve instance ID from device path '{CurrentDevicePath}'");
            return false;
        }

        instanceId = id;
        return true;
    }

    /// <summary>
    /// Creates the page ViewModel and subscribes to the shell's controller selection.
    /// </summary>
    public DeviceInfoPageViewModel()
    {
        _mainViewModel = App.Services.GetRequiredService<MainViewModel>();
        _controllerService = App.Services.GetRequiredService<ControllerInfoService>();
        _illustrationService = App.Services.GetRequiredService<ControllerIllustrationService>();
        _hiding = App.Services.GetRequiredService<IControllerHidingService>();
        _profileService = App.Services.GetRequiredService<ProfileService>();
        foreach (string skin in _illustrationService.GetSkins())
        {
            Skins.Add(skin);
        }

        // Rebuild the assignment picker when profiles change (create/duplicate/delete/rename
        // on the customization page) so a new profile appears without re-selecting.
        _profileService.ProfilesChanged += OnProfilesChanged;
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        UpdateDevice();
    }

    /// <summary>
    /// Rebuilds the assigned-profile options when the profile list changes.
    /// </summary>
    private void OnProfilesChanged(object? sender, EventArgs e) => BuildAssignedProfileOptions();

    /// <summary>
    /// Re-reads the selected controller's firmware info report and refreshes the display.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        ControllerItem? selected = _mainViewModel.SelectedItem;
        if (selected is null)
        {
            return;
        }

        selected.FirmwareInfo = FeatureReader.ReadFirmwareInfo(selected.Device);
        if (selected.FirmwareInfo is null)
        {
            _log.Warning("Firmware info refresh returned no data");
        }

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
    /// Rebuilds the display items from the shell's selected controller. Releases the
    /// previous items' event subscriptions before replacing them.
    /// </summary>
    private void UpdateDevice()
    {
        _previousItem?.Dispose();
        _previousMonitorState?.Dispose();
        _previousLights?.Dispose();

        ControllerItem? selected = _mainViewModel.SelectedItem;
        CurrentDevice = selected is not null ? new DeviceInfoItem(selected) : null;
        _previousItem = CurrentDevice;
        MonitorState = selected is not null ? new InputMonitorItem(selected) : null;
        _previousMonitorState = MonitorState;
        Lights = selected is not null ? new LightsItem(selected) : null;
        _previousLights = Lights;

        if (Lights is not null)
        {
            Profile? applied = GetCurrentControllerProfile();
            if (applied is not null)
            {
                Lights.SetPreview(applied.Lightbar.Red, applied.Lightbar.Green, applied.Lightbar.Blue);
            }
        }

        _controllerName = selected is null
            ? string.Empty
            : _controllerService.GetDisplayName(CurrentMac, CurrentDevicePath, selected.DisplayName);

        string storedSkin = selected is null ? string.Empty : _controllerService.GetSkin(CurrentMac, CurrentDevicePath) ?? string.Empty;
        int storedIndex = Skins.IndexOf(storedSkin);
        _skinIndex = storedIndex >= 0 ? storedIndex : 0;

        OnPropertyChanged(nameof(CurrentDevice));
        OnPropertyChanged(nameof(MonitorState));
        OnPropertyChanged(nameof(Lights));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(ControllerName));
        OnPropertyChanged(nameof(SkinIndex));
        OnPropertyChanged(nameof(SkinName));

        BuildAssignedProfileOptions();

        HidingAvailable = _hiding.IsAvailable;
        OnPropertyChanged(nameof(HidingAvailable));
        OnPropertyChanged(nameof(IsControllerHidden));
        OnPropertyChanged(nameof(CanHideController));
        OnPropertyChanged(nameof(HidingDescription));
        OnPropertyChanged(nameof(IsHidingVisible));
        OnPropertyChanged(nameof(IsHidingRowVisible));
    }
}