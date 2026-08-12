using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Creates libVIIPER-backed virtual controllers that mirror a physical DualSense.
/// </summary>
public interface IVirtualControllerFactory
{
    /// <summary>
    /// Creates a virtual controller of the given mode on the given USB bus.
    /// Returns <c>null</c> when the native library could not create the device.
    /// </summary>
    /// <param name="mode">The controller type to create.</param>
    /// <param name="serverHandle">The USB server hosting the device.</param>
    /// <param name="busId">The bus to attach the device to.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    /// <param name="edge">True to create a DualSense Edge instead of the standard DualSense (DualSense mode only).</param>
    IVirtualController? Create(EmulationMode mode, nuint serverHandle, uint busId, IDualSenseOutputs outputs, bool vibrationV2, bool edge = false);
}

/// <summary>
/// Default <see cref="IVirtualControllerFactory"/> implementation.
/// </summary>
public sealed class VirtualControllerFactory : IVirtualControllerFactory
{
    /// <inheritdoc/>
    public IVirtualController? Create(EmulationMode mode, nuint serverHandle, uint busId, IDualSenseOutputs outputs, bool vibrationV2, bool edge = false)
    {
        return mode switch
        {
            EmulationMode.Xbox360 => new VirtualXbox360Controller(serverHandle, busId, outputs),
            EmulationMode.DualShock4 => new VirtualDualShock4Controller(serverHandle, busId, outputs),
            EmulationMode.DualSense => new VirtualDualSenseController(serverHandle, busId, outputs, vibrationV2, edge),
            EmulationMode.Off => null,
            _ => null
        };
    }
}