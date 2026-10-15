namespace DualSenseClient.Controllers.DualSense.Triggers;

/// <summary>
/// Creates <see cref="TriggerEffectBlock"/> values for the adaptive trigger effect modes.
/// </summary>
/// <remarks>
/// Byte layouts and validation follow Nielk1's verified TriggerEffectGenerator
/// (revision 6, see README credits). Official modes use 10 trigger zones
/// (position 0-9) with strength 0-8 packed as 3 bits per zone; the simple modes
/// use raw 0-255 bytes with no packing. Out-of-range parameters throw
/// <see cref="ArgumentOutOfRangeException"/>; a zero-effect parameter set yields
/// an <see cref="TriggerEffectType.Off"/> block, matching Sony's official behavior.
/// </remarks>
public static class TriggerEffectBuilder
{
    /// <summary>
    /// Turn the trigger effect off and return the trigger stop to the neutral position.
    /// </summary>
    public static TriggerEffectBlock Off()
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Off;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Trigger will resist movement beyond the start position.
    /// </summary>
    /// <param name="position">Starting zone of the effect (0-9).</param>
    /// <param name="strength">Force of the resistance (0-8, 0 yields Off).</param>
    public static TriggerEffectBlock Feedback(byte position, byte strength)
    {
        if (position > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be 0-9.");
        }

        if (strength > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be 0-8.");
        }

        if (strength == 0)
        {
            return Off();
        }

        byte forceValue = (byte)((strength - 1) & 0x07);
        uint forceZones = 0;
        ushort activeZones = 0;
        for (int i = position; i < 10; i++)
        {
            forceZones |= (uint)(forceValue << (3 * i));
            activeZones |= (ushort)(1 << i);
        }

        return BuildFeedbackBlock(activeZones, forceZones);
    }

    /// <summary>
    /// Trigger will resist movement beyond the start position until the end position.
    /// </summary>
    /// <param name="startPosition">Starting zone (2-7).</param>
    /// <param name="endPosition">Ending zone (start+1 to 8).</param>
    /// <param name="strength">Force of the resistance (0-8, 0 yields Off).</param>
    public static TriggerEffectBlock Weapon(byte startPosition, byte endPosition, byte strength)
    {
        if (startPosition is < 2 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(startPosition), "Start position must be 2-7.");
        }

        if (endPosition > 8 || endPosition <= startPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be start+1 to 8.");
        }

        if (strength > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be 0-8.");
        }

        if (strength == 0)
        {
            return Off();
        }

        ushort startAndStopZones = (ushort)((1 << startPosition) | (1 << endPosition));
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Weapon;
        raw[1] = (byte)(startAndStopZones & 0xff);
        raw[2] = (byte)((startAndStopZones >> 8) & 0xff);
        raw[3] = (byte)(strength - 1);
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Trigger will vibrate with the input amplitude and frequency beyond the start position.
    /// </summary>
    /// <param name="position">Starting zone (0-9).</param>
    /// <param name="amplitude">Strength of the cycling action (0-8, 0 yields Off).</param>
    /// <param name="frequency">Frequency of the cycling action in hertz (0 yields Off).</param>
    public static TriggerEffectBlock Vibration(byte position, byte amplitude, byte frequency)
    {
        if (position > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be 0-9.");
        }

        if (amplitude > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), "Amplitude must be 0-8.");
        }

        if (amplitude == 0 || frequency == 0)
        {
            return Off();
        }

        byte strengthValue = (byte)((amplitude - 1) & 0x07);
        uint amplitudeZones = 0;
        ushort activeZones = 0;
        for (int i = position; i < 10; i++)
        {
            amplitudeZones |= (uint)(strengthValue << (3 * i));
            activeZones |= (ushort)(1 << i);
        }

        byte[] raw = PackedZonesBlock(TriggerEffectType.Vibration, activeZones, amplitudeZones);
        raw[9] = frequency;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Trigger will resist movement at varying strengths in 10 regions.
    /// </summary>
    /// <remarks>
    /// Entries above 8 throw instead of silently wrapping (the gist masks with
    /// <c>&amp; 0x07</c>); this matches the builder's throw-on-out-of-range contract.
    /// </remarks>
    /// <param name="strength">10 resistance values for zones 0-9 (each 0-8).</param>
    public static TriggerEffectBlock MultiplePositionFeedback(byte[] strength)
    {
        if (strength.Length != 10)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must have exactly 10 entries.");
        }

        if (strength.Any(s => s > 8))
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength entries must be 0-8.");
        }

        if (strength.All(s => s == 0))
        {
            return Off();
        }

        uint forceZones = 0;
        ushort activeZones = 0;
        for (int i = 0; i < 10; i++)
        {
            if (strength[i] > 0)
            {
                byte forceValue = (byte)((strength[i] - 1) & 0x07);
                forceZones |= (uint)(forceValue << (3 * i));
                activeZones |= (ushort)(1 << i);
            }
        }

        return BuildFeedbackBlock(activeZones, forceZones);
    }

    /// <summary>
    /// Trigger will resist movement at a linear range of strengths.
    /// </summary>
    /// <param name="startPosition">Starting zone (0-8).</param>
    /// <param name="endPosition">Ending zone (start+1 to 9).</param>
    /// <param name="startStrength">Force at the start (1-8).</param>
    /// <param name="endStrength">Force at the end (1-8).</param>
    public static TriggerEffectBlock SlopeFeedback(byte startPosition, byte endPosition, byte startStrength, byte endStrength)
    {
        if (startPosition > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(startPosition), "Start position must be 0-8.");
        }

        if (endPosition > 9 || endPosition <= startPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be start+1 to 9.");
        }

        if (startStrength is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(startStrength), "Start strength must be 1-8.");
        }

        if (endStrength is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(endStrength), "End strength must be 1-8.");
        }

        byte[] strength = new byte[10];
        float slope = 1.0f * (endStrength - startStrength) / (endPosition - startPosition);
        for (int i = startPosition; i < 10; i++)
        {
            strength[i] = i <= endPosition
                ? (byte)Math.Round(startStrength + slope * (i - startPosition))
                : endStrength;
        }

        return MultiplePositionFeedback(strength);
    }

    /// <summary>
    /// Trigger will vibrate at varying amplitudes and one frequency in 10 regions.
    /// </summary>
    /// <param name="frequency">Frequency of the cycling action in hertz (0 yields Off).</param>
    /// <param name="amplitude">10 amplitude values for zones 0-9 (each 0-8).</param>
    public static TriggerEffectBlock MultiplePositionVibration(byte frequency, byte[] amplitude)
    {
        if (amplitude.Length != 10)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), "Amplitude must have exactly 10 entries.");
        }

        if (amplitude.Any(a => a > 8))
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude), "Amplitude entries must be 0-8.");
        }

        if (frequency == 0 || amplitude.All(a => a == 0))
        {
            return Off();
        }

        uint strengthZones = 0;
        ushort activeZones = 0;
        for (int i = 0; i < 10; i++)
        {
            if (amplitude[i] > 0)
            {
                byte strengthValue = (byte)((amplitude[i] - 1) & 0x07);
                strengthZones |= (uint)(strengthValue << (3 * i));
                activeZones |= (ushort)(1 << i);
            }
        }

        byte[] raw = PackedZonesBlock(TriggerEffectType.Vibration, activeZones, strengthZones);
        raw[9] = frequency;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Weapon-like effect with a snap-back force that attempts to reset the trigger.
    /// Unofficial; may be removed in future firmware.
    /// </summary>
    /// <param name="startPosition">Starting zone (0-8).</param>
    /// <param name="endPosition">Ending zone (start+1 to 8).</param>
    /// <param name="strength">Force of the resistance (0-8).</param>
    /// <param name="snapForce">Force of the snap-back (0-8).</param>
    public static TriggerEffectBlock Bow(byte startPosition, byte endPosition, byte strength, byte snapForce)
    {
        if (startPosition > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(startPosition), "Start position must be 0-8.");
        }

        if (endPosition > 8 || endPosition <= startPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be start+1 to 8.");
        }

        if (strength > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be 0-8.");
        }

        if (snapForce > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(snapForce), "Snap force must be 0-8.");
        }

        if (endPosition == 0 || strength == 0 || snapForce == 0)
        {
            return Off();
        }

        ushort startAndStopZones = (ushort)((1 << startPosition) | (1 << endPosition));
        uint forcePair = (uint)((((strength - 1) & 0x07) << (3 * 0))
                                | (((snapForce - 1) & 0x07) << (3 * 1)));

        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Bow;
        raw[1] = (byte)(startAndStopZones & 0xff);
        raw[2] = (byte)((startAndStopZones >> 8) & 0xff);
        raw[3] = (byte)(forcePair & 0xff);
        raw[4] = (byte)((forcePair >> 8) & 0xff);
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Rhythmic galloping oscillation. Unofficial; may be removed in future firmware.
    /// </summary>
    /// <param name="startPosition">Starting zone (0-8).</param>
    /// <param name="endPosition">Ending zone (start+1 to 9).</param>
    /// <param name="firstFoot">Position of first foot in cycle (0-6).</param>
    /// <param name="secondFoot">Position of second foot in cycle (first+1 to 7).</param>
    /// <param name="frequency">Frequency of the cycling action in hertz (0 yields Off).</param>
    public static TriggerEffectBlock Galloping(byte startPosition, byte endPosition, byte firstFoot, byte secondFoot, byte frequency)
    {
        if (startPosition > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(startPosition), "Start position must be 0-8.");
        }

        if (endPosition > 9 || endPosition <= startPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be start+1 to 9.");
        }

        if (firstFoot > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFoot), "First foot must be 0-6.");
        }

        if (secondFoot > 7 || secondFoot <= firstFoot)
        {
            throw new ArgumentOutOfRangeException(nameof(secondFoot), "Second foot must be first+1 to 7.");
        }

        if (frequency == 0)
        {
            return Off();
        }

        ushort startAndStopZones = (ushort)((1 << startPosition) | (1 << endPosition));
        uint timeAndRatio = (uint)(((secondFoot & 0x07) << (3 * 0))
                                   | ((firstFoot & 0x07) << (3 * 1)));

        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Galloping;
        raw[1] = (byte)(startAndStopZones & 0xff);
        raw[2] = (byte)((startAndStopZones >> 8) & 0xff);
        raw[3] = (byte)(timeAndRatio & 0xff);
        raw[4] = frequency;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Vibration oscillating between two amplitudes. Unofficial; may be removed in future firmware.
    /// </summary>
    /// <param name="startPosition">Starting zone (0-8).</param>
    /// <param name="endPosition">Ending zone (start+1 to 9).</param>
    /// <param name="amplitudeA">Primary strength (0-7).</param>
    /// <param name="amplitudeB">Secondary strength (0-7).</param>
    /// <param name="frequency">Frequency of the cycling action in hertz (0 yields Off).</param>
    /// <param name="period">Period of the oscillation between amplitudes in tenths of a second.</param>
    public static TriggerEffectBlock Machine(byte startPosition, byte endPosition, byte amplitudeA, byte amplitudeB, byte frequency, byte period)
    {
        if (startPosition > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(startPosition), "Start position must be 0-8.");
        }

        if (endPosition > 9 || endPosition <= startPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be start+1 to 9.");
        }

        if (amplitudeA > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitudeA), "Amplitude A must be 0-7.");
        }

        if (amplitudeB > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitudeB), "Amplitude B must be 0-7.");
        }

        if (frequency == 0)
        {
            return Off();
        }

        ushort startAndStopZones = (ushort)((1 << startPosition) | (1 << endPosition));
        uint strengthPair = (uint)(((amplitudeA & 0x07) << (3 * 0))
                                   | ((amplitudeB & 0x07) << (3 * 1)));

        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Machine;
        raw[1] = (byte)(startAndStopZones & 0xff);
        raw[2] = (byte)((startAndStopZones >> 8) & 0xff);
        raw[3] = (byte)(strengthPair & 0xff);
        raw[4] = frequency;
        raw[5] = period;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Simplistic feedback effect. Prefer <see cref="Feedback(byte,byte)"/>.
    /// </summary>
    public static TriggerEffectBlock SimpleFeedback(byte position, byte strength)
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Simple_Feedback;
        raw[1] = position;
        raw[2] = strength;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Simplistic weapon effect. Prefer <see cref="Weapon(byte,byte,byte)"/>.
    /// </summary>
    public static TriggerEffectBlock SimpleWeapon(byte startPosition, byte endPosition, byte strength)
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Simple_Weapon;
        raw[1] = startPosition;
        raw[2] = endPosition;
        raw[3] = strength;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Simplistic vibration effect. Prefer <see cref="Vibration(byte,byte,byte)"/>.
    /// </summary>
    public static TriggerEffectBlock SimpleVibration(byte position, byte amplitude, byte frequency)
    {
        if (frequency == 0 || amplitude == 0)
        {
            return Off();
        }

        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Simple_Vibration;
        raw[1] = frequency;
        raw[2] = amplitude;
        raw[3] = position;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Simplistic feedback effect with stricter limits. Prefer <see cref="Feedback(byte,byte)"/>.
    /// </summary>
    /// <param name="position">Starting zone.</param>
    /// <param name="strength">Force (0-10, 0 yields Off).</param>
    public static TriggerEffectBlock LimitedFeedback(byte position, byte strength)
    {
        if (strength > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be 0-10.");
        }

        if (strength == 0)
        {
            return Off();
        }

        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Limited_Feedback;
        raw[1] = position;
        raw[2] = strength;
        return new TriggerEffectBlock(raw, 0);
    }

    /// <summary>
    /// Simplistic weapon effect with stricter limits. Prefer <see cref="Weapon(byte,byte,byte)"/>.
    /// </summary>
    /// <param name="startPosition">Starting zone (0x10 or higher).</param>
    /// <param name="endPosition">Ending zone (start to start+100).</param>
    /// <param name="strength">Force (0-10, 0 yields Off).</param>
    public static TriggerEffectBlock LimitedWeapon(byte startPosition, byte endPosition, byte strength)
    {
        if (startPosition < 0x10)
        {
            throw new ArgumentOutOfRangeException(nameof(startPosition), "Start position must be 0x10 or higher.");
        }

        if (endPosition < startPosition || endPosition > startPosition + 100)
        {
            throw new ArgumentOutOfRangeException(nameof(endPosition), "End position must be start to start+100.");
        }

        if (strength > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "Strength must be 0-10.");
        }

        if (strength == 0)
        {
            return Off();
        }

        byte[] raw = new byte[11];
        raw[0] = (byte)TriggerEffectType.Limited_Weapon;
        raw[1] = startPosition;
        raw[2] = endPosition;
        raw[3] = strength;
        return new TriggerEffectBlock(raw, 0);
    }

    private static TriggerEffectBlock BuildFeedbackBlock(ushort activeZones, uint forceZones)
    {
        byte[] raw = PackedZonesBlock(TriggerEffectType.Feedback, activeZones, forceZones);
        return new TriggerEffectBlock(raw, 0);
    }

    private static byte[] PackedZonesBlock(TriggerEffectType mode, ushort activeZones, uint forceZones)
    {
        byte[] raw = new byte[11];
        raw[0] = (byte)mode;
        raw[1] = (byte)(activeZones & 0xff);
        raw[2] = (byte)((activeZones >> 8) & 0xff);
        raw[3] = (byte)(forceZones & 0xff);
        raw[4] = (byte)((forceZones >> 8) & 0xff);
        raw[5] = (byte)((forceZones >> 16) & 0xff);
        raw[6] = (byte)((forceZones >> 24) & 0xff);
        return raw;
    }
}