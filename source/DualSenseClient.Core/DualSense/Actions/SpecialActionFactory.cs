using DualSenseClient.Core.DualSense.Actions.Handlers;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.Core.DualSense.Actions;

public class SpecialActionFactory
{
    private readonly Dictionary<(SpecialActionType, BatteryIndicatorType?), ISpecialActionHandler> _handlers = new();

    public SpecialActionFactory()
    {
        // Register battery indicator handlers
        _handlers[(SpecialActionType.BatteryIndicator, BatteryIndicatorType.Lightbar)] = new BatteryLightbarHandler();
        _handlers[(SpecialActionType.BatteryIndicator, BatteryIndicatorType.PlayerLed)] = new BatteryPlayerLedHandler();

        // Register disconnect controller handler
        _handlers[(SpecialActionType.DisconnectController, null)] = new DisconnectControllerHandler();

        // Register other handlers
        //_handlers[(SpecialActionType, null)] <- Like this
    }

    public ISpecialActionHandler? GetHandler(SpecialActionSettings action)
    {
        // If this is a swipe action, return the swipe handler
        if (action.IsSwipeAction)
        {
            return new SwipeActionHandler();
        }

        // For non-swipe actions (button combinations), use the original logic
        (SpecialActionType Type, BatteryIndicatorType?) key = action.Type == SpecialActionType.BatteryIndicator
            ? (action.Type, action.Settings.BatteryIndicatorType)
            : (action.Type, (BatteryIndicatorType?)null);

        if (_handlers.TryGetValue(key, out ISpecialActionHandler? handler))
        {
            return handler;
        }

        Logger.Warning<SpecialActionFactory>($"No handler found for action type: {action.Type}");
        return null;
    }
}