using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Availability rules for virtual controller modes on physical connections.
/// </summary>
public static class EmulationModePolicy
{
    /// <summary>
    /// Modes offered over USB: the controller already presents itself as a DualSense.
    /// </summary>
    private static readonly IReadOnlyList<EmulationMode> _usbModes =
    [
        EmulationMode.Off,
        EmulationMode.Xbox360,
        EmulationMode.DualShock4
    ];

    /// <summary>
    /// Modes offered over every other connection, including Bluetooth.
    /// </summary>
    private static readonly IReadOnlyList<EmulationMode> _allModes =
    [
        EmulationMode.Off,
        EmulationMode.Xbox360,
        EmulationMode.DualShock4,
        EmulationMode.DualSense
    ];

    /// <summary>
    /// Returns the modes offered for a physical connection. DualSense is omitted over USB,
    /// including a DS5Dongle, because the physical controller already presents itself as one.
    /// </summary>
    /// <param name="connectionType">The physical controller's connection.</param>
    /// <returns>The modes to offer, in <see cref="EmulationMode"/> order.</returns>
    public static IReadOnlyList<EmulationMode> GetAvailableModes(ConnectionType connectionType) => connectionType == ConnectionType.Usb ? _usbModes : _allModes;

    /// <summary>
    /// Returns the mode to use for the given connection. Virtual DualSense is redundant over
    /// USB, including a DS5Dongle, because the physical controller already presents itself as a
    /// DualSense; the requested mode becomes <see cref="EmulationMode.Off"/>.
    /// </summary>
    /// <param name="mode">The requested emulation mode.</param>
    /// <param name="connectionType">The physical controller's connection.</param>
    /// <returns>The mode that can run on the connection.</returns>
    public static EmulationMode Normalize(EmulationMode mode, ConnectionType connectionType)
    {
        return mode == EmulationMode.DualSense && connectionType == ConnectionType.Usb
            ? EmulationMode.Off
            : mode;
    }
}