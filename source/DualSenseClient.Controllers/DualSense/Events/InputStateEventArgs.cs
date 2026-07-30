using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for general input state changes (sticks, triggers, buttons).
/// </summary>
/// <remarks>
/// Raised once per report when any input state field changes (sticks, triggers, or buttons).
/// </remarks>
public class InputStateEventArgs : EventArgs
{
    /// <summary>
    /// The current input state after the change.
    /// </summary>
    public InputState CurrentState { get; }

    /// <summary>
    /// The input state before the change.
    /// </summary>
    public InputState PreviousState { get; }

    /// <summary>
    /// Creates a new input state event args instance.
    /// </summary>
    /// <param name="current">The current input state after the change.</param>
    /// <param name="previous">The input state before the change.</param>
    public InputStateEventArgs(InputState current, InputState previous)
    {
        CurrentState = current;
        PreviousState = previous;
    }
}