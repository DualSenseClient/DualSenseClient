namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// An application currently rendering audio on the default output device, offered by the
/// per-application forwarded-audio picker.
/// </summary>
public sealed class AudioApplicationInfo
{
    /// <summary>
    /// Creates an application descriptor.
    /// </summary>
    /// <param name="displayName">The process name shown in the picker.</param>
    /// <param name="executablePath">The full executable path, which is what gets stored.</param>
    /// <param name="processId">The process id at the time of enumeration (not stored).</param>
    public AudioApplicationInfo(string displayName, string executablePath, uint processId)
    {
        DisplayName = displayName;
        ExecutablePath = executablePath;
        ProcessId = processId;
    }

    /// <summary>
    /// The process name shown in the picker.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// The full executable path, the durable selector stored in the settings.
    /// </summary>
    public string ExecutablePath { get; }

    /// <summary>
    /// The process id at the time of enumeration. Purely informational: the capture
    /// resolves the id from <see cref="ExecutablePath"/> every time it starts.
    /// </summary>
    public uint ProcessId { get; }
}