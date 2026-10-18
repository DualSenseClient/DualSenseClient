namespace DualSenseClient.Emulation.Devices.DualSense;

/// <summary>
/// Charging state carried in the high nibble of the DualSense battery byte (the reference's
/// PowerState): the values match the encoding the controller serves in its input report and
/// feature reports.
/// </summary>
public enum DualSenseChargingState : byte
{
    /// <summary>Running on battery; the battery level nibble is the current charge (0x00).</summary>
    Discharging = 0x00,

    /// <summary>Charging; the battery level nibble is the current charge (0x01).</summary>
    Charging = 0x01,

    /// <summary>Charging complete: the battery is treated as full and the level as 100% (0x02).</summary>
    Complete = 0x02,

    /// <summary>Abnormal battery voltage; the level nibble is not meaningful (0x0A).</summary>
    AbnormalVoltage = 0x0A,

    /// <summary>Abnormal battery temperature; the level nibble is not meaningful (0x0B).</summary>
    AbnormalTemperature = 0x0B,

    /// <summary>Charging error; the level nibble is not meaningful (0x0F).</summary>
    ChargingError = 0x0F
}

/// <summary>
/// Identity of the DualSense persona (Sony 054C:0CE6): the device and string descriptor values plus the
/// battery defaults that describe one virtual controller. The defaults reproduce the reference
/// descriptor set in its no-serial build with wake off, and the
/// battery state of the reference's idle input report. Every value is settable at construction. The
/// descriptor set is built with <see cref="DualSenseDescriptorSet.Create"/>.
/// </summary>
public readonly struct DualSenseIdentity
{
    /// <summary>Initializes an identity with the reference DualSense defaults.</summary>
    public DualSenseIdentity()
    {
    }

    /// <summary>Vendor id served in the device descriptor (idVendor); defaults to 0x054C (Sony).</summary>
    public ushort IdVendor { get; init; } = 0x054C;

    /// <summary>
    /// Product id served in the device descriptor (idProduct); defaults to 0x0CE6 (DualSense; the Edge
    /// persona of the same reference serves 0x0DF2).
    /// </summary>
    public ushort IdProduct { get; init; } = 0x0CE6;

    /// <summary>Device release number served in the device descriptor (bcdDevice); defaults to 0x0100, the reference value.</summary>
    public ushort BcdDevice { get; init; } = 0x0100;

    /// <summary>
    /// Manufacturer string (string descriptor index 1 by default); defaults to "Sony Interactive Entertainment",
    /// the reference's manufacturer string. A null or empty value omits the manufacturer string descriptor and
    /// sets iManufacturer to zero.
    /// </summary>
    public string Manufacturer { get; init; } = "Sony Interactive Entertainment";

    /// <summary>
    /// Product string (string descriptor index 2 by default); defaults to "DualSense Wireless Controller", the
    /// name the reference serves in DualSense mode. A null or empty value omits the product string descriptor
    /// and sets iProduct to zero.
    /// </summary>
    public string Product { get; init; } = "DualSense Wireless Controller";

    /// <summary>
    /// Serial number string; defaults to empty. The reference descriptor set defines no serial string (its
    /// board serial is served at index 3 only when the USB-serial option is enabled), so an empty value omits
    /// the serial string descriptor and sets iSerialNumber to zero.
    /// </summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>
    /// Controller MAC address as six bytes in address order, consumed by the pairing-info feature report
    /// 0x09 the device serves through <see cref="DualSenseFeatureReports"/>; defaults to all zero because a
    /// virtual controller has no hardware address. The reference carries the MAC only over its HID feature
    /// reports, never in the USB descriptor set, so the descriptor set does not consume this value.
    /// </summary>
    public byte[] MacAddress { get; init; } = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    /// <summary>
    /// Default battery capacity served in the input report until a state or meta update overrides it: the low
    /// nibble of the battery byte (0x00-0x0A, 10 = 100%; the reference's PowerPercent). Defaults to 0x0A
    /// (full): the reference's idle report ends at <see cref="DualSenseChargingState.Complete"/> (raw byte
    /// 0x29, level nibble at 9/10) and the reference annotates Complete as "assume 100%".
    /// </summary>
    public byte BatteryLevel { get; init; } = 0x0A;

    /// <summary>
    /// Default charging state served in the input report until overridden: the high nibble of the battery
    /// byte. Defaults to <see cref="DualSenseChargingState.Complete"/>: the state of the reference's idle
    /// input report (battery byte 0x29, Complete at level 9/10), the charged state a virtual controller
    /// presents.
    /// </summary>
    public DualSenseChargingState ChargingState { get; init; } = DualSenseChargingState.Complete;
}