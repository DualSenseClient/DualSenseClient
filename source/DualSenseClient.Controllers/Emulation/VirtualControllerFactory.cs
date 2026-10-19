using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.Xbox360;
using EmulationDs4 = DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Creates virtual controllers that mirror a physical controller. This is the libVIIPER engine's
/// factory; <see cref="IEmulationControllerFactory"/> below is the Emulation engine's counterpart.
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
    /// <param name="ds4Variant">The DualShock 4 hardware generation to present (DualShock 4 mode only).</param>
    IVirtualController? Create(EmulationMode mode, nuint serverHandle, uint busId, IDualSenseOutputs outputs, bool vibrationV2, bool edge = false,
        DualShock4Variant ds4Variant = DualShock4Variant.V2);
}

/// <summary>
/// Default <see cref="IVirtualControllerFactory"/> implementation.
/// </summary>
public sealed class VirtualControllerFactory : IVirtualControllerFactory
{
    /// <inheritdoc/>
    public IVirtualController? Create(EmulationMode mode, nuint serverHandle, uint busId, IDualSenseOutputs outputs, bool vibrationV2, bool edge = false,
        DualShock4Variant ds4Variant = DualShock4Variant.V2)
    {
        return mode switch
        {
            EmulationMode.Xbox360 => new VirtualXbox360Controller(serverHandle, busId, outputs),
            EmulationMode.DualShock4 => new VirtualDualShock4Controller(serverHandle, busId, outputs, ds4Variant),
            EmulationMode.DualSense => new VirtualDualSenseController(serverHandle, busId, outputs, vibrationV2, edge),
            EmulationMode.Off => null,
            _ => null
        };
    }
}

/// <summary>
/// Creates Emulation-backed virtual controllers: registers a fresh persona device on the shared
/// Emulation server, seeds its identity (including the ownership MAC) and wraps it in the matching
/// adapter. The caller keeps the persona's lifecycle (attachment, discovery, detachment and
/// removal) and the adapter's disposal.
/// </summary>
public interface IEmulationControllerFactory
{
    /// <summary>
    /// Creates the persona device of the given mode on the given server and wraps it in the
    /// matching adapter, mirroring <see cref="IVirtualControllerFactory"/> for the Emulation
    /// engine. The persona is registered with the server but not attached. Returns <c>null</c>
    /// for modes without an Emulation persona.
    /// </summary>
    /// <param name="server">The emulation server hosting the persona.</param>
    /// <param name="mode">The controller type to create.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">True when the physical controller uses the v2 rumble encoding.</param>
    /// <param name="edge">True to create a DualSense Edge persona instead of the standard DualSense (DualSense mode only).</param>
    /// <param name="ds4Variant">The DualShock 4 hardware generation to present (DualShock 4 mode only).</param>
    EmulationControllerCreation? Create(EmulationServer server, EmulationMode mode, IDualSenseOutputs outputs, bool vibrationV2,
        bool edge = false, DualShock4Variant ds4Variant = DualShock4Variant.V2);
}

/// <summary>
/// One created Emulation virtual controller: the adapter mirroring the physical controller and the
/// persona device it pushes to, so the caller can tear both down together.
/// </summary>
public sealed class EmulationControllerCreation
{
    /// <summary>
    /// Initializes a creation result.
    /// </summary>
    /// <param name="controller">The adapter presenting the persona.</param>
    /// <param name="persona">The registered persona device backing the adapter.</param>
    public EmulationControllerCreation(IVirtualController controller, VirtualUsbDevice persona)
    {
        Controller = controller;
        Persona = persona;
    }

    /// <summary>
    /// The adapter presenting the persona to the emulation service.
    /// </summary>
    public IVirtualController Controller { get; }

    /// <summary>
    /// The persona device backing the adapter; never removed by the adapter.
    /// </summary>
    public VirtualUsbDevice Persona { get; }
}

/// <summary>
/// Default <see cref="IEmulationControllerFactory"/> implementation.
/// </summary>
public sealed class EmulationControllerFactory : IEmulationControllerFactory
{
    /// <inheritdoc/>
    public EmulationControllerCreation? Create(EmulationServer server, EmulationMode mode, IDualSenseOutputs outputs, bool vibrationV2,
        bool edge = false, DualShock4Variant ds4Variant = DualShock4Variant.V2)
    {
        switch (mode)
        {
            case EmulationMode.DualSense:
            {
                DualSenseDevice device = server.AddDualSense(edge, new DualSenseIdentity
                {
                    MacAddress = CreateOwnershipMacBytes()
                });
                return CreateChecked(server, device, () => new EmulationDualSenseController(device, outputs, vibrationV2));
            }

            case EmulationMode.DualShock4:
            {
                EmulationDs4.DualShock4Variant variant = ds4Variant == DualShock4Variant.V1
                    ? EmulationDs4.DualShock4Variant.V1
                    : EmulationDs4.DualShock4Variant.V2;
                EmulationDs4.DualShock4Device device = server.AddDualShock4(variant, new EmulationDs4.DualShock4Identity
                {
                    MacAddress = CreateOwnershipMacBytes()
                });
                return CreateChecked(server, device, () => new EmulationDualShock4Controller(device, outputs));
            }

            case EmulationMode.Xbox360:
            {
                Xbox360Device device = server.AddXbox360();
                return CreateChecked(server, device, () => new EmulationXbox360Controller(device, outputs));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Wraps <paramref name="adapterFactory"/> so a throwing adapter constructor does not leave
    /// the already registered persona behind: the service's own catch cannot reach it, so the
    /// registration is rolled back here before the exception propagates. The adapter constructors
    /// are allocation-only today; this guards future changes.
    /// </summary>
    /// <param name="server">The server the persona is registered with.</param>
    /// <param name="device">The registered persona device.</param>
    /// <param name="adapterFactory">Creates the adapter presenting the persona.</param>
    /// <returns>The creation result pairing the new adapter with the persona.</returns>
    private static EmulationControllerCreation CreateChecked(EmulationServer server, VirtualUsbDevice device,
        Func<IVirtualController> adapterFactory)
    {
        try
        {
            return new EmulationControllerCreation(adapterFactory(), device);
        }
        catch
        {
            server.RemoveDevice(device);
            throw;
        }
    }

    /// <summary>
    /// Parses <see cref="VirtualDeviceFilter.CreateOwnershipMac"/> into the six address bytes the
    /// persona identities carry, so every Emulation virtual controller is recognizable by the
    /// same ownership MAC as its libVIIPER counterpart.
    /// </summary>
    /// <returns>The ownership MAC in address order.</returns>
    private static byte[] CreateOwnershipMacBytes()
    {
        string[] parts = VirtualDeviceFilter.CreateOwnershipMac().Split(':');
        byte[] mac = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            mac[i] = Convert.ToByte(parts[i], 16);
        }

        return mac;
    }
}