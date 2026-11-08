namespace DualSenseClient.Controllers.TouchpadMouse;

/// <summary>
/// An <see cref="IMouseInjector"/> that ignores every event and reports failure. Used on
/// platforms without a mouse injection backend so the gesture engine can run unchanged.
/// </summary>
public sealed class NullMouseInjector : IMouseInjector
{
    /// <inheritdoc/>
    public bool Move(int dx, int dy) => false;

    /// <inheritdoc/>
    public bool LeftDown() => false;

    /// <inheritdoc/>
    public bool LeftUp() => false;

    /// <inheritdoc/>
    public bool RightDown() => false;

    /// <inheritdoc/>
    public bool RightUp() => false;

    /// <inheritdoc/>
    public bool Wheel(int delta) => false;

    /// <inheritdoc/>
    public bool HorizontalWheel(int delta) => false;
}