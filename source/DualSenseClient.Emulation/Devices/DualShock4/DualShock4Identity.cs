namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Identity of the DualShock 4 v2 persona (Sony 054C:09CC): the device and string descriptor values that
/// describe one physical controller. The defaults reproduce the v2 reference descriptor set; every
/// value is settable at construction. The descriptor set is
/// built with <see cref="DualShock4DescriptorSet.Create"/>.
/// </summary>
public readonly struct DualShock4Identity
{
    /// <summary>Initializes an identity with the reference DualShock 4 v2 defaults.</summary>
    public DualShock4Identity()
    {
    }

    /// <summary>Vendor id served in the device descriptor (idVendor); defaults to 0x054C (Sony).</summary>
    public ushort IdVendor { get; init; } = 0x054C;

    /// <summary>Product id served in the device descriptor (idProduct); defaults to 0x09CC (DualShock 4 v2, CUH-ZCT2).</summary>
    public ushort IdProduct { get; init; } = 0x09CC;

    /// <summary>Device release number served in the device descriptor (bcdDevice); defaults to 0x0100, the reference value.</summary>
    public ushort BcdDevice { get; init; } = 0x0100;

    /// <summary>
    /// Manufacturer string (string descriptor index 1 by default); defaults to "Sony Interactive Entertainment".
    /// A null or empty value omits the manufacturer string descriptor and sets iManufacturer to zero.
    /// </summary>
    public string Manufacturer { get; init; } = "Sony Interactive Entertainment";

    /// <summary>
    /// Product string (string descriptor index 2 by default); defaults to "Wireless Controller", the name the
    /// reference serves for this persona. A null or empty value omits the product string descriptor and sets
    /// iProduct to zero.
    /// </summary>
    public string Product { get; init; } = "Wireless Controller";

    /// <summary>
    /// Serial number string; defaults to empty. The reference descriptor set defines no serial string (the
    /// reference synthesizes one from hardware at runtime only when its USB-serial option is enabled), so an empty value
    /// omits the serial string descriptor and sets iSerialNumber to zero.
    /// </summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>
    /// Controller MAC address as six bytes in address order, kept for the feature reports that carry it in later
    /// milestones (reports 0x12/0x81); defaults to all zero because a virtual controller has no hardware address.
    /// The descriptor set does not consume this value.
    /// </summary>
    public byte[] MacAddress { get; init; } = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
}