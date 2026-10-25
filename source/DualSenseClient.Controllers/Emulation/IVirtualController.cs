using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// A virtual controller that mirrors the physical controller's input to the host, and
/// forwards host feedback to the physical device.
/// </summary>
public interface IVirtualController : IDisposable
{
    /// <summary>
    /// The emulation mode this controller was created for.
    /// </summary>
    EmulationMode Mode { get; }

    /// <summary>
    /// The host HID device path of the virtual controller, discovered after creation,
    /// or <c>null</c> when it could not be found. Used to exclude the virtual device
    /// from the app's own enumeration.
    /// </summary>
    string? VirtualDevicePath { get; set; }

    /// <summary>
    /// The button remapping rules applied to pushed input, or <c>null</c> to use the mode's
    /// built-in default mapping. Swapping in a new immutable table takes effect on the next
    /// input report without recreating the device.
    /// </summary>
    ButtonMappingTable? ButtonMappings { get; set; }

    /// <summary>
    /// Applies the host-feedback forwarding options without recreating the device.
    /// Channels switched off stop being forwarded and their current effect is cleared.
    /// </summary>
    /// <param name="settings">The forwarding options to apply.</param>
    void ApplyForwardingSettings(ForwardSettings settings);

    /// <summary>
    /// Pushes the latest physical input report to the virtual controller. Called on
    /// the physical device's read thread.
    /// </summary>
    void PushInput(InputReport report);

    /// <summary>
    /// Pushes the latest physical battery state to the virtual controller.
    /// </summary>
    void PushBattery(BatteryState battery);

    /// <summary>
    /// Pushes the latest physical connection status to the virtual controller.
    /// </summary>
    void PushConnectionStatus(ConnectionStatus status);
}

/// <summary>
/// Base class for virtual controllers.
/// </summary>
public abstract class VirtualControllerBase : IVirtualController
{
    /// <summary>
    /// The feedback path to the physical controller.
    /// </summary>
    protected readonly IDualSenseOutputs Outputs;

    /// <summary>
    /// Initializes a new virtual controller with the given feedback target.
    /// </summary>
    protected VirtualControllerBase(IDualSenseOutputs outputs)
    {
        Outputs = outputs;
    }

    /// <inheritdoc/>
    public abstract EmulationMode Mode { get; }

    /// <inheritdoc/>
    public string? VirtualDevicePath { get; set; }

    /// <inheritdoc/>
    public ButtonMappingTable? ButtonMappings { get; set; }

    /// <summary>Whether classic rumble is currently forwarded.</summary>
    protected bool RumbleForwardingEnabled
    {
        get
        {
            return _rumbleForwarding;
        }
    }

    /// <summary>Whether adaptive trigger effects are currently forwarded.</summary>
    protected bool TriggerForwardingEnabled
    {
        get
        {
            return _triggerForwarding;
        }
    }

    /// <summary>Whether lightbar updates are currently forwarded.</summary>
    protected bool LightbarForwardingEnabled
    {
        get
        {
            return _lightbarForwarding;
        }
    }

    /// <summary>Whether player LED updates are currently forwarded.</summary>
    protected bool PlayerLedsForwardingEnabled
    {
        get
        {
            return _playerLedsForwarding;
        }
    }

    /// <inheritdoc/>
    public virtual void ApplyForwardingSettings(ForwardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UpdateForwarding(ref _rumbleForwarding, settings.Rumble, ClearRumble);
        UpdateForwarding(ref _triggerForwarding, settings.Triggers, ClearTriggers);
        UpdateForwarding(ref _lightbarForwarding, settings.Lightbar, ClearLightbar);
        UpdateForwarding(ref _playerLedsForwarding, settings.PlayerLeds, ClearPlayerLeds);
    }

    /// <summary>Backing field for <see cref="RumbleForwardingEnabled"/>.</summary>
    private bool _rumbleForwarding = true;

    /// <summary>Backing field for <see cref="TriggerForwardingEnabled"/>.</summary>
    private bool _triggerForwarding = true;

    /// <summary>Backing field for <see cref="LightbarForwardingEnabled"/>.</summary>
    private bool _lightbarForwarding = true;

    /// <summary>Backing field for <see cref="PlayerLedsForwardingEnabled"/>.</summary>
    private bool _playerLedsForwarding = true;

    /// <summary>
    /// Updates one forwarding flag, clearing the physical effect when it is switched off.
    /// </summary>
    private static void UpdateForwarding(ref bool field, bool value, Action clear)
    {
        // Disable before clearing so a report racing this change can no longer
        // forward the channel after the physical effect was cleared.
        bool wasEnabled = field;
        field = value;
        if (wasEnabled && !value)
        {
            clear();
        }
    }

    /// <summary>Clears the physical rumble motors.</summary>
    protected virtual void ClearRumble() => Outputs.SetVibration(0, 0);

    /// <summary>Clears the physical adaptive trigger effects.</summary>
    protected virtual void ClearTriggers()
    {
    }

    /// <summary>Clears the physical lightbar.</summary>
    protected virtual void ClearLightbar()
    {
    }

    /// <summary>Clears the physical player LEDs.</summary>
    protected virtual void ClearPlayerLeds()
    {
    }

    /// <inheritdoc/>
    public abstract void PushInput(InputReport report);

    /// <inheritdoc/>
    public virtual void PushBattery(BatteryState battery)
    {
    }

    /// <inheritdoc/>
    public virtual void PushConnectionStatus(ConnectionStatus status)
    {
    }

    /// <inheritdoc/>
    public abstract void Dispose();
}