using System.Diagnostics;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Triggers;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Hid;
using DualSenseClient.VIIPER.DualSense;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

public class ViiperDualSenseAudioForwarderTests
{
    /// <summary>
    /// Fake physical-controller audio lane recording every call.
    /// </summary>
    private sealed class FakeAudioOutputs : IDualSenseAudioOutputs
    {
        public ConnectionType ConnectionType { get; init; } = ConnectionType.Bluetooth;

        public int ResetCount { get; private set; }
        public int PrimeCount { get; private set; }
        public int CombinedReportCount { get; private set; }
        public int AudioOnlyReportCount { get; private set; }
        public int AudioOutputApplyCount { get; private set; }
        public byte? LastAppliedSpeakerVolume { get; private set; }
        public AudioControl? LastAppliedControl { get; private set; }
        public byte LastOpusFrameLength { get; private set; }
        public byte LastHapticsLength { get; private set; }
        public byte[] LastHapticsFrame { get; private set; } = [];
        public bool AnyHapticsNonZero { get; private set; }

        public int ReportCount => CombinedReportCount + AudioOnlyReportCount;

        public byte[] LastStateBlock { get; private set; } = new byte[SetStateData.PayloadSize];
        public byte[] LastPrimeStateBlock { get; private set; } = new byte[SetStateData.PayloadSize];

        public void SetVibration(byte left, byte right)
        {
        }

        public void SendOutputState(SetStateData payload)
        {
        }

        public void ResetBluetoothAudioStream() => ResetCount++;

        public void SendBluetoothAudioPrime(SetStateData state)
        {
            PrimeCount++;
            state.CopyTo(LastPrimeStateBlock, 0);
        }

        public void SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route)
        {
            CombinedReportCount++;
            state.CopyTo(LastStateBlock, 0);
            LastOpusFrameLength = (byte)opusFrame.Length;
            LastHapticsLength = (byte)hapticsPcm.Length;
            LastHapticsFrame = hapticsPcm.ToArray();
            AnyHapticsNonZero |= LastHapticsFrame.Any(b => b != 0);
        }

        public void SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route)
        {
            AudioOnlyReportCount++;
            LastOpusFrameLength = (byte)opusFrame.Length;
        }

        public void SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume)
        {
            AudioOutputApplyCount++;
            LastAppliedControl = outputControl;
            LastAppliedSpeakerVolume = speakerVolume;
        }
    }

    private static readonly float[] AudioBlock = MakeAudioBlock(0.5f);

    /// <summary>
    /// Builds a 398-byte vDS-style combined Bluetooth report carrying the given haptics
    /// payload: <c>0x36</c> id, <c>0x91 0x07 0xFE</c> session block, <c>0x90 0x3F</c>
    /// state block, haptics at offset 78.
    /// </summary>
    private static DSOutputState MakeCombinedReport(byte[] haptics, byte reportId = 0x36)
    {
        byte[] combined = new byte[398];
        combined[0] = reportId;
        combined[2] = 0x91;
        combined[3] = 0x07;
        combined[4] = 0xFE;
        combined[11] = 0x90;
        combined[12] = 0x3F;
        haptics.CopyTo(combined, 78);
        return new DSOutputState
        {
            BluetoothCombinedOutputReport = combined
        };
    }

    /// <summary>
    /// Builds one 512-frame stereo block with a constant amplitude on both channels.
    /// </summary>
    private static float[] MakeAudioBlock(float amplitude)
    {
        float[] block = new float[DualSenseBtAudioPipeline.FramesPerBlock * 2];
        Array.Fill(block, amplitude);
        return block;
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or the timeout elapses.
    /// </summary>
    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < timeout)
        {
            Thread.Sleep(5);
        }
    }

    [Test]
    public void Start_LeavesBluetoothLaneClosedUntilAudioIsFed()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);

        Assert.That(forwarder.Start(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(fake.ResetCount, Is.Zero, "Start must not open the Bluetooth audio lane");
            Assert.That(fake.PrimeCount, Is.Zero, "Start must not open the Bluetooth audio lane");
            Assert.That(fake.AudioOutputApplyCount, Is.Zero, "Start must not touch the audio configuration");
            Assert.That(fake.ReportCount, Is.Zero, "Start must not send any reports");
        });

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount >= 8 && fake.PrimeCount == 1, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(fake.ResetCount, Is.EqualTo(1), "the first audio block must open the stream");
            Assert.That(fake.PrimeCount, Is.EqualTo(1));
            Assert.That(fake.AudioOutputApplyCount, Is.EqualTo(1));
            Assert.That(fake.LastAppliedControl, Is.EqualTo(AudioControl.OutputPathSpeaker));
            Assert.That(fake.LastAppliedSpeakerVolume, Is.EqualTo(0x50));
            Assert.That(fake.ReportCount, Is.GreaterThanOrEqualTo(8), "the first block must warm up the stream with the 8 silence reports");
        });

        forwarder.Stop();
    }

    [Test]
    public void LongIdleGap_RePrimesTheStreamOnResume()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount > 0 && fake.PrimeCount == 1, TimeSpan.FromSeconds(3));

        Thread.Sleep(400);

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.PrimeCount >= 2, TimeSpan.FromSeconds(3));

        Assert.That(fake.PrimeCount, Is.GreaterThanOrEqualTo(2), "resuming after a long idle gap must re-open the audio lane");

        forwarder.Stop();
    }

    [Test]
    public void Start_NoTransportReturnsFalse()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Usb
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);

        Assert.That(forwarder.Start(), Is.False);
    }

    [Test]
    public void FeedPcm_SendsCombinedAudioAndHapticsReports()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        for (int i = 0; i < 4; i++)
        {
            forwarder.FeedPcm(AudioBlock);
        }

        WaitUntil(() => fake.ReportCount >= 12, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(fake.ReportCount, Is.GreaterThanOrEqualTo(12), "8 preroll reports plus the 4 fed blocks must all be sent");
            Assert.That(fake.CombinedReportCount, Is.EqualTo(fake.ReportCount), "haptics enabled must fold into the combined report");
            Assert.That(fake.AudioOnlyReportCount, Is.Zero);
            Assert.That(fake.LastOpusFrameLength, Is.EqualTo(200));
            Assert.That(fake.LastHapticsLength, Is.EqualTo(64));
            Assert.That(fake.AnyHapticsNonZero, Is.True, "a constant audio block must produce non-zero haptics");
        });

        forwarder.Stop();
    }

    [Test]
    public void FeedPcm_HapticsDisabledSendsAudioOnlyReports()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.HapticsEnabled = false;
        forwarder.Start();

        for (int i = 0; i < 3; i++)
        {
            forwarder.FeedPcm(AudioBlock);
        }

        WaitUntil(() => fake.ReportCount >= 11, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(fake.AudioOnlyReportCount, Is.GreaterThanOrEqualTo(11), "8 preroll reports plus the 3 fed blocks must all be sent audio-only");
            Assert.That(fake.CombinedReportCount, Is.Zero);
            Assert.That(fake.LastOpusFrameLength, Is.EqualTo(200));
        });

        forwarder.Stop();
    }

    [Test]
    public void FeedPcm_PartialBlocksAreAccumulatedAndChunked()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        int halfBlock = DualSenseBtAudioPipeline.FramesPerBlock;
        forwarder.FeedPcm(AudioBlock.AsSpan(0, halfBlock));
        forwarder.FeedPcm(AudioBlock.AsSpan(0, halfBlock));
        forwarder.FeedPcm(AudioBlock);

        WaitUntil(() => fake.ReportCount >= 10, TimeSpan.FromSeconds(3));

        Assert.That(fake.ReportCount, Is.GreaterThanOrEqualTo(10), "two half blocks plus a full block must yield two reports on top of the 8-report preroll");

        forwarder.Stop();
    }

    [Test]
    public void FeedPcm_VolumeChangeAppliesWithinOneTick()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.SpeakerVolume = 0x90;
        forwarder.FeedPcm(AudioBlock);

        WaitUntil(() => fake.AudioOutputApplyCount >= 1 && fake.LastAppliedSpeakerVolume == 0x90, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(fake.LastAppliedSpeakerVolume, Is.EqualTo(0x90), "the volume set before the first block must be applied when the stream opens");
            Assert.That(fake.LastAppliedControl, Is.EqualTo(AudioControl.OutputPathSpeaker));
        });

        forwarder.SpeakerVolume = 0x70;
        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.LastAppliedSpeakerVolume == 0x70, TimeSpan.FromSeconds(3));

        Assert.That(fake.LastAppliedSpeakerVolume, Is.EqualTo(0x70), "a volume change during streaming must be applied within one tick");

        forwarder.Stop();
    }

    [Test]
    public void Stop_NoFurtherReportsAreSent()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount > 8, TimeSpan.FromSeconds(3));
        forwarder.Stop();
        int countAtStop = fake.ReportCount;
        Assert.That(countAtStop, Is.GreaterThan(8));

        forwarder.FeedPcm(AudioBlock);
        Thread.Sleep(150);

        Assert.That(fake.ReportCount, Is.EqualTo(countAtStop), "feeding after stop must be ignored");
    }

    [Test]
    public void Dispose_StopsThePumpAndReleases()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount > 8, TimeSpan.FromSeconds(3));
        int reportsBeforeDispose = fake.ReportCount;
        Assert.That(reportsBeforeDispose, Is.GreaterThan(8));

        forwarder.Dispose();

        Assert.That(forwarder.IsActive, Is.False);
        forwarder.FeedPcm(AudioBlock);
        Thread.Sleep(100);
        Assert.That(fake.ReportCount, Is.EqualTo(reportsBeforeDispose), "no reports may be sent after dispose");
    }

    [Test]
    public void UpdateGameOutputState_StateRidesTheCombinedAndPrimeReports()
    {
        byte[] right = new byte[11];
        right[0] = (byte)TriggerEffectType.Trigger;
        byte[] left = new byte[11];
        left[0] = (byte)TriggerEffectType.Automatic;

        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.UpdateGameOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.UseRumbleNotHaptics | ValidFlags.EnableRumbleEmulation,
            ValidFlag2 = ValidFlags.EnableImprovedRumbleEmu,
            RumbleLeft = 0x90,
            RumbleRight = 0x40,
            MuteLedMode = 0x01,
            PlayerLeds = (PlayerLedMask)0x0A,
            LedRed = 0x12,
            LedGreen = 0x34,
            LedBlue = 0x56,
            R2TriggerEffect = new TriggerEffectBlock(right, 0),
            L2TriggerEffect = new TriggerEffectBlock(left, 0)
        });
        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount >= 8 && fake.PrimeCount == 1, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            byte[] prime = fake.LastPrimeStateBlock;
            Assert.That(prime[0], Is.EqualTo(0xBC), "the init-prime must carry the merged flags (rumble bits cleared, triggers and audio enabled)");
            Assert.That(prime[3], Is.EqualTo(0x90), "the init-prime must carry the game's rumble bytes");
            Assert.That(prime[44], Is.EqualTo(0x12), "the init-prime must carry the lightbar");
            Assert.That(prime[10], Is.EqualTo((byte)TriggerEffectType.Trigger), "the init-prime must carry the R2 trigger block");

            byte[] state = fake.LastStateBlock;
            Assert.That(state[0] & 0x03, Is.Zero, "the rumble-mode bits must be cleared while the audio lane is open");
            Assert.That(state[0] & 0x0C, Is.EqualTo(0x0C), "the trigger enable bits must stay set so the pad applies the blocks");
            Assert.That(state[0] & 0xB0, Is.EqualTo(0xB0), "the audio enable bits must be set");
            Assert.That(state[38] & 0x04, Is.Zero, "improved rumble emulation must be cleared while the lane is open");
            Assert.That(state[2], Is.EqualTo(0x40), "the rumble bytes must ride the combined state block");
            Assert.That(state[3], Is.EqualTo(0x90));
            Assert.That(state[8], Is.EqualTo(0x01), "the mic LED mode must ride the state block");
            Assert.That(state[43], Is.EqualTo(0x0A), "the player LED mask must ride the state block");
            Assert.That(state[44], Is.EqualTo(0x12), "the lightbar must ride the state block");
            Assert.That(state[45], Is.EqualTo(0x34));
            Assert.That(state[46], Is.EqualTo(0x56));
            Assert.That(state[10], Is.EqualTo((byte)TriggerEffectType.Trigger), "the R2 trigger block must ride the state block");
            Assert.That(state[21], Is.EqualTo((byte)TriggerEffectType.Automatic), "the L2 trigger block must ride the state block");
            Assert.That(state[4], Is.EqualTo(0x3F), "the headphone volume must be overridden for the speaker route");
            Assert.That(state[5], Is.EqualTo(0x50), "the speaker volume must be overridden");
            Assert.That(state[7], Is.EqualTo((byte)AudioControl.OutputPathSpeaker), "the audio control must be overridden");
            Assert.That(state[37], Is.EqualTo(0x02), "audio control 2 must be overridden");
        });

        forwarder.Stop();
    }

    [Test]
    public void FeedPcm_WithoutGameStateHapticsFollowTheAudio()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        for (int i = 0; i < 3; i++)
        {
            forwarder.FeedPcm(AudioBlock);
        }

        WaitUntil(() => fake.ReportCount >= 11, TimeSpan.FromSeconds(3));

        Assert.That(fake.LastHapticsFrame.Distinct().Count(), Is.EqualTo(1),
            "a constant audio block must produce a uniform haptics frame when no game rumble is mixed in");

        forwarder.Stop();
    }

    [Test]
    public void UpdateGameOutputState_GameRumbleIsSynthesizedIntoHapticsPcm()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.UpdateGameOutputState(new SetStateData
        {
            RumbleLeft = 0xFF,
            RumbleRight = 0x00
        });
        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount >= 12, TimeSpan.FromSeconds(3));

        byte[] frame = fake.LastHapticsFrame;
        Assert.Multiple(() =>
        {
            Assert.That(frame.Distinct().Count(), Is.GreaterThan(8), "the 60 Hz rumble sine must make the haptics vary across the frame");
            Assert.That(frame.Max(b => (sbyte)b), Is.GreaterThan(20), "the rumble must drive the voice coils");
        });

        forwarder.Stop();
    }

    [Test]
    public void Flush_DiscardsBufferedAudioAndClosesTheLane()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        for (int i = 0; i < 5; i++)
        {
            forwarder.FeedPcm(AudioBlock);
        }

        WaitUntil(() => fake.ReportCount >= 12, TimeSpan.FromSeconds(3));
        int beforeFlush = fake.ReportCount;
        Assert.That(beforeFlush, Is.GreaterThanOrEqualTo(12));

        forwarder.Flush();
        Thread.Sleep(100);

        Assert.That(fake.ReportCount - beforeFlush, Is.LessThanOrEqualTo(2),
            "the ring must be empty after Flush: the pump may only finish in-flight reports, not keep streaming buffered audio");

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ResetCount >= 2, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(fake.ResetCount, Is.GreaterThanOrEqualTo(2), "the next block must reopen the lane");
            Assert.That(fake.PrimeCount, Is.GreaterThanOrEqualTo(2));
        });

        forwarder.Stop();
    }

    [Test]
    public void Flush_ForcesRePrimeEvenWhenAudioResumesWithinTheIdleWindow()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.PrimeCount == 1, TimeSpan.FromSeconds(3));
        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount >= 10, TimeSpan.FromSeconds(3));

        forwarder.Flush();
        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.PrimeCount >= 2, TimeSpan.FromSeconds(3));

        Assert.That(fake.ResetCount, Is.GreaterThanOrEqualTo(2),
            "resuming within the idle window must still re-prime after Flush (unlike a plain short gap)");

        forwarder.Stop();
    }

    [Test]
    public void UpdateGameHaptics_ReplacesAudioDerivedHaptics()
    {
        byte[] gameHaptics = new byte[64];
        Array.Fill(gameHaptics, (byte)0x10);

        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        for (int i = 0; i < 8; i++)
        {
            forwarder.FeedPcm(MakeAudioBlock(0f));
        }

        WaitUntil(() => fake.ReportCount >= 8 && fake.PrimeCount == 1, TimeSpan.FromSeconds(3));

        // Deliver the payload only after the stream is primed: the ~85 ms prime and
        // preroll would otherwise age the payload past the 100 ms freshness window
        // under load, falling back to the audio-derived haptics. In the field the
        // callback fires continuously, so a post-prime delivery is the faithful shape.
        forwarder.UpdateGameHaptics(MakeCombinedReport(gameHaptics));
        WaitUntil(() => fake.LastHapticsFrame is { Length: 64 } && fake.LastHapticsFrame.All(b => b == 0x10), TimeSpan.FromSeconds(3));

        Assert.That(fake.LastHapticsFrame.All(b => b == 0x10), Is.True,
            "the game's haptics payload must replace the audio-derived haptics (silence would derive to zeros)");

        forwarder.Stop();
    }

    [Test]
    public void UpdateGameHaptics_SilentPayloadFallsBackToAudioDerived()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.UpdateGameHaptics(MakeCombinedReport(new byte[64]));
        forwarder.FeedPcm(AudioBlock);
        WaitUntil(() => fake.ReportCount >= 12, TimeSpan.FromSeconds(3));

        byte[] frame = fake.LastHapticsFrame;
        Assert.Multiple(() =>
        {
            Assert.That(frame.Distinct().Count(), Is.EqualTo(1), "a silent game payload must not suppress the audio-derived haptics");
            Assert.That(frame.Max(b => (sbyte)b), Is.GreaterThan(0), "the audio-derived haptics must still drive the voice coils");
        });

        forwarder.Stop();
    }

    [Test]
    public void UpdateGameHaptics_StalePayloadFallsBackToAudioDerived()
    {
        byte[] gameHaptics = new byte[64];
        Array.Fill(gameHaptics, (byte)0x10);

        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.UpdateGameHaptics(MakeCombinedReport(gameHaptics));
        Thread.Sleep(150);

        forwarder.FeedPcm(MakeAudioBlock(0f));
        WaitUntil(() => fake.ReportCount >= 12, TimeSpan.FromSeconds(3));

        Assert.That(fake.LastHapticsFrame.All(b => b == 0), Is.True,
            "a payload older than the freshness window must not be used (silence derives to zeros)");

        forwarder.Stop();
    }

    [Test]
    public void UpdateGameHaptics_InvalidReportIsIgnored()
    {
        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        forwarder.UpdateGameHaptics(MakeCombinedReport(new byte[64], reportId: 0x31));
        forwarder.FeedPcm(MakeAudioBlock(0f));
        WaitUntil(() => fake.ReportCount >= 12, TimeSpan.FromSeconds(3));

        Assert.That(fake.LastHapticsFrame.All(b => b == 0), Is.True,
            "a report without the 0x36 id must be rejected and the derived haptics kept");

        forwarder.Stop();
    }

    [Test]
    public void UpdateGameHaptics_AcceptsCapturedViiperReportShape()
    {
        // Regression for the real libVIIPER wire format captured in the field logs:
        // 36 00 91 07 FE 10 10 10 10 10 00 90 3F FD F7 00 ... with haptics at offset 78.
        byte[] gameHaptics = new byte[64];
        Array.Fill(gameHaptics, (byte)0x10);

        FakeAudioOutputs fake = new FakeAudioOutputs
        {
            ConnectionType = ConnectionType.Bluetooth
        };
        using ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(fake, null);
        forwarder.Start();

        DSOutputState output = MakeCombinedReport(gameHaptics);
        output.BluetoothCombinedOutputReport[1] = 0x00;
        output.BluetoothCombinedOutputReport[5] = 0x10;
        output.BluetoothCombinedOutputReport[6] = 0x10;
        output.BluetoothCombinedOutputReport[7] = 0x10;
        output.BluetoothCombinedOutputReport[8] = 0x10;
        output.BluetoothCombinedOutputReport[9] = 0x10;
        output.BluetoothCombinedOutputReport[13] = 0xFD;
        output.BluetoothCombinedOutputReport[14] = 0xF7;
        for (int i = 0; i < 8; i++)
        {
            forwarder.FeedPcm(MakeAudioBlock(0f));
        }

        WaitUntil(() => fake.ReportCount >= 8 && fake.PrimeCount == 1, TimeSpan.FromSeconds(3));

        // Deliver the payload only after the stream is primed: the ~85 ms prime and
        // preroll would otherwise age the payload past the 100 ms freshness window
        // under load, falling back to the audio-derived haptics.
        forwarder.UpdateGameHaptics(output);
        WaitUntil(() => fake.LastHapticsFrame is { Length: 64 } && fake.LastHapticsFrame.All(b => b == 0x10), TimeSpan.FromSeconds(3));

        Assert.That(fake.LastHapticsFrame.All(b => b == 0x10), Is.True,
            "the captured VIIPER combined-report shape (session block, 0x90 0x3F state block, haptics at 78) must be accepted");

        forwarder.Stop();
    }
}