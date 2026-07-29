namespace DualSenseClient.Hid;

/// <summary>
/// Thrown when a native SDL3 HID operation fails.
/// </summary>
public sealed class HidException(string message) : Exception(message);