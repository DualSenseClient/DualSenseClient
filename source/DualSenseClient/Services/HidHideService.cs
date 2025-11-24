using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using DualSenseClient.Core.Logging;
using Microsoft.Win32;

namespace DualSenseClient.Services;

[SupportedOSPlatform("windows")]
public class HidHideService : IHidHideService
{
    private readonly string? _cliPath;
    private readonly string _appPath;

    public HidHideService()
    {
        _cliPath = FindHidHideCLI();
        _appPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    private string? FindHidHideCLI()
    {
        string? installLocation = GetInstalledPath("HidHide");
        if (string.IsNullOrEmpty(installLocation))
        {
            return null;
        }

        // First, try the root directory
        string cliPath = Path.Combine(installLocation, "HidHideCLI.exe");
        if (File.Exists(cliPath))
        {
            return cliPath;
        }

        // If not found in root, search recursively in subdirectories
        try
        {
            var files = Directory.GetFiles(installLocation, "HidHideCLI.exe", SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error searching for HidHideCLI.exe: {ex.Message}";
            Logger.Warning<HidHideService>(errorMsg);
            Logger.LogExceptionDetails<HidHideService>(ex, includeEnvironmentInfo: false);
            return null;
        }
    }

    private static string? GetInstalledPath(string appNameOrPartialName, string? exactExeName = null)
    {
        string[] registryKeys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" // 32-bit apps on 64-bit OS
        };

        foreach (string baseKey in registryKeys)
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(baseKey);
            if (key == null)
            {
                continue;
            }

            foreach (string subkeyName in key.GetSubKeyNames())
            {
                using RegistryKey? subkey = key.OpenSubKey(subkeyName);
                if (subkey == null)
                {
                    continue;
                }

                string? displayName = subkey.GetValue("DisplayName") as string;
                if (string.IsNullOrEmpty(displayName))
                {
                    continue;
                }

                // Case-insensitive partial or exact match
                if (!displayName.Contains(appNameOrPartialName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Optional: verify the actual executable exists
                string? installLocation = subkey.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(installLocation))
                {
                    // Some installers put the path in UninstallString instead
                    string? uninstallString = subkey.GetValue("UninstallString") as string;
                    if (!string.IsNullOrEmpty(uninstallString))
                    {
                        installLocation = ExtractPathFromUninstallString(uninstallString);
                    }
                }

                if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                {
                    if (exactExeName != null)
                    {
                        string exePath = Path.Combine(installLocation, exactExeName);
                        if (File.Exists(exePath))
                            return exePath;
                    }
                    else
                    {
                        return installLocation;
                    }
                }
            }
        }

        return null;
    }

    private static string? ExtractPathFromUninstallString(string uninstallString)
    {
        // Handles cases like: "MsiExec.exe /I{...}" or "\"C:\Program Files\App\unins000.exe\""
        if (string.IsNullOrEmpty(uninstallString))
        {
            return null;
        }

        if (uninstallString.StartsWith("\""))
        {
            int secondQuote = uninstallString.IndexOf('"', 1);
            if (secondQuote > 1)
            {
                return Path.GetDirectoryName(uninstallString.Substring(1, secondQuote - 1));
            }
        }

        // Fallback: take first valid path found
        MatchCollection matches = Regex.Matches(uninstallString, @"[a-z]:\\.+?\\", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            string path = m.Value.TrimEnd('\\');
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public bool IsInstalled => !string.IsNullOrEmpty(_cliPath);

    public bool IsRunningAsAdmin()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // If we can't determine admin status, assume not running as admin
            return false;
        }
    }

    public bool IsReady => IsInstalled && IsRunningAsAdmin();

    public bool IsAppRegistered()
    {
        if (!IsInstalled)
        {
            return false;
        }

        string output = RunCommand("--app-list");
        return output.Contains(_appPath, StringComparison.OrdinalIgnoreCase);
    }

    public bool RegisterApp()
    {
        if (!IsInstalled)
        {
            return false;
        }

        if (IsAppRegistered())
        {
            return true; // Already registered
        }

        string output = RunCommand($"--app-reg \"{_appPath}\"");
        return output.Contains("OK") || string.IsNullOrEmpty(output.Trim());
    }

    public bool UnregisterApp()
    {
        if (!IsInstalled)
        {
            return false;
        }

        if (!IsAppRegistered())
        {
            return true; // Already unregistered
        }

        string output = RunCommand($"--app-unreg \"{_appPath}\"");
        return output.Contains("OK") || string.IsNullOrEmpty(output.Trim());
    }

    public bool IsDeviceHidden(string deviceInstanceId)
    {
        if (!IsInstalled)
        {
            return false;
        }

        string output = RunCommand("--dev-list");
        return output.Contains(deviceInstanceId, StringComparison.OrdinalIgnoreCase);
    }

    public bool HideDevice(string deviceInstanceId)
    {
        if (!IsInstalled)
        {
            return false;
        }

        // First register the app if it's not already registered
        if (!IsAppRegistered())
        {
            RegisterApp();
        }

        string output = RunCommand($"--dev-hide \"{deviceInstanceId}\"");
        return output.Contains("OK") || string.IsNullOrEmpty(output.Trim());
    }

    public bool UnhideDevice(string deviceInstanceId)
    {
        if (!IsInstalled)
        {
            return false;
        }

        string output = RunCommand($"--dev-unhide \"{deviceInstanceId}\"");
        return output.Contains("OK") || string.IsNullOrEmpty(output.Trim());
    }

    public bool SetCloakingState(bool active)
    {
        if (!IsInstalled)
        {
            return false;
        }

        string command = active ? "--cloak-on" : "--cloak-off";
        string output = RunCommand(command);
        return output.Contains("OK") || string.IsNullOrEmpty(output.Trim());
    }

    public bool IsCloakingActive()
    {
        if (!IsInstalled)
        {
            return false;
        }

        string output = RunCommand("--cloak-state");
        return output.Contains("active", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the device instance ID for a DualSense controller using its MAC address
    /// </summary>
    /// <param name="macAddress">The MAC address of the controller</param>
    /// <returns>The device instance ID if found, null otherwise</returns>
    public string? FindDeviceInstanceIdByMacAddress(string macAddress)
    {
        try
        {
            // Clean up the MAC address format (remove colons, spaces, etc.)
            string cleanMac = Regex.Replace(macAddress, @"[^0-9A-Fa-f]", "").ToUpper();

            // Use the --dev-gaming command to get detailed device information
            string output = RunCommand("--dev-gaming");

            if (string.IsNullOrEmpty(output))
            {
                Logger.Warning<HidHideService>("Could not get gaming device list from HidHideCLI");
                return null;
            }

            // Parse the JSON output to find DualSense controllers
            List<Dictionary<string, object>> devices = ParseGamingDevicesJson(output);

            foreach (Dictionary<string, object> device in devices)
            {
                // Check if this is a DualSense device and its MAC address matches
                if (device.ContainsKey("description") &&
                    device["description"].ToString()?.Contains("DualSense") == true &&
                    device.TryGetValue("deviceInstancePath", out object? value))
                {
                    string? deviceInstancePath = value.ToString();

                    // If the device instance path contains the MAC address pattern, return it
                    if (deviceInstancePath != null &&
                        (deviceInstancePath.Contains(cleanMac, StringComparison.OrdinalIgnoreCase) ||
                         macAddress.Replace(":", "").Contains(cleanMac, StringComparison.OrdinalIgnoreCase)))
                    {
                        return deviceInstancePath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error finding device instance ID for MAC {macAddress}: {ex.Message}";
            Logger.Error<HidHideService>(errorMsg);
            Logger.LogExceptionDetails<HidHideService>(ex, includeEnvironmentInfo: false);
        }

        return null;
    }

    /// <summary>
    /// Parses the JSON output from --dev-gaming command to extract device information
    /// </summary>
    /// <param name="json">JSON output from --dev-gaming command</param>
    /// <returns>List of device dictionaries</returns>
    private List<Dictionary<string, object>> ParseGamingDevicesJson(string json)
    {
        List<Dictionary<string, object>> devices = new List<Dictionary<string, object>>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            // The --dev-gaming output is an array of objects
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in root.EnumerateArray())
                {
                    // Each item might have a "friendlyName" and a "devices" array
                    if (item.TryGetProperty("friendlyName", out JsonElement friendlyNameElement) && item.TryGetProperty("devices", out JsonElement devicesElement))
                    {
                        string friendlyName = friendlyNameElement.GetString() ?? string.Empty;

                        // Only process if the friendly name contains "DualSense"
                        if (friendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
                        {
                            if (devicesElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement device in devicesElement.EnumerateArray())
                                {
                                    if (device.TryGetProperty("deviceInstancePath", out JsonElement pathElement))
                                    {
                                        string deviceInstancePath = pathElement.GetString() ?? string.Empty;

                                        Dictionary<string, object?> deviceInfo = new Dictionary<string, object?>
                                        {
                                            ["deviceInstancePath"] = deviceInstancePath,
                                            ["description"] = friendlyName,
                                            ["present"] = device.TryGetProperty("present", out JsonElement presentElement) ? presentElement.GetBoolean() : false,
                                            ["product"] = device.TryGetProperty("product", out JsonElement productElement) ? productElement.GetString() : string.Empty,
                                            ["vendor"] = device.TryGetProperty("vendor", out JsonElement vendorElement) ? vendorElement.GetString() : string.Empty
                                        };

                                        devices.Add(deviceInfo!);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            string errorMsg = $"JSON parsing error: {ex.Message}";
            Logger.Error<HidHideService>(errorMsg);
            Logger.LogExceptionDetails<HidHideService>(ex, includeEnvironmentInfo: false);
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error parsing gaming devices JSON: {ex.Message}";
            Logger.Error<HidHideService>(errorMsg);
            Logger.LogExceptionDetails<HidHideService>(ex, includeEnvironmentInfo: false);
        }

        return devices;
    }

    private string ExtractInstanceId(string deviceId)
    {
        // Device IDs might have extra qualifiers like &0, &1, etc. at the end
        // We want just the main part of the instance ID
        int lastAnd = deviceId.LastIndexOf('&');
        if (lastAnd > 0)
        {
            // Check if what follows is a number (like &0, &1, etc.)
            string suffix = deviceId.Substring(lastAnd + 1);
            if (int.TryParse(suffix, out _))
            {
                return deviceId.Substring(0, lastAnd);
            }
        }
        return deviceId;
    }

    private string RunCommand(string arguments)
    {
        if (string.IsNullOrEmpty(_cliPath))
        {
            return string.Empty;
        }
        if (!File.Exists(_cliPath))
        {
            return string.Empty;
        }

        try
        {
            using Process process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // Check if the exit code indicates a permissions error
            if (process.ExitCode == 5) // ERROR_ACCESS_DENIED = 5
            {
                string errorMsg = $"Error: Access denied. Try running the application as Administrator. Details: {error}";
                Logger.Warning<HidHideService>(errorMsg);
                return errorMsg;
            }

            // Return output if no error, otherwise return error
            return string.IsNullOrEmpty(error) ? output : error;
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error running HidHideCLI command '{arguments}': {ex.Message}";
            Logger.Error<HidHideService>(errorMsg);
            Logger.LogExceptionDetails<HidHideService>(ex, includeEnvironmentInfo: false);
            return $"Error: {ex.Message}";
        }
    }
}