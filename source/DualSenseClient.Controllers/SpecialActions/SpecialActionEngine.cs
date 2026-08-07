using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.SpecialActions;

/// <summary>
/// Provides the action that was executed when a special action fires.
/// </summary>
public sealed class SpecialActionExecutedEventArgs : EventArgs
{
    /// <summary>
    /// The action that was executed.
    /// </summary>
    public SpecialAction Action { get; }

    /// <summary>
    /// Creates a new event args instance.
    /// </summary>
    /// <param name="action">The action that was executed.</param>
    public SpecialActionExecutedEventArgs(SpecialAction action)
    {
        Action = action;
    }
}

/// <summary>
/// Watches a controller's button events and executes special actions when the user holds an
/// exact button combination: the action fires once the moment the held set equals the
/// combination (extra buttons held prevent it from firing), and re-arms only after at least
/// one combination button is released.
/// </summary>
/// <remarks>
/// <para>
/// An action can declare a hold duration (<see cref="SpecialAction.HoldTimeMs"/>): the exact
/// combination must then be held continuously for that long before the action fires, and
/// interrupting it (pressing an extra button or releasing a combination button) resets the
/// timer. Light actions can also be marked <see cref="SpecialAction.ApplyWhileHeld"/>: they
/// are applied while the combination is held and the controller reverts to its bound profile
/// (see <see cref="ProfileProvider"/>) once a combination button is released. Alternatively
/// they can carry a duration (<see cref="SpecialAction.DurationMs"/>): they stay applied for
/// that long and the controller then reverts to its bound profile automatically, whether or
/// not the combination is still held. Sound actions
/// play an audio file through the controller speaker (see <see cref="SoundPlayerFactory"/>);
/// marked <see cref="SpecialAction.ApplyWhileHeld"/>, the sound plays while the combination
/// is held and stops on release.
/// </para>
/// <para>
/// Only actions enabled for the attached controller (see
/// <see cref="SpecialAction.EnabledControllers"/>) are evaluated. The configuration is
/// provided as a plain list via <see cref="UpdateActions"/> so the engine stays decoupled
/// from persistence; callers re-supply it whenever the settings change.
/// </para>
/// <para>
/// Button events fire on the controller's read-loop thread, while the hold-duration timer
/// runs on a thread-pool thread; all matching state is guarded by a lock.
/// </para>
/// </remarks>
public sealed class SpecialActionEngine : IDisposable
{
    /// <summary>
    /// How often the hold-duration timer checks for expired holds.
    /// </summary>
    private static readonly TimeSpan HoldCheckInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Upper bound for a hold duration.
    /// </summary>
    public const int MaxHoldTimeMs = 10000;

    /// <summary>
    /// Upper bound for how long a light effect stays applied before the profile is restored.
    /// </summary>
    public const int MaxDurationMs = 60000;

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("SpecialActions");

    /// <summary>
    /// Guards all mutable matching state: button events come from the device's read-loop
    /// thread, while <see cref="_timer"/> ticks on a thread-pool thread.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Checks pending holds on the thread pool; fires actions whose hold duration elapsed.
    /// </summary>
    private readonly Timer _timer;

    /// <summary>
    /// The controller currently being watched, or <c>null</c> when detached.
    /// </summary>
    private DualSenseDevice? _device;

    /// <summary>
    /// Identifier of the attached controller used to filter enabled actions.
    /// </summary>
    private string? _controllerId;

    /// <summary>
    /// The actions to evaluate.
    /// </summary>
    private IReadOnlyList<SpecialAction> _actions = [];

    /// <summary>
    /// The buttons currently held on the attached controller.
    /// </summary>
    private readonly HashSet<ButtonType> _held = new HashSet<ButtonType>();

    /// <summary>
    /// Actions that have already fired for the current hold, re-armed on release.
    /// </summary>
    private readonly HashSet<Guid> _fired = new HashSet<Guid>();

    /// <summary>
    /// Actions whose hold duration has not elapsed yet, keyed by action id and mapped to
    /// their fire deadline.
    /// </summary>
    private readonly Dictionary<Guid, DateTime> _pending = new Dictionary<Guid, DateTime>();

    /// <summary>
    /// While-held light actions that are currently applied (waiting for a release to revert).
    /// </summary>
    private readonly HashSet<Guid> _sustainedActive = new HashSet<Guid>();

    /// <summary>
    /// Timed light actions that are currently applied, keyed by action id and mapped to
    /// their restore deadline (after which the bound profile is re-applied).
    /// </summary>
    private readonly Dictionary<Guid, DateTime> _timedActive = new Dictionary<Guid, DateTime>();

    /// <summary>
    /// The player used by <see cref="SpecialActionTypes.PlaySound"/> actions, created lazily
    /// from <see cref="SoundPlayerFactory"/> on the first sound trigger and released on
    /// detach.
    /// </summary>
    private ISpecialActionSoundPlayer? _soundPlayer;

    /// <summary>
    /// Raised after an action has been executed.
    /// </summary>
    public event EventHandler<SpecialActionExecutedEventArgs>? ActionExecuted;

    /// <summary>
    /// Resolves the profile to revert to when a while-held light action ends. When this is
    /// <c>null</c>, or returns <c>null</c>, the lights keep whatever the action set.
    /// </summary>
    public Func<DualSenseDevice, Profile?>? ProfileProvider { get; set; }

    /// <summary>
    /// Creates the player used by <see cref="SpecialActionTypes.PlaySound"/> actions for the
    /// attached controller. When <c>null</c>, sound actions log a warning and do nothing.
    /// </summary>
    public Func<DualSenseDevice, ISpecialActionSoundPlayer>? SoundPlayerFactory { get; set; }

    /// <summary>
    /// Creates a new engine instance and starts its hold-duration timer.
    /// </summary>
    public SpecialActionEngine()
    {
        _timer = new Timer(_ => CheckPendingHolds(), null, HoldCheckInterval, HoldCheckInterval);
    }

    /// <summary>
    /// Attaches the engine to a controller: subscribes to its button events and evaluates
    /// the configured actions. Detaches any previously attached controller first.
    /// </summary>
    /// <param name="device">The controller to watch.</param>
    public void Attach(DualSenseDevice device)
    {
        lock (_lock)
        {
            DetachCore();

            _device = device;
            _controllerId = SpecialActionService.GetControllerId(device.PairingInfo?.ClientMac, device.Info.Path);
            _held.Clear();
            _fired.Clear();
            _pending.Clear();
            _sustainedActive.Clear();
            _timedActive.Clear();

            device.ButtonPressed += OnButtonPressed;
            device.ButtonReleased += OnButtonReleased;
            _log.Debug($"Special actions attached to {device.Info.ProductName} (controller id: {_controllerId ?? "unknown"})");
        }
    }

    /// <summary>
    /// Detaches the engine from the current controller and resets its matching state.
    /// </summary>
    public void Detach()
    {
        lock (_lock)
        {
            DetachCore();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.Dispose();
        Detach();
    }

    /// <summary>
    /// Replaces the action configuration. Called on startup and whenever the settings change.
    /// </summary>
    /// <param name="actions">The actions to evaluate for the attached controller.</param>
    public void UpdateActions(IReadOnlyList<SpecialAction> actions)
    {
        lock (_lock)
        {
            // A config change is a deliberate user action: re-evaluate holds from scratch.
            // The sustained set is checked against the previous configuration, since the
            // new list is what will be evaluated next.
            bool hadSustainedSound = _sustainedActive.Count > 0
                                     && _actions.Any(a => _sustainedActive.Contains(a.Id)
                                                          && a.Effects.Any(e => e.Enabled
                                                                                && e.Type == SpecialActionTypes.PlaySound));
            // Snapshot the list: the caller passes the live settings collection, which the
            // UI thread keeps mutating (create/delete/import). Evaluating a stale snapshot
            // is safe and prevents a modified-during-enumeration exception on the read-loop
            // thread (which would kill the controller's input loop).
            _actions = (actions ?? []).ToList();
            _pending.Clear();
            _fired.Clear();
            if (_sustainedActive.Count > 0)
            {
                _sustainedActive.Clear();
                RestoreBaseState();
                if (hadSustainedSound)
                {
                    StopSound();
                }
            }

            if (_timedActive.Count > 0)
            {
                _timedActive.Clear();
                RestoreBaseState();
            }
        }
    }

    /// <summary>
    /// Tracks a newly pressed button and evaluates the combinations.
    /// </summary>
    private void OnButtonPressed(object? sender, ButtonEventArgs e)
    {
        lock (_lock)
        {
            _held.Add(e.Button);
            EvaluateCombos();
        }
    }

    /// <summary>
    /// Tracks a released button and evaluates the combinations (a release can complete an
    /// exact combination that was previously overfull, and re-arms broken combinations).
    /// </summary>
    private void OnButtonReleased(object? sender, ButtonEventArgs e)
    {
        lock (_lock)
        {
            _held.Remove(e.Button);
            EvaluateCombos();
        }
    }

    /// <summary>
    /// Evaluates every action against the current held set:
    /// schedules or fires it when the held set exactly equals the combination, and re-arms
    /// an action when a combination button is released (extra held buttons neither re-arm
    /// nor fire). Any deviation from the exact combination cancels a pending hold, so a
    /// fresh exact hold restarts the hold duration.
    /// </summary>
    private void EvaluateCombos()
    {
        if (_device is null)
        {
            return;
        }

        foreach (SpecialAction action in _actions)
        {
            if (!SpecialActionService.IsEnabledFor(action, _controllerId))
            {
                continue;
            }

            HashSet<ButtonType>? combo = TryParseCombo(action);
            if (combo is null)
            {
                continue;
            }

            if (_held.SetEquals(combo))
            {
                if (_fired.Contains(action.Id) || _sustainedActive.Contains(action.Id))
                {
                    continue;
                }

                int holdMs = Math.Clamp(action.HoldTimeMs, 0, MaxHoldTimeMs);
                if (holdMs > 0)
                {
                    // (Re)start the hold timer; repeated events keep the existing deadline.
                    _pending[action.Id] = DateTime.UtcNow.AddMilliseconds(holdMs);
                }
                else
                {
                    Fire(action);
                }
            }
            else
            {
                // Not the exact combination: the hold is interrupted, if any.
                _pending.Remove(action.Id);

                if (!_held.IsSupersetOf(combo))
                {
                    // A combination button was released: re-arm the action.
                    _fired.Remove(action.Id);
                    if (_sustainedActive.Remove(action.Id))
                    {
                        if (action.Effects.Any(e => e.Enabled && e.Type == SpecialActionTypes.PlaySound))
                        {
                            StopSound();
                        }

                        if (action.Effects.Any(e => e.Enabled
                                                    && e.Type is SpecialActionTypes.SetLightbarColor
                                                        or SpecialActionTypes.SetPlayerLeds
                                                        or SpecialActionTypes.ShowBatteryLevel))
                        {
                            RestoreBaseState();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Fires actions whose hold deadline has passed while the exact combination is still
    /// held, and restores the bound profile for timed light actions whose duration elapsed.
    /// Called by the timer on the thread pool.
    /// </summary>
    private void CheckPendingHolds()
    {
        lock (_lock)
        {
            if (_device is null || (_pending.Count == 0 && _timedActive.Count == 0))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            foreach (Guid id in _pending.Keys.ToList())
            {
                if (now < _pending[id])
                {
                    continue;
                }

                _pending.Remove(id);

                SpecialAction? action = _actions.FirstOrDefault(a => a.Id == id);
                if (action is null)
                {
                    continue;
                }

                HashSet<ButtonType>? combo = TryParseCombo(action);
                if (combo is null || !_held.SetEquals(combo))
                {
                    // The combination was broken before the deadline.
                    continue;
                }

                Fire(action);
            }

            // Timed light actions: restore the bound profile once their duration elapsed.
            foreach (Guid id in _timedActive.Keys.ToList())
            {
                if (now >= _timedActive[id])
                {
                    _timedActive.Remove(id);
                    RestoreBaseState();
                }
            }
        }
    }

    /// <summary>
    /// Parses an action's button names into a set, or <c>null</c> when the combination is
    /// empty or contains an unknown button name (skipped like a disabled action).
    /// </summary>
    private static HashSet<ButtonType>? TryParseCombo(SpecialAction action)
    {
        if (action.Buttons is null || action.Buttons.Count == 0)
        {
            return null;
        }

        HashSet<ButtonType> combo = new HashSet<ButtonType>();
        foreach (string name in action.Buttons)
        {
            if (!Enum.TryParse(name, ignoreCase: true, out ButtonType button))
            {
                return null;
            }

            combo.Add(button);
        }

        return combo;
    }

    /// <summary>
    /// Executes the action on the attached controller.
    /// </summary>
    private void Fire(SpecialAction action)
    {
        if (_device is null)
        {
            return;
        }

        _log.Info($"Executing special action '{action.Name}' ({action.Effects.Count} effect(s))");

        try
        {
            // Skip actions with no usable effects: nothing to execute and no point
            // marking them, so a later edit can fire the next hold immediately.
            if (!action.Effects.Any(e => e.Enabled && IsKnownEffectType(e.Type)))
            {
                _log.Warning($"Special action '{action.Name}' has no usable effects");
                return;
            }

            // The hold state is per action, not per effect: the whole set of effects is
            // one-shot, applied while held, or applied for a duration together.
            if (action.ApplyWhileHeld && action.Effects.Any(e => e.Enabled && IsSustainedEffect(e.Type)))
            {
                _sustainedActive.Add(action.Id);
            }
            else
            {
                _fired.Add(action.Id);

                int durationMs = Math.Clamp(action.DurationMs, 0, MaxDurationMs);
                if (durationMs > 0 && action.Effects.Any(e => e.Enabled && IsTimedEffect(e.Type)))
                {
                    // A repeated fire (re-hold) restarts the duration.
                    _timedActive[action.Id] = DateTime.UtcNow.AddMilliseconds(durationMs);
                }
            }

            foreach (SpecialActionEffect effect in action.Effects)
            {
                if (effect.Enabled)
                {
                    ExecuteEffect(effect);
                }
            }

            ActionExecuted?.Invoke(this, new SpecialActionExecutedEventArgs(action));
        }
        catch (Exception ex)
        {
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Whether an effect type can be executed at all.
    /// </summary>
    private static bool IsKnownEffectType(string type) =>
        type is SpecialActionTypes.Disconnect
            or SpecialActionTypes.SetLightbarColor
            or SpecialActionTypes.SetPlayerLeds
            or SpecialActionTypes.PlaySound
            or SpecialActionTypes.ShowBatteryLevel;

    /// <summary>
    /// Whether an effect type supports apply-while-held behavior (light or sound effects;
    /// disconnect happens once either way).
    /// </summary>
    private static bool IsSustainedEffect(string type) =>
        type is SpecialActionTypes.SetLightbarColor
            or SpecialActionTypes.SetPlayerLeds
            or SpecialActionTypes.PlaySound
            or SpecialActionTypes.ShowBatteryLevel;

    /// <summary>
    /// Whether an effect type supports the timed duration behavior (<see cref="SpecialAction.DurationMs"/>):
    /// the light effects, which can be reverted by re-applying the bound profile.
    /// </summary>
    private static bool IsTimedEffect(string type) =>
        type is SpecialActionTypes.SetLightbarColor
            or SpecialActionTypes.SetPlayerLeds
            or SpecialActionTypes.ShowBatteryLevel;

    /// <summary>
    /// Executes a single effect on the attached controller.
    /// </summary>
    private void ExecuteEffect(SpecialActionEffect effect)
    {
        switch (effect.Type)
        {
            case SpecialActionTypes.Disconnect:
                _device!.DisconnectController();
                break;
            case SpecialActionTypes.SetLightbarColor:
                SendLightbarColor(effect.Red, effect.Green, effect.Blue);
                break;
            case SpecialActionTypes.SetPlayerLeds:
                SendPlayerLeds(effect.PlayerLedMask);
                break;
            case SpecialActionTypes.PlaySound:
                PlaySound(effect);
                break;
            case SpecialActionTypes.ShowBatteryLevel:
                ShowBatteryLevel(effect);
                break;
            default:
                _log.Warning($"Unknown special action effect type '{effect.Type}'");
                break;
        }
    }

    /// <summary>
    /// Reverts a while-held or timed light action by re-applying the profile resolved via
    /// <see cref="ProfileProvider"/> (the protocol cannot read the current light state back,
    /// so the bound profile is the source of truth). No-op when no provider is set or it
    /// resolves to <c>null</c>.
    /// </summary>
    private void RestoreBaseState()
    {
        if (_device is null)
        {
            return;
        }

        Profile? profile = ProfileProvider?.Invoke(_device);
        if (profile is null)
        {
            return;
        }

        _log.Debug("Restoring bound profile after special action");
        try
        {
            _device.ApplyProfile(profile);
        }
        catch (Exception ex)
        {
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Sets the lightbar color, including the fade animation setup byte needed to take
    /// over the lightbar from the controller's default state (mirrors
    /// <see cref="DualSenseDevice.ApplyProfile"/>).
    /// </summary>
    private void SendLightbarColor(byte red, byte green, byte blue)
    {
        SetStateData payload = new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowLedColor,
            ValidFlag2 = ValidFlags.AllowColorFadeAnim,
            LightFadeAnimation = 0x02,
            LedRed = red,
            LedGreen = green,
            LedBlue = blue
        };

        _device!.SendOutputState(payload);
    }

    /// <summary>
    /// Sets the player LED layout from the raw byte mask (bit 0 = LED 1, ... bit 4 = LED 5).
    /// </summary>
    private void SendPlayerLeds(byte mask)
    {
        SetStateData payload = new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowPlayerIndicators,
            PlayerLeds = (PlayerLedMask)mask
        };

        _device!.SendOutputState(payload);
    }

    /// <summary>
    /// Shows the controller's current battery charge on the lightbar: the level is derived
    /// from the latest reported battery percentage (10 levels, level 0 = lowest charge) and
    /// the lightbar is set to that level's color (custom colors, or the effect defaults).
    /// An unknown battery level is logged and skipped, so the lightbar is never corrupted.
    /// </summary>
    private void ShowBatteryLevel(SpecialActionEffect effect)
    {
        int percentage = _device!.InputReport.Battery.DisplayPercentage;
        if (percentage < 0)
        {
            _log.Warning("Battery level unknown; battery special action skipped");
            return;
        }

        int level = Math.Min(percentage / 10, 9);
        BatteryLevelColor color = effect.GetBatteryColor(level);
        _log.Debug($"Showing battery level {level} ({percentage}%) with color {color.Red},{color.Green},{color.Blue}");
        SendLightbarColor(color.Red, color.Green, color.Blue);
    }

    /// <summary>
    /// Starts playing the sound effect's file through the controller speaker, creating the
    /// player from <see cref="SoundPlayerFactory"/> on first use. Missing files and a
    /// missing factory are logged and ignored, so a broken sound effect never disturbs
    /// matching.
    /// </summary>
    private void PlaySound(SpecialActionEffect effect)
    {
        if (string.IsNullOrEmpty(effect.SoundPath))
        {
            _log.Warning("Special action sound effect has no sound file selected");
            return;
        }

        if (_soundPlayer is null)
        {
            if (_device is null || SoundPlayerFactory is null)
            {
                _log.Warning("Sound player unavailable; sound special action skipped");
                return;
            }

            _soundPlayer = SoundPlayerFactory(_device);
        }

        _soundPlayer.Play(
            effect.SoundPath,
            string.Equals(effect.SoundOutputDevice, SoundOutputDevices.Headset, StringComparison.OrdinalIgnoreCase)
                ? SoundOutputTarget.Headset
                : SoundOutputTarget.Speaker,
            effect.SoundVolume,
            effect.HapticFeedback,
            effect.HapticStrength);
    }

    /// <summary>
    /// Stops the sound playing for a while-held sound action.
    /// </summary>
    private void StopSound()
    {
        _log.Debug("Stopping sound after while-held special action");
        try
        {
            _soundPlayer?.Stop();
        }
        catch (Exception ex)
        {
            _log.LogExceptionDetails(ex);
        }
    }

    /// <summary>
    /// Unsubscribes from the attached controller and resets all matching state. Callers
    /// must hold <see cref="_lock"/>.
    /// </summary>
    private void DetachCore()
    {
        if (_device is not null)
        {
            _device.ButtonPressed -= OnButtonPressed;
            _device.ButtonReleased -= OnButtonReleased;
        }

        _device = null;
        _controllerId = null;
        _held.Clear();
        _fired.Clear();
        _pending.Clear();
        _sustainedActive.Clear();
        _timedActive.Clear();

        _soundPlayer?.Dispose();
        _soundPlayer = null;
    }
}