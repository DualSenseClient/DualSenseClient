using DualSenseClient.Core.DualSense.Actions.State;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.Logging;

namespace DualSenseClient.Core.DualSense.Actions;

public class TemporaryBatteryIndicatorManager
{
    private static readonly Dictionary<string, Timer> _activeIndicators = new Dictionary<string, Timer>();
    private static readonly ControllerStateSaver _stateSaver = new ControllerStateSaver();

    public static void ShowTemporaryBatteryIndicator(
        DualSenseController controller,
        BatteryIndicatorType indicatorType,
        float batteryLevel,
        int durationMs = 5000)
    {
        string controllerId = controller.MacAddress ?? controller.Device.DevicePath;

        // Save the current state
        _stateSaver.SaveState(controllerId, controller);

        // Set the battery indicator immediately
        SetBatteryIndicator(controller, indicatorType, batteryLevel);

        // Set up a timer to restore the state after the duration
        Timer timer = new Timer(RestoreStateCallback, new object[] { controllerId, controller }, durationMs, Timeout.Infinite);

        // Store the active timer to prevent garbage collection and allow cleanup
        lock (_activeIndicators)
        {
            // Dispose any existing timer for this controller
            if (_activeIndicators.ContainsKey(controllerId))
            {
                _activeIndicators[controllerId].Dispose();
                _activeIndicators.Remove(controllerId);
            }
            _activeIndicators[controllerId] = timer;
        }
    }

    private static void SetBatteryIndicator(DualSenseController controller, BatteryIndicatorType indicatorType, float batteryLevel)
    {
        Logger.Info<TemporaryBatteryIndicatorManager>($"Setting temporary battery indicator ({indicatorType}) for battery level: {batteryLevel}%");

        switch (indicatorType)
        {
            case BatteryIndicatorType.Lightbar:
                // Change the lightbar color based on battery level
                byte red = (byte)(255 * (100 - batteryLevel) / 100); // Red for low battery
                byte green = (byte)(255 * batteryLevel / 100); // Green for high battery
                controller.SetLightbar(red, green, 0);
                break;
            case BatteryIndicatorType.PlayerLed:
                // Set player LED based on battery level
                PlayerLed ledPattern = batteryLevel switch
                {
                    > 75 => PlayerLed.LED_1,
                    > 50 => PlayerLed.LED_1 | PlayerLed.LED_2,
                    > 25 => PlayerLed.LED_1 | PlayerLed.LED_2 | PlayerLed.LED_3,
                    _ => PlayerLed.LED_1 | PlayerLed.LED_2 | PlayerLed.LED_3 | PlayerLed.LED_4
                };
                controller.SetPlayerLeds(ledPattern);
                break;
        }
    }

    private static void RestoreStateCallback(object? state)
    {
        if (state == null)
        {
            return;
        }

        object[] parameters = (object[])state;
        string controllerId = (string)parameters[0];
        DualSenseController controller = (DualSenseController)parameters[1];

        try
        {
            // Restore the original state
            _stateSaver.RestoreState(controllerId, controller);

            // Remove the timer for this controller
            lock (_activeIndicators)
            {
                if (_activeIndicators.ContainsKey(controllerId))
                {
                    _activeIndicators[controllerId].Dispose();
                    _activeIndicators.Remove(controllerId);
                }
            }

            Logger.Info<TemporaryBatteryIndicatorManager>($"Restored original state for controller {controllerId}");
        }
        catch (Exception ex)
        {
            Logger.Error<TemporaryBatteryIndicatorManager>($"Error restoring state for controller {controllerId}: {ex.Message}");
        }
    }

    public static void CancelTemporaryIndicator(string controllerId)
    {
        lock (_activeIndicators)
        {
            if (_activeIndicators.ContainsKey(controllerId))
            {
                _activeIndicators[controllerId].Dispose();
                _activeIndicators.Remove(controllerId);
            }
        }

        _stateSaver.ResetState(controllerId);
    }
}