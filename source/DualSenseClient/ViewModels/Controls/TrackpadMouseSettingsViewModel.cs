using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.ViewModels.Controls;

public partial class TrackpadMouseSettingsViewModel : ControllerViewModelBase
{
    [ObservableProperty] private bool _useTrackpadAsMouse;
    [ObservableProperty] private double _trackpadSensitivity = 1.0;
    [ObservableProperty] private bool _trackpadInvertX;
    [ObservableProperty] private bool _trackpadInvertY;

    private DualSenseProfileManager? _profileManager;

    public TrackpadMouseSettingsViewModel(DualSenseController controller, ControllerInfo? controllerInfo) : base(controller, controllerInfo)
    {
        Logger.Debug<TrackpadMouseSettingsViewModel>($"Creating TrackpadMouseSettingsViewModel for controller: {controllerInfo?.Name ?? "Unknown"}");
        InitializeTrackpadMouseSettings();
        Logger.Debug<TrackpadMouseSettingsViewModel>("TrackpadMouseSettingsViewModel initialized successfully");
    }

    public TrackpadMouseSettingsViewModel(DualSenseController controller, ControllerInfo? controllerInfo, DualSenseProfileManager profileManager) : base(controller, controllerInfo)
    {
        Logger.Debug<TrackpadMouseSettingsViewModel>($"Creating TrackpadMouseSettingsViewModel for controller: {controllerInfo?.Name ?? "Unknown"} with profile manager");
        _profileManager = profileManager;
        InitializeTrackpadMouseSettings();
        Logger.Debug<TrackpadMouseSettingsViewModel>("TrackpadMouseSettingsViewModel with profile manager initialized successfully");
    }

    private void InitializeTrackpadMouseSettings()
    {
        Logger.Debug<TrackpadMouseSettingsViewModel>("Initializing trackpad mouse settings from controller profile");

        // Initialize trackpad mouse settings from profile
        if (_controllerInfo?.ProfileId != null && _profileManager != null)
        {
            ControllerProfile? profile = _profileManager.GetControllerProfile(_controllerInfo.ProfileId);
            if (profile != null)
            {
                UseTrackpadAsMouse = profile.VirtualControllerSettings.TrackpadMouse.Enabled;
                TrackpadSensitivity = profile.VirtualControllerSettings.TrackpadMouse.Sensitivity;
                TrackpadInvertX = profile.VirtualControllerSettings.TrackpadMouse.InvertX;
                TrackpadInvertY = profile.VirtualControllerSettings.TrackpadMouse.InvertY;
            }
            else
            {
                // Default values if no profile exists
                UseTrackpadAsMouse = false;
                TrackpadSensitivity = 1.0;
                TrackpadInvertX = false;
                TrackpadInvertY = false;
            }
        }
        else
        {
            // Default values when no profile manager or profile ID is available
            UseTrackpadAsMouse = false;
            TrackpadSensitivity = 1.0;
            TrackpadInvertX = false;
            TrackpadInvertY = false;
        }

        Logger.Trace<TrackpadMouseSettingsViewModel>($"Trackpad Mouse loaded: UseTrackpadAsMouse={UseTrackpadAsMouse}, Sensitivity={TrackpadSensitivity}, InvertX={TrackpadInvertX}, InvertY={TrackpadInvertY}");
    }

    // Property changed handlers that save immediately
    partial void OnUseTrackpadAsMouseChanged(bool value)
    {
        ApplyTrackpadMouseSettings();
    }

    partial void OnTrackpadSensitivityChanged(double value)
    {
        ApplyTrackpadMouseSettings();
    }

    partial void OnTrackpadInvertXChanged(bool value)
    {
        ApplyTrackpadMouseSettings();
    }

    partial void OnTrackpadInvertYChanged(bool value)
    {
        ApplyTrackpadMouseSettings();
    }

    private void ApplyTrackpadMouseSettings()
    {
        Logger.Info<TrackpadMouseSettingsViewModel>("Applying trackpad mouse settings to controller and profile");

        // Apply settings to the controller's trackpad mouse service
        _controller.SetTrackpadMouseEnabled(UseTrackpadAsMouse, new VirtualControllerSettings
        {
            EnableEmulation = _controller.ControllerEmulationService?.IsEmulating ?? false,
            EmulationType = _controller.ControllerEmulationService?.IsEmulating360 == true ? VirtualControllerType.X360 : VirtualControllerType.DS4,
            ForceStopRumble = _controller.ControllerEmulationService?.ForceStopRumble ?? false,
            IgnoreDS4Lightbar = _controller.ControllerEmulationService?.IgnoreDS4Lightbar ?? false,
            LeftTriggerThreshold = _controller.ControllerEmulationService?.LeftTriggerThreshold ?? 0,
            RightTriggerThreshold = _controller.ControllerEmulationService?.RightTriggerThreshold ?? 0,
            TrackpadMouse = new TrackpadMouseSettings
            {
                Enabled = UseTrackpadAsMouse,
                Sensitivity = TrackpadSensitivity,
                InvertX = TrackpadInvertX,
                InvertY = TrackpadInvertY
            }
        });

        Logger.Debug<TrackpadMouseSettingsViewModel>($"Trackpad mouse settings applied: UseTrackpadAsMouse={UseTrackpadAsMouse}, Sensitivity={TrackpadSensitivity}, InvertX={TrackpadInvertX}, InvertY={TrackpadInvertY}");

        // If profile manager is available, update the current profile
        if (_profileManager != null && _controllerInfo != null)
        {
            ControllerProfile? currentProfile = _profileManager.GetControllerProfile(_controllerInfo.Id);
            if (currentProfile != null)
            {
                // Update profile's trackpad mouse settings
                currentProfile.VirtualControllerSettings.TrackpadMouse.Enabled = UseTrackpadAsMouse;
                currentProfile.VirtualControllerSettings.TrackpadMouse.Sensitivity = TrackpadSensitivity;
                currentProfile.VirtualControllerSettings.TrackpadMouse.InvertX = TrackpadInvertX;
                currentProfile.VirtualControllerSettings.TrackpadMouse.InvertY = TrackpadInvertY;

                Logger.Debug<TrackpadMouseSettingsViewModel>($"Updated profile trackpad mouse settings: {currentProfile.Name}");
                _profileManager.SaveProfile(currentProfile);
            }
            else
            {
                Logger.Warning<TrackpadMouseSettingsViewModel>("No current profile found to update");
            }
        }
    }

    // Method to load trackpad mouse settings from a profile
    public void LoadFromProfile(ControllerProfile profile)
    {
        Logger.Debug<TrackpadMouseSettingsViewModel>($"Loading trackpad mouse settings from profile: {profile.Name}");

        UseTrackpadAsMouse = profile.VirtualControllerSettings.TrackpadMouse.Enabled;
        TrackpadSensitivity = profile.VirtualControllerSettings.TrackpadMouse.Sensitivity;
        TrackpadInvertX = profile.VirtualControllerSettings.TrackpadMouse.InvertX;
        TrackpadInvertY = profile.VirtualControllerSettings.TrackpadMouse.InvertY;

        Logger.Trace<TrackpadMouseSettingsViewModel>($"Trackpad Mouse loaded: UseTrackpadAsMouse={UseTrackpadAsMouse}, Sensitivity={TrackpadSensitivity}, InvertX={TrackpadInvertX}, InvertY={TrackpadInvertY}");
    }
}