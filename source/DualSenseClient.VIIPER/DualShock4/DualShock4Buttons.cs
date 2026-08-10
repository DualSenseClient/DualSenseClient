namespace DualSenseClient.VIIPER.DualShock4;

/// <summary>
/// DualShock 4 button bit flags.
/// </summary>
[Flags]
public enum DualShock4Buttons : ushort
{
    Square = 0x0010,
    Cross = 0x0020,
    Circle = 0x0040,
    Triangle = 0x0080,
    L1 = 0x0100,
    R1 = 0x0200,
    L2 = 0x0400,
    R2 = 0x0800,
    Share = 0x1000,
    Options = 0x2000,
    L3 = 0x4000,
    R3 = 0x8000,
    PS = 0x0001,
    Touchpad = 0x0002
}