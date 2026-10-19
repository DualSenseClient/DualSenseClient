using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// <see cref="IUsbIpAttachStrategy"/> test double: records every call and returns the configured
/// per-operation result, so compositions that choose between strategies can be tested without a
/// real attach mechanism. Both operations fail out of the box with a synthetic error, so a test
/// only configures the outcomes it cares about. Each operation can instead be configured to throw,
/// deliberately violating the seam's no-throw contract to prove that a composition maps a
/// misbehaving strategy to its documented failure shape.
/// </summary>
public sealed class FakeAttachStrategy : IUsbIpAttachStrategy
{
    private readonly object _lock = new object();

    private readonly List<RecordedAttachCall> _attachCalls = new List<RecordedAttachCall>();

    private readonly List<RecordedDetachCall> _detachCalls = new List<RecordedDetachCall>();

    /// <summary>Result returned by every <see cref="TryAttach"/>; defaults to failing.</summary>
    public bool AttachResult { get; set; }

    /// <summary>Error reported by a failing <see cref="TryAttach"/>; never reported when the attach succeeds.</summary>
    public string? AttachError { get; set; } = "synthetic attach failure";

    /// <summary>Exception thrown by every <see cref="TryAttach"/> instead of returning <see cref="AttachResult"/>; <see langword="null"/> returns it.</summary>
    public Exception? AttachExceptionToThrow { get; set; }

    /// <summary>Result returned by every <see cref="TryDetach"/>; defaults to failing.</summary>
    public bool DetachResult { get; set; }

    /// <summary>Error reported by a failing <see cref="TryDetach"/>; never reported when the detach succeeds.</summary>
    public string? DetachError { get; set; } = "synthetic detach failure";

    /// <summary>Exception thrown by every <see cref="TryDetach"/> instead of returning <see cref="DetachResult"/>; <see langword="null"/> returns it.</summary>
    public Exception? DetachExceptionToThrow { get; set; }

    /// <summary>Number of <see cref="TryAttach"/> calls so far.</summary>
    public int AttachCallCount
    {
        get
        {
            lock (_lock)
            {
                return _attachCalls.Count;
            }
        }
    }

    /// <summary>Number of <see cref="TryDetach"/> calls so far.</summary>
    public int DetachCallCount
    {
        get
        {
            lock (_lock)
            {
                return _detachCalls.Count;
            }
        }
    }

    /// <summary>Snapshot of every <see cref="TryAttach"/> call so far, in invocation order.</summary>
    public IReadOnlyList<RecordedAttachCall> AttachCalls
    {
        get
        {
            lock (_lock)
            {
                return _attachCalls.ToArray();
            }
        }
    }

    /// <summary>Snapshot of every <see cref="TryDetach"/> call so far, in invocation order.</summary>
    public IReadOnlyList<RecordedDetachCall> DetachCalls
    {
        get
        {
            lock (_lock)
            {
                return _detachCalls.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public bool TryAttach(string host, int port, string busId, out string? error)
    {
        lock (_lock)
        {
            _attachCalls.Add(new RecordedAttachCall(host, port, busId));
        }

        if (AttachExceptionToThrow != null)
        {
            throw AttachExceptionToThrow;
        }

        error = AttachResult ? null : AttachError;
        return AttachResult;
    }

    /// <inheritdoc/>
    public bool TryDetach(string busId, out string? error)
    {
        lock (_lock)
        {
            _detachCalls.Add(new RecordedDetachCall(busId));
        }

        if (DetachExceptionToThrow != null)
        {
            throw DetachExceptionToThrow;
        }

        error = DetachResult ? null : DetachError;
        return DetachResult;
    }
}

/// <summary>One <see cref="FakeAttachStrategy.TryAttach"/> invocation captured by the fake.</summary>
public readonly struct RecordedAttachCall
{
    /// <summary>
    /// Initializes a record of one attach with the arguments the caller passed.
    /// </summary>
    /// <param name="host">Host the caller asked to attach from.</param>
    /// <param name="port">TCP port the caller asked to attach to.</param>
    /// <param name="busId">Bus id the caller asked to attach.</param>
    public RecordedAttachCall(string host, int port, string busId)
    {
        Host = host;
        Port = port;
        BusId = busId;
    }

    /// <summary>Host the caller asked to attach from.</summary>
    public string Host { get; }

    /// <summary>TCP port the caller asked to attach to.</summary>
    public int Port { get; }

    /// <summary>Bus id the caller asked to attach.</summary>
    public string BusId { get; }
}

/// <summary>One <see cref="FakeAttachStrategy.TryDetach"/> invocation captured by the fake.</summary>
public readonly struct RecordedDetachCall
{
    /// <summary>
    /// Initializes a record of one detach with the bus id the caller passed.
    /// </summary>
    /// <param name="busId">Bus id the caller asked to detach.</param>
    public RecordedDetachCall(string busId)
    {
        BusId = busId;
    }

    /// <summary>Bus id the caller asked to detach.</summary>
    public string BusId { get; }
}