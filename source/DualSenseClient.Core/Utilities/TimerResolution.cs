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
/// Since Windows 11, the system silently ignores this process's resolution request whenever
/// its window becomes fully occluded, minimized or otherwise invisible and it renders no
/// audible endpoint stream (the controller's speaker is fed via HID reports, which does not
/// count), reverting sleeps to ~15.6 ms and breaking the report cadence. While raising the
/// resolution, the process therefore also opts out of that power-throttling behavior via
/// <see cref="NativeMethods.SetProcessInformation"/> (same opt-out as Chromium/OBS).
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
    /// Raises the Windows timer resolution and opts out of the Windows 11 background
    /// power throttling that would otherwise ignore the request while the window is not
    /// visible. No-op on other platforms.
    /// </summary>
    private static void Apply()
    {
        if (OperatingSystem.IsWindows())
        {
            DisableBackgroundTimerThrottling();
            NativeMethods.TimeBeginPeriod(1);
            _log.Debug("Windows multimedia timer resolution raised to 1 ms");
        }
        else
        {
            _log.Trace("Timer resolution not adjusted (high-resolution timers already available)");
        }
    }

    /// <summary>
    /// Disables <c>PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION</c> and
    /// <c>PROCESS_POWER_THROTTLING_EXECUTION_SPEED</c> for the process: without the opt-out,
    /// Windows 11 stops honoring the resolution request as soon as the window is fully
    /// occluded, minimized or unfocused, and additionally slows the spin-wait via EcoQoS.
    /// Failures are ignored: pre-Windows-11 rejects the unknown control mask, and neither
    /// throttling behavior applies there anyway. Must be called on Windows only.
    /// </summary>
    private static void DisableBackgroundTimerThrottling()
    {
        NativeMethods.ProcessPowerThrottlingState state = new NativeMethods.ProcessPowerThrottlingState
        {
            Version = NativeMethods.CurrentVersion,
            ControlMask = NativeMethods.IgnoreTimerResolution | NativeMethods.ExecutionSpeed,
            StateMask = 0
        };
        if (!NativeMethods.SetProcessInformation(NativeMethods.GetCurrentProcess(), NativeMethods.ProcessPowerThrottlingClass,
                ref state, Marshal.SizeOf(state)))
        {
            _log.Debug($"SetProcessInformation(ProcessPowerThrottling) failed with error {Marshal.GetLastWin32Error()}; continuing without the opt-out");
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

        /// <summary>
        /// The <see cref="SetProcessInformation"/> information class selecting power throttling.
        /// </summary>
        public const int ProcessPowerThrottlingClass = 4;

        /// <summary>
        /// <c>PROCESS_POWER_THROTTLING_CURRENT_VERSION</c>.
        /// </summary>
        public const uint CurrentVersion = 1;

        /// <summary>
        /// <c>PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION</c>: the system may ignore the
        /// process's timer resolution requests while its window is invisible.
        /// </summary>
        public const uint IgnoreTimerResolution = 0x4;

        /// <summary>
        /// <c>PROCESS_POWER_THROTTLING_EXECUTION_SPEED</c>: the system may run the process as
        /// EcoQoS (reduced CPU frequency / efficiency cores) when it is backgrounded.
        /// </summary>
        public const uint ExecutionSpeed = 0x1;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// Mirrors <c>PROCESS_POWER_THROTTLING_STATE</c>.
        /// </summary>
        public struct ProcessPowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref ProcessPowerThrottlingState processInformation, int size);
    }
}