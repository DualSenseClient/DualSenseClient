using System;
using System.Reflection;
using System.Runtime.Versioning;
using DualSenseClient.Core.Logging;
using DualSenseClient.ViewModels;

namespace DualSenseClient.Services.Helpers;

/// <summary>
/// Helper class for handling HidHide operations such as hiding and unhiding controllers.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HidHideHelper
{
    /// <summary>
    /// Attempts to hide the specified controller using HidHide service.
    /// </summary>
    /// <param name="hidHideService">The HidHide service to use</param>
    /// <param name="controller">The controller to hide</param>
    [SupportedOSPlatform("windows")]
    public static void HideController(IHidHideService hidHideService, ControllerViewModelBase controller)
    {
        try
        {
            if (hidHideService is not { IsReady: true })
            {
                Logger.Warning<TrayIconService>("HidHide is not ready or not available");
                return;
            }

            // Get the MAC address from the controller
            Type controllerType = controller.Controller.GetType();
            PropertyInfo? macAddressProperty = controllerType.GetProperty("MacAddress");
            if (macAddressProperty == null)
            {
                Logger.Warning<TrayIconService>($"Could not find MAC address property for controller: {controller.Name}");
                return;
            }
            string? macAddress = macAddressProperty.GetValue(controller.Controller) as string;
            if (!string.IsNullOrEmpty(macAddress))
            {
                // Find the device instance ID using the HidHideService
                string? deviceInstanceId = hidHideService.FindDeviceInstanceIdByMacAddress(macAddress);
                if (!string.IsNullOrEmpty(deviceInstanceId))
                {
                    Logger.Info<TrayIconService>($"Hiding controller with device ID: {deviceInstanceId}");
                    bool success = hidHideService.HideDevice(deviceInstanceId);

                    if (success)
                    {
                        Logger.Info<TrayIconService>($"Successfully hid controller: {controller.Name}");
                        // Optionally activate cloaking if not already active
                        if (!hidHideService.IsCloakingActive())
                        {
                            hidHideService.SetCloakingState(true);
                        }
                    }
                    else
                    {
                        Logger.Warning<TrayIconService>($"Failed to hide controller: {controller.Name}");
                    }
                }
                else
                {
                    Logger.Warning<TrayIconService>($"Could not find device instance ID for controller: {controller.Name}");
                }
            }
            else
            {
                Logger.Warning<TrayIconService>($"Could not get MAC address for controller: {controller.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to hide controller '{controller.Name}': {ex.Message}");
            Logger.LogExceptionDetails<TrayIconService>(ex);
        }
    }

    /// <summary>
    /// Attempts to unhide the specified controller using HidHide service.
    /// </summary>
    /// <param name="hidHideService">The HidHide service to use</param>
    /// <param name="controller">The controller to unhide</param>
    [SupportedOSPlatform("windows")]
    public static void UnhideController(IHidHideService hidHideService, ControllerViewModelBase controller)
    {
        try
        {
            if (hidHideService is not { IsReady: true })
            {
                Logger.Warning<TrayIconService>("HidHide is not ready or not available");
                return;
            }

            // Get the MAC address from the controller
            Type controllerType = controller.Controller.GetType();
            PropertyInfo? macAddressProperty = controllerType.GetProperty("MacAddress");
            if (macAddressProperty == null)
            {
                Logger.Warning<TrayIconService>($"Could not find MAC address property for controller: {controller.Name}");
                return;
            }
            string? macAddress = macAddressProperty.GetValue(controller.Controller) as string;
            if (!string.IsNullOrEmpty(macAddress))
            {
                // Find the device instance ID using the HidHideService
                string? deviceInstanceId = hidHideService.FindDeviceInstanceIdByMacAddress(macAddress);
                if (!string.IsNullOrEmpty(deviceInstanceId))
                {
                    Logger.Info<TrayIconService>($"Unhiding controller with device ID: {deviceInstanceId}");
                    bool success = hidHideService.UnhideDevice(deviceInstanceId);

                    if (success)
                    {
                        Logger.Info<TrayIconService>($"Successfully unhid controller: {controller.Name}");
                    }
                    else
                    {
                        Logger.Warning<TrayIconService>($"Failed to unhide controller: {controller.Name}");
                    }
                }
                else
                {
                    Logger.Warning<TrayIconService>($"Could not find device instance ID for controller: {controller.Name}");
                }
            }
            else
            {
                Logger.Warning<TrayIconService>($"Could not get MAC address for controller: {controller.Name}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error<TrayIconService>($"Failed to unhide controller '{controller.Name}': {ex.Message}");
            Logger.LogExceptionDetails<TrayIconService>(ex);
        }
    }
}