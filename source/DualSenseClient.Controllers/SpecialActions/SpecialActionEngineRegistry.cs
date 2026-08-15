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
    /// Guards access to <see cref="_engines"/> and <see cref="_actions"/>.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// The engine per attached controller.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, SpecialActionEngine> _engines = new();

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
            _engines.Add(device, engine);
            return engine;
        }
    }

    /// <summary>
    /// Removes and disposes the engine of a detached controller. Idempotent.
    /// </summary>
    public void Remove(DualSenseDevice device)
    {
        lock (_lock)
        {
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
        }
    }
}