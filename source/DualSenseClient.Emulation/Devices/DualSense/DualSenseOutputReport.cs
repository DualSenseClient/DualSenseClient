using System.Buffers.Binary;

namespace DualSenseClient.Emulation.Devices.DualSense;

// Output report layout:
// - The descriptor declares output report 0x02 ("Report ID (2)") with "Report Count (47)", so the
//   wire report is the id byte plus a 47-byte payload.
// - SET_REPORT receives the interrupt-OUT payload with the report id at buffer[0] ("case 0x02")
//   and copies the following 47 bytes into a SetStateData value ("memcpy(&state, buffer + 1,
//   sizeof(SetStateData))"), so USB report offset N corresponds to SetStateData offset N - 1.
// - The SetStateData layout: the two valid-flag bytes 0-1, the two
//   rumble emulation bytes 2-3, the three volume bytes 4-6, the AudioControl bitfield byte 7, the
//   MuteLight mode byte 8, the MuteControl bitfield byte 9, the 11-byte RightTriggerFFB block 10-20,
//   the 11-byte LeftTriggerFFB block 21-31, the host timestamp word 32-35, the motor power levels
//   byte 36, the AudioControl2 bitfield byte 37, the third valid-flag byte 38, the haptics byte 39,
//   the lightbar fade/brightness bytes 41-42, the player-indicator byte 43 and the RGB bytes 44-46.

/// <summary>
/// Decoded host output report of the DualSense (USB report id 0x02): the valid-flag bits that select
/// the fields the host wants applied, the rumble, volume, audio-control, mute-light, adaptive-trigger,
/// lightbar and player-indicator fields of the 47-byte payload. <see cref="TryParse"/> reads the
/// id-prefixed shape both delivery paths produce: the interrupt OUT path carries the id in the
/// payload (like the reference's <c>buffer[0]</c>) and the HID class SET_REPORT path gets it
/// prepended from wValue. A bare payload is only possible through a direct <c>OnOutputReport</c>
/// call and is therefore not recognized. The audio-control bitfields are carried raw for the audio
/// milestone to action.
/// </summary>
public readonly struct DualSenseOutputReport
{
    /// <summary>Report id of the output report (USB report 0x02).</summary>
    public const byte ReportId = 0x02;

    /// <summary>
    /// Length in bytes of the reference report: the report id byte plus the 47-byte SetStateData
    /// payload. Longer payloads (the endpoint buffer can be padded) parse as well and their trailing
    /// bytes are ignored.
    /// </summary>
    public const int MinimumSize = 48;

    /// <summary>
    /// Length in bytes of one raw adaptive-trigger effect block: the effect mode byte followed by the
    /// ten mode-dependent parameter bytes (the reference's RightTriggerFFB/LeftTriggerFFB arrays).
    /// </summary>
    public const int TriggerEffectSize = 11;

    /// <summary>Flag bit of the rumble emulation request (valid-flag byte 0, reference bit 0.0).</summary>
    private const byte RumbleEmulationEnabledBit = 0x01;

    /// <summary>Flag bit selecting the rumble mode (valid-flag byte 0, reference bit 0.1).</summary>
    private const byte UseRumbleNotHapticsBit = 0x02;

    /// <summary>Flag bit of the right adaptive-trigger effect allowance (valid-flag byte 0, reference bit 0.2).</summary>
    private const byte RightTriggerEffectAllowedBit = 0x04;

    /// <summary>Flag bit of the left adaptive-trigger effect allowance (valid-flag byte 0, reference bit 0.3).</summary>
    private const byte LeftTriggerEffectAllowedBit = 0x08;

    /// <summary>Flag bit of the headphone volume allowance (valid-flag byte 0, reference bit 0.4).</summary>
    private const byte HeadphoneVolumeAllowedBit = 0x10;

    /// <summary>Flag bit of the speaker volume allowance (valid-flag byte 0, reference bit 0.5).</summary>
    private const byte SpeakerVolumeAllowedBit = 0x20;

    /// <summary>Flag bit of the microphone volume allowance (valid-flag byte 0, reference bit 0.6).</summary>
    private const byte MicVolumeAllowedBit = 0x40;

    /// <summary>Flag bit of the AudioControl allowance (valid-flag byte 0, reference bit 0.7).</summary>
    private const byte AudioControlAllowedBit = 0x80;

    /// <summary>Flag bit of the mute-light mode allowance (valid-flag byte 1, reference bit 1.0).</summary>
    private const byte MuteLightAllowedBit = 0x01;

    /// <summary>Flag bit of the mute-control allowance (valid-flag byte 1, reference bit 1.1).</summary>
    private const byte AudioMuteAllowedBit = 0x02;

    /// <summary>Flag bit of the lightbar color allowance (valid-flag byte 1, reference bit 1.2).</summary>
    private const byte LightbarColorAllowedBit = 0x04;

    /// <summary>Flag bit releasing the LEDs from firmware control (valid-flag byte 1, reference bit 1.3).</summary>
    private const byte ResetLightsBit = 0x08;

    /// <summary>Flag bit of the player-indicator allowance (valid-flag byte 1, reference bit 1.4).</summary>
    private const byte PlayerIndicatorsAllowedBit = 0x10;

    /// <summary>Flag bit of the haptic low-pass filter allowance (valid-flag byte 1, reference bit 1.5).</summary>
    private const byte HapticLowPassFilterAllowedBit = 0x20;

    /// <summary>Flag bit of the motor power level allowance (valid-flag byte 1, reference bit 1.6).</summary>
    private const byte MotorPowerLevelAllowedBit = 0x40;

    /// <summary>Flag bit of the AudioControl2 allowance (valid-flag byte 1, reference bit 1.7).</summary>
    private const byte AudioControl2AllowedBit = 0x80;

    /// <summary>Flag bit of the lightbar brightness allowance (valid-flag byte 2, reference bit 38.0).</summary>
    private const byte LightBrightnessAllowedBit = 0x01;

    /// <summary>Flag bit of the lightbar fade animation allowance (valid-flag byte 2, reference bit 38.1).</summary>
    private const byte LightFadeAllowedBit = 0x02;

    /// <summary>Flag bit of the improved rumble emulation mode (valid-flag byte 2, reference bit 38.2).</summary>
    private const byte ImprovedRumbleEmulationEnabledBit = 0x04;

    /// <summary>Flag bit selecting the second rumble mode (valid-flag byte 2, reference bit 38.3).</summary>
    private const byte UseRumbleNotHaptics2Bit = 0x08;

    /// <summary>Report offset of the first valid-flag byte (payload byte 0).</summary>
    private const int OffsetFlags0 = 1;

    /// <summary>Report offset of the second valid-flag byte (payload byte 1).</summary>
    private const int OffsetFlags1 = 2;

    /// <summary>Report offset of the right (light) rumble emulation motor byte.</summary>
    private const int OffsetRumbleRight = 3;

    /// <summary>Report offset of the left (heavy) rumble emulation motor byte.</summary>
    private const int OffsetRumbleLeft = 4;

    /// <summary>Report offset of the headphone volume byte (reference "max 0x7f").</summary>
    private const int OffsetHeadphoneVolume = 5;

    /// <summary>Report offset of the speaker volume byte (reference: PS5 uses 0x3d-0x64).</summary>
    private const int OffsetSpeakerVolume = 6;

    /// <summary>Report offset of the microphone volume byte (reference: not linear, max 64).</summary>
    private const int OffsetMicVolume = 7;

    /// <summary>Report offset of the AudioControl bitfield byte (mic select, echo/noise cancel, paths).</summary>
    private const int OffsetAudioControl = 8;

    /// <summary>Report offset of the mute-light mode byte (the mic-mute LED mode).</summary>
    private const int OffsetMuteLightMode = 9;

    /// <summary>Report offset of the MuteControl bitfield byte (touch/motion/haptic/audio power save and the mic/speaker/headphone/haptic mutes).</summary>
    private const int OffsetMuteControl = 10;

    /// <summary>Report offset of the right trigger's raw 11-byte effect block (the reference's RightTriggerFFB[11]).</summary>
    private const int OffsetRightTriggerEffect = 11;

    /// <summary>Report offset of the left trigger's raw 11-byte effect block (the reference's LeftTriggerFFB[11]).</summary>
    private const int OffsetLeftTriggerEffect = 22;

    /// <summary>Report offset of the little-endian 32-bit host timestamp word.</summary>
    private const int OffsetHostTimestamp = 33;

    /// <summary>Report offset of the motor power level byte (rumble in the low nibble, trigger in the high nibble).</summary>
    private const int OffsetMotorPowerLevel = 37;

    /// <summary>Report offset of the AudioControl2 bitfield byte (speaker pre-gain, beamforming, unknown bits).</summary>
    private const int OffsetAudioControl2 = 38;

    /// <summary>Report offset of the third valid-flag byte (light brightness, fade and the rumble-mode selector bits).</summary>
    private const int OffsetFlags2 = 39;

    /// <summary>Report offset of the haptics byte carrying the low-pass filter bit (reference bit 39.0).</summary>
    private const int OffsetHapticLowPassFilter = 40;

    /// <summary>Bit of the haptic low-pass filter in the haptics byte (reference bit 39.0).</summary>
    private const byte HapticLowPassFilterBit = 0x01;

    /// <summary>Report offset of the lightbar fade animation byte (reference LightFadeAnimation: 0 none, 1 fade in, 2 fade out).</summary>
    private const int OffsetLightFadeAnimation = 42;

    /// <summary>Report offset of the lightbar brightness byte (reference LightBrightness: 0 bright, 1 mid, 2 dim).</summary>
    private const int OffsetLightBrightness = 43;

    /// <summary>Report offset of the player-indicator bitfield byte (five white LEDs plus the fade bit).</summary>
    private const int OffsetPlayerLeds = 44;

    /// <summary>Report offset of the lightbar red component.</summary>
    private const int OffsetRed = 45;

    /// <summary>Report offset of the lightbar green component.</summary>
    private const int OffsetGreen = 46;

    /// <summary>Report offset of the lightbar blue component.</summary>
    private const int OffsetBlue = 47;

    /// <summary>Whether the host requested rumble emulation (valid-flag byte 0, reference bit 0.0).</summary>
    public bool RumbleEmulationEnabled { get; init; }

    /// <summary>Whether the host selected the rumble-not-haptics mode (valid-flag byte 0, reference bit 0.1).</summary>
    public bool UseRumbleNotHaptics { get; init; }

    /// <summary>Whether the host allows the right adaptive-trigger effect block to be applied.</summary>
    public bool RightTriggerEffectAllowed { get; init; }

    /// <summary>Whether the host allows the left adaptive-trigger effect block to be applied.</summary>
    public bool LeftTriggerEffectAllowed { get; init; }

    /// <summary>Whether the host allows the headphone volume byte to be applied.</summary>
    public bool HeadphoneVolumeAllowed { get; init; }

    /// <summary>Whether the host allows the speaker volume byte to be applied.</summary>
    public bool SpeakerVolumeAllowed { get; init; }

    /// <summary>Whether the host allows the microphone volume byte to be applied.</summary>
    public bool MicVolumeAllowed { get; init; }

    /// <summary>Whether the host allows the AudioControl bitfield to be applied.</summary>
    public bool AudioControlAllowed { get; init; }

    /// <summary>Whether the host allows the mute-light mode to be applied.</summary>
    public bool MuteLightAllowed { get; init; }

    /// <summary>Whether the host allows the mute-control bitfield to be applied.</summary>
    public bool AudioMuteAllowed { get; init; }

    /// <summary>Whether the host allows the lightbar color to be applied.</summary>
    public bool LightbarColorAllowed { get; init; }

    /// <summary>Whether the host released the LEDs from firmware control (reference bit 1.3).</summary>
    public bool ResetLights { get; init; }

    /// <summary>Whether the host allows the player-indicator mask to be applied.</summary>
    public bool PlayerIndicatorsAllowed { get; init; }

    /// <summary>Whether the host allows the haptic low-pass filter setting to be applied.</summary>
    public bool HapticLowPassFilterAllowed { get; init; }

    /// <summary>Whether the host allows the motor power level byte to be applied.</summary>
    public bool MotorPowerLevelAllowed { get; init; }

    /// <summary>Whether the host allows the AudioControl2 bitfield to be applied.</summary>
    public bool AudioControl2Allowed { get; init; }

    /// <summary>Whether the host allows the lightbar brightness to be applied (valid-flag byte 2, reference bit 38.0).</summary>
    public bool LightBrightnessAllowed { get; init; }

    /// <summary>Whether the host allows the lightbar fade animation to be applied (valid-flag byte 2, reference bit 38.1).</summary>
    public bool LightFadeAllowed { get; init; }

    /// <summary>Whether the host selected the improved rumble emulation mode (valid-flag byte 2, reference bit 38.2).</summary>
    public bool ImprovedRumbleEmulationEnabled { get; init; }

    /// <summary>Whether the host selected the second rumble mode (valid-flag byte 2, reference bit 38.3).</summary>
    public bool UseRumbleNotHaptics2 { get; init; }

    /// <summary>Right (light) rumble motor strength, raw host value.</summary>
    public byte RumbleRight { get; init; }

    /// <summary>Left (heavy) rumble motor strength, raw host value.</summary>
    public byte RumbleLeft { get; init; }

    /// <summary>Headphone volume, raw host value (reference: max 0x7f).</summary>
    public byte HeadphoneVolume { get; init; }

    /// <summary>Speaker volume, raw host value (reference: PS5 uses the range 0x3d-0x64).</summary>
    public byte SpeakerVolume { get; init; }

    /// <summary>Microphone volume, raw host value (reference: not linear, max 64).</summary>
    public byte MicVolume { get; init; }

    /// <summary>Audio control bitfield, raw host value (reference byte 7: mic select and the echo/noise cancel and input/output path bits), carried for the audio milestone.</summary>
    public byte AudioControl { get; init; }

    /// <summary>Mute-light mode byte driving the mic-mute LED (reference MuteLight: 0 off, 1 on, 2 breathing).</summary>
    public byte MuteLightMode { get; init; }

    /// <summary>Mute-control bitfield, raw host value (reference byte 9: the power-save bits and the mic/speaker/headphone/haptic mutes), carried for the audio milestone.</summary>
    public byte MuteControl { get; init; }

    /// <summary>
    /// Raw 11-byte right adaptive-trigger effect block (the reference's RightTriggerFFB[11]); the
    /// block is opaque to the reference, which forwards it verbatim. Null on the default value.
    /// </summary>
    public byte[] RightTriggerEffect { get; init; }

    /// <summary>Right adaptive-trigger effect mode, the first byte of <see cref="RightTriggerEffect"/>.</summary>
    public byte RightTriggerEffectMode { get; init; }

    /// <summary>
    /// Raw 11-byte left adaptive-trigger effect block (the reference's LeftTriggerFFB[11]); the
    /// block is opaque to the reference, which forwards it verbatim. Null on the default value.
    /// </summary>
    public byte[] LeftTriggerEffect { get; init; }

    /// <summary>Left adaptive-trigger effect mode, the first byte of <see cref="LeftTriggerEffect"/>.</summary>
    public byte LeftTriggerEffectMode { get; init; }

    /// <summary>Host timestamp the controller mirrors back (reference "mirrored into report read").</summary>
    public uint HostTimestamp { get; init; }

    /// <summary>Motor power level byte, raw host value (rumble reduction in the low nibble, trigger reduction in the high nibble).</summary>
    public byte MotorPowerLevel { get; init; }

    /// <summary>Audio control 2 bitfield, raw host value (reference byte 37: speaker pre-gain, beamforming and unknown bits), carried for the audio milestone.</summary>
    public byte AudioControl2 { get; init; }

    /// <summary>Whether the host asked for the haptic low-pass filter (reference bit 39.0).</summary>
    public bool HapticLowPassFilter { get; init; }

    /// <summary>Lightbar fade animation byte (reference LightFadeAnimation: 0 nothing, 1 fade in, 2 fade out).</summary>
    public byte LightFadeAnimation { get; init; }

    /// <summary>Lightbar brightness byte (reference LightBrightness: 0 bright, 1 mid, 2 dim).</summary>
    public byte LightBrightness { get; init; }

    /// <summary>Player-indicator bitfield, raw host value (bits 0-4 light the five white LEDs, bit 5 is the fade selector).</summary>
    public byte PlayerLeds { get; init; }

    /// <summary>Lightbar red component, raw host value.</summary>
    public byte Red { get; init; }

    /// <summary>Lightbar green component, raw host value.</summary>
    public byte Green { get; init; }

    /// <summary>Lightbar blue component, raw host value.</summary>
    public byte Blue { get; init; }

    /// <summary>
    /// Parses one id-prefixed host payload: <paramref name="data"/> must start with
    /// <see cref="ReportId"/> and carry at least <see cref="MinimumSize"/> bytes. Any other report id
    /// (or an empty, truncated or bare control-path payload) yields
    /// <see langword="false"/> with <paramref name="report"/> at its default value.
    /// </summary>
    /// <param name="data">Host payload, report id first (the shape both the interrupt OUT and HID SET_REPORT paths produce).</param>
    /// <param name="report">Parsed report when the payload is recognized; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the payload carries output report <see cref="ReportId"/>; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out DualSenseOutputReport report)
    {
        if (data.Length < MinimumSize || data[0] != ReportId)
        {
            report = default;
            return false;
        }

        byte flags0 = data[OffsetFlags0];
        byte flags1 = data[OffsetFlags1];
        byte flags2 = data[OffsetFlags2];
        byte[] rightTriggerEffect = data.Slice(OffsetRightTriggerEffect, TriggerEffectSize).ToArray();
        byte[] leftTriggerEffect = data.Slice(OffsetLeftTriggerEffect, TriggerEffectSize).ToArray();
        report = new DualSenseOutputReport
        {
            RumbleEmulationEnabled = (flags0 & RumbleEmulationEnabledBit) != 0,
            UseRumbleNotHaptics = (flags0 & UseRumbleNotHapticsBit) != 0,
            RightTriggerEffectAllowed = (flags0 & RightTriggerEffectAllowedBit) != 0,
            LeftTriggerEffectAllowed = (flags0 & LeftTriggerEffectAllowedBit) != 0,
            HeadphoneVolumeAllowed = (flags0 & HeadphoneVolumeAllowedBit) != 0,
            SpeakerVolumeAllowed = (flags0 & SpeakerVolumeAllowedBit) != 0,
            MicVolumeAllowed = (flags0 & MicVolumeAllowedBit) != 0,
            AudioControlAllowed = (flags0 & AudioControlAllowedBit) != 0,
            MuteLightAllowed = (flags1 & MuteLightAllowedBit) != 0,
            AudioMuteAllowed = (flags1 & AudioMuteAllowedBit) != 0,
            LightbarColorAllowed = (flags1 & LightbarColorAllowedBit) != 0,
            ResetLights = (flags1 & ResetLightsBit) != 0,
            PlayerIndicatorsAllowed = (flags1 & PlayerIndicatorsAllowedBit) != 0,
            HapticLowPassFilterAllowed = (flags1 & HapticLowPassFilterAllowedBit) != 0,
            MotorPowerLevelAllowed = (flags1 & MotorPowerLevelAllowedBit) != 0,
            AudioControl2Allowed = (flags1 & AudioControl2AllowedBit) != 0,
            LightBrightnessAllowed = (flags2 & LightBrightnessAllowedBit) != 0,
            LightFadeAllowed = (flags2 & LightFadeAllowedBit) != 0,
            ImprovedRumbleEmulationEnabled = (flags2 & ImprovedRumbleEmulationEnabledBit) != 0,
            UseRumbleNotHaptics2 = (flags2 & UseRumbleNotHaptics2Bit) != 0,
            RumbleRight = data[OffsetRumbleRight],
            RumbleLeft = data[OffsetRumbleLeft],
            HeadphoneVolume = data[OffsetHeadphoneVolume],
            SpeakerVolume = data[OffsetSpeakerVolume],
            MicVolume = data[OffsetMicVolume],
            AudioControl = data[OffsetAudioControl],
            MuteLightMode = data[OffsetMuteLightMode],
            MuteControl = data[OffsetMuteControl],
            RightTriggerEffect = rightTriggerEffect,
            RightTriggerEffectMode = rightTriggerEffect[0],
            LeftTriggerEffect = leftTriggerEffect,
            LeftTriggerEffectMode = leftTriggerEffect[0],
            HostTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(data[OffsetHostTimestamp..]),
            MotorPowerLevel = data[OffsetMotorPowerLevel],
            AudioControl2 = data[OffsetAudioControl2],
            HapticLowPassFilter = (data[OffsetHapticLowPassFilter] & HapticLowPassFilterBit) != 0,
            LightFadeAnimation = data[OffsetLightFadeAnimation],
            LightBrightness = data[OffsetLightBrightness],
            PlayerLeds = data[OffsetPlayerLeds],
            Red = data[OffsetRed],
            Green = data[OffsetGreen],
            Blue = data[OffsetBlue]
        };
        return true;
    }
}