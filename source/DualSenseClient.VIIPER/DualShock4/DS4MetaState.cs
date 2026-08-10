using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualShock4;

/// <summary>
/// Meta (identity/battery) state of a DualShock 4 device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DS4MetaState
{
    /// <summary>
    /// NULL = use default.
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? SerialNumber;

    /// <summary>
    /// NULL = use default.
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? Board;

    /// <summary>
    /// 0 = use default.
    /// </summary>
    public byte BatteryStatus;

    /// <summary>
    /// 0 = use default.
    /// </summary>
    public double TemperatureCelsius;

    /// <summary>
    /// 0 = use default.
    /// </summary>
    public double BatteryVoltage;

    /// <summary>
    /// NULL = use default (RFC3339 or "YYYY-MM-DD HH:MM:SS").
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? BuildTime;
}