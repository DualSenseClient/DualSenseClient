using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for connection status changes (headphone, mic, USB).
/// </summary>
/// <remarks>
/// Raised during input report processing when the headphone, mic, or USB connection status changes.
/// </remarks>
public class ConnectionStatusEventArgs : EventArgs
{
    /// <summary>
    /// The current connection status after the change.
    /// </summary>
    public ConnectionStatus CurrentStatus { get; }

    /// <summary>
    /// The connection status before the change.
    /// </summary>
    public ConnectionStatus PreviousStatus { get; }

    /// <summary>
    /// Creates a new connection status event args instance.
    /// </summary>
    /// <param name="current">The current connection status after the change.</param>
    /// <param name="previous">The connection status before the change.</param>
    public ConnectionStatusEventArgs(ConnectionStatus current, ConnectionStatus previous)
    {
        CurrentStatus = current;
        PreviousStatus = previous;
    }
}