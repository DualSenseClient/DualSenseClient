using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.Windows;

/// <summary>
/// <see cref="IUsbIpAttachStrategy"/> that composes a primary strategy with a fallback strategy:
/// a device is attached through the primary when it works and through the fallback when the
/// primary fails (the warning logged in between carries the primary error). The strategy that
/// attached a device is remembered per bus id, so a detach goes to exactly the same strategy: a
/// device the native VHCI driver attached cannot be released through the usbip.exe CLI and vice
/// versa. Without a remembered winner the detach probes the primary first and the fallback second,
/// and records no winner itself. Every failure is returned as <see langword="false"/> plus an
/// error that carries the cause; when both strategies fail, the error aggregates both causes in
/// the "native: ...; usbip.exe: ..." shape. A defensive catch around every strategy call maps an
/// unexpected exception (a misbehaving seam) to the same failure shape, and a defensive catch
/// around each public operation guarantees that no exception escapes the public methods.
/// </summary>
public sealed class FallbackAttachStrategy : IUsbIpAttachStrategy
{
    /// <summary>Label of the primary strategy in messages (T5 composes the native VHCI strategy as primary).</summary>
    private const string PrimaryLabel = "native";

    /// <summary>Label of the fallback strategy in messages (T5 composes the usbip.exe CLI strategy as fallback).</summary>
    private const string FallbackLabel = "usbip.exe";

    /// <summary>Install guidance the composition's own unexpected failures carry, so the caller can show one actionable line.</summary>
    private const string InstallGuidance =
        "USB/IP must be installed for the device to appear (usbip-win2: https://github.com/vadimgrn/usbip-win2).";

    private readonly IUsbIpAttachStrategy _primary;

    private readonly IUsbIpAttachStrategy _fallback;

    private readonly IEmulationLogger? _logger;

    private readonly object _winnersLock = new object();

    private readonly Dictionary<string, IUsbIpAttachStrategy> _winnersByBusId = new Dictionary<string, IUsbIpAttachStrategy>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a composition of the given strategies.
    /// </summary>
    /// <param name="primary">
    /// Strategy tried first on attach (typically the native VHCI strategy); also the first candidate
    /// for a detach without a remembered winner.
    /// </param>
    /// <param name="fallback">Strategy tried when the primary fails (typically the usbip.exe CLI strategy).</param>
    /// <param name="logger">Logging seam; <see langword="null"/> disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="primary"/> or <paramref name="fallback"/> is <see langword="null"/>.</exception>
    public FallbackAttachStrategy(IUsbIpAttachStrategy primary, IUsbIpAttachStrategy fallback, IEmulationLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
    }

    /// <summary>
    /// Attaches the device through the primary strategy; when that fails, logs a warning carrying
    /// the primary error and attaches through the fallback strategy. The strategy that succeeded is
    /// remembered per bus id, so a later <see cref="TryDetach"/> routes to exactly that strategy;
    /// a failed attach records nothing. When both strategies fail, the returned error aggregates
    /// both causes in the "native: ...; usbip.exe: ..." shape and is logged as a warning.
    /// </summary>
    /// <param name="host">Host of the USB/IP server (typically loopback).</param>
    /// <param name="port">TCP port of the USB/IP server.</param>
    /// <param name="busId">Bus id of the exported device.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when either strategy attached the device.</returns>
    public bool TryAttach(string host, int port, string busId, out string? error)
    {
        try
        {
            return Attach(host, port, busId, out error);
        }
        catch (Exception exception)
        {
            error = $"{DescribeAttach(busId)} failed unexpectedly: {exception.Message} {InstallGuidance}";
            LogWarning(error);
            return false;
        }
    }

    /// <summary>
    /// Detaches the device through the strategy a successful <see cref="TryAttach"/> remembered for
    /// the bus id. A failed detach keeps the winner, so the caller can retry through the same
    /// strategy. Without a remembered winner the primary is tried first and the fallback second;
    /// that probe records no winner. When both strategies fail, the returned error aggregates both
    /// causes (prefixed so the detach attempt is clear) and is logged as a warning.
    /// </summary>
    /// <param name="busId">Bus id of the device an earlier <see cref="TryAttach"/> attached.</param>
    /// <param name="error">Actionable failure reason; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when a strategy detached the device.</returns>
    public bool TryDetach(string busId, out string? error)
    {
        try
        {
            return Detach(busId, out error);
        }
        catch (Exception exception)
        {
            error = $"{DescribeDetach(busId)} failed unexpectedly: {exception.Message} {InstallGuidance}";
            LogWarning(error);
            return false;
        }
    }

    private bool Attach(string host, int port, string busId, out string? error)
    {
        if (TryAttachWith(_primary, PrimaryLabel, host, port, busId, out string? primaryError))
        {
            RecordWinner(busId, _primary);
            error = null;
            return true;
        }

        LogWarning($"{PrimaryLabel} attach for bus id '{busId}' failed: {DescribeCause(primaryError)} Trying the {FallbackLabel} fallback.");

        if (TryAttachWith(_fallback, FallbackLabel, host, port, busId, out string? fallbackError))
        {
            RecordWinner(busId, _fallback);
            error = null;
            return true;
        }

        error = BuildAggregatedFailure(primaryError, fallbackError);
        LogWarning(error);
        return false;
    }

    private bool Detach(string busId, out string? error)
    {
        if (FindWinner(busId) is { } winner)
        {
            string label = DescribeStrategy(winner);
            if (TryDetachWith(winner, label, busId, out string? winnerError))
            {
                RemoveWinner(busId);
                error = null;
                return true;
            }

            // The winner stays remembered, so the caller can retry the detach through it.
            error = $"{label} detach for bus id '{busId}' failed: {DescribeCause(winnerError)}";
            LogWarning(error);
            return false;
        }

        if (TryDetachWith(_primary, PrimaryLabel, busId, out string? primaryError))
        {
            error = null;
            return true;
        }

        LogWarning($"{PrimaryLabel} detach for bus id '{busId}' failed: {DescribeCause(primaryError)} Trying the {FallbackLabel} fallback.");

        if (TryDetachWith(_fallback, FallbackLabel, busId, out string? fallbackError))
        {
            error = null;
            return true;
        }

        error = $"{DescribeDetach(busId)} failed: {BuildAggregatedFailure(primaryError, fallbackError)}";
        LogWarning(error);
        return false;
    }

    /// <summary>
    /// Calls one strategy's attach, mapping an unexpected exception to the strategy's failure
    /// shape, so a misbehaving seam yields <see langword="false"/> plus a cause instead of escaping.
    /// </summary>
    private static bool TryAttachWith(IUsbIpAttachStrategy strategy, string label, string host, int port, string busId, out string? error)
    {
        try
        {
            bool attached = strategy.TryAttach(host, port, busId, out error);
            if (!attached && string.IsNullOrWhiteSpace(error))
            {
                error = $"{label} attach for bus id '{busId}' failed without reporting a cause.";
            }

            return attached;
        }
        catch (Exception exception)
        {
            error = $"{label} attach for bus id '{busId}' failed unexpectedly: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Calls one strategy's detach, mapping an unexpected exception to the strategy's failure
    /// shape, so a misbehaving seam yields <see langword="false"/> plus a cause instead of escaping.
    /// </summary>
    private static bool TryDetachWith(IUsbIpAttachStrategy strategy, string label, string busId, out string? error)
    {
        try
        {
            bool detached = strategy.TryDetach(busId, out error);
            if (!detached && string.IsNullOrWhiteSpace(error))
            {
                error = $"{label} detach for bus id '{busId}' failed without reporting a cause.";
            }

            return detached;
        }
        catch (Exception exception)
        {
            error = $"{label} detach for bus id '{busId}' failed unexpectedly: {exception.Message}";
            return false;
        }
    }

    private void RecordWinner(string busId, IUsbIpAttachStrategy winner)
    {
        lock (_winnersLock)
        {
            _winnersByBusId[busId] = winner;
        }
    }

    private void RemoveWinner(string busId)
    {
        lock (_winnersLock)
        {
            _winnersByBusId.Remove(busId);
        }
    }

    private IUsbIpAttachStrategy? FindWinner(string busId)
    {
        lock (_winnersLock)
        {
            return _winnersByBusId.GetValueOrDefault(busId);
        }
    }

    private static string BuildAggregatedFailure(string? primaryError, string? fallbackError) =>
        $"{PrimaryLabel}: {DescribeCause(primaryError)}; {FallbackLabel}: {DescribeCause(fallbackError)}";

    private static string DescribeCause(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "no cause reported" : error;

    private string DescribeStrategy(IUsbIpAttachStrategy strategy) =>
        ReferenceEquals(strategy, _primary) ? PrimaryLabel : FallbackLabel;

    private static string DescribeAttach(string busId) => $"attach for bus id '{busId}'";

    private static string DescribeDetach(string busId) => $"detach for bus id '{busId}'";

    private void LogWarning(string message) => _logger?.Log(EmulationLogLevel.Warning, message);
}