namespace DualSenseClient.VIIPER.NS2Pro;

/// <summary>
/// Nintendo Switch 2 Pro Controller button bit flags.
/// </summary>
[Flags]
public enum NS2ProButtons : uint
{
    B = 0x00000001,
    A = 0x00000002,
    Y = 0x00000004,
    X = 0x00000008,
    R = 0x00000010,
    ZR = 0x00000020,
    Plus = 0x00000040,
    RightStick = 0x00000080,
    Down = 0x00000100,
    Right = 0x00000200,
    Left = 0x00000400,
    Up = 0x00000800,
    L = 0x00001000,
    ZL = 0x00002000,
    Minus = 0x00004000,
    LeftStick = 0x00008000,
    Home = 0x00010000,
    Capture = 0x00020000,
    Gr = 0x00040000,
    Gl = 0x00080000,
    C = 0x00100000,
    Headset = 0x00200000
}