using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.NS2Pro;

/// <summary>
/// Full output state of a Nintendo Switch 2 Pro Controller, delivered by output callbacks.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NS2ProOutputState
{
    /// <summary>
    /// 16-byte HD rumble data for the left motor.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] LeftRumble;

    /// <summary>
    /// 16-byte HD rumble data for the right motor.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] RightRumble;

    /// <summary>
    /// Output flag bitmask.
    /// </summary>
    public byte Flags;

    /// <summary>
    /// Player LED mask (one bit per player).
    /// </summary>
    public byte PlayerLedMask;
}