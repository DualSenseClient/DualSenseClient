namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// Valid flag bits that gate which fields in <see cref="SetStateData"/> the controller
/// applies. Clearing a bit makes the controller ignore the corresponding field
/// and retain its current state.
/// </summary>
/// <remarks>
/// The bits span three independent bytes — Valid Flag 0 (payload offset 0), Valid Flag 1
/// (payload offset 1) and Valid Flag 2 (payload offset 38) — so a value is written to
/// whichever byte the <see cref="SetStateData"/> property targets.
/// </remarks>
[Flags]
public enum ValidFlags : byte
{
    /// <summary>
    /// No features enabled.
    /// </summary>
    None = 0,

    // Valid Flag 0 (payload offset 0)
    /// <summary>
    /// Enable rumble motor emulation.
    /// </summary>
    EnableRumbleEmulation = 1 << 0,

    /// <summary>
    /// Use rumble motors instead of haptics.
    /// </summary>
    UseRumbleNotHaptics = 1 << 1,

    /// <summary>
    /// Allow the right (R2) adaptive trigger effect to be applied.
    /// </summary>
    AllowRightTriggerFfb = 1 << 2,

    /// <summary>
    /// Allow the left (L2) adaptive trigger effect to be applied.
    /// </summary>
    AllowLeftTriggerFfb = 1 << 3,

    /// <summary>
    /// Allow the headphone volume field to be applied.
    /// </summary>
    AllowHeadphoneVolume = 1 << 4,

    /// <summary>
    /// Allow the speaker volume field to be applied.
    /// </summary>
    AllowSpeakerVolume = 1 << 5,

    /// <summary>
    /// Allow the microphone volume field to be applied.
    /// </summary>
    AllowMicVolume = 1 << 6,

    /// <summary>
    /// Allow the audio control field to be applied.
    /// </summary>
    AllowAudioControl = 1 << 7,

    // Valid Flag 1 (payload offset 1)
    /// <summary>
    /// Allow the mute LED mode field to be applied.
    /// </summary>
    AllowMuteLight = 1 << 0,

    /// <summary>
    /// Allow audio mute / power save control.
    /// </summary>
    AllowAudioMute = 1 << 1,

    /// <summary>
    /// Allow the LED color fields to be applied.
    /// </summary>
    AllowLedColor = 1 << 2,

    /// <summary>
    /// Reset the lights.
    /// </summary>
    ResetLights = 1 << 3,

    /// <summary>
    /// Allow the player LED mask to be applied.
    /// </summary>
    AllowPlayerIndicators = 1 << 4,

    /// <summary>
    /// Allow the haptic low-pass filter field to be applied.
    /// </summary>
    AllowHapticLpf = 1 << 5,

    /// <summary>
    /// Allow the motor power level field to be applied.
    /// </summary>
    AllowMotorPowerLevel = 1 << 6,

    /// <summary>
    /// Allow the audio control 2 field to be applied.
    /// </summary>
    AllowAudioControl2 = 1 << 7,

    // Valid Flag 2 (payload offset 38)
    /// <summary>
    /// Allow the light brightness field to be applied.
    /// </summary>
    AllowBrightnessChange = 1 << 0,

    /// <summary>
    /// Allow the light fade animation field to be applied.
    /// </summary>
    AllowColorFadeAnim = 1 << 1,

    /// <summary>
    /// Enable improved rumble emulation.
    /// </summary>
    EnableImprovedRumbleEmu = 1 << 2
}