using System;
using System.IO;
using System.Runtime.InteropServices;
using DualSenseClient.Core.Logging;
using Microsoft.Win32;
using DualSenseClient.Core.Settings;
using DualSenseClient.Core.Utils;

namespace DualSenseClient.Services;

public interface IAutoStartService
{
    void SetAutoStart(bool enable);
    bool IsAutoStartEnabled();
}

public class AutoStartService : IAutoStartService
{
    private readonly ISettingsManager _settingsManager;
    private const string AppName = "DualSenseClient";

    public AutoStartService(ISettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public void SetAutoStart(bool enable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetAutoStartWindows(enable);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SetAutoStartLinux(enable);
        }

        // Update the setting in the configuration
        _settingsManager.Application.Ui.StartOnLaunch = enable;
        _settingsManager.SaveAll();
    }

    public bool IsAutoStartEnabled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return IsAutoStartEnabledWindows();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return IsAutoStartEnabledLinux();
        }

        return _settingsManager.Application.Ui.StartOnLaunch;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void SetAutoStartWindows(bool enable)
    {
        try
        {
            if (!Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup)))
            {
                Logger.Info<AutoStartService>("Startup directory not found");
                Directory.CreateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
                Logger.Info<AutoStartService>("Startup directory created");
            }
            
            string startupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{AppName}.lnk");

            if (enable)
            {
                // Get the current executable path
                string exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;

                // Create a shortcut in the startup folder
                WindowsShortcut.CreateWindowsShortcut(startupPath, exePath, AppName);
                Logger.Info<AutoStartService>("Successfully created Windows startup shortcut");
            }
            else
            {
                // Remove the shortcut if it exists
                if (File.Exists(startupPath))
                {
                    File.Delete(startupPath);
                    Logger.Info<AutoStartService>("Successfully deleted Windows startup shortcut");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error<AutoStartService>("Failed to manage Windows auto-start");
            Logger.LogExceptionDetails<AutoStartService>(ex);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private bool IsAutoStartEnabledWindows()
    {
        try
        {
            string startupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{AppName}.lnk");

            return File.Exists(startupPath);
        }
        catch (Exception ex)
        {
            Logger.Error<AutoStartService>("Failed to check Windows auto-start");
            Logger.LogExceptionDetails<AutoStartService>(ex);
            return false;
        }
    }

    private void SetAutoStartLinux(bool enable)
    {
        try
        {
            // TODO: Linux Support
            return;
        }
        catch (Exception ex)
        {
            Logger.Error<AutoStartService>("Failed to manage Linux auto-start");
            Logger.LogExceptionDetails<AutoStartService>(ex);
        }
    }

    private bool IsAutoStartEnabledLinux()
    {
        try
        {
            // TODO: Linux Support
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error<AutoStartService>("Failed to check Linux auto-start");
            Logger.LogExceptionDetails<AutoStartService>(ex);
            return false;
        }
    }
}