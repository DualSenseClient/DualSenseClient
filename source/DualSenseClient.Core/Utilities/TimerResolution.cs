using System.Runtime.InteropServices;
using DualSenseClient.Logging;

namespace DualSenseClient.Core.Utilities;

/// <summary>
/// Raises the Windows multimedia timer resolution to 1 ms while the audio writer runs so
/// that <see cref="System.Threading.Thread.Sleep"/> pacing of Bluetooth reports is accurate.
/// Windows defaults to ~15.6 ms timing granularity, which would otherwise stretch each
/// 10 ms writer tick and slow report delivery.
/// </summary>
/// <remarks>
/// <para>
/// Reference counted so multiple players can share the process-wide timing request and the
/// default resolution is restored when the last one stops.
/// </para>
/// <para>
/// On Linux this is a no-op: the kernel uses high-resolution timers (CONFIG_HIGH_RES_TIMERS
/// with tickless idle), so <see cref="System.Threading.Thread.Sleep"/> is already accurate to
/// sub-millisecond without any process-wide setting to raise. The audio writer additionally
/// spins out the final sub-millisecond of each tick, so no equivalent is required.
/// </para>
/// </remarks>
public static class TimerResolution
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("TimerResolution");

    /// <summary>
    /// Serializes the reference-count updates so the underlying timing request is raised
    /// and restored exactly once.
    /// </summary>
    private static readonly Lock _lock = new Lock();

    /// <summary>
    /// Number of active timing requests; the default resolution is restored when this
    /// reaches zero.
    /// </summary>
    private static int _refCount;

    /// <summary>
    /// Requests 1 ms timing resolution. Safe to call multiple times.
    /// </summary>
    public static void AddRef()
    {
        lock (_lock)
        {
            _refCount++;
            if (_refCount == 1)
            {
                Apply();
            }
        }
    }

    /// <summary>
    /// Releases a timing request, restoring the default resolution when the last one ends.
    /// </summary>
    public static void Release()
    {
        lock (_lock)
        {
            if (_refCount == 0)
            {
                return;
            }

            _refCount--;
            if (_refCount == 0)
            {
                Restore();
            }
        }
    }

    /// <summary>
    /// Raises the Windows timer resolution. No-op on other platforms.
    /// </summary>
    private static void Apply()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeMethods.TimeBeginPeriod(1);
            _log.Debug("Windows multimedia timer resolution raised to 1 ms");
        }
        else
        {
            _log.Trace("Timer resolution not adjusted (high-resolution timers already available)");
        }
    }

    /// <summary>
    /// Restores the default timer resolution on Windows. No-op on other platforms.
    /// </summary>
    private static void Restore()
    {
        if (OperatingSystem.IsWindows())
        {
            NativeMethods.TimeEndPeriod(1);
            _log.Debug("Windows multimedia timer resolution restored");
        }
        else
        {
            _log.Trace("Timer resolution not adjusted (high-resolution timers already available)");
        }
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint uMilliseconds);
    }
}