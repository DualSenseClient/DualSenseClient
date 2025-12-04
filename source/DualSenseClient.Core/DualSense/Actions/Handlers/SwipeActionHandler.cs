using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.Core.DualSense.Actions.Handlers;

public class SwipeActionHandler : ISpecialActionHandler
{
    public void Execute(DualSenseController controller, SpecialActionSettings action)
    {
        if (action.SwipeAction == null)
        {
            Logger.Warning<SwipeActionHandler>("Swipe action is null, cannot execute");
            return;
        }

        Logger.Info<SwipeActionHandler>($"Executing swipe action: {action.Name} (Direction: {action.SwipeAction.Direction})");

        // Execute the action based on its type
        switch (action.Type)
        {
            case SpecialActionType.BatteryIndicator:
                HandleBatteryIndicator(controller, action);
                break;
            case SpecialActionType.DisconnectController:
                HandleDisconnectController(controller, action);
                break;
            // Add more action types as needed
            default:
                Logger.Warning<SwipeActionHandler>($"Unsupported action type for swipe: {action.Type}");
                break;
        }
    }

    private void HandleBatteryIndicator(DualSenseController controller, SpecialActionSettings action)
    {
        // This action shows battery indicator temporarily for 5 seconds then returns to original state
        float batteryLevel = controller.Battery.BatteryLevel;
        BatteryIndicatorType indicatorType = action.Settings.BatteryIndicatorType ?? BatteryIndicatorType.Lightbar;

        // Show the temporary battery indicator
        TemporaryBatteryIndicatorManager.ShowTemporaryBatteryIndicator(
            controller,
            indicatorType,
            batteryLevel,
            5000); // 5 seconds
    }

    private void HandleDisconnectController(DualSenseController controller, SpecialActionSettings action)
    {
        Logger.Info<SwipeActionHandler>($"Attempting to disconnect controller via swipe gesture");

        // Attempt to disconnect the controller
        bool success = controller.DisconnectBluetooth();
        if (success)
        {
            Logger.Info<SwipeActionHandler>("Controller disconnected successfully");
        }
        else
        {
            Logger.Warning<SwipeActionHandler>("Failed to disconnect controller");
        }
    }
}