namespace DualSenseClient.VIIPER.Keyboard;

/// <summary>
/// Keyboard LED bit flags.
/// </summary>
[Flags]
public enum KeyboardLeds : byte
{
    NumLock = 0x01,
    CapsLock = 0x02,
    ScrollLock = 0x04,
    Compose = 0x08,
    Kana = 0x10
}