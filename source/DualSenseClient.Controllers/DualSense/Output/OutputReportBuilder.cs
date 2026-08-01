using DualSenseClient.Controllers.DualSense.Triggers;

namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// Convenience builder for the DualSense output report fields that matter to an app:
/// rumble motors, adaptive trigger effects, mute LED, player LEDs, and the lightbar.
/// </summary>
/// <remarks>
/// <para>
/// Set the desired fields, then call <see cref="Build"/>. Unlike the low-level
/// <see cref="SetStateData"/> (which requires explicit valid flags), this builder derives
/// the valid flags from the fields set, so the controller actually applies them.
/// </para>
/// </remarks>
public sealed class OutputReportBuilder
{
    /// <summary>
    /// Rumble right motor / high-frequency (0-255).
    /// </summary>
    public byte RumbleRight { get; set; }

    /// <summary>
    /// Rumble left motor / low-frequency (0-255).
    /// </summary>
    public byte RumbleLeft { get; set; }

    /// <summary>
    /// Mute LED mode: <c>0x00</c> off, <c>0x01</c> on, <c>0x02</c> pulse.
    /// </summary>
    public byte MuteLedMode { get; set; }

    /// <summary>
    /// Right (R2) adaptive trigger effect.
    /// </summary>
    public TriggerEffectBlock R2TriggerEffect { get; set; } = TriggerEffectBuilder.Off();

    /// <summary>
    /// Left (L2) adaptive trigger effect.
    /// </summary>
    public TriggerEffectBlock L2TriggerEffect { get; set; } = TriggerEffectBuilder.Off();

    /// <summary>
    /// Light fade animation: <c>0x01</c> fade-in, <c>0x02</c> fade-out.
    /// </summary>
    public byte LightFadeAnimation { get; set; }

    /// <summary>
    /// Lightbar brightness: <c>0x00</c> high, <c>0x01</c> medium, <c>0x02</c> low.
    /// </summary>
    public byte LightBrightness { get; set; }

    /// <summary>
    /// Player LED mask (bits 0-4 = LEDs, bit 5 = fade).
    /// </summary>
    public PlayerLedMask PlayerLeds { get; set; } = PlayerLedMask.None;

    /// <summary>
    /// Lightbar red channel (0-255).
    /// </summary>
    public byte LedRed { get; set; }

    /// <summary>
    /// Lightbar green channel (0-255).
    /// </summary>
    public byte LedGreen { get; set; }

    /// <summary>
    /// Lightbar blue channel (0-255).
    /// </summary>
    public byte LedBlue { get; set; }

    /// <summary>
    /// Builds the <see cref="SetStateData"/> payload, deriving the valid flag bytes from
    /// the fields that are set. Frame the result with <see cref="OutputReport.ForUsb"/> or
    /// <see cref="OutputReport.ForBluetooth"/> before sending.
    /// </summary>
    public SetStateData Build()
    {
        ValidFlags flag0 = ValidFlags.EnableRumbleEmulation;
        if (R2TriggerEffect.Mode != TriggerEffectType.Off)
        {
            flag0 |= ValidFlags.AllowRightTriggerFfb;
        }
        if (L2TriggerEffect.Mode != TriggerEffectType.Off)
        {
            flag0 |= ValidFlags.AllowLeftTriggerFfb;
        }

        ValidFlags flag1 = ValidFlags.AllowMuteLight | ValidFlags.AllowLedColor;
        if (PlayerLeds != PlayerLedMask.None)
        {
            flag1 |= ValidFlags.AllowPlayerIndicators;
        }

        ValidFlags flag2 = ValidFlags.AllowBrightnessChange | ValidFlags.AllowColorFadeAnim;

        return new SetStateData
        {
            ValidFlag0 = flag0,
            ValidFlag1 = flag1,
            RumbleRight = RumbleRight,
            RumbleLeft = RumbleLeft,
            MuteLedMode = MuteLedMode,
            R2TriggerEffect = R2TriggerEffect,
            L2TriggerEffect = L2TriggerEffect,
            ValidFlag2 = flag2,
            LightFadeAnimation = LightFadeAnimation,
            LightBrightness = LightBrightness,
            PlayerLeds = PlayerLeds,
            LedRed = LedRed,
            LedGreen = LedGreen,
            LedBlue = LedBlue
        };
    }
}