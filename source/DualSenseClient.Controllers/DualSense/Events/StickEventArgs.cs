using DualSenseClient.Controllers.DualSense.Enum;

namespace DualSenseClient.Controllers.DualSense.Events;

/// <summary>
/// Event args for analog stick movement.
/// </summary>
/// <remarks>
/// Raised when either analog stick changes position.
/// </remarks>
public class StickEventArgs : EventArgs
{
    /// <summary>
    /// Which stick moved.
    /// </summary>
    public StickType Stick { get; }

    /// <summary>
    /// Current X position.
    /// </summary>
    public byte X { get; }

    /// <summary>
    /// Current Y position.
    /// </summary>
    public byte Y { get; }

    /// <summary>
    /// Previous X position.
    /// </summary>
    public byte PreviousX { get; }

    /// <summary>
    /// Previous Y position.
    /// </summary>
    public byte PreviousY { get; }

    /// <summary>
    /// Creates a new stick event args instance.
    /// </summary>
    /// <param name="stick">Which stick moved.</param>
    /// <param name="x">The current X position.</param>
    /// <param name="y">The current Y position.</param>
    /// <param name="previousX">The previous X position.</param>
    /// <param name="previousY">The previous Y position.</param>
    public StickEventArgs(StickType stick, byte x, byte y, byte previousX, byte previousY)
    {
        Stick = stick;
        X = x;
        Y = y;
        PreviousX = previousX;
        PreviousY = previousY;
    }
}