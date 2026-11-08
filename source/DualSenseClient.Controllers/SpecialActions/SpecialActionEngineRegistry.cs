using DualSenseClient.Controllers.Devices;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.SpecialActions;

/// <summary>
/// Owns one <see cref="SpecialActionEngine"/> per connected controller, shared between
/// the application coordinator (which attaches all tracked controllers) and the emulation
/// output path (which reads each device's active output overrides). A single engine per
/// device keeps the button/touchpad subscriptions and matching state isolated, so actions
/// on one controller never affect another.
/// </summary>
public sealed class SpecialActionEngineRegistry : IDisposable
{
    /// <summary>
    /// Guards access to <see cref="_engines"/>, <see cref="_actions"/> and
    /// <see cref="_touchpadActionsSuspended"/>.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// The engine per attached controller.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, SpecialActionEngine> _engines = new Dictionary<DualSenseDevice, SpecialActionEngine>();

    /// <summary>
    /// The controllers whose touchpad-triggered special actions are suspended because the
    /// touchpad mouse feature owns their touchpad. Remembered for controllers whose engine
    /// does not exist yet.
    /// </summary>
    private readonly HashSet<DualSenseDevice> _touchpadActionsSuspended = new HashSet<DualSenseDevice>();

    /// <summary>
    /// The current action configuration, applied to engines created after the last
    /// <see cref="UpdateActions"/> call.
    /// </summary>
    private IReadOnlyList<SpecialAction> _actions = [];

    /// <summary>
    /// Resolves the profile to revert to when a while-held light action ends. Applied to
    /// every engine created by this registry.
    /// </summary>
    public Func<DualSenseDevice, Profile?>? ProfileProvider { get; set; }

    /// <summary>
    /// Creates the player used by <see cref="SpecialActionTypes.PlaySound"/> actions.
    /// Applied to every engine created by this registry.
    /// </summary>
    public Func<DualSenseDevice, ISpecialActionSoundPlayer>? SoundPlayerFactory { get; set; }

    /// <summary>
    /// Gets the engine for a controller, creating it on first use with the current
    /// configuration and providers.
    /// </summary>
    public SpecialActionEngine GetOrCreate(DualSenseDevice device)
    {
        lock (_lock)
        {
            if (_engines.TryGetValue(device, out SpecialActionEngine? engine))
            {
                return engine;
            }

            engine = new SpecialActionEngine
            {
                ProfileProvider = ProfileProvider,
                SoundPlayerFactory = SoundPlayerFactory
            };
            engine.UpdateActions(_actions);
            engine.TouchpadActionsEnabled = !_touchpadActionsSuspended.Contains(device);
            _engines.Add(device, engine);
            return engine;
        }
    }

    /// <summary>
    /// Suspends or resumes the touchpad-triggered special actions (swipe gestures and
    /// combinations containing the touchpad button) of a controller, for as long as the
    /// touchpad mouse feature owns its touchpad. Applied immediately to an existing engine,
    /// or to the engine created later for the controller.
    /// </summary>
    /// <param name="device">The controller whose touchpad actions to suspend or resume.</param>
    /// <param name="suspended">Whether the touchpad-triggered actions are suspended.</param>
    public void SetTouchpadActionsSuspended(DualSenseDevice device, bool suspended)
    {
        lock (_lock)
        {
            if (suspended)
            {
                _touchpadActionsSuspended.Add(device);
            }
            else
            {
                _touchpadActionsSuspended.Remove(device);
            }

            if (_engines.TryGetValue(device, out SpecialActionEngine? engine))
            {
                engine.TouchpadActionsEnabled = !suspended;
            }
        }
    }

    /// <summary>
    /// Removes and disposes the engine of a detached controller. Idempotent.
    /// </summary>
    public void Remove(DualSenseDevice device)
    {
        lock (_lock)
        {
            _touchpadActionsSuspended.Remove(device);
            if (_engines.Remove(device, out SpecialActionEngine? engine))
            {
                engine.Dispose();
            }
        }
    }

    /// <summary>
    /// Replaces the action configuration on every engine, including engines created later.
    /// </summary>
    public void UpdateActions(IReadOnlyList<SpecialAction> actions)
    {
        lock (_lock)
        {
            _actions = (actions ?? []).ToList();
            foreach (SpecialActionEngine engine in _engines.Values)
            {
                engine.UpdateActions(_actions);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            foreach (SpecialActionEngine engine in _engines.Values)
            {
                engine.Dispose();
            }

            _engines.Clear();
            _touchpadActionsSuspended.Clear();
        }
    }
}