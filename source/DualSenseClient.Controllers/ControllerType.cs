namespace DualSenseClient.Controllers;

/// <summary>
/// Identifies the concrete controller model for a recognized game controller.
/// </summary>
public enum ControllerType
{
    /// <summary>
    /// Not a recognized controller.
    /// </summary>
    Unknown,

    /// <summary>
    /// Sony DualSense (PS5) controller.
    /// </summary>
    DualSense,

    /// <summary>
    /// Sony DualSense Edge (PS5) controller.
    /// </summary>
    DualSenseEdge

    // add new controllers here
}