using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Core.Foreground;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.GUI.Services;

/// <summary>
/// Polls the focused program and temporarily applies the matching auto profile rule
/// (profile + emulation mode) to every tracked controller. Stored bindings are never
/// modified: when no rule matches, the controller's bound profile and stored emulation
/// settings are re-applied. Resolved eagerly at startup (see
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
    /// The effective auto state per controller: applied profile name and emulation mode
    /// override (<c>null</c> mode means no override). Compared every tick so rule edits
    /// apply while focus is unchanged.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, (string ProfileName, EmulationMode? Mode)> _applied =
        new Dictionary<DualSenseDevice, (string, EmulationMode?)>();

    /// <summary>
    /// Whether the coordinator was disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates the coordinator and starts polling the focused program on supported platforms.
    /// </summary>
    public AutoProfileCoordinator(IControllerTracker tracker, AutoProfileService rules, ControllerInfoService controllers,
        ProfileService profiles, IEmulationService emulation, IForegroundAppProvider foreground, INotificationPopupService popups)
    {
        _tracker = tracker;
        _rules = rules;
        _controllers = controllers;
        _profiles = profiles;
        _emulation = emulation;
        _foreground = foreground;
        _popups = popups;

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

                if (_applied.TryGetValue(device, out (string ProfileName, EmulationMode? Mode) applied)
                    && string.Equals(applied.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                    && applied.Mode == mode)
                {
                    continue;
                }

                bool hadPrevious = _applied.TryGetValue(device, out (string ProfileName, EmulationMode? Mode) previous);
                EmulationMode? previousMode = hadPrevious ? previous.Mode : null;
                bool profileChanged = hadPrevious
                                      && !string.Equals(previous.ProfileName, profileName, StringComparison.OrdinalIgnoreCase);
                ApplyRule(device, profileName, mode, profileChanged);
                _applied[device] = (profileName, mode);
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
    /// Stops polling. Temporary emulation overrides die with the emulation service;
    /// stored settings were never modified.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer?.Dispose();
    }
}