namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Minimal logging seam of the Emulation engine: one message with its severity. The engine never
/// formats or filters messages itself, so an adapter controls routing, and passing no logger
/// disables logging entirely.
/// </summary>
public interface IEmulationLogger
{
    /// <summary>Logs one message at the given severity.</summary>
    /// <param name="level">Severity of the message.</param>
    /// <param name="message">Message text.</param>
    void Log(EmulationLogLevel level, string message);
}