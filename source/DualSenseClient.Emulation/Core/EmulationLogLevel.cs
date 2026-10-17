namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Severity of one message logged by the Emulation engine. The numeric order is meaningful:
/// <see cref="Trace"/> is the most verbose and <see cref="Error"/> the most severe level.
/// </summary>
public enum EmulationLogLevel
{
    /// <summary>Verbose protocol traffic tracing.</summary>
    Trace = 0,

    /// <summary>Diagnostic detail of the session lifecycle.</summary>
    Debug = 1,

    /// <summary>Normal lifecycle events such as a device being imported.</summary>
    Information = 2,

    /// <summary>A recoverable anomaly such as a dropped garbage frame.</summary>
    Warning = 3,

    /// <summary>An unexpected session failure.</summary>
    Error = 4
}