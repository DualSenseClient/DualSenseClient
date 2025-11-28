using System;

namespace DualSenseClient.Core.DualSense.Models;

/// <summary>
/// Represents the firmware version of a DualSense controller
/// </summary>
public class FirmwareVersion
{
    /// <summary>
    /// Major version number
    /// </summary>
    public int Major { get; set; }

    /// <summary>
    /// Minor version number
    /// </summary>
    public int Minor { get; set; }

    /// <summary>
    /// Build number
    /// </summary>
    public int Build { get; set; }

    /// <summary>
    /// Hardware version - Variation (bits 16-23)
    /// </summary>
    public int HardwareVersionVariation { get; set; }

    /// <summary>
    /// Hardware version - Generation (bits 8-15)
    /// </summary>
    public int HardwareVersionGeneration { get; set; }

    /// <summary>
    /// Hardware version - Trial (bits 0-5)
    /// </summary>
    public int HardwareVersionTrial { get; set; }

    /// <summary>
    /// Creates a new FirmwareVersion instance
    /// </summary>
    public FirmwareVersion(int major, int minor, int build, int variation, int generation, int trial)
    {
        Major = major;
        Minor = minor;
        Build = build;
        HardwareVersionVariation = variation;
        HardwareVersionGeneration = generation;
        HardwareVersionTrial = trial;
    }

    /// <summary>
    /// Creates a FirmwareVersion from raw firmware bytes (little-endian 32-bit value from bytes 28-31)
    /// Format: 0xAABBCCCC -> AA.BB.CCCC
    /// </summary>
    public static FirmwareVersion FromRawFirmwareVersion(uint rawVersion)
    {
        int major = (int)((rawVersion >> 24) & 0xFF);      // AA (bits 24-31)
        int minor = (int)((rawVersion >> 16) & 0xFF);      // BB (bits 16-23)
        int build = (int)(rawVersion & 0xFFFF);            // CCCC (bits 0-15)

        return new FirmwareVersion(major, minor, build, 0, 0, 0);
    }

    /// <summary>
    /// Creates a FirmwareVersion from raw firmware and hardware bytes
    /// Firmware Format: 0xAABBCCCC -> AA.BB.CCCC
    /// Hardware Format: 0x00FF0000 (Variation) | 0x0000FF00 (Generation) | 0x0000003F (Trial)
    /// </summary>
    public static FirmwareVersion FromRawData(uint firmwareVersion, uint hardwareVersion)
    {
        int major = (int)((firmwareVersion >> 24) & 0xFF); // AA (bits 24-31)
        int minor = (int)((firmwareVersion >> 16) & 0xFF); // BB (bits 16-23)
        int build = (int)(firmwareVersion & 0xFFFF);       // CCCC (bits 0-15)

        // Parse hardware version according to the specified format
        int variation = (int)((hardwareVersion >> 16) & 0xFF); // 0x00FF0000 - Variation
        int generation = (int)((hardwareVersion >> 8) & 0xFF); // 0x0000FF00 - Generation
        int trial = (int)(hardwareVersion & 0x3F);             // 0x0000003F - Trial?

        return new FirmwareVersion(major, minor, build, variation, generation, trial);
    }

    public override string ToString()
    {
        return $"v{Major}.{Minor}.{Build}";
    }

    public string ToFullString()
    {
        return $"v{Major}.{Minor}.{Build} (HW: V{HardwareVersionVariation}.G{HardwareVersionGeneration}.T{HardwareVersionTrial})";
    }

    public string GetHardwareVersionString()
    {
        return $"V{HardwareVersionVariation}.G{HardwareVersionGeneration}.T{HardwareVersionTrial}";
    }
}