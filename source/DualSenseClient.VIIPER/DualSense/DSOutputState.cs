using System.Runtime.InteropServices;

namespace DualSenseClient.VIIPER.DualSense;

/// <summary>
/// Full output state of a DualSense device, delivered by output callbacks.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DSOutputState
{
    /// <summary>
    /// Small rumble motor intensity (0-255).
    /// </summary>
    public byte RumbleSmall;

    /// <summary>
    /// Large rumble motor intensity (0-255).
    /// </summary>
    public byte RumbleLarge;

    /// <summary>
    /// Lightbar red component (0-255).
    /// </summary>
    public byte LedRed;

    /// <summary>
    /// Lightbar green component (0-255).
    /// </summary>
    public byte LedGreen;

    /// <summary>
    /// Lightbar blue component (0-255).
    /// </summary>
    public byte LedBlue;

    /// <summary>
    /// Player indicator LED bitmask.
    /// </summary>
    public byte PlayerLeds;

    /// <summary>
    /// R2 trigger mode (0 = off).
    /// </summary>
    public byte TriggerR2Mode;

    /// <summary>
    /// R2 trigger start resistance.
    /// </summary>
    public byte TriggerR2StartResistance;

    /// <summary>
    /// R2 trigger effect force.
    /// </summary>
    public byte TriggerR2EffectForce;

    /// <summary>
    /// R2 trigger range force.
    /// </summary>
    public byte TriggerR2RangeForce;

    /// <summary>
    /// R2 trigger near-release strength.
    /// </summary>
    public byte TriggerR2NearReleaseStrength;

    /// <summary>
    /// R2 trigger near-middle strength.
    /// </summary>
    public byte TriggerR2NearMiddleStrength;

    /// <summary>
    /// R2 trigger pressed strength.
    /// </summary>
    public byte TriggerR2PressedStrength;

    /// <summary>
    /// R2 trigger vibration frequency.
    /// </summary>
    public byte TriggerR2Frequency;

    /// <summary>
    /// L2 trigger mode (0 = off).
    /// </summary>
    public byte TriggerL2Mode;

    /// <summary>
    /// L2 trigger start resistance.
    /// </summary>
    public byte TriggerL2StartResistance;

    /// <summary>
    /// L2 trigger effect force.
    /// </summary>
    public byte TriggerL2EffectForce;

    /// <summary>
    /// L2 trigger range force.
    /// </summary>
    public byte TriggerL2RangeForce;

    /// <summary>
    /// L2 trigger near-release strength.
    /// </summary>
    public byte TriggerL2NearReleaseStrength;

    /// <summary>
    /// L2 trigger near-middle strength.
    /// </summary>
    public byte TriggerL2NearMiddleStrength;

    /// <summary>
    /// L2 trigger pressed strength.
    /// </summary>
    public byte TriggerL2PressedStrength;

    /// <summary>
    /// L2 trigger vibration frequency.
    /// </summary>
    public byte TriggerL2Frequency;

    /// <summary>
    /// 48-byte native USB output report.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
    public byte[] RawOutputReport;

    /// <summary>
    /// 398-byte combined Bluetooth output report.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 398)]
    public byte[] BluetoothCombinedOutputReport;

    /// <summary>
    /// 0=off, 1=on, 2=pulse (native report byte 9).
    /// </summary>
    public byte MicLed;

    /// <summary>
    /// 0x01 default, 0x02 custom (native report byte 42).
    /// </summary>
    public byte LightbarSetup;

    /// <summary>
    /// 0=high, 1=medium, 2=low (native report byte 43).
    /// </summary>
    public byte LightbarBrightness;
}