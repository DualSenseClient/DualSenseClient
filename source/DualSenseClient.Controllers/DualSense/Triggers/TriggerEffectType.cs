namespace DualSenseClient.Controllers.DualSense.Triggers;

/// <summary>
/// Adaptive trigger effect modes accepted directly by the controller.
/// Byte values follow the verified firmware layout documented by Nielk1
/// (see README credits); see <see cref="TriggerEffectBuilder"/> for the
/// per-mode parameter packing.
/// </summary>
public enum TriggerEffectType : byte
{
    /// <summary>
    /// Turn the trigger effect off and return the trigger stop to the neutral position.
    /// Official effect.
    /// </summary>
    Off = 0x05,

    /// <summary>
    /// Trigger will resist movement beyond the start position.
    /// Official effect (multi-zone packing).
    /// </summary>
    Feedback = 0x21,

    /// <summary>
    /// Trigger will resist movement beyond the start position until the end position.
    /// Official effect ("weapon" mode).
    /// </summary>
    Weapon = 0x25,

    /// <summary>
    /// Trigger will vibrate beyond the start position.
    /// Official effect.
    /// </summary>
    Vibration = 0x26,

    /// <summary>
    /// Weapon-like effect with a snap-back force. Unofficial; may be removed in future firmware.
    /// </summary>
    Bow = 0x22,

    /// <summary>
    /// Rhythmic galloping oscillation. Unofficial; may be removed in future firmware.
    /// </summary>
    Galloping = 0x23,

    /// <summary>
    /// Vibration oscillating between two amplitudes. Unofficial; may be removed in future firmware.
    /// </summary>
    Machine = 0x27,

    /// <summary>
    /// Simplistic feedback effect. Leftover with no parameter protection; prefer <see cref="Feedback"/>.
    /// </summary>
    Simple_Feedback = 0x01,

    /// <summary>
    /// Simplistic weapon effect. Leftover with no parameter protection; prefer <see cref="Weapon"/>.
    /// </summary>
    Simple_Weapon = 0x02,

    /// <summary>
    /// Simplistic vibration effect. Leftover with no parameter protection; prefer <see cref="Vibration"/>.
    /// </summary>
    Simple_Vibration = 0x06,

    /// <summary>
    /// Simplistic feedback effect with stricter parameter limits; prefer <see cref="Feedback"/>.
    /// </summary>
    Limited_Feedback = 0x11,

    /// <summary>
    /// Simplistic weapon effect with stricter parameter limits; prefer <see cref="Weapon"/>.
    /// </summary>
    Limited_Weapon = 0x12
}