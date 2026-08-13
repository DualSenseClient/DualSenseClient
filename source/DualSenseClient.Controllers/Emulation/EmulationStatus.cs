using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// The current state of virtual controller emulation, surfaced for the UI.
/// </summary>
public sealed record EmulationStatus(
    EmulationMode Mode,
    bool Running,
    string? Detail,
    string? VirtualDevicePath,
    bool IsCreating = false);