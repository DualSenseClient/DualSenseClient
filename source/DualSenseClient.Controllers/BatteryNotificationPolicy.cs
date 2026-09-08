using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers;

/// <summary>
/// Pure decision logic for battery notification popups. Keeps the per-cycle
/// "notify once" rules testable without UI or device dependencies; per-device
/// notified flags are owned by the caller.
/// </summary>
public static class BatteryNotificationPolicy
{
    /// <summary>
    /// Extra percentage points above the low threshold that clear the low-battery
    /// "already notified" flag, so a battery hovering at the threshold does not
    /// re-notify on every report.
    /// </summary>
    public const int LowBatteryHysteresis = 10;

    /// <summary>
    /// Clamps a low-battery threshold to its valid range (5-50).
    /// </summary>
    public static int ClampLowThreshold(int threshold) => Math.Clamp(threshold, 5, 50);

    /// <summary>
    /// Clamps a charge threshold to its valid range (50-100).
    /// </summary>
    public static int ClampChargeThreshold(int threshold) => Math.Clamp(threshold, 50, 100);

    /// <summary>
    /// Whether the low-battery notification should fire. Fires once per discharge
    /// cycle: <paramref name="alreadyNotified"/> must be <c>false</c> and the
    /// percentage at or below the threshold. Unknown percentages (-1) never notify.
    /// </summary>
    public static bool ShouldNotifyLowBattery(int displayPercentage, int lowThreshold, bool alreadyNotified)
    {
        if (alreadyNotified || displayPercentage < 0)
        {
            return false;
        }

        return displayPercentage <= ClampLowThreshold(lowThreshold);
    }

    /// <summary>
    /// Whether the charge notification should fire. Fires once per charge cycle when
    /// the controller reports a completed charge, or while charging at or above the
    /// threshold. Never fires while discharging. Unknown percentages only notify on
    /// an explicit completed-charge report.
    /// </summary>
    public static bool ShouldNotifyChargeComplete(BatteryPowerState powerState, int displayPercentage, int chargeThreshold, bool alreadyNotified)
    {
        if (alreadyNotified)
        {
            return false;
        }

        if (powerState == BatteryPowerState.ChargingComplete)
        {
            return true;
        }

        return powerState == BatteryPowerState.Charging
               && displayPercentage >= 0
               && displayPercentage >= ClampChargeThreshold(chargeThreshold);
    }

    /// <summary>
    /// Whether the low-battery "already notified" flag should be cleared, re-arming
    /// the next discharge cycle. Clears once the level recovers past the threshold
    /// plus hysteresis. Unknown percentages never reset.
    /// </summary>
    public static bool ShouldResetLowNotification(int displayPercentage, int lowThreshold) =>
        displayPercentage >= 0 && displayPercentage > ClampLowThreshold(lowThreshold) + LowBatteryHysteresis;

    /// <summary>
    /// Whether the charge "already notified" flag should be cleared, re-arming the
    /// next charge cycle. Clears when the controller is back on battery power.
    /// </summary>
    public static bool ShouldResetChargeNotification(BatteryPowerState powerState) => powerState == BatteryPowerState.Discharging;
}