using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSenseClient.Core.DualSense;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.Settings.Models;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.DualSense.Events;

namespace DualSenseClient.ViewModels.Controls;

public partial class ControllerProfileViewModel : ControllerViewModelBase
{
    // Properties
    private readonly DualSenseProfileManager _profileManager;

    // Lightbar Control
    [ObservableProperty] private byte _lightbarRed;
    [ObservableProperty] private byte _lightbarGreen;
    [ObservableProperty] private byte _lightbarBlue;
    [ObservableProperty] private SolidColorBrush _lightbarPreviewColor = new SolidColorBrush(Colors.Black);
    [ObservableProperty] private string _lightbarColorHex = "#000000";

    // Player LED Control
    [ObservableProperty] private bool _playerLed1;
    [ObservableProperty] private bool _playerLed2;
    [ObservableProperty] private bool _playerLed3;
    [ObservableProperty] private bool _playerLed4;
    [ObservableProperty] private bool _playerLed5;
    [ObservableProperty] private int _playerLedBrightnessIndex;

    // Microphone LED Control
    [ObservableProperty] private MicLed _micLed;

    // Virtual Controller Settings
    [ObservableProperty] private bool _enableEmulation;
    [ObservableProperty] private int _emulationTypeIndex;
    [ObservableProperty] private bool _forceStopRumble;
    [ObservableProperty] private bool _ignoreDS4Lightbar;
    [ObservableProperty] private int _leftTriggerThreshold;
    [ObservableProperty] private int _rightTriggerThreshold;

    // OS Detection
    [ObservableProperty] private bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [ObservableProperty] private ObservableCollection<ControllerProfile> _profiles = new ObservableCollection<ControllerProfile>();
    [ObservableProperty] private ControllerProfile? _selectedProfile;
    [ObservableProperty] private string _newProfileName = string.Empty;

    // References to other ViewModels to coordinate profile updates
    private SpecialActionsViewModel? _specialActionsViewModel;
    private VirtualControllerSettingsViewModel? _virtualControllerSettingsViewModel;

    public SpecialActionsViewModel? SpecialActionsViewModel
    {
        get => _specialActionsViewModel;
        set => _specialActionsViewModel = value;
    }

    public VirtualControllerSettingsViewModel? VirtualControllerSettingsViewModel
    {
        get => _virtualControllerSettingsViewModel;
        set => _virtualControllerSettingsViewModel = value;
    }

    // Constructor
    public ControllerProfileViewModel(DualSenseController controller, ControllerInfo? controllerInfo, DualSenseProfileManager profileManager) : base(controller, controllerInfo)
    {
        Logger.Debug<ControllerProfileViewModel>($"Creating ControllerProfileViewModel for controller: {controllerInfo?.Name ?? "Unknown"}");
        _profileManager = profileManager;

        // Subscribe to profile changes to handle external profile changes (e.g., from tray icon)
        _profileManager.ProfileChanged += OnProfileChanged;

        InitializeLightControls();
        LoadProfiles();
        Logger.Debug<ControllerProfileViewModel>("ControllerProfileViewModel initialized successfully");
    }

    // Functions
    private void InitializeLightControls()
    {
        Logger.Debug<ControllerProfileViewModel>("Initializing light controls from controller state");

        // Lightbar
        LightbarRed = _controller.CurrentLightbarColor.Red;
        LightbarGreen = _controller.CurrentLightbarColor.Green;
        LightbarBlue = _controller.CurrentLightbarColor.Blue;
        Logger.Trace<ControllerProfileViewModel>($"Lightbar: RGB({LightbarRed}, {LightbarGreen}, {LightbarBlue})");

        // Player Leds
        PlayerLed1 = _controller.CurrentPlayerLeds.HasFlag(PlayerLed.LED_1);
        PlayerLed2 = _controller.CurrentPlayerLeds.HasFlag(PlayerLed.LED_2);
        PlayerLed3 = _controller.CurrentPlayerLeds.HasFlag(PlayerLed.LED_3);
        PlayerLed4 = _controller.CurrentPlayerLeds.HasFlag(PlayerLed.LED_4);
        PlayerLed5 = _controller.CurrentPlayerLeds.HasFlag(PlayerLed.LED_5);
        Logger.Trace<ControllerProfileViewModel>($"Player LEDs: {_controller.CurrentPlayerLeds}");

        PlayerLedBrightnessIndex = _controller.CurrentPlayerLedBrightness switch
        {
            PlayerLedBrightness.High => 0,
            PlayerLedBrightness.Medium => 1,
            PlayerLedBrightness.Low => 2,
            _ => 0
        };
        Logger.Trace<ControllerProfileViewModel>($"Player LED Brightness: {_controller.CurrentPlayerLedBrightness} (Index: {PlayerLedBrightnessIndex})");

        // Microphone Led
        MicLed = _controller.CurrentMicLed;
        Logger.Trace<ControllerProfileViewModel>($"Mic LED: {MicLed}");

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
        Logger.Trace<ControllerProfileViewModel>($"Virtual Controller: Enabled={EnableEmulation}, TypeIndex={EmulationTypeIndex}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");
    }

    private void LoadProfiles()
    {
        Logger.Debug<ControllerProfileViewModel>("Loading profiles into ViewModel");
        Profiles.Clear();

        foreach (ControllerProfile profile in _profileManager.GetAllProfiles().Values)
        {
            Profiles.Add(profile);
        }
        Logger.Debug<ControllerProfileViewModel>($"Loaded {Profiles.Count} profile(s)");

        if (_controllerInfo != null)
        {
            ControllerProfile assignedProfile = _profileManager.GetControllerProfile(_controllerInfo.Id);
            SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == assignedProfile.Id);
            Logger.Debug<ControllerProfileViewModel>($"Assigned profile for controller '{_controllerInfo.Name}': {assignedProfile.Name} (ID: {assignedProfile.Id})");
        }

        if (SelectedProfile == null)
        {
            SelectedProfile = Profiles.FirstOrDefault();
            Logger.Debug<ControllerProfileViewModel>($"No assigned profile found, selected first available: {SelectedProfile?.Name ?? "None"}");
        }
    }

    partial void OnLightbarRedChanged(byte value)
    {
        Logger.Trace<ControllerProfileViewModel>($"Lightbar Red changed: {value}");
        UpdateLightbarPreview();
    }

    partial void OnLightbarGreenChanged(byte value)
    {
        Logger.Trace<ControllerProfileViewModel>($"Lightbar Green changed: {value}");
        UpdateLightbarPreview();
    }

    partial void OnLightbarBlueChanged(byte value)
    {
        Logger.Trace<ControllerProfileViewModel>($"Lightbar Blue changed: {value}");
        UpdateLightbarPreview();
    }

    private void UpdateLightbarPreview()
    {
        LightbarPreviewColor = new SolidColorBrush(Color.FromRgb(LightbarRed, LightbarGreen, LightbarBlue));
        LightbarColorHex = $"#{LightbarRed:X2}{LightbarGreen:X2}{LightbarBlue:X2}";
        Logger.Trace<ControllerProfileViewModel>($"Lightbar preview updated: {LightbarColorHex}");
    }

    partial void OnSelectedProfileChanged(ControllerProfile? value)
    {
        if (value != null)
        {
            Logger.Debug<ControllerProfileViewModel>($"Selected profile changed to: {value.Name} (ID: {value.Id})");
            LoadProfileIntoControls(value);
        }
        else
        {
            Logger.Debug<ControllerProfileViewModel>("Selected profile cleared");
        }
    }

    private void OnProfileChanged(object? sender, ProfileChangedEventArgs e)
    {
        // Check if this event is for our controller
        if (e.ControllerId != _controllerInfo?.Id)
        {
            return;
        }

        // Skip if we're currently importing a profile to prevent infinite loops
        if (_isImportingProfile)
        {
            Logger.Debug<ControllerProfileViewModel>($"Skipping OnProfileChanged during import operation to prevent infinite loop");
            return;
        }

        Logger.Info<ControllerProfileViewModel>($"External profile change detected for this controller: {e.Profile.Name} (ID: {e.Profile.Id})");

        // Update the SelectedProfile property to match the changed profile
        // This will trigger the OnSelectedProfileChanged method to update the UI
        Dispatcher.UIThread.Post(() =>
        {
            ControllerProfile? matchingProfile = Profiles.FirstOrDefault(p => p.Id == e.Profile.Id);

            if (matchingProfile != null)
            {
                Logger.Debug<ControllerProfileViewModel>($"Updating SelectedProfile to match external change: {matchingProfile.Name}");
                SelectedProfile = matchingProfile;
            }
            else
            {
                Logger.Warning<ControllerProfileViewModel>($"Profile with ID {e.Profile.Id} not found in local Profiles collection, reloading profiles");
                // Reload profiles since this one might be new or updated
                LoadProfiles();
                // Try to find the profile again after reload
                matchingProfile = Profiles.FirstOrDefault(p => p.Id == e.Profile.Id);
                if (matchingProfile != null)
                {
                    SelectedProfile = matchingProfile;
                    Logger.Debug<ControllerProfileViewModel>($"Found profile after reload: {matchingProfile.Name}");
                }
                else
                {
                    Logger.Warning<ControllerProfileViewModel>($"Profile with ID {e.Profile.Id} still not found after reload");
                }
            }
        });
    }

    private void LoadProfileIntoControls(ControllerProfile profile)
    {
        Logger.Debug<ControllerProfileViewModel>($"Loading profile '{profile.Name}' into controls");

        // Load Lightbar
        LightbarRed = profile.Lightbar.Red;
        LightbarGreen = profile.Lightbar.Green;
        LightbarBlue = profile.Lightbar.Blue;
        UpdateLightbarPreview();
        Logger.Trace<ControllerProfileViewModel>($"  Lightbar: RGB({LightbarRed}, {LightbarGreen}, {LightbarBlue})");

        // Load Player LED
        PlayerLed1 = profile.PlayerLeds.Pattern.HasFlag(PlayerLed.LED_1);
        PlayerLed2 = profile.PlayerLeds.Pattern.HasFlag(PlayerLed.LED_2);
        PlayerLed3 = profile.PlayerLeds.Pattern.HasFlag(PlayerLed.LED_3);
        PlayerLed4 = profile.PlayerLeds.Pattern.HasFlag(PlayerLed.LED_4);
        PlayerLed5 = profile.PlayerLeds.Pattern.HasFlag(PlayerLed.LED_5);
        PlayerLedBrightnessIndex = profile.PlayerLeds.Brightness switch
        {
            PlayerLedBrightness.High => 0,
            PlayerLedBrightness.Medium => 1,
            PlayerLedBrightness.Low => 2,
            _ => 0
        };
        Logger.Trace<ControllerProfileViewModel>($"  Player LEDs: {profile.PlayerLeds.Pattern}, Brightness: {profile.PlayerLeds.Brightness}");

        // Microphone Led
        MicLed = profile.MicLed;
        Logger.Trace<ControllerProfileViewModel>($"  Mic LED: {MicLed}");

        // Load Virtual Controller Settings
        EnableEmulation = profile.VirtualControllerSettings.EnableEmulation;
        EmulationTypeIndex = profile.VirtualControllerSettings.EmulationType switch
        {
            VirtualControllerType.X360 => 0,
            VirtualControllerType.DS4 => 1,
            _ => 0 // Default to first option (X360)
        };
        ForceStopRumble = profile.VirtualControllerSettings.ForceStopRumble;
        IgnoreDS4Lightbar = profile.VirtualControllerSettings.IgnoreDS4Lightbar;
        LeftTriggerThreshold = profile.VirtualControllerSettings.LeftTriggerThreshold;
        RightTriggerThreshold = profile.VirtualControllerSettings.RightTriggerThreshold;
        Logger.Trace<ControllerProfileViewModel>($"  Virtual Controller: Enabled={EnableEmulation}, Type={profile.VirtualControllerSettings.EmulationType}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");

        Logger.Debug<ControllerProfileViewModel>("Profile loaded into controls successfully");
    }

    private PlayerLed GetCurrentPlayerLedPattern()
    {
        PlayerLed pattern = PlayerLed.None;
        if (PlayerLed1) pattern |= PlayerLed.LED_1;
        if (PlayerLed2) pattern |= PlayerLed.LED_2;
        if (PlayerLed3) pattern |= PlayerLed.LED_3;
        if (PlayerLed4) pattern |= PlayerLed.LED_4;
        if (PlayerLed5) pattern |= PlayerLed.LED_5;
        Logger.Trace<ControllerProfileViewModel>($"Current player LED pattern: {pattern}");
        return pattern;
    }

    private PlayerLedBrightness GetCurrentPlayerLedBrightness()
    {
        PlayerLedBrightness brightness = PlayerLedBrightnessIndex switch
        {
            0 => PlayerLedBrightness.High,
            1 => PlayerLedBrightness.Medium,
            2 => PlayerLedBrightness.Low,
            _ => PlayerLedBrightness.High
        };
        Logger.Trace<ControllerProfileViewModel>($"Current player LED brightness: {brightness}");
        return brightness;
    }

    [RelayCommand]
    private void ApplySelectedProfile()
    {
        if (SelectedProfile == null)
        {
            Logger.Warning<ControllerProfileViewModel>("ApplySelectedProfile called but no profile is selected");
            return;
        }

        Logger.Info<ControllerProfileViewModel>($"Applying profile '{SelectedProfile.Name}' to controller");
        _profileManager.ApplyProfileToController(_controller, SelectedProfile);

        if (_controllerInfo != null)
        {
            Logger.Debug<ControllerProfileViewModel>($"Assigning profile '{SelectedProfile.Name}' to controller '{_controllerInfo.Name}'");
            _profileManager.AssignProfileToController(_controllerInfo.Id, SelectedProfile.Id);

            // Trigger the profile changed event to notify other components about the change
            _profileManager.TriggerProfileChanged(_controllerInfo.Id, SelectedProfile);
        }

        InitializeLightControls();
        Logger.Info<ControllerProfileViewModel>("Profile applied successfully");
    }

    [RelayCommand]
    private void SaveCurrentAsProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            Logger.Warning<ControllerProfileViewModel>("SaveCurrentAsProfile called with empty profile name");
            return;
        }

        Logger.Info<ControllerProfileViewModel>($"Creating new profile '{NewProfileName}' from current controls");

        // Create profile from current control values
        ControllerProfile profile = new ControllerProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = NewProfileName,
            Lightbar = new LightbarSettings
            {
                Behavior = LightbarBehavior.Custom,
                Red = LightbarRed,
                Green = LightbarGreen,
                Blue = LightbarBlue
            },
            PlayerLeds = new PlayerLedSettings
            {
                Pattern = GetCurrentPlayerLedPattern(),
                Brightness = GetCurrentPlayerLedBrightness()
            },
            MicLed = MicLed,
            VirtualControllerSettings = new VirtualControllerSettings
            {
                EnableEmulation = EnableEmulation,
                EmulationType = EmulationTypeIndex switch
                {
                    0 => VirtualControllerType.X360,
                    1 => VirtualControllerType.DS4,
                    _ => VirtualControllerType.X360 // Default to X360 if invalid index
                },
                ForceStopRumble = ForceStopRumble,
                IgnoreDS4Lightbar = IgnoreDS4Lightbar,
                LeftTriggerThreshold = LeftTriggerThreshold,
                RightTriggerThreshold = RightTriggerThreshold
            }
        };

        Logger.Debug<ControllerProfileViewModel>(
            $"Profile details: ID={profile.Id}, Lightbar=RGB({profile.Lightbar.Red},{profile.Lightbar.Green},{profile.Lightbar.Blue}), PlayerLEDs={profile.PlayerLeds.Pattern}, MicLED={profile.MicLed}, VirtualControllerEnabled={profile.VirtualControllerSettings.EnableEmulation}");

        _profileManager.SaveProfile(profile);
        LoadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        NewProfileName = string.Empty;

        Logger.Info<ControllerProfileViewModel>($"Profile '{profile.Name}' created and saved successfully");
    }

    [RelayCommand]
    private void UpdateSelectedProfile()
    {
        if (SelectedProfile == null)
        {
            Logger.Warning<ControllerProfileViewModel>("UpdateSelectedProfile called but no profile is selected");
            return;
        }

        Logger.Info<ControllerProfileViewModel>($"Updating profile '{SelectedProfile.Name}' (ID: {SelectedProfile.Id})");

        // Check if we have references to other ViewModels to coordinate the update
        if (_specialActionsViewModel != null || _virtualControllerSettingsViewModel != null)
        {
            // Update with coordinated approach using all ViewModel settings
            Logger.Debug<ControllerProfileViewModel>("Using coordinated update approach with all ViewModels");

            // Update the selected profile with current control values
            SelectedProfile.Lightbar.Red = LightbarRed;
            SelectedProfile.Lightbar.Green = LightbarGreen;
            SelectedProfile.Lightbar.Blue = LightbarBlue;
            SelectedProfile.PlayerLeds.Pattern = GetCurrentPlayerLedPattern();
            SelectedProfile.PlayerLeds.Brightness = GetCurrentPlayerLedBrightness();
            SelectedProfile.MicLed = MicLed;

            // Update virtual controller settings from the dedicated ViewModel if available
            if (_virtualControllerSettingsViewModel != null)
            {
                SelectedProfile.VirtualControllerSettings.EnableEmulation = _virtualControllerSettingsViewModel.EnableEmulation;
                SelectedProfile.VirtualControllerSettings.EmulationType = _virtualControllerSettingsViewModel.EmulationTypeIndex switch
                {
                    0 => VirtualControllerType.X360,
                    1 => VirtualControllerType.DS4,
                    _ => VirtualControllerType.X360 // Default to X360 if invalid index
                };
                SelectedProfile.VirtualControllerSettings.ForceStopRumble = _virtualControllerSettingsViewModel.ForceStopRumble;
                SelectedProfile.VirtualControllerSettings.IgnoreDS4Lightbar = _virtualControllerSettingsViewModel.IgnoreDS4Lightbar;
                SelectedProfile.VirtualControllerSettings.LeftTriggerThreshold = _virtualControllerSettingsViewModel.LeftTriggerThreshold;
                SelectedProfile.VirtualControllerSettings.RightTriggerThreshold = _virtualControllerSettingsViewModel.RightTriggerThreshold;
            }
            else
            {
                // Fallback to local settings if VirtualControllerSettingsViewModel is not available
                SelectedProfile.VirtualControllerSettings.EnableEmulation = EnableEmulation;
                SelectedProfile.VirtualControllerSettings.EmulationType = EmulationTypeIndex switch
                {
                    0 => VirtualControllerType.X360,
                    1 => VirtualControllerType.DS4,
                    _ => VirtualControllerType.X360 // Default to X360 if invalid index
                };
                SelectedProfile.VirtualControllerSettings.ForceStopRumble = ForceStopRumble;
                SelectedProfile.VirtualControllerSettings.IgnoreDS4Lightbar = IgnoreDS4Lightbar;
                SelectedProfile.VirtualControllerSettings.LeftTriggerThreshold = LeftTriggerThreshold;
                SelectedProfile.VirtualControllerSettings.RightTriggerThreshold = RightTriggerThreshold;
            }

            // Update special actions from the dedicated ViewModel if available
            if (_specialActionsViewModel != null)
            {
                SelectedProfile.SpecialActions.Clear();
                foreach (SpecialActionSettings specialAction in _specialActionsViewModel.SpecialActions)
                {
                    SelectedProfile.SpecialActions.Add(specialAction);
                }
            }
        }
        else
        {
            // Update with just local settings (legacy behavior)
            Logger.Debug<ControllerProfileViewModel>("Using legacy update approach with only local settings");

            // Update the selected profile with current control values
            SelectedProfile.Lightbar.Red = LightbarRed;
            SelectedProfile.Lightbar.Green = LightbarGreen;
            SelectedProfile.Lightbar.Blue = LightbarBlue;
            SelectedProfile.PlayerLeds.Pattern = GetCurrentPlayerLedPattern();
            SelectedProfile.PlayerLeds.Brightness = GetCurrentPlayerLedBrightness();
            SelectedProfile.MicLed = MicLed;
            SelectedProfile.VirtualControllerSettings.EnableEmulation = EnableEmulation;
            SelectedProfile.VirtualControllerSettings.EmulationType = EmulationTypeIndex switch
            {
                0 => VirtualControllerType.X360,
                1 => VirtualControllerType.DS4,
                _ => VirtualControllerType.X360 // Default to X360 if invalid index
            };
            SelectedProfile.VirtualControllerSettings.ForceStopRumble = ForceStopRumble;
            SelectedProfile.VirtualControllerSettings.IgnoreDS4Lightbar = IgnoreDS4Lightbar;
            SelectedProfile.VirtualControllerSettings.LeftTriggerThreshold = LeftTriggerThreshold;
            SelectedProfile.VirtualControllerSettings.RightTriggerThreshold = RightTriggerThreshold;
        }

        Logger.Debug<ControllerProfileViewModel>(
            $"Updated values: Lightbar=RGB({LightbarRed},{LightbarGreen},{LightbarBlue}), PlayerLEDs={SelectedProfile.PlayerLeds.Pattern}, MicLED={MicLed}, VirtualControllerEnabled={SelectedProfile.VirtualControllerSettings.EnableEmulation}, SpecialActionsCount={SelectedProfile.SpecialActions.Count}");

        _profileManager.SaveProfile(SelectedProfile);
        Logger.Info<ControllerProfileViewModel>("Profile updated successfully");
    }

    [RelayCommand]
    private void RenameSelectedProfile()
    {
        if (SelectedProfile == null)
        {
            Logger.Warning<ControllerProfileViewModel>("RenameSelectedProfile called but no profile is selected");
            return;
        }

        Logger.Info<ControllerProfileViewModel>($"Renaming profile '{SelectedProfile.Name}' (ID: {SelectedProfile.Id}) to '{NewProfileName}'");

        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            Logger.Warning<ControllerProfileViewModel>("RenameSelectedProfile called with empty profile name");
            return;
        }

        string newName = NewProfileName.Trim();

        // Check if the new name is the same as the current name
        if (SelectedProfile.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warning<ControllerProfileViewModel>($"New name '{newName}' is the same as the current name '{SelectedProfile.Name}', skipping rename");
            return;
        }

        // Check if a profile with the new name already exists
        ControllerProfile? existingProfile = Profiles.FirstOrDefault(p => !p.Id.Equals(SelectedProfile.Id) &&
                                                                          p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

        if (existingProfile != null)
        {
            Logger.Warning<ControllerProfileViewModel>($"A profile with the name '{newName}' already exists");
            return;
        }

        // Store the old name for logging
        // Update the profile name and save
        string oldName = SelectedProfile.Name;
        SelectedProfile.Name = newName;
        _profileManager.SaveProfile(SelectedProfile);

        // Reload the profile list in the UI
        LoadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == SelectedProfile.Id);
        NewProfileName = string.Empty;
        Logger.Info<ControllerProfileViewModel>($"Profile renamed successfully from '{oldName}' to '{SelectedProfile?.Name}'");
    }


    [RelayCommand]
    private void DuplicateSelectedProfile()
    {
        if (SelectedProfile == null)
        {
            Logger.Warning<ControllerProfileViewModel>("DuplicateSelectedProfile called but no profile is selected");
            return;
        }

        Logger.Info<ControllerProfileViewModel>($"Duplicating profile '{SelectedProfile.Name}' (ID: {SelectedProfile.Id})");

        // Create a new profile based on current profile and ViewModel settings
        ControllerProfile newProfile = new ControllerProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{SelectedProfile.Name} (Copy)",
            Lightbar = new LightbarSettings
            {
                Behavior = SelectedProfile.Lightbar.Behavior,
                Red = SelectedProfile.Lightbar.Red,
                Green = SelectedProfile.Lightbar.Green,
                Blue = SelectedProfile.Lightbar.Blue
            },
            PlayerLeds = new PlayerLedSettings
            {
                Pattern = SelectedProfile.PlayerLeds.Pattern,
                Brightness = SelectedProfile.PlayerLeds.Brightness
            },
            MicLed = SelectedProfile.MicLed,
            VirtualControllerSettings = new VirtualControllerSettings
            {
                EnableEmulation = SelectedProfile.VirtualControllerSettings.EnableEmulation,
                EmulationType = SelectedProfile.VirtualControllerSettings.EmulationType,
                ForceStopRumble = SelectedProfile.VirtualControllerSettings.ForceStopRumble,
                IgnoreDS4Lightbar = SelectedProfile.VirtualControllerSettings.IgnoreDS4Lightbar,
                LeftTriggerThreshold = SelectedProfile.VirtualControllerSettings.LeftTriggerThreshold,
                RightTriggerThreshold = SelectedProfile.VirtualControllerSettings.RightTriggerThreshold
            }
        };

        // Check if we have references to other ViewModels to coordinate the duplication
        if (_specialActionsViewModel != null || _virtualControllerSettingsViewModel != null)
        {
            Logger.Debug<ControllerProfileViewModel>("Using coordinated duplication approach with all ViewModels");

            // Override virtual controller settings from the dedicated ViewModel if available
            if (_virtualControllerSettingsViewModel != null)
            {
                newProfile.VirtualControllerSettings.EnableEmulation = _virtualControllerSettingsViewModel.EnableEmulation;
                newProfile.VirtualControllerSettings.EmulationType = _virtualControllerSettingsViewModel.EmulationTypeIndex switch
                {
                    0 => VirtualControllerType.X360,
                    1 => VirtualControllerType.DS4,
                    _ => VirtualControllerType.X360 // Default to X360 if invalid index
                };
                newProfile.VirtualControllerSettings.ForceStopRumble = _virtualControllerSettingsViewModel.ForceStopRumble;
                newProfile.VirtualControllerSettings.IgnoreDS4Lightbar = _virtualControllerSettingsViewModel.IgnoreDS4Lightbar;
                newProfile.VirtualControllerSettings.LeftTriggerThreshold = _virtualControllerSettingsViewModel.LeftTriggerThreshold;
                newProfile.VirtualControllerSettings.RightTriggerThreshold = _virtualControllerSettingsViewModel.RightTriggerThreshold;
            }

            // Copy special actions from the dedicated ViewModel if available
            if (_specialActionsViewModel != null)
            {
                newProfile.SpecialActions.Clear();
                foreach (SpecialActionSettings specialAction in _specialActionsViewModel.SpecialActions)
                {
                    newProfile.SpecialActions.Add(specialAction);
                }
            }
            else
            {
                // Copy from source profile if SpecialActionsViewModel is unavailable
                foreach (SpecialActionSettings specialAction in SelectedProfile.SpecialActions)
                {
                    newProfile.SpecialActions.Add(specialAction);
                }
            }
        }
        else
        {
            // Copy from source profile (legacy behavior)
            Logger.Debug<ControllerProfileViewModel>("Using legacy duplication approach with only source profile settings");
            foreach (SpecialActionSettings specialAction in SelectedProfile.SpecialActions)
            {
                newProfile.SpecialActions.Add(specialAction);
            }
        }

        // Save the new profile
        _profileManager.SaveProfile(newProfile);
        Logger.Debug<ControllerProfileViewModel>($"New profile created with: Lightbar=RGB({newProfile.Lightbar.Red},{newProfile.Lightbar.Green},{newProfile.Lightbar.Blue}), SpecialActionsCount={newProfile.SpecialActions.Count}");

        // Update the UI to show the new profile
        LoadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == newProfile.Id);

        Logger.Info<ControllerProfileViewModel>("Profile duplicated successfully");
    }

    [RelayCommand]
    private void DeleteSelectedProfile()
    {
        if (SelectedProfile == null)
        {
            Logger.Warning<ControllerProfileViewModel>("DeleteSelectedProfile called but no profile is selected");
            return;
        }

        string profileId = SelectedProfile.Id;
        string profileName = SelectedProfile.Name;

        Logger.Info<ControllerProfileViewModel>($"Deleting profile '{profileName}' (ID: {profileId})");

        if (_profileManager.DeleteProfile(profileId))
        {
            Logger.Debug<ControllerProfileViewModel>("Profile deleted from manager, reloading profile list");
            LoadProfiles();
            SelectedProfile = Profiles.FirstOrDefault();
            Logger.Info<ControllerProfileViewModel>($"Profile '{profileName}' deleted successfully");
        }
        else
        {
            Logger.Warning<ControllerProfileViewModel>($"Failed to delete profile '{profileName}' (possibly default profile)");
        }
    }

    [RelayCommand]
    private void SaveControllerStateAsProfile()
    {
        string profileName = string.IsNullOrWhiteSpace(NewProfileName) ? $"Profile {DateTime.Now:yyyy-MM-dd HH:mm}" : NewProfileName;

        Logger.Info<ControllerProfileViewModel>($"Creating profile '{profileName}' from current controller state");

        ControllerProfile profile = _profileManager.CreateProfileFromController(_controller, profileName);

        Logger.Debug<ControllerProfileViewModel>($"Profile created with ID: {profile.Id}");

        LoadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        NewProfileName = string.Empty;

        Logger.Info<ControllerProfileViewModel>("Controller state saved as profile successfully");
    }

    [RelayCommand]
    private void ApplyAllSettings()
    {
        Logger.Info<ControllerProfileViewModel>("Applying all settings to controller");

        // Apply Lightbar
        _controller.SetLightbar(LightbarRed, LightbarGreen, LightbarBlue);
        Logger.Debug<ControllerProfileViewModel>($"Applied lightbar: RGB({LightbarRed}, {LightbarGreen}, {LightbarBlue})");

        // Apply Player LEDs
        PlayerLed leds = GetCurrentPlayerLedPattern();
        PlayerLedBrightness brightness = GetCurrentPlayerLedBrightness();
        _controller.SetPlayerLeds(leds, brightness);
        Logger.Debug<ControllerProfileViewModel>($"Applied player LEDs: Pattern={leds}, Brightness={brightness}");

        // Apply Mic LED
        _controller.SetMicLed(MicLed);
        Logger.Debug<ControllerProfileViewModel>($"Applied mic LED: {MicLed}");

        // Apply Virtual Controller Settings if available
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
                else if (EmulationTypeIndex == 1) // DS4
                {
                    _controller.ControllerEmulationService.StartDS4Emulation();
                }
            }
            else
            {
                _controller.ControllerEmulationService.StopEmulation();
            }

            Logger.Debug<ControllerProfileViewModel>($"Applied virtual controller settings: Enabled={EnableEmulation}, Type={EmulationTypeIndex}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");
        }
        else
        {
            Logger.Debug<ControllerProfileViewModel>("ControllerEmulationService not available, skipping virtual controller settings");
        }

        // Update controls to reflect current state
        InitializeLightControls();
        Logger.Info<ControllerProfileViewModel>("All settings applied successfully");
    }

    [RelayCommand]
    private void SetLightbarPreset(string colorString)
    {
        Logger.Debug<ControllerProfileViewModel>($"Setting lightbar preset: {colorString}");

        string[] parts = colorString.Split(',');
        if (parts.Length != 3 || !byte.TryParse(parts[0], out byte r) || !byte.TryParse(parts[1], out byte g) || !byte.TryParse(parts[2], out byte b))
        {
            Logger.Warning<ControllerProfileViewModel>($"Invalid color string format: {colorString}");
            return;
        }

        LightbarRed = r;
        LightbarGreen = g;
        LightbarBlue = b;

        Logger.Debug<ControllerProfileViewModel>($"Lightbar preset applied: RGB({r}, {g}, {b})");
    }

    [RelayCommand]
    private void ApplyLightbar()
    {
        Logger.Info<ControllerProfileViewModel>($"Applying lightbar color to controller: RGB({LightbarRed}, {LightbarGreen}, {LightbarBlue})");
        _controller.SetLightbar(LightbarRed, LightbarGreen, LightbarBlue);
        Logger.Debug<ControllerProfileViewModel>("Lightbar color applied successfully");
    }

    [RelayCommand]
    private void SetPlayerLeds(string preset)
    {
        Logger.Debug<ControllerProfileViewModel>($"Setting player LED preset: {preset}");

        switch (preset)
        {
            case "All":
                PlayerLed1 = PlayerLed2 = PlayerLed3 = PlayerLed4 = PlayerLed5 = true;
                Logger.Trace<ControllerProfileViewModel>("All player LEDs enabled");
                break;
            case "None":
                PlayerLed1 = PlayerLed2 = PlayerLed3 = PlayerLed4 = PlayerLed5 = false;
                Logger.Trace<ControllerProfileViewModel>("All player LEDs disabled");
                break;
            case "Center":
                PlayerLed1 = PlayerLed5 = false;
                PlayerLed2 = PlayerLed3 = PlayerLed4 = true;
                Logger.Trace<ControllerProfileViewModel>("Center player LEDs enabled");
                break;
            default:
                Logger.Warning<ControllerProfileViewModel>($"Unknown player LED preset: {preset}");
                break;
        }
    }

    [RelayCommand]
    private void ApplyPlayerLeds()
    {
        PlayerLed leds = PlayerLed.None;
        if (PlayerLed1) leds |= PlayerLed.LED_1;
        if (PlayerLed2) leds |= PlayerLed.LED_2;
        if (PlayerLed3) leds |= PlayerLed.LED_3;
        if (PlayerLed4) leds |= PlayerLed.LED_4;
        if (PlayerLed5) leds |= PlayerLed.LED_5;

        PlayerLedBrightness brightness = PlayerLedBrightnessIndex switch
        {
            0 => PlayerLedBrightness.High,
            1 => PlayerLedBrightness.Medium,
            2 => PlayerLedBrightness.Low,
            _ => PlayerLedBrightness.High
        };

        Logger.Info<ControllerProfileViewModel>($"Applying player LEDs to controller: Pattern={leds}, Brightness={brightness}");
        _controller.SetPlayerLeds(leds, brightness);
        InitializeLightControls();
        Logger.Debug<ControllerProfileViewModel>("Player LEDs applied successfully");
    }

    [RelayCommand]
    private void ApplyMicLed()
    {
        Logger.Info<ControllerProfileViewModel>($"Applying mic LED to controller: {MicLed}");
        _controller.SetMicLed(MicLed);
        InitializeLightControls();
        Logger.Debug<ControllerProfileViewModel>("Mic LED applied successfully");
    }

    [RelayCommand]
    private void ResetLightsToDefault()
    {
        Logger.Info<ControllerProfileViewModel>("Resetting all lights to default values");
        _controller.SetLightbar(0, 0, 255);
        _controller.SetPlayerLeds(PlayerLed.None, PlayerLedBrightness.Low);
        _controller.SetMicLed(MicLed.Off);
        InitializeLightControls();
        Logger.Debug<ControllerProfileViewModel>("Lights reset to default successfully");
    }

    [RelayCommand]
    private void TurnOffAllLights()
    {
        Logger.Info<ControllerProfileViewModel>("Turning off all lights");
        _controller.SetLightbar(0, 0, 0);
        _controller.SetPlayerLeds(PlayerLed.None, PlayerLedBrightness.High);
        _controller.SetMicLed(MicLed.Off);
        InitializeLightControls();
        Logger.Debug<ControllerProfileViewModel>("All lights turned off successfully");
    }

    [RelayCommand]
    private void ApplyVirtualControllerSettings()
    {
        Logger.Info<ControllerProfileViewModel>("Applying virtual controller settings to controller");

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
                else if (EmulationTypeIndex == 1) // DS4
                {
                    _controller.ControllerEmulationService.StartDS4Emulation();
                }
            }
            else
            {
                _controller.ControllerEmulationService.StopEmulation();
            }

            Logger.Debug<ControllerProfileViewModel>($"Virtual controller settings applied: Enabled={EnableEmulation}, Type={EmulationTypeIndex}, ForceStopRumble={ForceStopRumble}, IgnoreDS4Lightbar={IgnoreDS4Lightbar}");
        }
        else
        {
            Logger.Warning<ControllerProfileViewModel>("ControllerEmulationService not available, cannot apply virtual controller settings");
        }

        InitializeLightControls();
        Logger.Info<ControllerProfileViewModel>("Virtual controller settings applied successfully");
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (SelectedProfile == null)
        {
            Logger.Warning<ControllerProfileViewModel>("ExportProfile called but no profile is selected");
            return;
        }

        try
        {
            // This requires access to the main window
            Window? window = App.MainWindow;

            if (window != null)
            {
                IStorageFile? file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Profile",
                    SuggestedFileName = $"{SelectedProfile.Name.Replace(" ", "_")}.json",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("JSON files")
                        {
                            Patterns = ["*.json"],
                            MimeTypes = ["application/json"]
                        }
                    ]
                });

                if (file != null)
                {
                    // Serialize the profile to JSON
                    string json = System.Text.Json.JsonSerializer.Serialize(SelectedProfile, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    await using Stream stream = await file.OpenWriteAsync();
                    await using StreamWriter writer = new StreamWriter(stream);
                    await writer.WriteAsync(json);
                    await writer.FlushAsync();

                    Logger.Info<ControllerProfileViewModel>($"Profile '{SelectedProfile.Name}' exported to '{file.Path.AbsolutePath}'");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error<ControllerProfileViewModel>($"Failed to export profile: {ex.Message}");
        }
    }

    private bool _isImportingProfile = false;

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        // Prevent recursive execution
        if (_isImportingProfile)
        {
            Logger.Warning<ControllerProfileViewModel>("ImportProfileAsync called while already importing, skipping to prevent recursion");
            return;
        }

        try
        {
            _isImportingProfile = true;

            // This requires access to the main window
            Window? window = App.MainWindow;

            if (window != null)
            {
                IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Profile",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("JSON files")
                        {
                            Patterns = ["*.json"],
                            MimeTypes = ["application/json"]
                        }
                    ]
                });

                if (files.Any())
                {
                    IStorageFile file = files.First();
                    string json;

                    await using (Stream stream = await file.OpenReadAsync())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        json = await reader.ReadToEndAsync();
                    }

                    // Deserialize the profile
                    ControllerProfile? profile = System.Text.Json.JsonSerializer.Deserialize<ControllerProfile>(json, new System.Text.Json.JsonSerializerOptions());

                    if (profile != null)
                    {
                        // Check if a profile with the same name already exists
                        ControllerProfile? existingProfile = Profiles.FirstOrDefault(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));

                        if (existingProfile != null)
                        {
                            Logger.Warning<ControllerProfileViewModel>($"A profile with the name '{profile.Name}' already exists, importing with new ID");
                        }

                        // Generate a new ID for the imported profile to avoid conflicts
                        profile.Id = Guid.NewGuid().ToString();

                        // Save the imported profile
                        _profileManager.SaveProfile(profile);

                        // Reload profiles and select the new one
                        LoadProfiles();
                        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);

                        Logger.Info<ControllerProfileViewModel>($"Profile '{profile.Name}' imported successfully from '{file.Path.AbsolutePath}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error<ControllerProfileViewModel>($"Failed to import profile: {ex.Message}");
        }
        finally
        {
            _isImportingProfile = false;
        }
    }

    public override void Dispose()
    {
        Logger.Debug<ControllerProfileViewModel>($"Disposing ControllerProfileViewModel for controller: {_controllerInfo?.Name ?? "Unknown"}");

        // Unsubscribe from profile changes to prevent memory leaks
        if (_profileManager != null)
        {
            _profileManager.ProfileChanged -= OnProfileChanged;
        }

        base.Dispose();
    }
}