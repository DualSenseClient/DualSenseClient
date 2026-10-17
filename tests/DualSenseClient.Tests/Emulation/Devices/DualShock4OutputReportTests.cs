using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the DualShock 4 output report 0x05 decode and the persona device that ties the descriptor
/// set, the input report builder and the HID request engine together. The payload layout: the
/// interrupt OUT path forwards the id-prefixed payload (and the SET_REPORT payload with the report
/// id in wValue) to the Bluetooth output decoder, and the LED path pins the LED-enable flag at
/// payload byte 0 (USB report byte 1) and the RGB bytes at payload 5-7 (USB report bytes 6-8); the
/// rumble motors and flash durations follow the same layout (USB report bytes 4-5 and 9-10),
/// corroborated by the reference, which snapshots exactly USB output-report bytes 4-8: the two
/// rumble motors followed by the RGB bytes. Device assertions cover the neutral initialization
/// ruling (centered sticks, released D-pad), the device-owned counters and the output-report event
/// path.
/// </summary>
[TestFixture]
public sealed class DualShock4OutputReportTests
{
    /// <summary>Report id of the DualShock 4 output report under test (0x05).</summary>
    private const byte OutputReportId = 0x05;

    /// <summary>Report id of the DualShock 4 input report the device serves (0x01).</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Neutral stick value (center).</summary>
    private const byte NeutralStick = 128;

    /// <summary>D-pad hat value of the released state (reference: "hat released (0x08)").</summary>
    private const byte ReleasedHat = 0x08;

    /// <summary>Face button bit of Cross in the input report's hat/face byte.</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Length in bytes of the reference USB output report 0x05: report id plus a 31-byte payload.</summary>
    private const int OutputReportSize = 32;

    /// <summary>Report offset of the input report's first stick axis (LX, then LY, RX and RY).</summary>
    private const int OffsetSticks = 1;

    /// <summary>Report offset of the input report's hat/face byte.</summary>
    private const int OffsetFaceAndDpad = 5;

    /// <summary>Report offset of the input report's PS/touchpad bits and the 6-bit packet counter.</summary>
    private const int OffsetPacketCounter = 7;

    /// <summary>Report offset of the input report's battery/ext byte.</summary>
    private const int OffsetBattery = 30;

    /// <summary>Report offset of the input report's first rolling touch counter.</summary>
    private const int OffsetTouch1Counter = 34;

    [Test]
    public void TryParse_RumblePayload_ReadsTheEnableFlagAndBothMotorBytes()
    {
        byte[] payload = CreateOutputPayload(0x01, 0x40, 0x80);

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEnabled, Is.True);
        Assert.That(report.RightMotor, Is.EqualTo(0x40));
        Assert.That(report.LeftMotor, Is.EqualTo(0x80));
        Assert.That(report.LightbarEnabled, Is.False);
        Assert.That(report.FlashEnabled, Is.False);
        Assert.That(report.Red, Is.Zero);
        Assert.That(report.Green, Is.Zero);
        Assert.That(report.Blue, Is.Zero);
        Assert.That(report.FlashOn, Is.Zero);
        Assert.That(report.FlashOff, Is.Zero);
    }

    [Test]
    public void TryParse_RumbleDisabledPayload_ReportsNoRumble()
    {
        byte[] payload = CreateOutputPayload(0x00);

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEnabled, Is.False);
        Assert.That(report.RightMotor, Is.Zero);
        Assert.That(report.LeftMotor, Is.Zero);
    }

    [Test]
    public void TryParse_LightbarWhitePayload_ReadsTheRgbBytes()
    {
        byte[] payload = CreateOutputPayload(0x02, red: 0xFF, green: 0xFF, blue: 0xFF);

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.LightbarEnabled, Is.True);
        Assert.That(report.Red, Is.EqualTo(0xFF));
        Assert.That(report.Green, Is.EqualTo(0xFF));
        Assert.That(report.Blue, Is.EqualTo(0xFF));
    }

    [Test]
    public void TryParse_FlashPayload_ReadsTheFlashDurations()
    {
        byte[] payload = CreateOutputPayload(0x04, flashOn: 0x0A, flashOff: 0x14);

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.FlashEnabled, Is.True);
        Assert.That(report.FlashOn, Is.EqualTo(0x0A));
        Assert.That(report.FlashOff, Is.EqualTo(0x14));
    }

    [Test]
    public void TryParse_AllFeaturesEnabledPayload_ReadsEveryField()
    {
        byte[] payload = CreateOutputPayload(0x07, 0x28, 0x64, 0x12, 0x34, 0x56, 0x0A, 0x14);

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEnabled, Is.True);
        Assert.That(report.RightMotor, Is.EqualTo(0x28));
        Assert.That(report.LeftMotor, Is.EqualTo(0x64));
        Assert.That(report.LightbarEnabled, Is.True);
        Assert.That(report.Red, Is.EqualTo(0x12));
        Assert.That(report.Green, Is.EqualTo(0x34));
        Assert.That(report.Blue, Is.EqualTo(0x56));
        Assert.That(report.FlashEnabled, Is.True);
        Assert.That(report.FlashOn, Is.EqualTo(0x0A));
        Assert.That(report.FlashOff, Is.EqualTo(0x14));
    }

    [Test]
    public void TryParse_MinimumLengthPayload_ParsesTheCommonFields()
    {
        byte[] payload =
        [
            OutputReportId, 0x07, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77
        ];

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(payload.Length, Is.EqualTo(DualShock4OutputReport.MinimumSize));
        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEnabled, Is.True);
        Assert.That(report.RightMotor, Is.EqualTo(0x11));
        Assert.That(report.LeftMotor, Is.EqualTo(0x22));
        Assert.That(report.LightbarEnabled, Is.True);
        Assert.That(report.Red, Is.EqualTo(0x33));
        Assert.That(report.Green, Is.EqualTo(0x44));
        Assert.That(report.Blue, Is.EqualTo(0x55));
        Assert.That(report.FlashEnabled, Is.True);
        Assert.That(report.FlashOn, Is.EqualTo(0x66));
        Assert.That(report.FlashOff, Is.EqualTo(0x77));
    }

    [Test]
    public void TryParse_TrailingBytesBeyondTheCommonFields_AreIgnored()
    {
        byte[] payload = CreateOutputPayload(0x07, 0x28, 0x64, 0x12, 0x34, 0x56, 0x0A, 0x14);
        for (int i = DualShock4OutputReport.MinimumSize; i < payload.Length; i++)
        {
            payload[i] = 0xEE;
        }

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEnabled, Is.True);
        Assert.That(report.RightMotor, Is.EqualTo(0x28));
        Assert.That(report.LeftMotor, Is.EqualTo(0x64));
        Assert.That(report.LightbarEnabled, Is.True);
        Assert.That(report.Red, Is.EqualTo(0x12));
        Assert.That(report.Green, Is.EqualTo(0x34));
        Assert.That(report.Blue, Is.EqualTo(0x56));
        Assert.That(report.FlashEnabled, Is.True);
        Assert.That(report.FlashOn, Is.EqualTo(0x0A));
        Assert.That(report.FlashOff, Is.EqualTo(0x14));
    }

    [TestCase((byte)0x00)]
    [TestCase((byte)0x02)]
    [TestCase((byte)0x04)]
    [TestCase((byte)0x06)]
    [TestCase((byte)0x11)]
    [TestCase((byte)0xFF)]
    public void TryParse_WrongReportId_ReturnsFalse(byte reportId)
    {
        byte[] payload = CreateOutputPayload(0x07, 0x28);
        payload[0] = reportId;

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(DualShock4OutputReport)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(10)]
    public void TryParse_TruncatedPayload_ReturnsFalse(int length)
    {
        byte[] payload = CreateOutputPayload(0x01, 0x40)[..length];

        bool parsed = DualShock4OutputReport.TryParse(payload, out DualShock4OutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(DualShock4OutputReport)));
    }

    [Test]
    public void ProvideInputReport_UntouchedDevice_ReportsTheNeutralState()
    {
        DualShock4Device device = CreateDevice();
        byte[] report = new byte[DualShock4InputReport.Size];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.EqualTo(DualShock4InputReport.Size));
        Assert.That(report[0], Is.EqualTo(InputReportId));
        Assert.That(report[OffsetSticks], Is.EqualTo(NeutralStick)); // LX
        Assert.That(report[OffsetSticks + 1], Is.EqualTo(NeutralStick)); // LY
        Assert.That(report[OffsetSticks + 2], Is.EqualTo(NeutralStick)); // RX
        Assert.That(report[OffsetSticks + 3], Is.EqualTo(NeutralStick)); // RY
        Assert.That(report[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat)); // released D-pad, no face button
        Assert.That(report[OffsetBattery], Is.EqualTo(10)); // full battery while discharging
    }

    [Test]
    public void ProvideInputReport_CrossPressed_ReturnsTheReportWithTheCrossBit()
    {
        DualShock4Device device = CreateDevice();
        DualShock4InputState state = CreateNeutralState();
        state.Cross = true;
        device.SetInput(state);
        byte[] report = new byte[DualShock4InputReport.Size];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.EqualTo(DualShock4InputReport.Size));
        Assert.That(report[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | CrossBit));
        Assert.That(report[OffsetSticks], Is.EqualTo(NeutralStick)); // the stored state is what is reported
    }

    [Test]
    public void ProvideInputReport_TwoConsecutiveReports_DifferOnlyInThePacketCounter()
    {
        DualShock4Device device = CreateDevice();
        byte[] first = new byte[DualShock4InputReport.Size];
        byte[] second = new byte[DualShock4InputReport.Size];

        device.ProvideInputReport(first);
        device.ProvideInputReport(second);

        Assert.That(first[OffsetPacketCounter], Is.Zero); // the first report carries counter zero
        Assert.That(second[OffsetPacketCounter], Is.EqualTo(0x04)); // then one, shifted below the six counter bits
        List<int> differing = [];
        for (int i = 0; i < DualShock4InputReport.Size; i++)
        {
            if (first[i] != second[i])
            {
                differing.Add(i);
            }
        }

        Assert.That(differing, Is.EqualTo(new[]
        {
            OffsetPacketCounter
        }));
    }

    [Test]
    public void ProvideInputReport_ActiveTouchPoint_AdvancesItsCounterAndHoldsItWhenReleased()
    {
        DualShock4Device device = CreateDevice();
        DualShock4InputState touched = CreateNeutralState();
        touched.Touch1Active = true;
        touched.Touch1Id = 0x07;
        touched.Touch1X = 0x123;
        touched.Touch1Y = 0x231;
        byte[] active = new byte[DualShock4InputReport.Size];
        byte[] releasedFirst = new byte[DualShock4InputReport.Size];
        byte[] releasedSecond = new byte[DualShock4InputReport.Size];

        device.SetInput(touched);
        device.ProvideInputReport(active);
        device.SetInput(CreateNeutralState());
        device.ProvideInputReport(releasedFirst);
        device.ProvideInputReport(releasedSecond);

        Assert.That(active[OffsetTouch1Counter], Is.Zero); // the first report carries counter zero
        Assert.That(releasedFirst[OffsetTouch1Counter], Is.EqualTo(1)); // advanced once while the point was active
        Assert.That(releasedSecond[OffsetTouch1Counter], Is.EqualTo(releasedFirst[OffsetTouch1Counter])); // held while inactive
    }

    [Test]
    public void ProvideInputReport_ConcurrentCallers_WriteDistinctPacketCounters()
    {
        DualShock4Device device = CreateDevice();
        const int reportsPerTask = 32; // both tasks together consume all 64 counter values of one wrap
        byte[] counters = new byte[2 * reportsPerTask];
        using Barrier start = new Barrier(2); // force both callers to begin pulling simultaneously
        Task first = Task.Run(() =>
        {
            start.SignalAndWait();
            CollectPacketCounters(device, counters, 0, reportsPerTask);
        });
        Task second = Task.Run(() =>
        {
            start.SignalAndWait();
            CollectPacketCounters(device, counters, reportsPerTask, reportsPerTask);
        });

        Task.WaitAll(first, second);

        // Every report must have consumed a distinct counter value: the multiset is {0..63}, so the
        // sorted raw counter bytes (value << 2) are exactly 0, 4, ... 252. A lost or duplicated
        // increment (an unsynchronized read-modify-write between the two callers) breaks the order.
        byte[] sorted = [.. counters];
        Array.Sort(sorted);
        for (int i = 0; i < sorted.Length; i++)
        {
            Assert.That(sorted[i], Is.EqualTo((byte)(i << 2)), $"the packet counter written by report {i}");
        }
    }

    [Test]
    public void ProvideInputReport_UndersizedDestination_ProvidesNothing()
    {
        DualShock4Device device = CreateDevice();
        byte[] report = new byte[DualShock4InputReport.Size - 1];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.Zero);
        Assert.That(report, Is.EqualTo(new byte[DualShock4InputReport.Size - 1])); // nothing written
    }

    [Test]
    public void OnOutputReport_FullPayload_RaisesTheEventWithTheParsedFields()
    {
        DualShock4Device device = CreateDevice();
        List<DualShock4OutputReport> received = [];
        device.OutputReportReceived += report => received.Add(report);
        byte[] payload = CreateOutputPayload(0x07, 0x28, 0x64, 0x12, 0x34, 0x56, 0x0A, 0x14);

        device.OnOutputReport(payload);

        Assert.That(received, Has.Count.EqualTo(1));
        Assert.That(received[0].RumbleEnabled, Is.True);
        Assert.That(received[0].RightMotor, Is.EqualTo(0x28));
        Assert.That(received[0].LeftMotor, Is.EqualTo(0x64));
        Assert.That(received[0].LightbarEnabled, Is.True);
        Assert.That(received[0].Red, Is.EqualTo(0x12));
        Assert.That(received[0].Green, Is.EqualTo(0x34));
        Assert.That(received[0].Blue, Is.EqualTo(0x56));
        Assert.That(received[0].FlashEnabled, Is.True);
        Assert.That(received[0].FlashOn, Is.EqualTo(0x0A));
        Assert.That(received[0].FlashOff, Is.EqualTo(0x14));
    }

    [Test]
    public void OnOutputReport_UnknownReportId_RaisesNothingAndDoesNotThrow()
    {
        DualShock4Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(0x07, 0x28);
        payload[0] = 0x06;

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_TruncatedPayload_RaisesNothingAndDoesNotThrow()
    {
        DualShock4Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(0x01, 0x40)[..(DualShock4OutputReport.MinimumSize - 1)];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_ControlPathPayloadWithoutReportId_RaisesNothingAndDoesNotThrow()
    {
        // The class SET_REPORT path delivers the payload report-id-exclusive (the id travels in
        // wValue); the device decodes the id-prefixed interrupt OUT shape Windows uses, so a
        // control-path payload is ignored without error (the documented choice of the id ruling).
        DualShock4Device device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(0x07, 0x28)[1..];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_SubscriberThrows_LogsTheFaultAndKeepsRaisingTheEvent()
    {
        RecordingLogger logger = new RecordingLogger();
        DualShock4Device device = new DualShock4Device(new DualShock4Identity(), logger);
        int raised = 0;
        device.OutputReportReceived += _ => throw new InvalidOperationException("subscriber failure");
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(0x07, 0x28, 0x64);

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
        Assert.That(raised, Is.EqualTo(1)); // the later subscriber still received the report

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
        Assert.That(raised, Is.EqualTo(2)); // a subsequent output still raises the event

        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(2));
        Assert.That(logger.ErrorEntries[0].Message, Does.Contain("subscriber failure"));
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualShock4Device CreateDevice() => new DualShock4Device(new DualShock4Identity());

    /// <summary>
    /// Collects the packet-counter byte of <paramref name="count"/> consecutive reports of
    /// <paramref name="device"/> into <paramref name="counters"/> from <paramref name="offset"/>;
    /// the concurrency test runs one instance per task against the same device.
    /// </summary>
    private static void CollectPacketCounters(DualShock4Device device, byte[] counters, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte[] report = new byte[DualShock4InputReport.Size];
            device.ProvideInputReport(report);
            counters[offset + i] = report[OffsetPacketCounter];
        }
    }

    /// <summary>
    /// Creates the neutral input state: centered sticks, released D-pad, all buttons released and a
    /// full discharging battery.
    /// </summary>
    private static DualShock4InputState CreateNeutralState()
    {
        return new DualShock4InputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualShock4DpadDirection.Released,
            BatteryLevel = 10
        };
    }

    /// <summary>
    /// Creates a full 32-byte reference output report 0x05 with the given field bytes; every other
    /// byte stays zero.
    /// </summary>
    private static byte[] CreateOutputPayload(byte flags = 0, byte rightMotor = 0, byte leftMotor = 0, byte red = 0, byte green = 0, byte blue = 0,
        byte flashOn = 0, byte flashOff = 0)
    {
        byte[] payload = new byte[OutputReportSize];
        payload[0] = OutputReportId;
        payload[1] = flags;
        payload[4] = rightMotor;
        payload[5] = leftMotor;
        payload[6] = red;
        payload[7] = green;
        payload[8] = blue;
        payload[9] = flashOn;
        payload[10] = flashOff;
        return payload;
    }
}