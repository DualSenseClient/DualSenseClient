using DualSenseClient.Logging;
using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace DualSenseClient.HidHide;

/// <summary>
/// Windows implementation of <see cref="IControllerHidingService"/> backed by the
/// HidHide driver. Hiding a controller automatically enables the driver's global
/// hiding state; unhiding the last hidden controller disables it again, so a single
/// per-controller toggle fully controls the driver.
/// </summary>
/// <remarks>
/// All members are safe to call on any platform: when not running on Windows, or when
/// the driver is not installed, queries return <c>false</c>/empty and mutations are
/// no-ops. Failures are logged via <see cref="DualSenseClientLogger"/>.
/// </remarks>
public sealed class HidHideService : IControllerHidingService
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("HidHide");

    /// <summary>
    /// The underlying driver wrapper.
    /// </summary>
    private readonly HidHideControlService _driver = new();

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                return _driver.IsOperational;
            }
            catch (Exception ex)
            {
                _log.Debug($"HidHide availability check failed: {ex.Message}");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public bool IsControllerHidden(string instanceId)
    {
        try
        {
            foreach (string id in _driver.BlockedInstanceIds)
            {
                if (string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to read HidHide blacklist: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void SetControllerHidden(string instanceId, bool hidden)
    {
        if (!IsAvailable)
        {
            return;
        }

        if (hidden == IsControllerHidden(instanceId))
        {
            return;
        }

        try
        {
            if (hidden)
            {
                // Hiding only takes effect while the driver's global hiding is on,
                // so enable it when the first controller gets hidden.
                if (!_driver.IsActive)
                {
                    _driver.IsActive = true;
                    _log.Info("Enabled HidHide device hiding");
                }

                _driver.AddBlockedInstanceId(instanceId);
                _log.Info($"Hidden device: {instanceId}");
            }
            else
            {
                _driver.RemoveBlockedInstanceId(instanceId);
                _log.Info($"Unhidden device: {instanceId}");

                // Nothing hidden anymore, so global hiding is pointless: turn it off.
                if (_driver.BlockedInstanceIds.Count == 0 && _driver.IsActive)
                {
                    _driver.IsActive = false;
                    _log.Info("Disabled HidHide device hiding");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to update HidHide blacklist: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void EnsureSelfVisible()
    {
        if (!IsAvailable || Environment.ProcessPath is not { } exePath)
        {
            return;
        }

        if (IsAppWhitelisted)
        {
            _log.Debug("App is already on the HidHide whitelist");
            return;
        }

        _log.Info($"App is not on the HidHide whitelist, adding {exePath}");
        SetAppWhitelisted(true);
    }

    /// <inheritdoc />
    public bool TryGetInstanceId(string devicePath, out string instanceId)
    {
        instanceId = string.Empty;
        if (string.IsNullOrEmpty(devicePath))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                string? converted = PnPDevice.GetInstanceIdFromInterfaceId(devicePath);
                if (!string.IsNullOrEmpty(converted))
                {
                    instanceId = converted;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"SetupAPI instance ID conversion failed for '{devicePath}': {ex.Message}");
            }
        }

        string path = devicePath;
        const string prefix = @"\\?\";
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            path = path[prefix.Length..];
        }

        // Strip the trailing interface GUID segment, e.g. "#{4d1e55b2-...}".
        int guidSeparator = path.IndexOf("#{", StringComparison.Ordinal);
        if (guidSeparator > 0)
        {
            path = path[..guidSeparator];
        }

        path = path.Replace('#', '\\');
        if (path.Length == 0)
        {
            return false;
        }

        instanceId = path;
        return true;
    }

    /// <summary>
    /// Whether this application's executable is on the HidHide whitelist, i.e. allowed
    /// to see hidden devices. The whitelist comparison is case-insensitive.
    /// </summary>
    private bool IsAppWhitelisted
    {
        get
        {
            if (!IsAvailable || Environment.ProcessPath is not { } exePath)
            {
                return false;
            }

            try
            {
                foreach (string path in _driver.ApplicationPaths)
                {
                    if (string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to read HidHide whitelist: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Adds or removes this application's executable from the HidHide whitelist.
    /// </summary>
    private void SetAppWhitelisted(bool whitelisted)
    {
        if (!IsAvailable || Environment.ProcessPath is not { } exePath)
        {
            return;
        }

        if (whitelisted == IsAppWhitelisted)
        {
            return;
        }

        try
        {
            if (whitelisted)
            {
                _driver.AddApplicationPath(exePath);
                _log.Info($"Added app to HidHide whitelist: {exePath}");
            }
            else
            {
                _driver.RemoveApplicationPath(exePath);
                _log.Info($"Removed app from HidHide whitelist: {exePath}");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to update HidHide whitelist: {ex.Message}");
        }
    }
}