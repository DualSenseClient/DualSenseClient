using DualSenseClient.VIIPER.Callbacks;

namespace DualSenseClient.VIIPER;

/// <summary>
/// Log severity levels used by the <see cref="VIIPERLogCallback"/>.
/// </summary>
public enum VIIPERLogLevel
{
    Debug = -4,
    Info = 0,
    Warn = 4,
    Error = 8
}