using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for button press or release.
/// </summary>
/// <remarks>
/// Raised when a button transitions from released to pressed or from pressed to released.
/// </remarks>
public class ButtonEventArgs : EventArgs
{
    /// <summary>
    /// The button that was pressed or released.
    /// </summary>
    public ButtonType Button { get; }

    /// <summary>
    /// Creates a new button event args instance.
    /// </summary>
    /// <param name="button">The button that was pressed or released.</param>
    public ButtonEventArgs(ButtonType button)
    {
        Button = button;
    }
}