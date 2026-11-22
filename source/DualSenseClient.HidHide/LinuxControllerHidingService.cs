using DualSenseClient.Logging;

namespace DualSenseClient.HidHide;

/// <summary>
/// Linux implementation of <see cref="IControllerHidingService"/> backed by the
/// <c>dualsense-hiding-helper</c> daemon.
/// </summary>
/// <remarks>
/// The app must run with the effective GID of the <c>dualsenseclient</c> group so it can
/// open hidden device nodes and the helper socket. The helper performs all privileged work.
/// </remarks>
public sealed class LinuxControllerHidingService : IControllerHidingService, IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("LinuxHiding");

    /// <summary>
    /// Filesystem and group locations.
    /// </summary>
    private readonly LinuxHidingOptions _options;

    /// <summary>
    /// Helper transport.
    /// </summary>
    private readonly ILinuxHidingClient _client;

    /// <summary>
    /// Supplies the process's effective GID; injectable for tests.
    /// </summary>
    private readonly Func<uint> _effectiveGid;

    /// <summary>
    /// Guards the connection and hidden-ID cache.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// IDs this client believes are hidden.
    /// </summary>
    private readonly HashSet<string> _hiddenIds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether the transport is currently connected.
    /// </summary>
    private bool _connected;

    /// <summary>
    /// Whether cached hides must be re-sent after a reconnect.
    /// </summary>
    private bool _needsResync;

    /// <summary>
    /// Non-zero once disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Last observed client connection generation.
    /// </summary>
    private long _lastGeneration;

    /// <summary>
    /// Creates a service with the production paths and transport.
    /// </summary>
    public LinuxControllerHidingService()
    {
        LinuxHidingOptions options = new LinuxHidingOptions();
        _options = options;
        _client = new UnixSocketHidingClient(options.SocketPath);
        _effectiveGid = LinuxHidingIdentity.GetEffectiveGid;
    }

    /// <summary>
    /// Creates a service with injectable paths and transport.
    /// </summary>
    /// <param name="options">Filesystem and group locations.</param>
    /// <param name="client">Helper transport.</param>
    /// <param name="effectiveGid">Supplies the process's effective GID.</param>
    internal LinuxControllerHidingService(LinuxHidingOptions options, ILinuxHidingClient client, Func<uint> effectiveGid)
    {
        _options = options;
        _client = client;
        _effectiveGid = effectiveGid;
    }

    /// <inheritdoc/>
    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                if (_disposed || !OperatingSystem.IsLinux())
                {
                    return false;
                }

                return IsAvailableLocked();
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGetInstanceId(string devicePath, out string instanceId)
    {
        instanceId = string.Empty;
        if (string.IsNullOrEmpty(devicePath))
        {
            return false;
        }

        if (!LinuxHidingIdentity.TryGetHidUniq(_options.SysfsRoot, devicePath, out string id))
        {
            return false;
        }

        instanceId = id;
        return true;
    }

    /// <inheritdoc/>
    public bool IsControllerHidden(string instanceId)
    {
        if (!HidingProtocol.IsValidId(instanceId))
        {
            return false;
        }

        lock (_sync)
        {
            if (_disposed || !OperatingSystem.IsLinux() || !IsAvailableLocked())
            {
                return false;
            }

            if (!_client.TryGetStatus(instanceId, out bool hidden, out string error))
            {
                _log.Warning($"Failed to read Linux hiding status for '{instanceId}': {error}");
                _connected = false;
                _needsResync = true;

                // Keep the cached intention: a transient helper failure must not make the
                // app forget that the user asked to hide this controller.
                return _hiddenIds.Contains(instanceId);
            }

            if (_client.ConnectionGeneration != _lastGeneration)
            {
                _lastGeneration = _client.ConnectionGeneration;
                if (ResyncHiddenLocked() && _hiddenIds.Contains(instanceId))
                {
                    hidden = true;
                }
            }

            if (hidden)
            {
                _hiddenIds.Add(instanceId);
            }
            else
            {
                _hiddenIds.Remove(instanceId);
            }

            return hidden;
        }
    }

    /// <inheritdoc/>
    public void SetControllerHidden(string instanceId, bool hidden)
    {
        if (!HidingProtocol.IsValidId(instanceId))
        {
            _log.Warning($"Refusing to hide invalid controller ID '{instanceId}'");
            return;
        }

        lock (_sync)
        {
            if (_disposed || !OperatingSystem.IsLinux() || !IsAvailableLocked())
            {
                return;
            }

            if (hidden == _hiddenIds.Contains(instanceId))
            {
                return;
            }

            if (!_client.TrySetHidden(instanceId, hidden, out string error))
            {
                _log.Warning($"Failed to {(hidden ? "hide" : "unhide")} Linux controller '{instanceId}': {error}");
                _connected = false;
                _needsResync = true;
                return;
            }

            if (hidden)
            {
                _hiddenIds.Add(instanceId);
            }
            else
            {
                _hiddenIds.Remove(instanceId);
            }

            _log.Info($"{(hidden ? "Hidden" : "Unhidden")} Linux controller: {instanceId}");
        }
    }

    /// <inheritdoc/>
    public void EnsureSelfVisible()
    {
        lock (_sync)
        {
            if (_disposed || !OperatingSystem.IsLinux())
            {
                return;
            }

            // Force a fresh handshake and re-send cached hides. This covers a helper restart
            // that lost its in-memory state while the app stayed connected.
            _connected = false;
            _needsResync = _hiddenIds.Count > 0;
            IsAvailableLocked();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client.Dispose();
        }
    }

    /// <summary>
    /// Checks the group and connection while the caller holds <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when hiding is available.</returns>
    private bool IsAvailableLocked()
    {
        if (!LinuxHidingIdentity.TryGetGroupGid(_options.GroupFile, _options.GroupName, out uint groupGid))
        {
            _log.Debug($"Linux hiding unavailable: group '{_options.GroupName}' not found in '{_options.GroupFile}'");
            return false;
        }

        if (_effectiveGid() != groupGid)
        {
            _log.Debug(
                $"Linux hiding unavailable: effective GID {_effectiveGid()} does not match group '{_options.GroupName}' ({groupGid}); start the app through the hiding launcher");
            return false;
        }

        return EnsureConnectedLocked();
    }

    /// <summary>
    /// Connects and re-sends cached hides while the caller holds <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when connected and resynchronized.</returns>
    private bool EnsureConnectedLocked()
    {
        if (_connected)
        {
            return true;
        }

        if (!_client.TryConnect(out string error))
        {
            _log.Debug($"Linux hiding helper connection failed: {error}");
            return false;
        }

        _connected = true;
        _lastGeneration = _client.ConnectionGeneration;

        if (!_needsResync)
        {
            return true;
        }

        return ResyncHiddenLocked();
    }

    /// <summary>
    /// Re-sends HIDE for every cached controller while the caller holds <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when all cached hides were restored.</returns>
    private bool ResyncHiddenLocked()
    {
        foreach (string id in _hiddenIds)
        {
            if (!_client.TrySetHidden(id, true, out string resyncError))
            {
                _log.Warning($"Failed to restore Linux hidden state for '{id}': {resyncError}");
                _connected = false;
                _needsResync = true;
                return false;
            }
        }

        _needsResync = false;
        return true;
    }
}