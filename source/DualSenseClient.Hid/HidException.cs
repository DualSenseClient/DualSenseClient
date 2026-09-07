namespace DualSenseClient.Hid;

/// <summary>
/// Thrown when a native HIDAPI operation fails.
/// </summary>
public sealed class HidException(string message) : Exception(message);