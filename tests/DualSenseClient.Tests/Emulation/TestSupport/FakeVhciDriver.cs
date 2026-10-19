using DualSenseClient.Emulation.Windows;
using Microsoft.Win32.SafeHandles;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// <see cref="IVhciDriver"/> test double: records every call and returns the configured results,
/// so consumers of the seam can be tested without the usbip-win2 driver. The default handle is an
/// invalid, non-owned handle, so tests that only exercise connect/disconnect need no device.
/// </summary>
public sealed class FakeVhciDriver : IVhciDriver
{
    private readonly object _lock = new object();

    private readonly List<string> _openedPaths = new List<string>();

    private readonly List<VhciAttachRequest> _connectRequests = new List<VhciAttachRequest>();

    private readonly List<uint> _disconnectedPorts = new List<uint>();

    /// <summary>Handle returned by every <see cref="Open"/>; defaults to an invalid, non-owned handle.</summary>
    public SafeFileHandle HandleToReturn { get; set; } = new SafeFileHandle(new IntPtr(-1), false);

    /// <summary>Result returned by every <see cref="TryConnect"/>.</summary>
    public bool ConnectResult { get; set; }

    /// <summary>Port reported by every <see cref="TryConnect"/>.</summary>
    public uint PortToReturn { get; set; }

    /// <summary>Win32 error reported by every <see cref="TryConnect"/>.</summary>
    public int ConnectWin32Error { get; set; }

    /// <summary>Result returned by every <see cref="TryDisconnect"/>.</summary>
    public bool DisconnectResult { get; set; }

    /// <summary>Win32 error reported by every <see cref="TryDisconnect"/>.</summary>
    public int DisconnectWin32Error { get; set; }

    /// <summary>Snapshot of every device path passed to <see cref="Open"/>, in invocation order.</summary>
    public IReadOnlyList<string> OpenedPaths
    {
        get
        {
            lock (_lock)
            {
                return _openedPaths.ToArray();
            }
        }
    }

    /// <summary>Snapshot of every attach request passed to <see cref="TryConnect"/>, in invocation order.</summary>
    public IReadOnlyList<VhciAttachRequest> ConnectRequests
    {
        get
        {
            lock (_lock)
            {
                return _connectRequests.ToArray();
            }
        }
    }

    /// <summary>Snapshot of every port passed to <see cref="TryDisconnect"/>, in invocation order.</summary>
    public IReadOnlyList<uint> DisconnectedPorts
    {
        get
        {
            lock (_lock)
            {
                return _disconnectedPorts.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public SafeFileHandle Open(string devicePath)
    {
        lock (_lock)
        {
            _openedPaths.Add(devicePath);
        }

        return HandleToReturn;
    }

    /// <inheritdoc/>
    public bool TryConnect(SafeFileHandle device, VhciAttachRequest request, out uint port, out int win32Error)
    {
        lock (_lock)
        {
            _connectRequests.Add(request);
        }

        port = PortToReturn;
        win32Error = ConnectWin32Error;
        return ConnectResult;
    }

    /// <inheritdoc/>
    public bool TryDisconnect(SafeFileHandle device, uint port, out int win32Error)
    {
        lock (_lock)
        {
            _disconnectedPorts.Add(port);
        }

        win32Error = DisconnectWin32Error;
        return DisconnectResult;
    }
}