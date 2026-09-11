using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Core.Foreground;
using DualSenseClient.HidHide;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Polls the focused program and temporarily applies the matching auto profile rule
/// (profile + emulation mode + hiding) to every tracked controller. Stored bindings are never
/// modified: when no rule matches, the controller's bound profile and stored emulation
/// settings are re-applied and the pre-rule hidden state is restored. Resolved eagerly at startup (see
/// <see cref="Controls.AppSplashScreen"/>) so it runs for the app's lifetime.
/// </summary>
public sealed class AutoProfileCoordinator : IDisposable
{
    /// <summary>
    /// How often the focused program is re-evaluated (same cadence as DS4Windows).
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("AutoProfiles");

    /// <summary>
    /// Tracks the connected controllers.
    /// </summary>
    private readonly IControllerTracker _tracker;

    /// <summary>
    /// Stores the foreground-app auto profile rules.
    /// </summary>
    private readonly AutoProfileService _rules;

    /// <summary>
    /// Resolves the profile bound to a controller for reverting.
    /// </summary>
    private readonly ControllerInfoService _controllers;

    /// <summary>
    /// Stores the controller profiles.
    /// </summary>
    private readonly ProfileService _profiles;

    /// <summary>
    /// Applies temporary emulation mode overrides.
    /// </summary>
    private readonly IEmulationService _emulation;

    /// <summary>
    /// Hides or unhides physical controllers per rule.
    /// </summary>
    private readonly IControllerHidingService _hiding;

    /// <summary>
    /// Shows a popup when the applied profile changes.
    /// </summary>
    private readonly INotificationPopupService _popups;

    /// <summary>
    /// Resolves the program currently in focus.
    /// </summary>
    private readonly IForegroundAppProvider _foreground;

    /// <summary>
    /// Polls the focused program, or <c>null</c> on unsupported platforms.
    /// </summary>
    private readonly Timer? _timer;

    /// <summary>
    /// Guards <see cref="_applied"/> and serializes ticks.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The effective auto state per controller: applied profile name, emulation mode
    /// override (<c>null</c> mode means no override), and hiding override (<c>null</c>
    /// means no override). Compared every tick so rule edits apply while focus is unchanged.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, (string ProfileName, EmulationMode? Mode, bool? Hide)> _applied =
        new Dictionary<DualSenseDevice, (string, EmulationMode?, bool?)>();

    /// <summary>
    /// The hidden state per controller before the hiding override was applied,
    /// restored when no hiding rule matches anymore. Only present while overridden.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, bool> _hideBaseline = new Dictionary<DualSenseDevice, bool>();

    /// <summary>
    /// Whether the coordinator was disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates the coordinator and starts polling the focused program on supported platforms.
    /// </summary>
    public AutoProfileCoordinator(IControllerTracker tracker, AutoProfileService rules, ControllerInfoService controllers,
        ProfileService profiles, IEmulationService emulation, IForegroundAppProvider foreground, INotificationPopupService popups,
        IControllerHidingService hiding)
    {
        _tracker = tracker;
        _rules = rules;
        _controllers = controllers;
        _profiles = profiles;
        _emulation = emulation;
        _foreground = foreground;
        _popups = popups;
        _hiding = hiding;

        if (_foreground.IsSupported)
        {
            // One-shot rescheduling (not periodic): a slow tick from USB stalls
            // must never pile overlapping polls onto the thread pool.
            _timer = new Timer(OnTick, null, PollInterval, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _log.Debug("Foreground-app switching is not supported on this platform; auto profiles stay idle");
        }
    }

    /// <summary>
    /// Evaluates the focused program against the rules and applies the match per
    /// controller. Never throws: timer callbacks must not take down the process.
    /// Reschedules the next poll so ticks never overlap.
    /// </summary>
    private void OnTick(object? state)
    {
        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            _log.Error("Auto profile poll failed");
            _log.LogExceptionDetails(ex);
        }
        finally
        {
            try
            {
                _timer?.Change(PollInterval, Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Shut down while polling; nothing left to schedule.
            }
        }
    }

    /// <summary>
    /// Runs one poll: resolves the foreground app once, then applies or reverts the
    /// matching rule per tracked controller. Runs on a timer thread.
    /// </summary>
    private void Poll()
    {
        if (_disposed || !_foreground.IsSupported)
        {
            return;
        }

        ForegroundApp? app = _rules.Settings.Enabled ? _foreground.GetForegroundApp() : null;
        List<DualSenseDevice> devices = _tracker.Controllers.OfType<DualSenseDevice>().ToList();
        HashSet<DualSenseDevice> current = devices.ToHashSet();

        bool emulationChanged = false;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (DualSenseDevice stale in _applied.Keys.Where(device => !current.Contains(device)).ToList())
            {
                RestoreHiding(stale);
                _applied.Remove(stale);
            }

            foreach (DualSenseDevice device in devices)
            {
                string? mac = device.PairingInfo?.ClientMac;
                string? path = device.Info.Path;
                AutoProfileRule? rule = app is { } foreground
                    ? _rules.FindMatch(foreground.ExePath, foreground.WindowTitle, mac, path)
                    : null;

                string profileName = !string.IsNullOrEmpty(rule?.ProfileName)
                    ? rule.ProfileName
                    : _controllers.GetBoundProfileName(mac, path) ?? ProfileService.DefaultProfileName;
                EmulationMode? mode = rule?.EmulationMode;
                bool? hide = rule?.HideController;

                if (_applied.TryGetValue(device, out (string ProfileName, EmulationMode? Mode, bool? Hide) applied)
                    && string.Equals(applied.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                    && applied.Mode == mode
                    && applied.Hide == hide)
                {
                    continue;
                }

                bool hadPrevious = _applied.TryGetValue(device, out (string ProfileName, EmulationMode? Mode, bool? Hide) previous);
                EmulationMode? previousMode = hadPrevious ? previous.Mode : null;
                bool profileChanged = hadPrevious
                                      && !string.Equals(previous.ProfileName, profileName, StringComparison.OrdinalIgnoreCase);
                ApplyRule(device, profileName, mode, profileChanged);
                ApplyHiding(device, hide);
                _applied[device] = (profileName, mode, hide);
                if (mode != previousMode)
                {
                    emulationChanged = true;
                }
            }
        }

        if (emulationChanged)
        {
            _emulation.Refresh();
        }
    }

    /// <summary>
    /// Applies a profile and a temporary emulation mode override to a controller.
    /// Unknown profile names are skipped (the previous lights stay). Shows a popup when
    /// the profile changed. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void ApplyRule(DualSenseDevice device, string profileName, EmulationMode? mode, bool notify)
    {
        Profile? profile = _profiles.GetProfile(profileName);
        if (profile is not null)
        {
            _log.Info($"Applying auto profile '{profile.Name}' to {device.Info.ProductName}");
            device.ApplyProfile(profile);
            if (notify)
            {
                string displayName = _controllers.GetDisplayName(device.PairingInfo?.ClientMac, device.Info.Path,
                    device.Info.ProductName);
                string title = LocalizationService.GetText("Notification.AutoProfile.Title");
                string message = string.Format(LocalizationService.GetText("Notification.AutoProfile.Message"),
                    displayName, profile.Name);
                _popups.ShowMessage(title, message);
            }
        }
        else
        {
            _log.Warning($"Auto profile '{profileName}' not found; leaving lights unchanged");
        }

        _emulation.SetTemporaryEmulationMode(device, mode);
    }

    /// <summary>
    /// Applies a hiding override to a controller, capturing the pre-rule hidden state
    /// for later restore. A <c>null</c> override restores the captured state.
    /// Unavailable backends and unresolvable devices are no-ops. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void ApplyHiding(DualSenseDevice device, bool? hide)
    {
        if (!hide.HasValue)
        {
            RestoreHiding(device);
            return;
        }

        if (!TryGetInstanceId(device, out string instanceId))
        {
            return;
        }

        if (!_hideBaseline.ContainsKey(device))
        {
            _hideBaseline[device] = _hiding.IsControllerHidden(instanceId);
        }

        if (_hiding.IsControllerHidden(instanceId) != hide.Value)
        {
            _log.Info($"{(hide.Value ? "Hiding" : "Unhiding")} {device.Info.ProductName} via auto profile");
            _hiding.SetControllerHidden(instanceId, hide.Value);
        }
    }

    /// <summary>
    /// Restores the pre-rule hidden state captured by <see cref="ApplyHiding"/>.
    /// No-op when the controller was never overridden. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void RestoreHiding(DualSenseDevice device)
    {
        if (!_hideBaseline.TryGetValue(device, out bool baseline))
        {
            return;
        }

        _hideBaseline.Remove(device);
        if (TryGetInstanceId(device, out string instanceId) && _hiding.IsControllerHidden(instanceId) != baseline)
        {
            _log.Info($"Restoring hidden state for {device.Info.ProductName} via auto profile");
            _hiding.SetControllerHidden(instanceId, baseline);
        }
    }

    /// <summary>
    /// Resolves the controller's HID path to the hiding backend identifier.
    /// Returns <c>false</c> when hiding is unavailable or the path cannot be resolved.
    /// </summary>
    private bool TryGetInstanceId(DualSenseDevice device, out string instanceId)
    {
        instanceId = string.Empty;
        if (!_hiding.IsAvailable)
        {
            return false;
        }

        string? path = device.Info.Path;
        return !string.IsNullOrEmpty(path) && _hiding.TryGetInstanceId(path, out instanceId);
    }

    /// <summary>
    /// Stops polling. Temporary emulation overrides die with the emulation service;
    /// stored settings were never modified. Outstanding hiding overrides are restored.
    /// </summary>
    public void Dispose()
    {
        List<(DualSenseDevice Device, bool Baseline)> toRestore;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            toRestore = _hideBaseline.Select(pair => (pair.Key, pair.Value)).ToList();
            _hideBaseline.Clear();
        }

        foreach ((DualSenseDevice device, bool baseline) in toRestore)
        {
            try
            {
                if (TryGetInstanceId(device, out string instanceId) && _hiding.IsControllerHidden(instanceId) != baseline)
                {
                    _hiding.SetControllerHidden(instanceId, baseline);
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"Failed to restore hidden state on dispose: {ex.Message}");
            }
        }

        _timer?.Dispose();
    }
}