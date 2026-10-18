using System.Buffers.Binary;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the DualSense output report 0x02 decode and the persona device that ties the descriptor set,
/// the input report builder and the HID request engine together. The payload layout: the descriptor
/// declares output report 0x02 as "Report ID (2)" with "Report Count (47)", so the wire report is 48
/// bytes, and SET_REPORT copies the 47 bytes after the id byte into the SetStateData struct, whose
/// field-by-field comments pin every offset, flag bit and trigger effect block used here (the same
/// 47 bytes ride the BT 0x31 state packet). Device assertions cover
/// the neutral initialization ruling (centered sticks, released D-pad, the identity's battery
/// defaults), the device-owned counters (the shared rolling touch timestamp advances with the touch
/// point the report describes) and the output-report event path.
/// </summary>
[TestFixture]
public sealed class DualSenseOutputReportTests
{
    /// <summary>Report id of the DualSense output report under test (0x02).</summary>
    private const byte OutputReportId = 0x02;

    /// <summary>Report id of the DualSense input report the device serves (0x01).</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Length in bytes of the reference USB output report 0x02: report id plus a 47-byte payload.</summary>
    private const int OutputReportSize = 48;

    /// <summary>Neutral stick value (center).</summary>
    private const byte NeutralStick = 128;

    /// <summary>D-pad hat value of the released state (reference: released hat 0x08).</summary>
    private const byte ReleasedHat = 0x08;

    /// <summary>Battery/power byte of the neutral state: percent 0x0A with power state Complete (2) in the high nibble.</summary>
    private const byte NeutralBattery = 0x2A;

    /// <summary>Face button bit of Cross in the input report's hat/face byte.</summary>
    private const byte CrossBit = 0x20;

    /// <summary>Report offset of the input report's first stick axis (LX, then LY, RX and RY).</summary>
    private const int OffsetSticks = 1;

    /// <summary>Report offset of the input report's report sequence counter (the reference's SeqNo byte).</summary>
    private const int OffsetPacketCounter = 7;

    /// <summary>Report offset of the input report's hat/face byte.</summary>
    private const int OffsetFaceAndDpad = 8;

    /// <summary>Report offset of the input report's rolling touch timestamp of the pair.</summary>
    private const int OffsetTouchTimestamp = 41;

    /// <summary>Report offset of the input report's battery/power byte.</summary>
    private const int OffsetBattery = 53;

    /// <summary>Report offset of the output report's first valid-flag byte (payload byte 0).</summary>
    private const int OffsetFlags0 = 1;

    /// <summary>Report offset of the output report's second valid-flag byte (payload byte 1).</summary>
    private const int OffsetFlags1 = 2;

    /// <summary>Report offset of the right (light) rumble motor byte (payload byte 2).</summary>
    private const int OffsetRumbleRight = 3;

    /// <summary>Report offset of the left (heavy) rumble motor byte (payload byte 3).</summary>
    private const int OffsetRumbleLeft = 4;

    /// <summary>Report offset of the headphone volume byte (payload byte 4).</summary>
    private const int OffsetHeadphoneVolume = 5;

    /// <summary>Report offset of the speaker volume byte (payload byte 5).</summary>
    private const int OffsetSpeakerVolume = 6;

    /// <summary>Report offset of the microphone volume byte (payload byte 6).</summary>
    private const int OffsetMicVolume = 7;

    /// <summary>Report offset of the audio control bitfield byte (payload byte 7).</summary>
    private const int OffsetAudioControl = 8;

    /// <summary>Report offset of the mute-light mode byte (payload byte 8).</summary>
    private const int OffsetMuteLightMode = 9;

    /// <summary>Report offset of the mute control bitfield byte (payload byte 9).</summary>
    private const int OffsetMuteControl = 10;

    /// <summary>Report offset of the right trigger's raw effect block (payload bytes 10-20).</summary>
    private const int OffsetRightTriggerEffect = 11;

    /// <summary>Report offset of the left trigger's raw effect block (payload bytes 21-31).</summary>
    private const int OffsetLeftTriggerEffect = 22;

    /// <summary>Report offset of the little-endian host timestamp word (payload bytes 32-35).</summary>
    private const int OffsetHostTimestamp = 33;

    /// <summary>Report offset of the motor power level byte (payload byte 36).</summary>
    private const int OffsetMotorPowerLevel = 37;

    /// <summary>Report offset of the audio control 2 bitfield byte (payload byte 37).</summary>
    private const int OffsetAudioControl2 = 38;

    /// <summary>Report offset of the third valid-flag byte (payload byte 38).</summary>
    private const int OffsetFlags2 = 39;

    /// <summary>Report offset of the haptics low-pass filter byte (payload byte 39, bit 0).</summary>
    private const int OffsetHapticLowPassFilter = 40;

    /// <summary>Report offset of the lightbar fade animation byte (payload byte 41).</summary>
    private const int OffsetLightFadeAnimation = 42;

    /// <summary>Report offset of the lightbar brightness byte (payload byte 42).</summary>
    private const int OffsetLightBrightness = 43;

    /// <summary>Report offset of the player LED bitfield byte (payload byte 43).</summary>
    private const int OffsetPlayerLeds = 44;

    /// <summary>Report offset of the lightbar red component (payload byte 44).</summary>
    private const int OffsetRed = 45;

    /// <summary>Report offset of the lightbar green component (payload byte 45).</summary>
    private const int OffsetGreen = 46;

    /// <summary>Report offset of the lightbar blue component (payload byte 46).</summary>
    private const int OffsetBlue = 47;

    /// <summary>Flag bit of the rumble emulation request (valid-flag byte 0, bit 0.0).</summary>
    private const byte RumbleEmulationEnabledBit = 0x01;

    /// <summary>Flag bit of the right adaptive-trigger effect allowance (valid-flag byte 0, bit 0.2).</summary>
    private const byte RightTriggerEffectAllowedBit = 0x04;

    /// <summary>Flag bit of the lightbar color allowance (valid-flag byte 1, bit 1.2).</summary>
    private const byte LightbarColorAllowedBit = 0x04;

    [Test]
    public void TryParse_RumblePayload_ReadsTheEnableFlagAndBothMotorBytes()
    {
        byte[] payload = CreateOutputPayload(RumbleEmulationEnabledBit, rumbleRight: 0x40, rumbleLeft: 0x80);

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEmulationEnabled, Is.True);
        Assert.That(report.RumbleRight, Is.EqualTo(0x40));
        Assert.That(report.RumbleLeft, Is.EqualTo(0x80));
        Assert.That(report.LightbarColorAllowed, Is.False);
        Assert.That(report.Red, Is.Zero);
        Assert.That(report.Green, Is.Zero);
        Assert.That(report.Blue, Is.Zero);
    }

    [Test]
    public void TryParse_RumbleDisabledPayload_ReportsNoRumble()
    {
        byte[] payload = CreateOutputPayload();

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEmulationEnabled, Is.False);
        Assert.That(report.RumbleRight, Is.Zero);
        Assert.That(report.RumbleLeft, Is.Zero);
    }

    [Test]
    public void TryParse_LightbarWhitePayload_ReadsTheColorFlagAndTheRgbBytes()
    {
        byte[] payload = CreateOutputPayload(flags1: LightbarColorAllowedBit, red: 0xFF, green: 0xFF, blue: 0xFF);

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.LightbarColorAllowed, Is.True);
        Assert.That(report.Red, Is.EqualTo(0xFF));
        Assert.That(report.Green, Is.EqualTo(0xFF));
        Assert.That(report.Blue, Is.EqualTo(0xFF));
        Assert.That(report.RumbleEmulationEnabled, Is.False);
    }

    [Test]
    public void TryParse_AdaptiveTriggerBlock_RoundTripsTheRawBlockAndItsDecodedMode()
    {
        byte[] rightTriggerEffect = [0x21, 0x0A, 0x14, 0x1E, 0x28, 0x32, 0x3C, 0x46, 0x50, 0x5A, 0x64];
        byte[] payload = CreateOutputPayload(RightTriggerEffectAllowedBit);
        rightTriggerEffect.CopyTo(payload, OffsetRightTriggerEffect);

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RightTriggerEffectAllowed, Is.True);
        Assert.That(report.LeftTriggerEffectAllowed, Is.False);
        Assert.That(report.RightTriggerEffect, Is.EqualTo(rightTriggerEffect)); // the raw 11-byte block is preserved
        Assert.That(report.RightTriggerEffectMode, Is.EqualTo(0x21)); // the block's mode byte
        Assert.That(report.RightTriggerEffect[1..], Is.EqualTo(rightTriggerEffect[1..])); // the ten value bytes
        Assert.That(report.LeftTriggerEffect, Is.EqualTo(new byte[DualSenseOutputReport.TriggerEffectSize]));
        Assert.That(report.LeftTriggerEffectMode, Is.Zero);
    }

    [Test]
    public void TryParse_FullPayload_ReadsEveryField()
    {
        byte[] rightTriggerEffect = [0x21, 0x0A, 0x14, 0x1E, 0x28, 0x32, 0x3C, 0x46, 0x50, 0x5A, 0x64];
        byte[] leftTriggerEffect = [0x05, 0x32, 0x2D, 0x28, 0x23, 0x1E, 0x19, 0x14, 0x0F, 0x0A, 0x05];
        byte[] payload = CreateOutputPayload(0xFF, 0xFF);
        payload[OffsetRumbleRight] = 0x40;
        payload[OffsetRumbleLeft] = 0x80;
        payload[OffsetHeadphoneVolume] = 0x30;
        payload[OffsetSpeakerVolume] = 0x50;
        payload[OffsetMicVolume] = 0x28;
        payload[OffsetAudioControl] = 0x1B;
        payload[OffsetMuteLightMode] = 0x02;
        payload[OffsetMuteControl] = 0x10;
        rightTriggerEffect.CopyTo(payload, OffsetRightTriggerEffect);
        leftTriggerEffect.CopyTo(payload, OffsetLeftTriggerEffect);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(OffsetHostTimestamp), 0x12345678);
        payload[OffsetMotorPowerLevel] = 0x53;
        payload[OffsetAudioControl2] = 0x2D;
        payload[OffsetFlags2] = 0xFF;
        payload[OffsetHapticLowPassFilter] = 0x01;
        payload[OffsetLightFadeAnimation] = 0x01;
        payload[OffsetLightBrightness] = 0x02;
        payload[OffsetPlayerLeds] = 0x1B;
        payload[OffsetRed] = 0x12;
        payload[OffsetGreen] = 0x34;
        payload[OffsetBlue] = 0x56;

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEmulationEnabled, Is.True); // valid-flag byte 0, bits 0.0-0.7
        Assert.That(report.UseRumbleNotHaptics, Is.True);
        Assert.That(report.RightTriggerEffectAllowed, Is.True);
        Assert.That(report.LeftTriggerEffectAllowed, Is.True);
        Assert.That(report.HeadphoneVolumeAllowed, Is.True);
        Assert.That(report.SpeakerVolumeAllowed, Is.True);
        Assert.That(report.MicVolumeAllowed, Is.True);
        Assert.That(report.AudioControlAllowed, Is.True);
        Assert.That(report.MuteLightAllowed, Is.True); // valid-flag byte 1, bits 1.0-1.7
        Assert.That(report.AudioMuteAllowed, Is.True);
        Assert.That(report.LightbarColorAllowed, Is.True);
        Assert.That(report.ResetLights, Is.True);
        Assert.That(report.PlayerIndicatorsAllowed, Is.True);
        Assert.That(report.HapticLowPassFilterAllowed, Is.True);
        Assert.That(report.MotorPowerLevelAllowed, Is.True);
        Assert.That(report.AudioControl2Allowed, Is.True);
        Assert.That(report.LightBrightnessAllowed, Is.True); // valid-flag byte 2, bits 38.0-38.3
        Assert.That(report.LightFadeAllowed, Is.True);
        Assert.That(report.ImprovedRumbleEmulationEnabled, Is.True);
        Assert.That(report.UseRumbleNotHaptics2, Is.True);
        Assert.That(report.RumbleRight, Is.EqualTo(0x40));
        Assert.That(report.RumbleLeft, Is.EqualTo(0x80));
        Assert.That(report.HeadphoneVolume, Is.EqualTo(0x30));
        Assert.That(report.SpeakerVolume, Is.EqualTo(0x50));
        Assert.That(report.MicVolume, Is.EqualTo(0x28));
        Assert.That(report.AudioControl, Is.EqualTo(0x1B));
        Assert.That(report.MuteLightMode, Is.EqualTo(0x02));
        Assert.That(report.MuteControl, Is.EqualTo(0x10));
        Assert.That(report.RightTriggerEffect, Is.EqualTo(rightTriggerEffect));
        Assert.That(report.RightTriggerEffectMode, Is.EqualTo(0x21));
        Assert.That(report.LeftTriggerEffect, Is.EqualTo(leftTriggerEffect));
        Assert.That(report.LeftTriggerEffectMode, Is.EqualTo(0x05));
        Assert.That(report.HostTimestamp, Is.EqualTo(0x12345678));
        Assert.That(report.MotorPowerLevel, Is.EqualTo(0x53));
        Assert.That(report.AudioControl2, Is.EqualTo(0x2D));
        Assert.That(report.HapticLowPassFilter, Is.True);
        Assert.That(report.LightFadeAnimation, Is.EqualTo(0x01));
        Assert.That(report.LightBrightness, Is.EqualTo(0x02));
        Assert.That(report.PlayerLeds, Is.EqualTo(0x1B));
        Assert.That(report.Red, Is.EqualTo(0x12));
        Assert.That(report.Green, Is.EqualTo(0x34));
        Assert.That(report.Blue, Is.EqualTo(0x56));
    }

    [Test]
    public void TryParse_ReferenceLengthPayload_ParsesTheReferenceReport()
    {
        byte[] payload = CreateOutputPayload(RumbleEmulationEnabledBit, rumbleRight: 0x11, rumbleLeft: 0x22);
        payload[OffsetBlue] = 0x77;

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(payload.Length, Is.EqualTo(DualSenseOutputReport.MinimumSize));
        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEmulationEnabled, Is.True);
        Assert.That(report.RumbleRight, Is.EqualTo(0x11));
        Assert.That(report.RumbleLeft, Is.EqualTo(0x22));
        Assert.That(report.Blue, Is.EqualTo(0x77));
    }

    [Test]
    public void TryParse_TrailingBytesBeyondTheReferenceReport_AreIgnored()
    {
        byte[] payload = new byte[DualSenseInputReport.Size]; // the endpoint buffer can carry padding
        CreateOutputPayload(RumbleEmulationEnabledBit, rumbleRight: 0x40, blue: 0x77).CopyTo(payload, 0);
        for (int i = OutputReportSize; i < payload.Length; i++)
        {
            payload[i] = 0xEE;
        }

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.True);
        Assert.That(report.RumbleEmulationEnabled, Is.True);
        Assert.That(report.RumbleRight, Is.EqualTo(0x40));
        Assert.That(report.Blue, Is.EqualTo(0x77));
    }

    [TestCase((byte)0x00)]
    [TestCase((byte)0x01)]
    [TestCase((byte)0x03)]
    [TestCase((byte)0x05)]
    [TestCase((byte)0x31)]
    [TestCase((byte)0xFF)]
    public void TryParse_WrongReportId_ReturnsFalse(byte reportId)
    {
        byte[] payload = CreateOutputPayload(0x07);
        payload[0] = reportId;

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(DualSenseOutputReport)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(32)]
    [TestCase(47)]
    public void TryParse_TruncatedPayload_ReturnsFalse(int length)
    {
        byte[] payload = CreateOutputPayload(RumbleEmulationEnabledBit, rumbleRight: 0x40)[..length];

        bool parsed = DualSenseOutputReport.TryParse(payload, out DualSenseOutputReport report);

        Assert.That(parsed, Is.False);
        Assert.That(report, Is.EqualTo(default(DualSenseOutputReport)));
    }

    [Test]
    public void ProvideInputReport_UntouchedDevice_ReportsTheNeutralState()
    {
        DualSenseDevice device = CreateDevice();
        byte[] report = new byte[DualSenseInputReport.Size];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.EqualTo(DualSenseInputReport.Size));
        Assert.That(report[0], Is.EqualTo(InputReportId));
        Assert.That(report[OffsetSticks], Is.EqualTo(NeutralStick)); // LX
        Assert.That(report[OffsetSticks + 1], Is.EqualTo(NeutralStick)); // LY
        Assert.That(report[OffsetSticks + 2], Is.EqualTo(NeutralStick)); // RX
        Assert.That(report[OffsetSticks + 3], Is.EqualTo(NeutralStick)); // RY
        Assert.That(report[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat)); // released D-pad, no face button
        Assert.That(report[OffsetBattery], Is.EqualTo(NeutralBattery)); // percent 0x0A, power state Complete
    }

    [Test]
    public void ProvideInputReport_IdentityBatteryDefaults_SeedTheNeutralState()
    {
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity
        {
            BatteryLevel = 0x05,
            ChargingState = DualSenseChargingState.Charging
        });
        byte[] report = new byte[DualSenseInputReport.Size];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.EqualTo(DualSenseInputReport.Size));
        Assert.That(report[OffsetBattery], Is.EqualTo(0x15)); // percent 5 with power state Charging (1) in the high nibble
    }

    [Test]
    public void ProvideInputReport_CrossPressed_ReturnsTheReportWithTheCrossBit()
    {
        DualSenseDevice device = CreateDevice();
        DualSenseInputState state = CreateNeutralState();
        state.Cross = true;
        device.SetInput(state);
        byte[] report = new byte[DualSenseInputReport.Size];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.EqualTo(DualSenseInputReport.Size));
        Assert.That(report[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | CrossBit));
        Assert.That(report[OffsetSticks], Is.EqualTo(NeutralStick)); // the stored state is what is reported
    }

    [Test]
    public void ProvideInputReport_TwoConsecutiveReports_DifferOnlyInThePacketCounter()
    {
        DualSenseDevice device = CreateDevice();
        byte[] first = new byte[DualSenseInputReport.Size];
        byte[] second = new byte[DualSenseInputReport.Size];

        device.ProvideInputReport(first);
        device.ProvideInputReport(second);

        Assert.That(first[OffsetPacketCounter], Is.Zero); // the first report carries counter zero
        Assert.That(second[OffsetPacketCounter], Is.EqualTo(1)); // then one, written verbatim into the SeqNo byte
        List<int> differing = [];
        for (int i = 0; i < DualSenseInputReport.Size; i++)
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
        DualSenseDevice device = CreateDevice();
        DualSenseInputState touched = CreateNeutralState();
        touched.Touch1Active = true;
        touched.Touch1Id = 0x07;
        touched.Touch1X = 0x123;
        touched.Touch1Y = 0x231;
        byte[] active = new byte[DualSenseInputReport.Size];
        byte[] releasedFirst = new byte[DualSenseInputReport.Size];
        byte[] releasedSecond = new byte[DualSenseInputReport.Size];

        device.SetInput(touched);
        device.ProvideInputReport(active);
        device.SetInput(CreateNeutralState());
        device.ProvideInputReport(releasedFirst);
        device.ProvideInputReport(releasedSecond);

        Assert.That(active[OffsetTouchTimestamp], Is.Zero); // the first report carries counter zero
        Assert.That(releasedFirst[OffsetTouchTimestamp], Is.EqualTo(1)); // advanced once while the point was active
        Assert.That(releasedSecond[OffsetTouchTimestamp], Is.EqualTo(releasedFirst[OffsetTouchTimestamp])); // held while inactive
    }

    [Test]
    public void ProvideInputReport_SecondTouchPointOnly_AdvancesTheSecondCounter()
    {
        DualSenseDevice device = CreateDevice();
        DualSenseInputState touched = CreateNeutralState();
        touched.Touch2Active = true;
        touched.Touch2Id = 0x03;
        byte[] first = new byte[DualSenseInputReport.Size];
        byte[] second = new byte[DualSenseInputReport.Size];

        device.SetInput(touched);
        device.ProvideInputReport(first);
        device.ProvideInputReport(second);

        Assert.That(first[OffsetTouchTimestamp], Is.Zero); // the second counter's first value
        Assert.That(second[OffsetTouchTimestamp], Is.EqualTo(1)); // advanced while point 2 was active and point 1 inactive
    }

    [Test]
    public void ProvideInputReport_BothPointsActive_AdvanceOnlyTheFirstCounter()
    {
        DualSenseDevice device = CreateDevice();
        DualSenseInputState bothTouching = CreateNeutralState();
        bothTouching.Touch1Active = true;
        bothTouching.Touch2Active = true;
        DualSenseInputState secondOnly = CreateNeutralState();
        secondOnly.Touch2Active = true;
        byte[] bothFirst = new byte[DualSenseInputReport.Size];
        byte[] bothSecond = new byte[DualSenseInputReport.Size];
        byte[] secondFirst = new byte[DualSenseInputReport.Size];
        byte[] secondSecond = new byte[DualSenseInputReport.Size];

        device.SetInput(bothTouching);
        device.ProvideInputReport(bothFirst);
        device.ProvideInputReport(bothSecond);
        device.SetInput(secondOnly);
        device.ProvideInputReport(secondFirst);
        device.ProvideInputReport(secondSecond);

        Assert.That(bothFirst[OffsetTouchTimestamp], Is.Zero); // the first counter's first value
        Assert.That(bothSecond[OffsetTouchTimestamp], Is.EqualTo(1)); // advanced while point 1 was active
        Assert.That(secondFirst[OffsetTouchTimestamp], Is.Zero); // the second counter held while both points were active
        Assert.That(secondSecond[OffsetTouchTimestamp], Is.EqualTo(1)); // and advances now that only point 2 is active
    }

    [Test]
    public void ProvideInputReport_ConcurrentCallers_WriteDistinctPacketCounters()
    {
        DualSenseDevice device = CreateDevice();
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

        // Every report must have consumed a distinct counter value: the multiset is {0..63}. A lost
        // or duplicated increment (an unsynchronized read-modify-write between the two callers)
        // breaks the sorted order.
        byte[] sorted = [.. counters];
        Array.Sort(sorted);
        for (int i = 0; i < sorted.Length; i++)
        {
            Assert.That(sorted[i], Is.EqualTo((byte)i), $"the packet counter written by report {i}");
        }
    }

    [Test]
    public void ProvideInputReport_UndersizedDestination_ProvidesNothing()
    {
        DualSenseDevice device = CreateDevice();
        byte[] report = new byte[DualSenseInputReport.Size - 1];

        int written = device.ProvideInputReport(report);

        Assert.That(written, Is.Zero);
        Assert.That(report, Is.EqualTo(new byte[DualSenseInputReport.Size - 1])); // nothing written
    }

    [Test]
    public void ProvideInputReport_RawReportSet_ServesTheRawBytesVerbatimAcrossTicks()
    {
        DualSenseDevice device = CreateDevice();
        byte[] raw = CreateRawReport();
        device.SetRawInputReport(raw);
        byte[] first = new byte[DualSenseInputReport.Size];
        byte[] second = new byte[DualSenseInputReport.Size];
        byte[] third = new byte[DualSenseInputReport.Size];

        int written = device.ProvideInputReport(first);
        device.ProvideInputReport(second);
        device.ProvideInputReport(third);

        Assert.That(written, Is.EqualTo(DualSenseInputReport.Size));
        Assert.That(first, Is.EqualTo(raw)); // byte-exact: report id, SeqNo, touch and battery bytes as supplied
        Assert.That(second, Is.EqualTo(raw)); // the same bytes again on the next tick
        Assert.That(third, Is.EqualTo(raw));
    }

    [Test]
    public void ProvideInputReport_RawCleared_ResumesTheSyntheticCountersWithoutAJump()
    {
        DualSenseDevice device = CreateDevice();
        byte[] synthetic = new byte[DualSenseInputReport.Size];
        byte[] whileRawFirst = new byte[DualSenseInputReport.Size];
        byte[] whileRawSecond = new byte[DualSenseInputReport.Size];
        byte[] resumed = new byte[DualSenseInputReport.Size];
        byte[] raw = CreateRawReport();
        DualSenseInputState pressed = CreateNeutralState();
        pressed.Cross = true;

        device.ProvideInputReport(synthetic); // the synthetic builder's first tick, SeqNo zero
        device.SetRawInputReport(raw);
        device.ProvideInputReport(whileRawFirst);
        device.ProvideInputReport(whileRawSecond);
        device.SetInput(pressed); // buffered while raw is set; not reported until raw clears
        device.ClearRawInputReport();
        device.ProvideInputReport(resumed);

        Assert.That(synthetic[OffsetPacketCounter], Is.Zero);
        Assert.That(whileRawFirst, Is.EqualTo(raw)); // raw wins over the stored state
        Assert.That(whileRawSecond, Is.EqualTo(raw));
        Assert.That(resumed[OffsetPacketCounter], Is.EqualTo(1)); // one increment past the first tick (SeqNo is written verbatim), not three
        Assert.That(resumed[OffsetFaceAndDpad], Is.EqualTo(ReleasedHat | CrossBit)); // the state stored while raw was set is served now
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(63)]
    [TestCase(65)]
    [TestCase(128)]
    public void SetRawInputReport_WrongLength_ThrowsArgumentException(int length)
    {
        DualSenseDevice device = CreateDevice();

        Assert.That(() => device.SetRawInputReport(new byte[length]), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OnOutputReport_FullPayload_RaisesTheEventWithTheParsedFields()
    {
        DualSenseDevice device = CreateDevice();
        List<DualSenseOutputReport> received = [];
        device.OutputReportReceived += report => received.Add(report);
        byte[] payload = CreateOutputPayload(RumbleEmulationEnabledBit | RightTriggerEffectAllowedBit, LightbarColorAllowedBit,
            0x40, 0x80, 0x12, 0x34, 0x56);
        payload[OffsetMuteLightMode] = 0x01;
        payload[OffsetPlayerLeds] = 0x1B;
        payload[OffsetRightTriggerEffect] = 0x21;
        payload[OffsetRightTriggerEffect + 1] = 0x0A;

        device.OnOutputReport(payload);

        Assert.That(received, Has.Count.EqualTo(1));
        Assert.That(received[0].RumbleEmulationEnabled, Is.True);
        Assert.That(received[0].RumbleRight, Is.EqualTo(0x40));
        Assert.That(received[0].RumbleLeft, Is.EqualTo(0x80));
        Assert.That(received[0].RightTriggerEffectAllowed, Is.True);
        Assert.That(received[0].RightTriggerEffectMode, Is.EqualTo(0x21));
        Assert.That(received[0].LightbarColorAllowed, Is.True);
        Assert.That(received[0].Red, Is.EqualTo(0x12));
        Assert.That(received[0].Green, Is.EqualTo(0x34));
        Assert.That(received[0].Blue, Is.EqualTo(0x56));
        Assert.That(received[0].MuteLightMode, Is.EqualTo(0x01));
        Assert.That(received[0].PlayerLeds, Is.EqualTo(0x1B));
    }

    [Test]
    public void OnOutputReport_UnknownReportId_RaisesNothingAndDoesNotThrow()
    {
        DualSenseDevice device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(0x07);
        payload[0] = 0x03;

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_TruncatedPayload_RaisesNothingAndDoesNotThrow()
    {
        DualSenseDevice device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(RumbleEmulationEnabledBit, rumbleRight: 0x40)[..(DualSenseOutputReport.MinimumSize - 1)];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_ControlPathPayloadWithoutReportId_RaisesNothingAndDoesNotThrow()
    {
        // The direct OnOutputReport contract is id-prefixed; the base engine prepends the wValue id
        // for SET_REPORT, so a bare payload is only invalid when passed directly.
        DualSenseDevice device = CreateDevice();
        int raised = 0;
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(0x07)[1..];

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void OnOutputReport_SubscriberThrows_LogsTheFaultAndKeepsRaisingTheEvent()
    {
        RecordingLogger logger = new RecordingLogger();
        DualSenseDevice device = new DualSenseDevice(new DualSenseIdentity(), logger);
        int raised = 0;
        device.OutputReportReceived += _ => throw new InvalidOperationException("subscriber failure");
        device.OutputReportReceived += _ => raised++;
        byte[] payload = CreateOutputPayload(RumbleEmulationEnabledBit, rumbleRight: 0x40);

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
        Assert.That(raised, Is.EqualTo(1)); // the later subscriber still received the report

        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);
        Assert.That(raised, Is.EqualTo(2)); // a subsequent output still raises the event

        Assert.That(logger.ErrorEntries, Has.Count.EqualTo(2));
        Assert.That(logger.ErrorEntries[0].Message, Does.Contain("subscriber failure"));
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualSenseDevice CreateDevice() => new DualSenseDevice(new DualSenseIdentity());

    /// <summary>
    /// Collects the packet-counter byte of <paramref name="count"/> consecutive reports of
    /// <paramref name="device"/> into <paramref name="counters"/> from <paramref name="offset"/>;
    /// the concurrency test runs one instance per task against the same device.
    /// </summary>
    private static void CollectPacketCounters(DualSenseDevice device, byte[] counters, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            byte[] report = new byte[DualSenseInputReport.Size];
            device.ProvideInputReport(report);
            counters[offset + i] = report[OffsetPacketCounter];
        }
    }

    /// <summary>
    /// Creates the neutral input state: centered sticks, released D-pad, all buttons released and
    /// the identity's battery defaults (percent 0x0A, power state Complete).
    /// </summary>
    private static DualSenseInputState CreateNeutralState()
    {
        return new DualSenseInputState
        {
            LeftStickX = NeutralStick,
            LeftStickY = NeutralStick,
            RightStickX = NeutralStick,
            RightStickY = NeutralStick,
            Dpad = DualSenseDpadDirection.Released,
            BatteryLevel = 0x0A,
            PowerState = DualSenseChargingState.Complete
        };
    }

    /// <summary>
    /// Creates one raw 64-byte report with the given fill byte; report byte 0 stays the input
    /// report id, like the report id byte of a captured hardware report.
    /// </summary>
    private static byte[] CreateRawReport(byte fill = 0x5A)
    {
        byte[] report = new byte[DualSenseInputReport.Size];
        Array.Fill(report, fill);
        report[0] = InputReportId;
        return report;
    }

    /// <summary>
    /// Creates a full 48-byte reference output report 0x02 with the given flag and field bytes;
    /// every other byte stays zero.
    /// </summary>
    private static byte[] CreateOutputPayload(byte flags0 = 0, byte flags1 = 0, byte rumbleRight = 0, byte rumbleLeft = 0,
        byte red = 0, byte green = 0, byte blue = 0)
    {
        byte[] payload = new byte[OutputReportSize];
        payload[0] = OutputReportId;
        payload[OffsetFlags0] = flags0;
        payload[OffsetFlags1] = flags1;
        payload[OffsetRumbleRight] = rumbleRight;
        payload[OffsetRumbleLeft] = rumbleLeft;
        payload[OffsetRed] = red;
        payload[OffsetGreen] = green;
        payload[OffsetBlue] = blue;
        return payload;
    }
}