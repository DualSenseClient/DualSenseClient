using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.DualSense.Events;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings;
using DualSenseClient.Core.Settings.Models;
using HidSharp;

namespace DualSenseClient.Core.DualSense;

public class DualSenseProfileManager
{
    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    // Constructor
    public DualSenseProfileManager()
    {
        Logger.Info<DualSenseProfileManager>("Initializing DualSenseProfileManager");

        // Subscribe to controller events through the service locator to avoid circular dependency
        DualSenseManager? dualSenseManager = DualSenseServiceLocator.GetDualSenseManager();
        if (dualSenseManager != null)
        {
            Logger.Debug<DualSenseProfileManager>("Subscribing to controller events");
            dualSenseManager.ControllerConnected += OnControllerConnected;
            dualSenseManager.ControllerDisconnected += OnControllerDisconnected;

            // Apply profiles to already connected controllers
            InitializeExistingControllers();
        }
        else
        {
            Logger.Warning<DualSenseProfileManager>("DualSenseManager not available during initialization, controller events will be subscribed later");
        }

        Logger.Info<DualSenseProfileManager>("DualSenseProfileManager initialized successfully");
    }

    // Functions
    /// <summary>
    /// Completes the initialization of DualSenseProfileManager after all services are registered
    /// This is needed because DualSenseManager may not be available during construction due to DI ordering
    /// </summary>
    public void CompleteInitialization()
    {
        DualSenseManager? dualSenseManager = DualSenseServiceLocator.GetDualSenseManager();
        if (dualSenseManager != null)
        {
            Logger.Debug<DualSenseProfileManager>("Completing initialization, subscribing to controller events");
            dualSenseManager.ControllerConnected += OnControllerConnected;
            dualSenseManager.ControllerDisconnected += OnControllerDisconnected;

            // Apply profiles to already connected controllers
            InitializeExistingControllers();
        }
        else
        {
            Logger.Error<DualSenseProfileManager>("DualSenseManager still not available during CompleteInitialization");
        }
    }

    private void OnControllerConnected(object? sender, DualSenseController controller)
    {
        Logger.Info<DualSenseProfileManager>($"Controller connected event: {controller.Device.GetProductName()}");
        Logger.Debug<DualSenseProfileManager>($"  Connection type: {controller.ConnectionType}");
        Logger.Debug<DualSenseProfileManager>($"  MAC Address: {controller.MacAddress ?? "Unknown"}");

        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot handle controller connection");
            return;
        }

        try
        {
            // Get or create controller info
            ControllerInfo controllerInfo = GetOrCreateControllerInfo(controller);

            // Update last seen
            controllerInfo.LastSeen = DateTime.UtcNow;
            controllerInfo.LastConnectionType = controller.ConnectionType;
            Logger.Debug<DualSenseProfileManager>($"  Updated last seen: {controllerInfo.LastSeen:O}");

            // Apply profile
            ApplyProfile(controller, controllerInfo);

            // Save settings
            Logger.Debug<DualSenseProfileManager>("Saving settings after controller connection");
            settingsManager.SaveAll();

            Logger.Info<DualSenseProfileManager>($"Controller '{controllerInfo.Name}' connected and configured successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<DualSenseProfileManager>($"Failed to handle controller connection");
            Logger.LogExceptionDetails<DualSenseProfileManager>(ex, includeEnvironmentInfo: false);
        }
    }

    private void OnControllerDisconnected(object? sender, string devicePath)
    {
        Logger.Info<DualSenseProfileManager>($"Controller disconnected: {devicePath}");
        Logger.Debug<DualSenseProfileManager>("Settings are already saved, no action needed");
    }

    /// <summary>
    /// Applies profiles to controllers that are already connected at startup
    /// </summary>
    private void InitializeExistingControllers()
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot initialize existing controllers");
            return;
        }

        Logger.Info<DualSenseProfileManager>("Checking for already connected controllers at startup");

        DualSenseManager? dualSenseManager = DualSenseServiceLocator.GetDualSenseManager();
        if (dualSenseManager == null)
        {
            Logger.Warning<DualSenseProfileManager>("DualSenseManager not available, cannot initialize existing controllers");
            return;
        }

        List<DualSenseController> connectedControllers = dualSenseManager.Controllers.Values.ToList();

        if (connectedControllers.Count == 0)
        {
            Logger.Debug<DualSenseProfileManager>("No controllers currently connected");
            return;
        }

        Logger.Info<DualSenseProfileManager>($"Found {connectedControllers.Count} connected controller(s), applying profiles");

        bool settingsChanged = false;
        int successCount = 0;
        int failureCount = 0;

        foreach (DualSenseController controller in connectedControllers)
        {
            try
            {
                string productName = controller.Device.GetProductName();
                Logger.Debug<DualSenseProfileManager>($"Processing existing controller: {productName}");
                Logger.Trace<DualSenseProfileManager>($"  Device path: {controller.Device.DevicePath}");
                Logger.Trace<DualSenseProfileManager>($"  Connection type: {controller.ConnectionType}");

                // Get or create controller info
                ControllerInfo controllerInfo = GetOrCreateControllerInfo(controller);

                // Update last seen
                controllerInfo.LastSeen = DateTime.UtcNow;
                controllerInfo.LastConnectionType = controller.ConnectionType;

                // Apply profile
                ApplyProfile(controller, controllerInfo);

                settingsChanged = true;
                successCount++;
                Logger.Debug<DualSenseProfileManager>($"Successfully initialized controller '{controllerInfo.Name}'");
            }
            catch (Exception ex)
            {
                failureCount++;
                Logger.Error<DualSenseProfileManager>($"Failed to initialize existing controller");
                Logger.LogExceptionDetails<DualSenseProfileManager>(ex, includeEnvironmentInfo: false);
            }
        }

        // Save settings if any controller was processed
        if (settingsChanged)
        {
            Logger.Debug<DualSenseProfileManager>("Saving settings after initializing existing controllers");
            settingsManager.SaveAll();
        }

        Logger.Info<DualSenseProfileManager>($"Existing controllers initialization complete: {successCount} succeeded, {failureCount} failed");
    }

    public ControllerInfo GetOrCreateControllerInfo(DualSenseController controller)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot get or create controller info");
            throw new InvalidOperationException("Settings manager not available");
        }

        Logger.Debug<DualSenseProfileManager>($"Getting or creating controller info");
        ControllerSettings settings = settingsManager.Application.Controllers;

        // Try to find by MAC address first (most reliable for Bluetooth)
        if (!string.IsNullOrEmpty(controller.MacAddress))
        {
            Logger.Trace<DualSenseProfileManager>($"Searching by MAC address: {controller.MacAddress}");
            ControllerInfo? existing = settings.KnownControllers.Values.FirstOrDefault(c => c.MacAddress == controller.MacAddress);

            if (existing != null)
            {
                Logger.Debug<DualSenseProfileManager>($"Found existing controller by MAC: {existing.Name} (ID: {existing.Id})");
                return existing;
            }
        }
        else
        {
            Logger.Warning<DualSenseProfileManager>("Controller has no MAC address, cannot search by MAC");
        }

        // Create new controller info
        Logger.Info<DualSenseProfileManager>("Creating new controller entry");
        string controllerId = GenerateControllerId(controller);
        ControllerInfo controllerInfo = new ControllerInfo
        {
            Id = controllerId,
            Name = $"DualSense {settings.KnownControllers.Count + 1}",
            MacAddress = controller.MacAddress,
            SerialNumber = TryGetSerialNumber(controller.Device),
            ProfileId = settings.DefaultProfileId,
            LastSeen = DateTime.UtcNow,
            LastConnectionType = controller.ConnectionType
        };

        Logger.Debug<DualSenseProfileManager>($"  Controller ID: {controllerId}");
        Logger.Debug<DualSenseProfileManager>($"  Name: {controllerInfo.Name}");
        Logger.Debug<DualSenseProfileManager>($"  MAC: {controllerInfo.MacAddress ?? "None"}");
        Logger.Debug<DualSenseProfileManager>($"  Serial: {controllerInfo.SerialNumber ?? "None"}");
        Logger.Debug<DualSenseProfileManager>($"  Profile ID: {controllerInfo.ProfileId ?? "None"}");

        settings.KnownControllers[controllerId] = controllerInfo;
        Logger.Info<DualSenseProfileManager>($"Added new controller: {controllerInfo.Name} (ID: {controllerId})");

        // Create default profile if none exists
        if (settings.Profiles.Count == 0)
        {
            Logger.Warning<DualSenseProfileManager>("No profiles exist, creating default profile");
            CreateDefaultProfile();
        }

        return controllerInfo;
    }

    private string GenerateControllerId(DualSenseController controller)
    {
        if (!string.IsNullOrEmpty(controller.MacAddress))
        {
            string id = controller.MacAddress.Replace(":", "");
            Logger.Trace<DualSenseProfileManager>($"Generated controller ID from MAC: {id}");
            return id;
        }

        Logger.Error<DualSenseProfileManager>("Cannot generate controller ID: MAC address is missing");
        throw new Exception("Couldn't find MAC address of the controller");
    }

    private string? TryGetSerialNumber(HidDevice device)
    {
        try
        {
            string? serial = device.GetSerialNumber();
            Logger.Trace<DualSenseProfileManager>($"Retrieved serial number: {serial ?? "None"}");
            return serial;
        }
        catch (Exception ex)
        {
            Logger.Debug<DualSenseProfileManager>($"Could not get serial number: {ex.Message}");
            return null;
        }
    }

    private void ApplyProfile(DualSenseController controller, ControllerInfo controllerInfo)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot apply profile");
            return;
        }

        Logger.Debug<DualSenseProfileManager>($"Applying profile to controller '{controllerInfo.Name}'");
        ControllerSettings settings = settingsManager.Application.Controllers;

        // Get profile ID (use default if controller doesn't have one)
        string? profileId = controllerInfo.ProfileId ?? settings.DefaultProfileId;
        Logger.Trace<DualSenseProfileManager>($"Profile ID to apply: {profileId ?? "None"}");

        if (string.IsNullOrEmpty(profileId) || !settings.Profiles.TryGetValue(profileId, out var profile))
        {
            Logger.Warning<DualSenseProfileManager>($"No valid profile found for controller {controllerInfo.Name}");
            return;
        }

        Logger.Info<DualSenseProfileManager>($"Applying profile '{profile.Name}' to '{controllerInfo.Name}'");
        Logger.Debug<DualSenseProfileManager>($"  Lightbar: RGB({profile.Lightbar.Red}, {profile.Lightbar.Green}, {profile.Lightbar.Blue})");
        Logger.Debug<DualSenseProfileManager>($"  Player LEDs: {profile.PlayerLeds.Pattern} @ {profile.PlayerLeds.Brightness}");
        Logger.Debug<DualSenseProfileManager>($"  Mic LED: {profile.MicLed}");
        Logger.Debug<DualSenseProfileManager>($"  Virtual Controller: Enabled={profile.VirtualControllerSettings.EnableEmulation}, Type={profile.VirtualControllerSettings.EmulationType}");

        try
        {
            // Apply lightbar settings
            controller.SetLightbar(profile.Lightbar.Red, profile.Lightbar.Green, profile.Lightbar.Blue);
            Logger.Trace<DualSenseProfileManager>("Lightbar applied");

            // Apply player LEDs
            controller.SetPlayerLeds(profile.PlayerLeds.Pattern, profile.PlayerLeds.Brightness);
            Logger.Trace<DualSenseProfileManager>("Player LEDs applied");

            // Apply mic LED
            controller.SetMicLed(profile.MicLed);
            Logger.Trace<DualSenseProfileManager>("Mic LED applied");

            // Apply virtual controller settings if available
            if (controller.ControllerEmulationService != null)
            {
                controller.ControllerEmulationService.ForceStopRumble = profile.VirtualControllerSettings.ForceStopRumble;
                controller.ControllerEmulationService.IgnoreDS4Lightbar = profile.VirtualControllerSettings.IgnoreDS4Lightbar;
                controller.ControllerEmulationService.LeftTriggerThreshold = profile.VirtualControllerSettings.LeftTriggerThreshold;
                controller.ControllerEmulationService.RightTriggerThreshold = profile.VirtualControllerSettings.RightTriggerThreshold;

                // Start or stop emulation based on settings
                if (profile.VirtualControllerSettings.EnableEmulation)
                {
                    if (profile.VirtualControllerSettings.EmulationType == VirtualControllerType.X360)
                    {
                        controller.ControllerEmulationService.StartX360Emulation();
                    }
                    else if (profile.VirtualControllerSettings.EmulationType == VirtualControllerType.DS4)
                    {
                        controller.ControllerEmulationService.StartDS4Emulation();
                    }
                }
                else
                {
                    controller.ControllerEmulationService.StopEmulation();
                }

                Logger.Trace<DualSenseProfileManager>($"Virtual controller settings applied: Enabled={profile.VirtualControllerSettings.EnableEmulation}, Type={profile.VirtualControllerSettings.EmulationType}");
            }
            else
            {
                Logger.Debug<DualSenseProfileManager>("ControllerEmulationService not available, skipping virtual controller settings");
            }

            Logger.Info<DualSenseProfileManager>("Profile applied successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<DualSenseProfileManager>($"Failed to apply profile settings");
            Logger.LogExceptionDetails<DualSenseProfileManager>(ex, includeEnvironmentInfo: false);
        }
    }

    /// <summary>
    /// Manually refreshes profiles for all connected controllers
    /// </summary>
    public void RefreshAllProfiles()
    {
        DualSenseManager? dualSenseManager = DualSenseServiceLocator.GetDualSenseManager();
        if (dualSenseManager == null)
        {
            Logger.Warning<DualSenseProfileManager>("DualSenseManager not available, cannot refresh profiles");
            return;
        }

        Logger.Info<DualSenseProfileManager>("Refreshing profiles for all connected controllers");
        int count = dualSenseManager.Controllers.Count;
        Logger.Debug<DualSenseProfileManager>($"Connected controllers: {count}");

        foreach (DualSenseController controller in dualSenseManager.Controllers.Values)
        {
            try
            {
                ControllerInfo controllerInfo = GetOrCreateControllerInfo(controller);
                ApplyProfile(controller, controllerInfo);
            }
            catch (Exception ex)
            {
                Logger.Error<DualSenseProfileManager>($"Failed to refresh profile for controller");
                Logger.LogExceptionDetails<DualSenseProfileManager>(ex, includeEnvironmentInfo: false);
            }
        }

        Logger.Info<DualSenseProfileManager>("Profile refresh complete");
    }

    public void CreateDefaultProfile()
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot create default profile");
            return;
        }

        Logger.Info<DualSenseProfileManager>("Creating default controller profile");
        ControllerSettings settings = settingsManager.Application.Controllers;

        ControllerProfile defaultProfile = new ControllerProfile
        {
            Id = "default",
            Name = "Default",
            Lightbar = new LightbarSettings { Red = 0, Green = 0, Blue = 0 },
            PlayerLeds = new PlayerLedSettings { Pattern = PlayerLed.None },
            MicLed = MicLed.Off,
            VirtualControllerSettings = new VirtualControllerSettings { EnableEmulation = false, EmulationType = VirtualControllerType.X360 }
        };

        Logger.Debug<DualSenseProfileManager>("Default profile settings: Lightbar=Off, PlayerLEDs=None, MicLED=Off");

        settings.Profiles["default"] = defaultProfile;
        settings.DefaultProfileId = "default";

        Logger.Info<DualSenseProfileManager>("Default controller profile created and set");
    }

    public void AssignProfileToController(string controllerId, string profileId)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot assign profile to controller");
            return;
        }

        Logger.Info<DualSenseProfileManager>($"Assigning profile '{profileId}' to controller '{controllerId}'");
        ControllerSettings settings = settingsManager.Application.Controllers;

        if (!settings.KnownControllers.TryGetValue(controllerId, out var controllerInfo))
        {
            Logger.Warning<DualSenseProfileManager>($"Controller not found: {controllerId}");
            return;
        }

        if (!settings.Profiles.ContainsKey(profileId))
        {
            Logger.Warning<DualSenseProfileManager>($"Profile not found: {profileId}");
            return;
        }

        Logger.Debug<DualSenseProfileManager>($"Updating controller '{controllerInfo.Name}' profile assignment");
        controllerInfo.ProfileId = profileId;

        // Apply immediately if controller is connected
        DualSenseController? connectedController = FindConnectedController(controllerInfo);
        if (connectedController != null)
        {
            Logger.Debug<DualSenseProfileManager>("Controller is connected, applying profile immediately");
            ApplyProfile(connectedController, controllerInfo);
        }
        else
        {
            Logger.Debug<DualSenseProfileManager>("Controller is not currently connected, profile will be applied on next connection");
        }

        // Get the profile to trigger the ProfileChanged event
        if (settings.Profiles.TryGetValue(profileId, out var profile))
        {
            // Trigger the profile changed event to notify other components about the change
            TriggerProfileChanged(controllerId, profile);
        }

        settingsManager.SaveAll();
        Logger.Info<DualSenseProfileManager>($"Profile assignment saved successfully");
    }

    private DualSenseController? FindConnectedController(ControllerInfo info)
    {
        Logger.Trace<DualSenseProfileManager>($"Searching for connected controller: {info.Name}");

        DualSenseManager? dualSenseManager = DualSenseServiceLocator.GetDualSenseManager();
        if (dualSenseManager == null)
        {
            Logger.Warning<DualSenseProfileManager>("DualSenseManager not available, cannot find connected controller");
            return null;
        }

        // Try to find by MAC address
        if (!string.IsNullOrEmpty(info.MacAddress))
        {
            DualSenseController? controller = dualSenseManager.Controllers.Values.FirstOrDefault(c => c.MacAddress == info.MacAddress);
            if (controller != null)
            {
                Logger.Trace<DualSenseProfileManager>($"Found by MAC address");
                return controller;
            }
        }

        // Try to find by serial number
        if (!string.IsNullOrEmpty(info.SerialNumber))
        {
            DualSenseController? controller = dualSenseManager.Controllers.Values.FirstOrDefault(c => TryGetSerialNumber(c.Device) == info.SerialNumber);
            if (controller != null)
            {
                Logger.Trace<DualSenseProfileManager>($"Found by serial number");
                return controller;
            }
        }

        Logger.Trace<DualSenseProfileManager>("Controller not found in connected devices");
        return null;
    }

    /// <summary>
    /// Gets all available profiles
    /// </summary>
    public Dictionary<string, ControllerProfile> GetAllProfiles()
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot get all profiles");
            return new Dictionary<string, ControllerProfile>();
        }

        int count = settingsManager.Application.Controllers.Profiles.Count;
        Logger.Trace<DualSenseProfileManager>($"GetAllProfiles: returning {count} profile(s)");
        return settingsManager.Application.Controllers.Profiles;
    }

    /// <summary>
    /// Gets a profile by ID
    /// </summary>
    public ControllerProfile? GetProfile(string profileId)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot get profile");
            return null;
        }

        Logger.Trace<DualSenseProfileManager>($"GetProfile: {profileId}");
        bool found = settingsManager.Application.Controllers.Profiles.TryGetValue(profileId, out ControllerProfile? profile);
        Logger.Trace<DualSenseProfileManager>($"Profile {(found ? "found" : "not found")}");
        return profile;
    }

    /// <summary>
    /// Gets the default profile
    /// </summary>
    public ControllerProfile GetDefaultProfile()
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot get default profile");
            throw new InvalidOperationException("Settings manager not available");
        }

        Logger.Trace<DualSenseProfileManager>("GetDefaultProfile called");
        string? defaultId = settingsManager.Application.Controllers.DefaultProfileId;

        if (defaultId != null && settingsManager.Application.Controllers.Profiles.TryGetValue(defaultId, out ControllerProfile? profile))
        {
            Logger.Debug<DualSenseProfileManager>($"Returning default profile: {profile.Name} (ID: {defaultId})");
            return profile;
        }

        // Create default if it doesn't exist
        Logger.Warning<DualSenseProfileManager>("Default profile doesn't exist, creating it");
        CreateDefaultProfile();

        defaultId = settingsManager.Application.Controllers.DefaultProfileId;
        return settingsManager.Application.Controllers.Profiles[defaultId!];
    }

    /// <summary>
    /// Gets the profile assigned to a specific controller
    /// </summary>
    public ControllerProfile GetControllerProfile(string controllerId)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot get controller profile");
            throw new InvalidOperationException("Settings manager not available");
        }

        Logger.Trace<DualSenseProfileManager>($"GetControllerProfile: {controllerId}");

        if (settingsManager.Application.Controllers.KnownControllers.TryGetValue(controllerId, out ControllerInfo? controllerInfo) &&
            controllerInfo.ProfileId != null &&
            settingsManager.Application.Controllers.Profiles.TryGetValue(controllerInfo.ProfileId, out ControllerProfile? profile))
        {
            Logger.Debug<DualSenseProfileManager>($"Controller '{controllerInfo.Name}' assigned profile: {profile.Name}");
            return profile;
        }

        Logger.Debug<DualSenseProfileManager>($"No specific profile assigned to controller {controllerId}, returning default");
        return GetDefaultProfile();
    }

    /// <summary>
    /// Creates a new profile
    /// </summary>
    public ControllerProfile CreateProfile(string name)
    {
        Logger.Info<DualSenseProfileManager>($"Creating new profile: {name}");

        ControllerProfile profile = new ControllerProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Lightbar = new LightbarSettings
            {
                Behavior = LightbarBehavior.Custom,
                Red = 0,
                Green = 0,
                Blue = 255
            },
            PlayerLeds = new PlayerLedSettings
            {
                Pattern = PlayerLed.None,
                Brightness = PlayerLedBrightness.High
            },
            MicLed = MicLed.Off,
            VirtualControllerSettings = new VirtualControllerSettings { EnableEmulation = false, EmulationType = VirtualControllerType.X360 }
        };

        Logger.Debug<DualSenseProfileManager>($"Profile ID: {profile.Id}");
        Logger.Debug<DualSenseProfileManager>($"Default settings: Lightbar=Blue, PlayerLEDs=None, MicLED=Off");

        SaveProfile(profile);
        Logger.Info<DualSenseProfileManager>($"Profile '{name}' created successfully");

        return profile;
    }

    /// <summary>
    /// Saves a profile
    /// </summary>
    public void SaveProfile(ControllerProfile profile)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot save profile");
            return;
        }

        Logger.Debug<DualSenseProfileManager>($"Saving profile: {profile.Name} (ID: {profile.Id})");
        settingsManager.Application.Controllers.Profiles[profile.Id] = profile;
        settingsManager.SaveAll();
        Logger.Debug<DualSenseProfileManager>("Profile saved successfully");
    }

    /// <summary>
    /// Deletes a profile
    /// </summary>
    public bool DeleteProfile(string profileId)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot delete profile");
            return false;
        }

        Logger.Info<DualSenseProfileManager>($"Attempting to delete profile: {profileId}");

        if (settingsManager.Application.Controllers.DefaultProfileId == profileId)
        {
            Logger.Warning<DualSenseProfileManager>($"Cannot delete default profile: {profileId}");
            return false;
        }

        // Get profile name for logging
        string? profileName = GetProfile(profileId)?.Name;

        // Remove profile assignment from controllers and assign default
        string? defaultProfileId = settingsManager.Application.Controllers.DefaultProfileId;
        Logger.Debug<DualSenseProfileManager>($"Reassigning controllers from deleted profile to default: {defaultProfileId}");

        int reassignedCount = 0;
        foreach (ControllerInfo controller in settingsManager.Application.Controllers.KnownControllers.Values)
        {
            if (controller.ProfileId == profileId)
            {
                Logger.Trace<DualSenseProfileManager>($"Reassigning controller '{controller.Name}' to default profile");
                controller.ProfileId = defaultProfileId;
                reassignedCount++;
            }
        }

        if (reassignedCount > 0)
        {
            Logger.Debug<DualSenseProfileManager>($"Reassigned {reassignedCount} controller(s) to default profile");
        }

        bool removed = settingsManager.Application.Controllers.Profiles.Remove(profileId);
        if (removed)
        {
            settingsManager.SaveAll();
            Logger.Info<DualSenseProfileManager>($"Profile '{profileName}' deleted successfully");
        }
        else
        {
            Logger.Warning<DualSenseProfileManager>($"Failed to remove profile '{profileName}' from profiles dictionary");
        }

        return removed;
    }

    /// <summary>
    /// Sets the default profile
    /// </summary>
    public void SetDefaultProfile(string profileId)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot set default profile");
            return;
        }

        Logger.Info<DualSenseProfileManager>($"Setting default profile: {profileId}");

        if (settingsManager.Application.Controllers.Profiles.ContainsKey(profileId))
        {
            string? profileName = GetProfile(profileId)?.Name;
            settingsManager.Application.Controllers.DefaultProfileId = profileId;
            settingsManager.SaveAll();
            Logger.Info<DualSenseProfileManager>($"Default profile set to: {profileName}");
        }
        else
        {
            Logger.Warning<DualSenseProfileManager>($"Cannot set default profile: profile {profileId} not found");
        }
    }

    /// <summary>
    /// Duplicates an existing profile
    /// </summary>
    public ControllerProfile DuplicateProfile(string sourceProfileId, string newName)
    {
        Logger.Info<DualSenseProfileManager>($"Duplicating profile {sourceProfileId} as '{newName}'");

        ControllerProfile? sourceProfile = GetProfile(sourceProfileId);
        if (sourceProfile == null)
        {
            Logger.Error<DualSenseProfileManager>($"Source profile not found: {sourceProfileId}");
            throw new ArgumentException($"Profile not found: {sourceProfileId}");
        }

        Logger.Debug<DualSenseProfileManager>($"Source profile: {sourceProfile.Name}");

        ControllerProfile newProfile = new ControllerProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = newName,
            Lightbar = new LightbarSettings
            {
                Behavior = sourceProfile.Lightbar.Behavior,
                Red = sourceProfile.Lightbar.Red,
                Green = sourceProfile.Lightbar.Green,
                Blue = sourceProfile.Lightbar.Blue
            },
            PlayerLeds = new PlayerLedSettings
            {
                Pattern = sourceProfile.PlayerLeds.Pattern,
                Brightness = sourceProfile.PlayerLeds.Brightness
            },
            MicLed = sourceProfile.MicLed,
            VirtualControllerSettings = new VirtualControllerSettings
            {
                EnableEmulation = sourceProfile.VirtualControllerSettings.EnableEmulation,
                EmulationType = sourceProfile.VirtualControllerSettings.EmulationType,
                ForceStopRumble = sourceProfile.VirtualControllerSettings.ForceStopRumble,
                IgnoreDS4Lightbar = sourceProfile.VirtualControllerSettings.IgnoreDS4Lightbar,
                LeftTriggerThreshold = sourceProfile.VirtualControllerSettings.LeftTriggerThreshold,
                RightTriggerThreshold = sourceProfile.VirtualControllerSettings.RightTriggerThreshold
            }
        };

        Logger.Debug<DualSenseProfileManager>($"New profile ID: {newProfile.Id}");
        Logger.Debug<DualSenseProfileManager>($"Copied settings: Lightbar=RGB({newProfile.Lightbar.Red},{newProfile.Lightbar.Green},{newProfile.Lightbar.Blue}), PlayerLEDs={newProfile.PlayerLeds.Pattern}");

        SaveProfile(newProfile);
        Logger.Info<DualSenseProfileManager>($"Profile duplicated successfully: '{newName}' (ID: {newProfile.Id})");

        return newProfile;
    }

    /// <summary>
    /// Creates a profile from current controller state
    /// </summary>
    public ControllerProfile CreateProfileFromController(DualSenseController controller, string name)
    {
        Logger.Info<DualSenseProfileManager>($"Creating profile '{name}' from controller state");

        LightbarColor lightbar = controller.CurrentLightbarColor;
        Logger.Debug<DualSenseProfileManager>($"Current controller state:");
        Logger.Debug<DualSenseProfileManager>($"  Lightbar: RGB({lightbar.Red}, {lightbar.Green}, {lightbar.Blue})");
        Logger.Debug<DualSenseProfileManager>($"  Player LEDs: {controller.CurrentPlayerLeds} @ {controller.CurrentPlayerLedBrightness}");
        Logger.Debug<DualSenseProfileManager>($"  Mic LED: {controller.CurrentMicLed}");

        ControllerProfile profile = new ControllerProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Lightbar = new LightbarSettings
            {
                Behavior = controller.CurrentLightbarBehavior,
                Red = lightbar.Red,
                Green = lightbar.Green,
                Blue = lightbar.Blue
            },
            PlayerLeds = new PlayerLedSettings
            {
                Pattern = controller.CurrentPlayerLeds,
                Brightness = controller.CurrentPlayerLedBrightness
            },
            MicLed = controller.CurrentMicLed,
            VirtualControllerSettings = new VirtualControllerSettings
            {
                EnableEmulation = false,
                EmulationType = VirtualControllerType.X360,
                TrackpadMouse = new TrackpadMouseSettings
                {
                    Enabled = false,
                    Sensitivity = 1.0,
                    InvertX = false,
                    InvertY = false
                }
            }
        };

        Logger.Debug<DualSenseProfileManager>($"Profile ID: {profile.Id}");
        SaveProfile(profile);
        Logger.Info<DualSenseProfileManager>($"Profile created from controller state successfully");

        return profile;
    }

    /// <summary>
    /// Applies a profile to a controller
    /// </summary>
    public void ApplyProfileToController(DualSenseController controller, ControllerProfile profile)
    {
        Logger.Info<DualSenseProfileManager>($"Applying profile '{profile.Name}' to controller");
        Logger.Debug<DualSenseProfileManager>($"Profile settings:");
        Logger.Debug<DualSenseProfileManager>($"  Lightbar: RGB({profile.Lightbar.Red}, {profile.Lightbar.Green}, {profile.Lightbar.Blue})");
        Logger.Debug<DualSenseProfileManager>($"  Player LEDs: {profile.PlayerLeds.Pattern} @ {profile.PlayerLeds.Brightness}");
        Logger.Debug<DualSenseProfileManager>($"  Mic LED: {profile.MicLed}");

        try
        {
            // Apply lightbar settings
            controller.SetLightbar(profile.Lightbar.Red, profile.Lightbar.Green, profile.Lightbar.Blue);
            Logger.Trace<DualSenseProfileManager>("Lightbar applied");

            // Apply player LEDs
            controller.SetPlayerLeds(profile.PlayerLeds.Pattern, profile.PlayerLeds.Brightness);
            Logger.Trace<DualSenseProfileManager>("Player LEDs applied");

            // Apply mic LED
            controller.SetMicLed(profile.MicLed);
            Logger.Trace<DualSenseProfileManager>("Mic LED applied");

            // Apply virtual controller settings including trackpad settings
            if (controller.ControllerEmulationService != null)
            {
                controller.ControllerEmulationService.ForceStopRumble = profile.VirtualControllerSettings.ForceStopRumble;
                controller.ControllerEmulationService.IgnoreDS4Lightbar = profile.VirtualControllerSettings.IgnoreDS4Lightbar;
                controller.ControllerEmulationService.LeftTriggerThreshold = profile.VirtualControllerSettings.LeftTriggerThreshold;
                controller.ControllerEmulationService.RightTriggerThreshold = profile.VirtualControllerSettings.RightTriggerThreshold;

                // Start or stop emulation based on settings
                if (profile.VirtualControllerSettings.EnableEmulation)
                {
                    if (profile.VirtualControllerSettings.EmulationType == VirtualControllerType.X360)
                    {
                        controller.ControllerEmulationService.StartX360Emulation();
                    }
                    else
                    {
                        controller.ControllerEmulationService.StartDS4Emulation();
                    }
                }
                else
                {
                    controller.ControllerEmulationService.StopEmulation();
                }
            }

            // Apply trackpad mouse settings
            controller.SetTrackpadMouseEnabled(profile.VirtualControllerSettings.TrackpadMouse.Enabled, profile.VirtualControllerSettings);

            Logger.Trace<DualSenseProfileManager>("Virtual controller and trackpad settings applied");
            Logger.Info<DualSenseProfileManager>("Profile applied to controller successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<DualSenseProfileManager>("Failed to apply profile to controller");
            Logger.LogExceptionDetails<DualSenseProfileManager>(ex, includeEnvironmentInfo: false);
            throw;
        }
    }

    /// <summary>
    /// Gets all known controllers
    /// </summary>
    public Dictionary<string, ControllerInfo> GetKnownControllers()
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot get known controllers");
            return new Dictionary<string, ControllerInfo>();
        }

        int count = settingsManager.Application.Controllers.KnownControllers.Count;
        Logger.Trace<DualSenseProfileManager>($"GetKnownControllers: returning {count} controller(s)");
        return settingsManager.Application.Controllers.KnownControllers;
    }

    /// <summary>
    /// Updates a controller's name
    /// </summary>
    public void UpdateControllerName(string controllerId, string newName)
    {
        ISettingsManager? settingsManager = DualSenseServiceLocator.GetSettingsManager();
        if (settingsManager == null)
        {
            Logger.Error<DualSenseProfileManager>("Settings manager not available, cannot update controller name");
            return;
        }

        Logger.Info<DualSenseProfileManager>($"Updating controller name: {controllerId} -> '{newName}'");

        if (settingsManager.Application.Controllers.KnownControllers.TryGetValue(controllerId, out var controllerInfo))
        {
            string oldName = controllerInfo.Name;
            controllerInfo.Name = newName;
            settingsManager.SaveAll();
            Logger.Info<DualSenseProfileManager>($"Controller name updated: '{oldName}' -> '{newName}'");
        }
        else
        {
            Logger.Warning<DualSenseProfileManager>($"Cannot update name: controller {controllerId} not found");
        }
    }

    protected virtual void OnProfileChanged(string controllerId, ControllerProfile profile)
    {
        ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(controllerId, profile));
    }

    /// <summary>
    /// Triggers the ProfileChanged event for a specific controller
    /// </summary>
    public void TriggerProfileChanged(string controllerId, ControllerProfile profile)
    {
        OnProfileChanged(controllerId, profile);
    }

    public void Dispose()
    {
        Logger.Info<DualSenseProfileManager>("Disposing DualSenseProfileManager");

        DualSenseManager? dualSenseManager = DualSenseServiceLocator.GetDualSenseManager();
        if (dualSenseManager != null)
        {
            dualSenseManager.ControllerConnected -= OnControllerConnected;
            dualSenseManager.ControllerDisconnected -= OnControllerDisconnected;
        }

        Logger.Debug<DualSenseProfileManager>("DualSenseProfileManager disposed");
    }
}