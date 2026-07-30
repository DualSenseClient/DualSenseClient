using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for touchpad state changes.
/// </summary>
/// <remarks>
/// Raised during input report processing when the touchpad touch state changes.
/// </remarks>
public class TouchpadEventArgs : EventArgs
{
    /// <summary>
    /// The current touchpad state after the change.
    /// </summary>
    public TouchpadState CurrentState { get; }

    /// <summary>
    /// The touchpad state before the change.
    /// </summary>
    public TouchpadState PreviousState { get; }

    /// <summary>
    /// Creates a new touchpad event args instance.
    /// </summary>
    /// <param name="current">The current touchpad state after the change.</param>
    /// <param name="previous">The touchpad state before the change.</param>
    public TouchpadEventArgs(TouchpadState current, TouchpadState previous)
    {
        CurrentState = current;
        PreviousState = previous;
    }
}