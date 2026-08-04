namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// Audio output route carried by the report <c>0x35</c> route tag.
/// </summary>
public enum BluetoothAudioRoute : byte
{
    /// <summary>
    /// Route to the internal speaker (route tag <c>0x93</c>).
    /// </summary>
    Speaker = 0x93,

    /// <summary>
    /// Route to the headset jack (route tag <c>0x96</c>).
    /// </summary>
    Headset = 0x96
}