using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// Full output state of a DualSense device, delivered by output callbacks.
/// Matches libVIIPER DSOutputState (47-byte USB output payload, report ID excluded).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DSOutputState
{
    public byte Flags0;
    public byte Flags1;
    public byte RumbleSmall;
    public byte RumbleLarge;
    public byte VolumeHeadphones;
    public byte VolumeSpeaker;
    public byte VolumeMic;
    public byte AudioControl;
    public byte MuteLightMode;
    public byte MuteControl;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
    public byte[] TriggerRight;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
    public byte[] TriggerLeft;

    public uint HostTimestamp;
    public byte MotorPower;
    public byte AudioControl2;
    public byte Flags3;
    public byte HapticFilter;
    public byte UnkByte;
    public byte LightFade;
    public byte LightBrightness;
    public byte PlayerLeds;
    public byte LedRed;
    public byte LedGreen;
    public byte LedBlue;
}