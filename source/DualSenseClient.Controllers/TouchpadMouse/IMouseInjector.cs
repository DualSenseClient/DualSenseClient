namespace DualSenseClient.Controllers.TouchpadMouse;

/// <summary>
/// Injects relative mouse movement, button, and wheel events into the host operating system.
/// Methods report whether the host accepted the event; a blocked (elevated) foreground window
/// can reject injected input even on a supported platform.
/// </summary>
public interface IMouseInjector
{
    /// <summary>
    /// Moves the pointer by a relative offset.
    /// </summary>
    /// <param name="dx">Horizontal offset in mouse units; positive values move right.</param>
    /// <param name="dy">Vertical offset in mouse units; positive values move down.</param>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool Move(int dx, int dy);

    /// <summary>
    /// Presses the left mouse button.
    /// </summary>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool LeftDown();

    /// <summary>
    /// Releases the left mouse button.
    /// </summary>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool LeftUp();

    /// <summary>
    /// Presses the right mouse button.
    /// </summary>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool RightDown();

    /// <summary>
    /// Releases the right mouse button.
    /// </summary>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool RightUp();

    /// <summary>
    /// Turns the vertical wheel by the given delta.
    /// </summary>
    /// <param name="delta">Wheel delta; +120 is one notch up.</param>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool Wheel(int delta);

    /// <summary>
    /// Turns the horizontal wheel by the given delta.
    /// </summary>
    /// <param name="delta">Wheel delta; +120 is one notch right.</param>
    /// <returns><see langword="true"/> when the host accepted the event.</returns>
    bool HorizontalWheel(int delta);
}