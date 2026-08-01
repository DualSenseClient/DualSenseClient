namespace DualSenseClient.Controllers.DualSense.Triggers;

/// <summary>
/// Adaptive trigger effect modes accepted directly by the controller.
/// </summary>
public enum TriggerEffectType : byte
{
    /// <summary>
    /// No resistance; the trigger moves freely.
    /// </summary>
    Off = 0x00,

    /// <summary>
    /// Constant resistance beginning at a start position.
    /// </summary>
    Resistance = 0x01,

    /// <summary>
    /// Resistance between a start and end position ("weapon" mode).
    /// </summary>
    Trigger = 0x02,

    /// <summary>
    /// Vibrating/automatic effect at a given frequency (0-15).
    /// </summary>
    Automatic = 0x06,

    /// <summary>
    /// Resistance across a set of zones (multi-position feedback).
    /// </summary>
    MultiplePositionFeedback = 0x21,

    /// <summary>
    /// Vibration across a set of zones (multi-position vibration).
    /// </summary>
    MultiplePositionVibration = 0x26
}