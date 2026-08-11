using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// A virtual controller created with libVIIPER that mirrors the physical DualSense
/// input to the host, and forwards host feedback to the physical device.
/// </summary>
public interface IVirtualController : IDisposable
{
    /// <summary>
    /// The emulation mode this controller was created for.
    /// </summary>
    EmulationMode Mode { get; }

    /// <summary>
    /// The native device handle, or <c>null</c> when creation failed.
    /// </summary>
    nuint? DeviceHandle { get; }

    /// <summary>
    /// The host HID device path of the virtual controller, discovered after creation,
    /// or <c>null</c> when it could not be found. Used to exclude the virtual device
    /// from the app's own enumeration.
    /// </summary>
    string? VirtualDevicePath { get; set; }

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
/// Base class for the libVIIPER-backed virtual controllers.
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
    protected VirtualControllerBase(IDualSenseOutputs outputs) => Outputs = outputs;

    /// <inheritdoc/>
    public abstract EmulationMode Mode { get; }

    /// <inheritdoc/>
    public nuint? DeviceHandle { get; protected set; }

    /// <inheritdoc/>
    public string? VirtualDevicePath { get; set; }

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