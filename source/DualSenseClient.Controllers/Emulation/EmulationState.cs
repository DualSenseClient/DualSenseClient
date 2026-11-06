namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// The lifecycle state of one Virtual Controller Slot.
/// </summary>
public enum EmulationState
{
    /// <summary>
    /// No virtual controller is wanted or possible.
    /// </summary>
    Off,

    /// <summary>
    /// A virtual controller is being created and discovered.
    /// </summary>
    Creating,

    /// <summary>
    /// The virtual controller is created and attached.
    /// </summary>
    Running,

    /// <summary>
    /// The last creation attempt failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The slot has been disposed.
    /// </summary>
    Stopped
}