namespace DualSenseClient.HidHide;

/// <summary>
/// Platform-agnostic facade for hiding physical controllers from other applications.
/// Each platform provides an implementation (HidHide driver on Windows, udev/evdev
/// based approach on Linux, ...) behind the same interface.
/// </summary>
/// <remarks>
/// Implementations must be safe to call on any platform and handle an unavailable
/// backend gracefully: <see cref="IsAvailable"/> returns <c>false</c> and mutations
/// become no-ops instead of throwing.
/// </remarks>
public interface IControllerHidingService
{
    /// <summary>
    /// Whether the hiding backend is installed and operational on this system.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Resolves a platform HID device path to the stable identifier the backend
    /// uses to address a controller (e.g. the device instance ID on Windows).
    /// </summary>
    /// <param name="devicePath">The platform HID device path.</param>
    /// <param name="instanceId">The resolved identifier when successful.</param>
    /// <returns><c>true</c> if the path could be resolved; otherwise <c>false</c>.</returns>
    bool TryGetInstanceId(string devicePath, out string instanceId);

    /// <summary>
    /// Whether the controller with the given identifier is currently hidden.
    /// </summary>
    bool IsControllerHidden(string instanceId);

    /// <summary>
    /// Hides or unhides the controller with the given identifier. Hiding implies the
    /// backend's global hiding state is enabled as needed; unhiding the last hidden
    /// controller turns it off again.
    /// </summary>
    void SetControllerHidden(string instanceId, bool hidden);

    /// <summary>
    /// Ensures this application itself stays able to see hidden controllers, e.g. by
    /// adding itself to the driver's whitelist on Windows. No-op on platforms without
    /// such a mechanism. Called once at startup.
    /// </summary>
    void EnsureSelfVisible();
}