namespace DualSenseClient.Settings.Sections;

/// <summary>
/// Screen positions for desktop notification popups, relative to the working
/// area of the screen they appear on.
/// </summary>
public enum NotificationPosition
{
    /// <summary>
    /// Top-left corner of the working area.
    /// </summary>
    TopLeft,

    /// <summary>
    /// Top edge, horizontally centered.
    /// </summary>
    TopCenter,

    /// <summary>
    /// Top-right corner of the working area.
    /// </summary>
    TopRight,

    /// <summary>
    /// Left edge, vertically centered.
    /// </summary>
    MiddleLeft,

    /// <summary>
    /// Right edge, vertically centered.
    /// </summary>
    MiddleRight,

    /// <summary>
    /// Bottom-left corner of the working area.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Bottom edge, horizontally centered.
    /// </summary>
    BottomCenter,

    /// <summary>
    /// Bottom-right corner of the working area.
    /// </summary>
    BottomRight
}