using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.ViewModels.Controls;

public partial class VirtualControllerSettingsViewModel : ControllerViewModelBase
{
    // Virtual Controller Settings
    [ObservableProperty] private bool _enableEmulation;
    [ObservableProperty] private int _emulationTypeIndex;
    [ObservableProperty] private bool _forceStopRumble;
    [ObservableProperty] private bool _ignoreDS4Lightbar;
    [ObservableProperty] private int _leftTriggerThreshold;
    [ObservableProperty] private int _rightTriggerThreshold;
    
    // OS Detection
    [ObservableProperty] private bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Profile Management
    private DualSenseProfileManager? _profileManager;

    // Trackpad Mouse Settings ViewModel
    [ObservableProperty] private TrackpadMouseSettingsViewModel? _trackpadMouseSettingsViewModel;

    public VirtualControllerSettingsViewModel(DualSenseController controller, ControllerInfo? controllerInfo) : base(controller, controllerInfo)
    {
        Logger.Debug<VirtualControllerSettingsViewModel>($"Creating VirtualControllerSettingsViewModel for controller: {controllerInfo?.Name ?? "Unknown"}");
        InitializeVirtualControllerSettings();
        InitializeTrackpadMouseSettingsViewModel();
        Logger.Debug<VirtualControllerSettingsViewModel>("VirtualControllerSettingsViewModel initialized successfully");
    }

    public VirtualControllerSettingsViewModel(DualSenseController controller, ControllerInfo? controllerInfo, DualSenseProfileManager profileManager) : base(controller, controllerInfo)
    {
        Logger.Debug<VirtualControllerSettingsViewModel>($"Creating VirtualControllerSettingsViewModel for controller: {controllerInfo?.Name ?? "Unknown"} with profile manager");
        _profileManager = profileManager;
        InitializeVirtualControllerSettings();
        InitializeTrackpadMouseSettingsViewModel();
        Logger.Debug<VirtualControllerSettingsViewModel>("VirtualControllerSettingsViewModel with profile manager initialized successfully");
    }

    private void InitializeVirtualControllerSettings()
    {
        Logger.Debug<VirtualControllerSettingsViewModel>("Initializing virtual controller settings from controller state");

        // Virtual Controller Settings (Initialize from controller's emulation service if available)
        if (_controller.ControllerEmulationService != null)
        {
            EnableEmulation = _controller.ControllerEmulationService.IsEmulating;
            EmulationTypeIndex = _controller.ControllerEmulationService.IsEmulating360 ? 0 : (_controller.ControllerEmulationService.IsViGEMBusInstalled ? 1 : 0); // 0 = X360, 1 = DS4
            ForceStopRumble = _controller.ControllerEmulationService.ForceStopRumble;
            IgnoreDS4Lightbar = _controller.ControllerEmulationService.IgnoreDS4Lightbar;
            LeftTriggerThreshold = _controller.ControllerEmulationService.LeftTriggerThreshold;
            RightTriggerThreshold = _controller.ControllerEmulationService.RightTriggerThreshold;
        }
        else
        {
            EnableEmulation = false;
            EmulationTypeIndex = 0;
            ForceStopRumble = false;
            IgnoreDS4Lightbar = false;
            LeftTriggerThreshold = 0;
            RightTriggerThreshold = 0;
        }

        Logger.Trace<VirtualControllerSettingsViewModel>($"Virtual Controller: Enabled={EnableEmulation}, TypeIndex={EmulationTypeIndex}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");
    }

    private void InitializeTrackpadMouseSettingsViewModel()
    {
        if (_profileManager != null)
        {
            TrackpadMouseSettingsViewModel = new TrackpadMouseSettingsViewModel(_controller, _controllerInfo, _profileManager);
        }
        else
        {
            TrackpadMouseSettingsViewModel = new TrackpadMouseSettingsViewModel(_controller, _controllerInfo);
        }
    }

    [RelayCommand]
    private void ApplyVirtualControllerSettings()
    {
        Logger.Info<VirtualControllerSettingsViewModel>("Applying virtual controller settings to controller");

        if (_controller.ControllerEmulationService != null)
        {
            _controller.ControllerEmulationService.ForceStopRumble = ForceStopRumble;
            _controller.ControllerEmulationService.IgnoreDS4Lightbar = IgnoreDS4Lightbar;
            _controller.ControllerEmulationService.LeftTriggerThreshold = LeftTriggerThreshold;
            _controller.ControllerEmulationService.RightTriggerThreshold = RightTriggerThreshold;

            // Start or stop emulation based on settings
            if (EnableEmulation)
            {
                if (EmulationTypeIndex == 0) // X360
                {
                    _controller.ControllerEmulationService.StartX360Emulation();
                }
                else // DS4
                {
                    _controller.ControllerEmulationService.StartDS4Emulation();
                }
            }
            else
            {
                _controller.ControllerEmulationService.StopEmulation();
            }

        Logger.Debug<VirtualControllerSettingsViewModel>($"Virtual controller settings applied: Enabled={EnableEmulation}, Type={EmulationTypeIndex}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");
        }
        else
        {
            Logger.Warning<VirtualControllerSettingsViewModel>("ControllerEmulationService not available, cannot apply virtual controller settings");
        }

        // If profile manager is available, update the current profile
        if (_profileManager != null && _controllerInfo != null)
        {
            ControllerProfile? currentProfile = _profileManager.GetControllerProfile(_controllerInfo.Id);
            if (currentProfile != null)
            {
                // Update profile's virtual controller settings
                currentProfile.VirtualControllerSettings.EnableEmulation = EnableEmulation;
                currentProfile.VirtualControllerSettings.EmulationType = EmulationTypeIndex switch
                {
                    0 => VirtualControllerType.X360,
                    1 => VirtualControllerType.DS4,
                    _ => VirtualControllerType.X360  // Default to X360
                };
                currentProfile.VirtualControllerSettings.ForceStopRumble = ForceStopRumble;
                currentProfile.VirtualControllerSettings.IgnoreDS4Lightbar = IgnoreDS4Lightbar;
                currentProfile.VirtualControllerSettings.LeftTriggerThreshold = LeftTriggerThreshold;
                currentProfile.VirtualControllerSettings.RightTriggerThreshold = RightTriggerThreshold;
                Logger.Debug<VirtualControllerSettingsViewModel>($"Updated profile virtual controller settings: {currentProfile.Name}");
                _profileManager.SaveProfile(currentProfile);
            }
            else
            {
                Logger.Warning<VirtualControllerSettingsViewModel>("No current profile found to update");
            }
        }
    }

    // Method to load virtual controller settings from a profile
    public void LoadFromProfile(ControllerProfile profile)
    {
        Logger.Debug<VirtualControllerSettingsViewModel>($"Loading virtual controller settings from profile: {profile.Name}");

        EnableEmulation = profile.VirtualControllerSettings.EnableEmulation;
        EmulationTypeIndex = profile.VirtualControllerSettings.EmulationType switch
        {
            VirtualControllerType.X360 => 0,
            VirtualControllerType.DS4 => 1,
            _ => 0  // Default to first option (X360)
        };
        ForceStopRumble = profile.VirtualControllerSettings.ForceStopRumble;
        IgnoreDS4Lightbar = profile.VirtualControllerSettings.IgnoreDS4Lightbar;
        LeftTriggerThreshold = profile.VirtualControllerSettings.LeftTriggerThreshold;
        RightTriggerThreshold = profile.VirtualControllerSettings.RightTriggerThreshold;

        Logger.Trace<VirtualControllerSettingsViewModel>($"Virtual Controller loaded: Enabled={EnableEmulation}, TypeIndex={EmulationTypeIndex}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");
    }
    
    public override void Dispose()
    {
        Logger.Debug<VirtualControllerSettingsViewModel>($"Disposing VirtualControllerSettingsViewModel for controller: {_controllerInfo?.Name ?? "Unknown"}");
        base.Dispose();
    }
}