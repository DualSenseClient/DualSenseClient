using System.Diagnostics;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// Shared plumbing of the env-gated Emulation end-to-end fixtures: HID enumeration collection, the
/// new-device and disappearance polls, the predicate-driven input-report reader and the
/// write-output-report-and-wait pattern. The helpers are generalized over the persona's VID/PID and
/// decoded output-report type while carrying the M1 end-to-end constant set (every timeout and
/// poll interval of the original DualShock 4 fixture), so every persona reuses the same plumbing
/// and pins only its own ids, layouts and payloads. Every helper is blocking and bounded by the
/// timeout it receives; a condition the caller is expected to assert on yields <see langword="false"/>
/// or <see langword="null"/> instead of throwing, and only the output-report wait fails the test
/// itself (its timeout is the assertion).
/// </summary>
public static class EmulationIntegrationSupport
{
    /// <summary>Timeout of the HID enumeration poll for the attached controllers.</summary>
    public static readonly TimeSpan DeviceEnumerationTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Timeout of the HID enumeration poll after detach.</summary>
    public static readonly TimeSpan DeviceDisappearanceTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Timeout of the poll for the first input report of an attached controller.</summary>
    public static readonly TimeSpan InputReportTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Timeout of the poll for a pinned input-state change, e.g. a pressed button bit.</summary>
    public static readonly TimeSpan InputChangeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Timeout of the wait for an output report to travel back from the host.</summary>
    public static readonly TimeSpan OutputReportTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Timeout of one blocking HID read inside a longer poll.</summary>
    private const int ReadTimeoutMilliseconds = 250;

    /// <summary>Delay between the enumeration polls.</summary>
    private const int PollIntervalMilliseconds = 100;

    /// <summary>Collects the platform paths of every currently enumerated HID device of one VID/PID.</summary>
    /// <param name="enumerator">HID enumerator to poll.</param>
    /// <param name="vendorId">Vendor id of the persona to collect.</param>
    /// <param name="productId">Product id of the persona to collect.</param>
    /// <returns>The paths present before the attach, so the new device can be identified.</returns>
    public static HashSet<string> CollectDevicePaths(HidDeviceEnumerator enumerator, ushort vendorId, ushort productId)
    {
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IHidDeviceInfo info in enumerator.Enumerate(vendorId, productId))
        {
            paths.Add(info.Path);
        }

        return paths;
    }

    /// <summary>
    /// Polls the HID enumeration for the first device of the given VID/PID whose path was not
    /// present in <paramref name="baselinePaths"/>, i.e. the controller a test just attached.
    /// </summary>
    /// <param name="enumerator">HID enumerator to poll.</param>
    /// <param name="baselinePaths">Paths enumerated before the attach.</param>
    /// <param name="vendorId">Vendor id of the persona to watch.</param>
    /// <param name="productId">Product id of the persona to watch.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns>The newly enumerated device, or <see langword="null"/> on timeout.</returns>
    public static IHidDeviceInfo? WaitForNewDevice(
        HidDeviceEnumerator enumerator,
        ISet<string> baselinePaths,
        ushort vendorId,
        ushort productId,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            foreach (IHidDeviceInfo info in enumerator.Enumerate(vendorId, productId))
            {
                if (!baselinePaths.Contains(info.Path))
                {
                    return info;
                }
            }

            Thread.Sleep(PollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return null;
    }

    /// <summary>Polls the HID enumeration until <paramref name="devicePath"/> is gone.</summary>
    /// <param name="enumerator">HID enumerator to poll.</param>
    /// <param name="vendorId">Vendor id of the persona to watch.</param>
    /// <param name="productId">Product id of the persona to watch.</param>
    /// <param name="devicePath">Path of the controller to watch.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns><see langword="true"/> when the device disappeared; <see langword="false"/> on timeout.</returns>
    public static bool WaitForDeviceDisappearance(
        HidDeviceEnumerator enumerator,
        ushort vendorId,
        ushort productId,
        string devicePath,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            bool present = false;
            foreach (IHidDeviceInfo info in enumerator.Enumerate(vendorId, productId))
            {
                if (string.Equals(info.Path, devicePath, StringComparison.OrdinalIgnoreCase))
                {
                    present = true;
                    break;
                }
            }

            if (!present)
            {
                return true;
            }

            Thread.Sleep(PollIntervalMilliseconds);
        } while (stopwatch.Elapsed < timeout);

        return false;
    }

    /// <summary>
    /// Reads input reports until <paramref name="matches"/> accepts one; every received report is
    /// recorded in <paramref name="lastReportHex"/> for failure diagnostics. The predicate receives
    /// the report buffer and the number of bytes the last read returned, so a persona's own
    /// layout assertion (report id, length, pinned bit) stays in its fixture.
    /// </summary>
    /// <param name="hid">Open controller handle.</param>
    /// <param name="report">Report buffer the reads fill.</param>
    /// <param name="matches">Predicate deciding whether the last read carried the awaited report.</param>
    /// <param name="lastReportHex">Receives the hex of the last report seen.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns><see langword="true"/> when the predicate accepted a report.</returns>
    public static bool WaitForInputReport(
        IHidDevice hid,
        byte[] report,
        Func<byte[], int, bool> matches,
        ref string lastReportHex,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            int length = hid.Read(report, 0, report.Length, ReadTimeoutMilliseconds);
            if (length > 0)
            {
                lastReportHex = Convert.ToHexString(report.AsSpan(0, length));
            }

            if (matches(report, length))
            {
                return true;
            }
        } while (stopwatch.Elapsed < timeout);

        return false;
    }

    /// <summary>
    /// Writes one output report through the HID wrapper and waits until the persona raises its
    /// output-report event with a report <paramref name="matchesWritten"/> accepts; a report the
    /// predicate rejects (e.g. a lightbar initialization another host process sends) is recorded
    /// for diagnostics but does not satisfy the wait. The written report is expected to be
    /// id-prefixed, the shape the interrupt OUT path delivers.
    /// </summary>
    /// <typeparam name="TOutput">Decoded output-report type of the persona's event.</typeparam>
    /// <param name="hid">Open handle of the enumerated controller.</param>
    /// <param name="outputReport">Report bytes to write, report id first.</param>
    /// <param name="reportId">Report id of <paramref name="outputReport"/>, for the diagnostics.</param>
    /// <param name="subscribe">Attaches the wait's handler to the persona's output-report event.</param>
    /// <param name="unsubscribe">Detaches the handler attached by <paramref name="subscribe"/>.</param>
    /// <param name="matchesWritten">Returns whether a decoded report is the written one.</param>
    /// <param name="describe">Describes a decoded report for the test output.</param>
    /// <param name="timeout">How long to wait for the report to come back.</param>
    public static void WriteOutputReportAndWait<TOutput>(
        IHidDevice hid,
        byte[] outputReport,
        byte reportId,
        Action<Action<TOutput>> subscribe,
        Action<Action<TOutput>> unsubscribe,
        Func<TOutput, bool> matchesWritten,
        Func<TOutput, string> describe,
        TimeSpan timeout)
    {
        TaskCompletionSource<TOutput> outputMatched = new TaskCompletionSource<TOutput>(TaskCreationOptions.RunContinuationsAsynchronously);
        object outputLock = new object();
        bool outputSeen = false;
        TOutput lastOutput = default!;
        Action<TOutput> handler = received =>
        {
            lock (outputLock)
            {
                lastOutput = received;
                outputSeen = true;
            }

            if (matchesWritten(received))
            {
                outputMatched.TrySetResult(received);
            }
        };

        subscribe(handler);
        try
        {
            int written = hid.Write(outputReport, 0, outputReport.Length);
            TestContext.Out.WriteLine($"Wrote {written} byte(s) of output report 0x{reportId:X2}.");

            bool matched = outputMatched.Task.Wait(timeout);
            if (!matched)
            {
                string observed;
                lock (outputLock)
                {
                    observed = outputSeen ? describe(lastOutput) : "(no output report received)";
                }

                Assert.Fail($"The written output report 0x{reportId:X2} did not come back through the persona within "
                            + $"{timeout.TotalSeconds:0} s; last report observed on the persona: {observed}");
            }

            TestContext.Out.WriteLine($"Output report came back from the host: {describe(outputMatched.Task.Result)}");
        }
        finally
        {
            unsubscribe(handler);
        }
    }
}