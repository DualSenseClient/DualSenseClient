namespace DualSenseClient.Controllers.DualSense.Enum;

/// <summary>
/// Battery power/charging state reported by the DualSense controller.
/// </summary>
public enum BatteryPowerState : byte
{
    /// <summary>
    /// Battery is discharging (running on battery power).
    /// </summary>
    Discharging = 0x0,

    /// <summary>
    /// Battery is currently charging.
    /// </summary>
    Charging = 0x1,

    /// <summary>
    /// Battery charging is complete (100%).
    /// </summary>
    ChargingComplete = 0x2,

    /// <summary>
    /// Abnormal voltage detected.
    /// </summary>
    AbnormalVoltage = 0xA,

    /// <summary>
    /// Abnormal temperature detected.
    /// </summary>
    AbnormalTemperature = 0xB,

    /// <summary>
    /// Charging error detected.
    /// </summary>
    ChargingError = 0xF,

    /// <summary>
    /// Power state could not be determined.
    /// </summary>
    Unknown = 0xFF
}