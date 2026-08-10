using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Keyboard;

/// <summary>
/// Input state of a keyboard device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct KeyboardDeviceState
{
    /// <summary>
    /// <see cref="KeyboardModifiers"/> bitmask.
    /// </summary>
    public byte Modifiers;

    /// <summary>
    /// 256-bit key bitmap indexed by <see cref="KeyboardKey"/> usage code.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] KeyBitmap;
}