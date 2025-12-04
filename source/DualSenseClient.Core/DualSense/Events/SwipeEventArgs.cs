using DualSenseClient.Core.DualSense.Devices;

namespace DualSenseClient.Core.DualSense.Events;

public class SwipeEventArgs : EventArgs
{
    public DualSenseController Controller { get; }
    public Actions.SwipeDirection Direction { get; }

    public SwipeEventArgs(DualSenseController controller, Actions.SwipeDirection direction)
    {
        Controller = controller;
        Direction = direction;
    }
}