using DualSenseClient.Core.DualSense.Enums;

namespace DualSenseClient.Core.DualSense.Actions;

/// <summary>
/// Represents a swipe gesture as a special action trigger
/// </summary>
public class SwipeActionCombination
{
    public SwipeDirection Direction { get; set; }
    public bool IsLongPress { get; set; } = false; // For future extension
}

public enum SwipeDirection
{
    Left,
    Right,
    Up,
    Down
}