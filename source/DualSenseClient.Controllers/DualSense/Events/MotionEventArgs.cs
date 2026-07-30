using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for gyroscope and accelerometer changes.
/// </summary>
/// <remarks>
/// Raised during input report processing when gyroscope or accelerometer data changes.
/// </remarks>
public class MotionEventArgs : EventArgs
{
    /// <summary>
    /// The current motion state after the change.
    /// </summary>
    public MotionState CurrentState { get; }

    /// <summary>
    /// The motion state before the change.
    /// </summary>
    public MotionState PreviousState { get; }

    /// <summary>
    /// Creates a new motion event args instance.
    /// </summary>
    /// <param name="current">The current motion state after the change.</param>
    /// <param name="previous">The motion state before the change.</param>
    public MotionEventArgs(MotionState current, MotionState previous)
    {
        CurrentState = current;
        PreviousState = previous;
    }
}