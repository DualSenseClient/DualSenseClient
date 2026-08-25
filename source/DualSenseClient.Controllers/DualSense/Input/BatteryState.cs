using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// DualSense battery status.
/// </summary>
public readonly struct BatteryState : IEquatable<BatteryState>
{
    /// <summary>
    /// Raw battery report byte from the controller.
    /// </summary>
    public byte Raw { get; }

    /// <summary>
    /// Initializes a new battery state from a raw report byte.
    /// </summary>
    public BatteryState(byte raw)
    {
        Raw = raw;
    }

    /// <summary>
    /// Battery level as a raw nibble value (0-10, where 10 is full).
    /// </summary>
    public byte RawLevel
    {
        get
        {
            return (byte)(Raw & 0x0F);
        }
    }

    /// <summary>
    /// Battery level as a percentage (0-100), or null if unknown.
    /// </summary>
    public int? Percentage
    {
        get
        {
            byte level = RawLevel;

            if (level > 0xA)
            {
                return null; // unknown / invalid
            }

            return PowerState switch
            {
                BatteryPowerState.Discharging => Math.Min(level * 10 + 5, 100),
                BatteryPowerState.Charging => Math.Max((level - 1) * 10, 0),
                BatteryPowerState.ChargingComplete => 100,
                _ => null
            };
        }
    }

    /// <summary>
    /// Current battery power/charging state.
    /// </summary>
    public BatteryPowerState PowerState
    {
        get
        {
            return (Raw >> 4) switch
            {
                0x0 => BatteryPowerState.Discharging,
                0x1 => BatteryPowerState.Charging,
                0x2 => BatteryPowerState.ChargingComplete,
                0xA => BatteryPowerState.AbnormalVoltage,
                0xB => BatteryPowerState.AbnormalTemperature,
                0xF => BatteryPowerState.ChargingError,
                _ => BatteryPowerState.Unknown
            };
        }
    }

    /// <summary>
    /// Display-friendly battery percentage (100% when charging complete, -1 when unknown).
    /// </summary>
    public int DisplayPercentage
    {
        get
        {
            return Percentage ?? -1;
        }
    }

    /// <summary>
    /// Returns true if the raw battery byte is equal.
    /// </summary>
    public bool Equals(BatteryState other) => Raw == other.Raw;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BatteryState other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Raw.GetHashCode();

    /// <summary>
    /// Returns true if the two battery states are equal.
    /// </summary>
    public static bool operator ==(BatteryState left, BatteryState right) => left.Equals(right);

    /// <summary>
    /// Returns true if the two battery states are not equal.
    /// </summary>
    public static bool operator !=(BatteryState left, BatteryState right) => !left.Equals(right);
}