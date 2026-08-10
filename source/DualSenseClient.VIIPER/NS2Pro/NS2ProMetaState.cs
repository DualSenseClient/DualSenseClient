using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.NS2Pro;

/// <summary>
/// Meta (identity/battery) state of a Nintendo Switch 2 Pro Controller.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NS2ProMetaState
{
    /// <summary>
    /// NULL = use default.
    /// </summary>
    [MarshalAs(UnmanagedType.LPUTF8Str)] public string? SerialNumber;

    /// <summary>
    /// 0-9; 0 = use default (9 = full).
    /// </summary>
    public byte BatteryLevel;

    /// <summary>
    /// 0 = not charging.
    /// </summary>
    public byte Charging;

    /// <summary>
    /// 0 = battery only.
    /// </summary>
    public byte ExternalPower;

    /// <summary>
    /// mV; 0 = use default (3800).
    /// </summary>
    public ushort BatteryVolts;
}