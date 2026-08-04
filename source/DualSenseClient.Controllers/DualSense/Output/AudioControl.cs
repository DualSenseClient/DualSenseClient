namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// Audio control bits for <see cref="SetStateData.AudioControl"/> (payload offset 7).
/// </summary>
[Flags]
public enum AudioControl : byte
{
    /// <summary>
    /// Automatic microphone selection.
    /// </summary>
    MicSelectAuto = 0x00,

    /// <summary>
    /// Internal microphone selected.
    /// </summary>
    MicSelectInternal = 0x01,

    /// <summary>
    /// External microphone selected.
    /// </summary>
    MicSelectExternal = 0x02,

    /// <summary>
    /// Mask for the microphone select field (bits 0-1).
    /// </summary>
    MicSelectMask = 0x03,

    /// <summary>
    /// Enable audio echo cancellation.
    /// </summary>
    EchoCancelEnable = 0x04,

    /// <summary>
    /// Enable audio noise cancellation.
    /// </summary>
    NoiseCancelEnable = 0x08,

    /// <summary>
    /// Route audio output to the headphones (L+R channels to the jack, speaker muted).
    /// </summary>
    OutputPathHeadphones = 0x00,

    /// <summary>
    /// Route audio output to both the headphones and the internal speaker. The speaker is
    /// mono, so the output-path matrix splits the source: left channel to the headset and
    /// right channel to the speaker.
    /// </summary>
    OutputPathBoth = 0x20,

    /// <summary>
    /// Route audio output to the speaker (right channel to the mono speaker, headset muted).
    /// </summary>
    OutputPathSpeaker = 0x30,

    /// <summary>
    /// Mask for the output path field (bits 4-5).
    /// </summary>
    OutputPathMask = 0x30,

    /// <summary>
    /// Mask for the input path field (bits 6-7).
    /// </summary>
    InputPathMask = 0xC0
}