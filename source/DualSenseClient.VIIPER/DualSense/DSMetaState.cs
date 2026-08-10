using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// Meta (identity/battery/sensor) state of a DualSense device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DSMetaState
{
    /// <summary>
    /// NULL = use default.
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? SerialNumber;

    /// <summary>
    /// NULL = use default.
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? MACAddress;

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
    /// NULL = use default (2-char code, e.g. "00", "Z1").
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? ShellColor;

    /// <summary>
    /// NULL = use default (RFC3339 or "YYYY-MM-DD HH:MM:SS").
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? BuildTime;

    /// <summary>
    /// 0 = use default (<see cref="DualSenseConnectionFlags"/>).
    /// </summary>
    public byte ConnectionStatus;
}