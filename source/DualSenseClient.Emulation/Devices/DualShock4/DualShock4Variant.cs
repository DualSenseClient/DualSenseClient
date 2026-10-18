namespace DualSenseClient.Emulation.Devices.DualShock4;

/// <summary>
/// Descriptor variant of the DualShock 4 persona: the v1 (CUH-ZCT1) or the v2 (CUH-ZCT2) controller.
/// The variant selects the facts that differ between the two reference personas: the served product id,
/// the manufacturer string and the HID report descriptor (467 bytes for the v1 reference,
/// 507 bytes for the v2 reference). Every other descriptor fact is identity-driven, and the
/// 64-byte input report 0x01 and the output report 0x05 are identical for both variants. Descriptor
/// sets are created with <see cref="DualShock4DescriptorSet.Create(DualShock4Identity, DualShock4Variant)"/>,
/// devices with the matching <see cref="DualShock4Device"/> constructor parameter.
/// </summary>
public enum DualShock4Variant
{
    /// <summary>DualShock 4 v1 (CUH-ZCT1, Sony 054C:05C4); descriptor facts from the v1 reference.</summary>
    V1,

    /// <summary>DualShock 4 v2 (CUH-ZCT2, Sony 054C:09CC); descriptor facts from the v2 reference; the default.</summary>
    V2
}