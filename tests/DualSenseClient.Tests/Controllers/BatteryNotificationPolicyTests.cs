using DualSenseClient.Controllers;
using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Tests.Controllers;

public class BatteryNotificationPolicyTests
{
    [Test]
    public void ShouldNotifyLowBattery_AtOrBelowThreshold_Notifies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(15, 15, false), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(5, 15, false), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(0, 15, false), Is.True);
        });
    }

    [Test]
    public void ShouldNotifyLowBattery_AboveThreshold_DoesNotNotify() =>
        Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(25, 15, false), Is.False);

    [Test]
    public void ShouldNotifyLowBattery_AlreadyNotified_DoesNotNotify() =>
        Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(5, 15, true), Is.False);

    [Test]
    public void ShouldNotifyLowBattery_UnknownPercentage_DoesNotNotify() =>
        Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(-1, 15, false), Is.False);

    [Test]
    public void ShouldNotifyLowBattery_ThresholdClampedToValidRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(5, 0, false), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(55, 100, false), Is.False);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyLowBattery(45, 100, false), Is.True);
        });
    }

    [Test]
    public void ShouldNotifyChargeComplete_ChargingAtOrAboveThreshold_Notifies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.Charging, 100, 100, false), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.Charging, 85, 80, false), Is.True);
        });
    }

    [Test]
    public void ShouldNotifyChargeComplete_ChargingBelowThreshold_DoesNotNotify() =>
        Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.Charging, 55, 80, false), Is.False);

    [Test]
    public void ShouldNotifyChargeComplete_CompletedCharge_NotifiesRegardlessOfPercentage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.ChargingComplete, 100, 100, false), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.ChargingComplete, -1, 100, false), Is.True);
        });
    }

    [Test]
    public void ShouldNotifyChargeComplete_DischargingOrUnknownPercentage_DoesNotNotify()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.Discharging, 100, 100, false), Is.False);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.Charging, -1, 80, false), Is.False);
            Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.Unknown, 100, 80, false), Is.False);
        });
    }

    [Test]
    public void ShouldNotifyChargeComplete_AlreadyNotified_DoesNotNotify() =>
        Assert.That(BatteryNotificationPolicy.ShouldNotifyChargeComplete(BatteryPowerState.ChargingComplete, 100, 100, true), Is.False);

    [Test]
    public void ShouldResetLowNotification_RecoveredPastHysteresis_Resets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldResetLowNotification(26, 15), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldResetLowNotification(15, 15), Is.False);
            Assert.That(BatteryNotificationPolicy.ShouldResetLowNotification(25, 15), Is.False);
            Assert.That(BatteryNotificationPolicy.ShouldResetLowNotification(-1, 15), Is.False);
        });
    }

    [Test]
    public void ShouldResetChargeNotification_Discharging_Resets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BatteryNotificationPolicy.ShouldResetChargeNotification(BatteryPowerState.Discharging), Is.True);
            Assert.That(BatteryNotificationPolicy.ShouldResetChargeNotification(BatteryPowerState.Charging), Is.False);
            Assert.That(BatteryNotificationPolicy.ShouldResetChargeNotification(BatteryPowerState.ChargingComplete), Is.False);
        });
    }
}