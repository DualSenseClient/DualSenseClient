using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// Physical raw-input metadata accompanying <see cref="DSDeviceState"/>.
/// Together they form the 53-byte V5RawInput wire payload.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DSRawInputMetadata
{
    /// <summary>
    /// 0 = metadata invalid/ignored; non-zero = metadata valid.
    /// Pass a zero-initialized struct to behave exactly like <see cref="LibVIIPER.SetDualSenseDeviceState"/>.
    /// </summary>
    public byte Valid;

    /// <summary>
    /// Non-zero = metadata normalized from an Edge-layout report.
    /// </summary>
    public byte EdgeLayout;

    /// <summary>
    /// Reserved padding.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public byte[] Reserved;

    /// <summary>
    /// Physical input report bytes 28:32 (sensor timestamp).
    /// </summary>
    public uint SensorTimestamp;

    /// <summary>
    /// Normalized physical report metadata (15 bytes, report bytes 41:56 filtered).
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
    public byte[] PhysicalMetadata;
}