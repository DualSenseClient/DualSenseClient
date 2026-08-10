namespace DualSenseClient.VIIPER.NS2Pro;

/// <summary>
/// Nintendo Switch 2 Pro Controller feature bit flags.
/// </summary>
[Flags]
public enum NS2ProFeatureFlags : byte
{
    Buttons = 0x01,
    Sticks = 0x02,
    Imu = 0x04,
    Mouse = 0x10,
    Rumble = 0x20
}