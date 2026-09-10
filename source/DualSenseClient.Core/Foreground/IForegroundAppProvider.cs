namespace DualSenseClient.Core.Foreground;

/// <summary>
/// Resolves the program currently in focus for foreground-app auto profiles.
/// </summary>
public interface IForegroundAppProvider
{
    /// <summary>
    /// Whether focus tracking is available on this platform.
    /// Only Windows is supported; Linux focus tracking is out of scope for now.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets the program currently in focus, or <c>null</c> when it cannot be determined.
    /// </summary>
    ForegroundApp? GetForegroundApp();
}