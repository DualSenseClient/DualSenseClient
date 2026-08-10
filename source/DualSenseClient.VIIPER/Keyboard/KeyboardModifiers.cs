namespace DualSenseClient.VIIPER.Keyboard;

/// <summary>
/// Keyboard modifier bit flags.
/// </summary>
[Flags]
public enum KeyboardModifiers : byte
{
    LeftCtrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    LeftGui = 0x08,
    RightCtrl = 0x10,
    RightShift = 0x20,
    RightAlt = 0x40,
    RightGui = 0x80
}