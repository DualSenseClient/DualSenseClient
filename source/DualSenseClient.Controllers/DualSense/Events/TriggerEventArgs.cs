using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for analog trigger value changes.
/// </summary>
/// <remarks>
/// Raised when either analog trigger changes value.
/// </remarks>
public class TriggerEventArgs : EventArgs
{
    /// <summary>
    /// Which trigger changed.
    /// </summary>
    public TriggerType Trigger { get; }

    /// <summary>
    /// The current trigger value.
    /// </summary>
    public byte CurrentValue { get; }

    /// <summary>
    /// The previous trigger value.
    /// </summary>
    public byte PreviousValue { get; }

    /// <summary>
    /// Creates a new trigger event args instance.
    /// </summary>
    /// <param name="trigger">Which trigger changed.</param>
    /// <param name="currentValue">The current trigger value.</param>
    /// <param name="previousValue">The previous trigger value.</param>
    public TriggerEventArgs(TriggerType trigger, byte currentValue, byte previousValue)
    {
        Trigger = trigger;
        CurrentValue = currentValue;
        PreviousValue = previousValue;
    }
}