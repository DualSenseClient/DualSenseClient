using DualSenseClient.Controllers.TouchpadMouse;

namespace DualSenseClient.Tests.Controllers.TouchpadMouse;

/// <summary>
/// Records every injected event so tests can assert on pointer movement, wheel deltas, and
/// the ordered call sequence without touching the host operating system.
/// </summary>
internal sealed class RecordingMouseInjector : IMouseInjector
{
    /// <summary>Relative pointer movements in call order.</summary>
    public List<(int Dx, int Dy)> Moves { get; } = new List<(int Dx, int Dy)>();

    /// <summary>Vertical wheel deltas in call order.</summary>
    public List<int> Wheels { get; } = new List<int>();

    /// <summary>Horizontal wheel deltas in call order.</summary>
    public List<int> HorizontalWheels { get; } = new List<int>();

    /// <summary>Injected calls in order, for example <c>"LeftDown"</c> or <c>"Move(10,6)"</c>.</summary>
    public List<string> Calls { get; } = new List<string>();

    /// <summary>Whether <see cref="LeftDown"/> should report failure instead of success.</summary>
    public bool FailLeftDown { get; set; }

    /// <inheritdoc/>
    public bool Move(int dx, int dy)
    {
        Moves.Add((dx, dy));
        Calls.Add($"Move({dx},{dy})");
        return true;
    }

    /// <inheritdoc/>
    public bool LeftDown()
    {
        Calls.Add("LeftDown");
        return !FailLeftDown;
    }

    /// <inheritdoc/>
    public bool LeftUp()
    {
        Calls.Add("LeftUp");
        return true;
    }

    /// <inheritdoc/>
    public bool RightDown()
    {
        Calls.Add("RightDown");
        return true;
    }

    /// <inheritdoc/>
    public bool RightUp()
    {
        Calls.Add("RightUp");
        return true;
    }

    /// <inheritdoc/>
    public bool Wheel(int delta)
    {
        Wheels.Add(delta);
        Calls.Add($"Wheel({delta})");
        return true;
    }

    /// <inheritdoc/>
    public bool HorizontalWheel(int delta)
    {
        HorizontalWheels.Add(delta);
        Calls.Add($"HorizontalWheel({delta})");
        return true;
    }
}