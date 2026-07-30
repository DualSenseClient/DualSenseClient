using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for battery state changes.
/// </summary>
/// <remarks>
/// Raised during input report processing when the battery level or power state changes.
/// </remarks>
public class BatteryStateEventArgs : EventArgs
{
    /// <summary>
    /// The current battery state after the change.
    /// </summary>
    public BatteryState CurrentState { get; }

    /// <summary>
    /// The battery state before the change.
    /// </summary>
    public BatteryState PreviousState { get; }

    /// <summary>
    /// Creates a new battery state event args instance.
    /// </summary>
    /// <param name="current">The current battery state after the change.</param>
    /// <param name="previous">The battery state before the change.</param>
    public BatteryStateEventArgs(BatteryState current, BatteryState previous)
    {
        CurrentState = current;
        PreviousState = previous;
    }
}