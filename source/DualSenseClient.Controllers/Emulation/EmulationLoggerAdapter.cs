using DualSenseClient.Logging;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Routes the Emulation engine's <see cref="IEmulationLogger"/> messages into the application log,
/// mirroring the libVIIPER native log routing
/// (<c>DualSenseClientLogger.For("VIIPER")</c> in <see cref="EmulationService"/>).
/// </summary>
/// <remarks>
/// The engine calls this adapter from its session and device threads, so <see cref="Log"/> never
/// throws: a <c>null</c> message is normalized to <see cref="string.Empty"/> and a failure of the
/// logging pipeline is swallowed rather than propagated into the engine.
/// </remarks>
public sealed class EmulationLoggerAdapter : IEmulationLogger
{
    /// <summary>
    /// Logger receiving the Emulation engine's own log messages.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("Emulation");

    /// <summary>
    /// Logs one engine message through the application logger at the mapped severity
    /// (Trace→Trace, Debug→Debug, Information→Info, Warning→Warning, Error→Error).
    /// </summary>
    /// <param name="level">Severity of the message.</param>
    /// <param name="message">Message text; <c>null</c> is normalized to an empty string.</param>
    public void Log(EmulationLogLevel level, string message)
    {
        string text = message ?? string.Empty;
        try
        {
            switch (level)
            {
                case EmulationLogLevel.Trace:
                    _log.Trace(text);
                    break;
                case EmulationLogLevel.Debug:
                    _log.Debug(text);
                    break;
                case EmulationLogLevel.Information:
                    _log.Info(text);
                    break;
                case EmulationLogLevel.Warning:
                    _log.Warning(text);
                    break;
                case EmulationLogLevel.Error:
                    _log.Error(text);
                    break;
                default:
                    // The engine only emits the five defined levels; anything else is still logged.
                    _log.Info(text);
                    break;
            }
        }
        catch
        {
            // Logging is best-effort: a pipeline failure must never take an engine thread down.
        }
    }
}