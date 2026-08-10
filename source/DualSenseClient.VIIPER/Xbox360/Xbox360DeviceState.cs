using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.Xbox360;

/// <summary>
/// Input state of an Xbox 360 device.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Xbox360DeviceState
{
    /// <summary>
    /// <see cref="Xbox360Buttons"/> bitfield (lower 16 bits used typically).
    /// </summary>
    public uint Buttons;

    /// <summary>
    /// Left trigger: 0-255.
    /// </summary>
    public byte LT;

    /// <summary>
    /// Right trigger: 0-255.
    /// </summary>
    public byte RT;

    /// <summary>
    /// Left stick X axis: signed 16-bit little-endian value.
    /// </summary>
    public short LX;

    /// <summary>
    /// Left stick Y axis: signed 16-bit little-endian value.
    /// </summary>
    public short LY;

    /// <summary>
    /// Right stick X axis: signed 16-bit little-endian value.
    /// </summary>
    public short RX;

    /// <summary>
    /// Right stick Y axis: signed 16-bit little-endian value.
    /// </summary>
    public short RY;

    /// <summary>
    /// Reserved for future use
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] Reserved;
}