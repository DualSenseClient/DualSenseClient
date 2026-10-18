using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the edge cases of the raw input passthrough shared by the DualShock 4 and DualSense
/// personas: stored-copy isolation, clearing without a raw report, set/clear/set cycles and the
/// drain of concurrent set/clear flips while reports are being pulled. The semantics follow the
/// raw-passthrough contract (behavior oracle only): while a raw 64-byte report is set, every pull
/// serves it verbatim and the synthetic counters freeze; clearing resumes the synthetic builder
/// from its current counters. The per-persona basics
/// (byte-exact ticks, counter resume, length rejection) live in the two device test files.
/// </summary>
[TestFixture]
public sealed class RawInputPassthroughTests
{
    /// <summary>Report id of the input reports both personas serve (0x01).</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Raw report size of both personas: the report id byte plus a 63-byte payload.</summary>
    private const int RawReportSize = 64;

    /// <summary>Report offset of the sequence counter both personas write at byte 7 (DS4 in bits 2-7, DualSense verbatim).</summary>
    private const int OffsetPacketCounter = 7;

    /// <summary>Persona covered by an edge case; both personas share the base pass-through plumbing.</summary>
    public enum Persona
    {
        /// <summary>DualShock 4 v2 persona.</summary>
        DualShock4,

        /// <summary>DualSense persona.</summary>
        DualSense
    }

    [TestCase(Persona.DualShock4)]
    [TestCase(Persona.DualSense)]
    public void SetRawInputReport_CallerMutatesTheBufferAfterwards_ServesTheStoredCopy(Persona persona)
    {
        VirtualUsbDevice device = CreateDevice(persona);
        byte[] raw = CreateRawReport();
        byte[] expected = CreateRawReport();
        SetRawInputReport(device, raw);
        Array.Clear(raw); // the device owns its copy: mutating the caller's buffer must not reach it
        byte[] report = new byte[RawReportSize];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.EqualTo(RawReportSize));
        Assert.That(report, Is.EqualTo(expected));
    }

    [TestCase(Persona.DualShock4)]
    [TestCase(Persona.DualSense)]
    public void ClearRawInputReport_WithoutARawReport_LeavesSyntheticReportingIntact(Persona persona)
    {
        VirtualUsbDevice device = CreateDevice(persona);
        byte[] first = new byte[RawReportSize];
        byte[] second = new byte[RawReportSize];

        ClearRawInputReport(device); // no raw report is set: a no-op
        device.ProvideInputReport(first);
        device.ProvideInputReport(second);

        Assert.That(first[0], Is.EqualTo(InputReportId));
        Assert.That(second[0], Is.EqualTo(InputReportId));
        Assert.That(second[OffsetPacketCounter], Is.Not.EqualTo(first[OffsetPacketCounter])); // the synthetic counter still advances
    }

    [TestCase(Persona.DualShock4)]
    [TestCase(Persona.DualSense)]
    public void SetClearSetCycles_EveryCycleServesTheCurrentReport(Persona persona)
    {
        VirtualUsbDevice device = CreateDevice(persona);
        byte[] first = CreateRawReport(0xA1);
        byte[] second = CreateRawReport(0xB2);
        byte[] report = new byte[RawReportSize];

        SetRawInputReport(device, first);
        device.ProvideInputReport(report);
        Assert.That(report, Is.EqualTo(first));

        SetRawInputReport(device, second); // replacing without clearing swaps the served report
        device.ProvideInputReport(report);
        Assert.That(report, Is.EqualTo(second));

        ClearRawInputReport(device);
        device.ProvideInputReport(report);
        Assert.That(report[0], Is.EqualTo(InputReportId)); // synthetic resumed
        Assert.That(report, Is.Not.EqualTo(second));

        SetRawInputReport(device, first);
        device.ProvideInputReport(report);
        Assert.That(report, Is.EqualTo(first));

        ClearRawInputReport(device);
        device.ProvideInputReport(report);
        Assert.That(report[0], Is.EqualTo(InputReportId));
        Assert.That(report, Is.Not.EqualTo(first));
    }

    [TestCase(Persona.DualShock4)]
    [TestCase(Persona.DualSense)]
    public void ProvideInputReport_ConcurrentRawFlips_NeverServesATornReport(Persona persona)
    {
        VirtualUsbDevice device = CreateDevice(persona);
        byte[][] expectedSynthetic = CollectSyntheticReports(CreateDevice(persona));
        byte[] raw = CreateRawReport();
        const int pulls = 20_000;
        const int flips = 5_000;
        int rawCount = 0;
        int syntheticCount = 0;
        string? failure = null;

        SetRawInputReport(device, raw); // the first pull sees raw until the flipper's first clear
        using Barrier start = new Barrier(2);
        Task puller = Task.Run(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < pulls && failure is null; i++)
            {
                byte[] report = new byte[RawReportSize];
                int written = device.ProvideInputReport(report);
                if (written != RawReportSize)
                {
                    failure = $"pull {i} wrote {written} bytes instead of {RawReportSize}.";
                    break;
                }

                if (report.AsSpan().SequenceEqual(raw))
                {
                    rawCount++;
                    continue;
                }

                byte[] expected = expectedSynthetic[syntheticCount % expectedSynthetic.Length];
                if (!report.AsSpan().SequenceEqual(expected))
                {
                    failure =
                        $"pull {i} served a report that is neither the published raw report nor a fully built synthetic report: {Convert.ToHexString(report)}";
                    break;
                }

                syntheticCount++;
            }
        });
        Task flipper = Task.Run(() =>
        {
            start.SignalAndWait();
            for (int i = 0; i < flips; i++)
            {
                ClearRawInputReport(device);
                SetRawInputReport(device, raw);
            }
        });

        Task.WaitAll(puller, flipper);

        Assert.That(failure, Is.Null, failure);
        Assert.That(rawCount, Is.GreaterThan(0), "the raw report must have been served");
        Assert.That(syntheticCount, Is.GreaterThan(0), "synthetic reports must have been served");
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static VirtualUsbDevice CreateDevice(Persona persona)
    {
        return persona switch
        {
            Persona.DualShock4 => new DualShock4Device(new DualShock4Identity()),
            Persona.DualSense => new DualSenseDevice(new DualSenseIdentity()),
            _ => throw new ArgumentOutOfRangeException(nameof(persona), persona, "Unknown persona.")
        };
    }

    /// <summary>Publishes a raw report through the persona's public surface.</summary>
    private static void SetRawInputReport(VirtualUsbDevice device, byte[] report)
    {
        switch (device)
        {
            case DualShock4Device dualShock4:
                dualShock4.SetRawInputReport(report);
                break;

            case DualSenseDevice dualSense:
                dualSense.SetRawInputReport(report);
                break;

            default:
                throw new ArgumentException("The device is not a raw-capable persona.", nameof(device));
        }
    }

    /// <summary>Clears the raw report through the persona's public surface.</summary>
    private static void ClearRawInputReport(VirtualUsbDevice device)
    {
        switch (device)
        {
            case DualShock4Device dualShock4:
                dualShock4.ClearRawInputReport();
                break;

            case DualSenseDevice dualSense:
                dualSense.ClearRawInputReport();
                break;

            default:
                throw new ArgumentException("The device is not a raw-capable persona.", nameof(device));
        }
    }

    /// <summary>
    /// Creates one raw 64-byte report with the given fill byte; report byte 0 stays the input
    /// report id, like the report id byte of a captured hardware report.
    /// </summary>
    private static byte[] CreateRawReport(byte fill = 0x5A)
    {
        byte[] report = new byte[RawReportSize];
        Array.Fill(report, fill);
        report[0] = InputReportId;
        return report;
    }

    /// <summary>
    /// Collects 256 consecutive synthetic reports of a fresh device that never sees a raw report.
    /// The neutral sequence has no other varying byte than the sequence counter, whose wrap (DS4
    /// six bits, DualSense one byte) divides 256, so the k-th synthetic report a device serves
    /// equals <c>reports[k % 256]</c> while it stays neutral.
    /// </summary>
    private static byte[][] CollectSyntheticReports(VirtualUsbDevice device)
    {
        byte[][] reports = new byte[256][];
        for (int i = 0; i < reports.Length; i++)
        {
            reports[i] = new byte[RawReportSize];
            device.ProvideInputReport(reports[i]);
        }

        return reports;
    }
}