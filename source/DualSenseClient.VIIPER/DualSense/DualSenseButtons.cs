namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// DualSense button bit flags.
/// </summary>
[Flags]
public enum DualSenseButtons : uint
{
    Square = 0x00000010,
    Cross = 0x00000020,
    Circle = 0x00000040,
    Triangle = 0x00000080,
    L1 = 0x00000100,
    R1 = 0x00000200,
    L2 = 0x00000400,
    R2 = 0x00000800,
    Create = 0x00001000,
    Options = 0x00002000,
    L3 = 0x00004000,
    R3 = 0x00008000,
    PS = 0x00010000,
    Touchpad = 0x00020000,
    MicMute = 0x00040000,
    LeftFunction = 0x00100000,
    RightFunction = 0x00200000,
    L4 = 0x00400000,
    R4 = 0x00800000
}