namespace DualSenseClient.Controllers.DualSense.Triggers;

/// <summary>
/// Creates <see cref="TriggerEffectBlock"/> values for the simple effect modes.
/// </summary>
/// <remarks>
/// Parameter order follows the verified layout, where mode <c>0x06</c> reads
/// <i>frequency, force, start position</i> — not the other way round.
/// </remarks>
public static class TriggerEffectBuilder
{
    /// <summary>
    /// No resistance; the trigger moves freely.
    /// </summary>
    public static TriggerEffectBlock Off() => new TriggerEffectBlock(new byte[11], 0);

    /// <summary>
    /// Constant resistance beginning at <paramref name="startPosition"/>.
    /// </summary>
    /// <param name="startPosition">Trigger position where the resistance begins (0-255).</param>
    /// <param name="force">Resistance force (0-255).</param>
    public static TriggerEffectBlock Resistance(byte startPosition, byte force)
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Resistance;
        raw[1] = startPosition;
        raw[2] = force;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Resistance between <paramref name="startPosition"/> and <paramref name="endPosition"/>
    /// ("weapon" mode).
    /// </summary>
    /// <param name="startPosition">Trigger position where the resistance begins (0-255).</param>
    /// <param name="endPosition">Trigger position where the resistance ends (0-255).</param>
    /// <param name="force">Resistance force (0-255).</param>
    public static TriggerEffectBlock Trigger(byte startPosition, byte endPosition, byte force)
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Trigger;
        raw[1] = startPosition;
        raw[2] = endPosition;
        raw[3] = force;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Vibrating/automatic effect at <paramref name="frequency"/> (§5.2).
    /// </summary>
    /// <param name="frequency">Effect frequency (0-15).</param>
    /// <param name="force">Vibration force (0-255).</param>
    /// <param name="startPosition">Trigger position where the effect begins (0-255).</param>
    public static TriggerEffectBlock Automatic(byte frequency, byte force, byte startPosition)
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Automatic;
        raw[1] = frequency;
        raw[2] = force;
        raw[3] = startPosition;
        return new TriggerEffectBlock(raw, 0);
    }
}