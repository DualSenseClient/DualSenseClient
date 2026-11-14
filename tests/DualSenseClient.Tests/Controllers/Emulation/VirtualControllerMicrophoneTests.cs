using Concentus;
using Concentus.Enums;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.DualSense.Utilities;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices;
using DualSenseClient.Hid;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Tests.Controllers.Emulation;

public class VirtualControllerMicrophoneTests
{
    private const int SampleRate = 48000;
    private const int ToneSamples = 480;
    private const int PrebufferFrames = 5;
    private const int QueueCapacitySamples = 200 * 48;
    private const short LoudTone = 12000;

    private sealed class FakeEndpoint : IVirtualMicrophoneEndpoint
    {
        public bool MicrophoneActive { get; set; }

        public AudioFunctionFormat MicrophoneFormat { get; set; } = AudioFunctionFormat.DualSense;

        public event Action<bool>? MicrophoneActiveChanged;

        public event Action? MicrophonePacketServed;

        public List<byte[]> Frames { get; } = new List<byte[]>();

        public int ClearCount { get; private set; }

        public bool ThrowOnClear { get; set; }

        public void SetMicrophonePcm(ReadOnlySpan<byte> frame) => Frames.Add(frame.ToArray());

        public void ClearMicrophonePcm()
        {
            ClearCount++;
            if (ThrowOnClear)
            {
                throw new InvalidOperationException("Synthetic microphone flush failure");
            }
        }

        public void RaiseActive(bool active)
        {
            MicrophoneActive = active;
            MicrophoneActiveChanged?.Invoke(active);
        }

        public void RaiseServed() => MicrophonePacketServed?.Invoke();
    }

    private sealed class FakeTimerLease : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    private sealed class RecordingHidDevice : IHidDevice
    {
        public List<byte[]> Writes { get; } = new List<byte[]>();

        public byte[]? LastWrite
        {
            get
            {
                return Writes.Count == 0 ? null : Writes[^1];
            }
        }

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string DevicePath
        {
            get
            {
                return "test";
            }
        }

        public bool IsConnected
        {
            get
            {
                return true;
            }
        }

        public int Read(byte[] buffer, int offset, int count, int timeoutMs) => 0;

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);

        public int Write(byte[] buffer, int offset, int count)
        {
            Writes.Add(buffer[offset..(offset + count)]);
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test";

        public void Dispose()
        {
        }
    }

    private sealed class StubHidDeviceInfo(ConnectionType connectionType) : IHidDeviceInfo
    {
        public string Path
        {
            get
            {
                return "test";
            }
        }

        public ushort VendorId
        {
            get
            {
                return 0x054C;
            }
        }

        public ushort ProductId
        {
            get
            {
                return 0x0CE6;
            }
        }

        public string ProductName
        {
            get
            {
                return "DualSense Test";
            }
        }

        public string Manufacturer
        {
            get
            {
                return "Sony";
            }
        }

        public int InterfaceNumber
        {
            get
            {
                return 0;
            }
        }

        public ushort UsagePage
        {
            get
            {
                return 1;
            }
        }

        public HidUsageId Usage
        {
            get
            {
                return HidUsageId.GamePad;
            }
        }

        public ConnectionType BusType
        {
            get
            {
                return connectionType;
            }
        }
    }

    private sealed class Harness : IDisposable
    {
        public Harness(
            MicrophoneSettings? settings = null,
            bool passthroughEnabled = true,
            AudioFunctionFormat? format = null)
        {
            Endpoint = new FakeEndpoint
            {
                MicrophoneFormat = format ?? AudioFunctionFormat.DualSense
            };
            Device = new DualSenseDevice(Hid, new StubHidDeviceInfo(ConnectionType.Bluetooth));
            Bridge = new VirtualControllerMicrophone(
                Device,
                Endpoint,
                settings ?? new MicrophoneSettings(),
                passthroughEnabled,
                CreateLease);
        }

        public RecordingHidDevice Hid { get; } = new RecordingHidDevice();

        public DualSenseDevice Device { get; }

        public FakeEndpoint Endpoint { get; }

        public VirtualControllerMicrophone Bridge { get; }

        public List<FakeTimerLease> Leases { get; } = new List<FakeTimerLease>();

        public FakeTimerLease? LastLease
        {
            get
            {
                return Leases.Count == 0 ? null : Leases[^1];
            }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Device.Dispose();
        }

        private IDisposable CreateLease()
        {
            FakeTimerLease lease = new FakeTimerLease();
            Leases.Add(lease);
            return lease;
        }
    }

    private sealed class TestEncoder : IDisposable
    {
        private readonly IOpusEncoder _encoder;

        public TestEncoder()
        {
            _encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = 71 * 8 * 100;
            _encoder.UseVBR = false;
            _encoder.ExpertFrameDuration = OpusFramesize.OPUS_FRAMESIZE_10_MS;
        }

        public byte[] EncodeTone(short amplitude, double frequencyHz = 1000)
        {
            short[] pcm = new short[ToneSamples];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = (short)(Math.Sin(2 * Math.PI * frequencyHz * i / SampleRate) * amplitude);
            }

            byte[] frame = new byte[71];
            int written = _encoder.Encode(pcm, pcm.Length, frame, frame.Length);
            return frame[..written];
        }

        public void Dispose() => _encoder.Dispose();
    }

    [Test]
    public void Start_WhenPassthroughOnEndpointOpenRouteActive_AcquiresTimerAndSendsStatus()
    {
        using Harness harness = new Harness();
        int writesBefore = harness.Hid.Writes.Count;

        harness.Endpoint.RaiseActive(true);

        byte[]? state = FindMicrophoneState(harness.Hid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases, Has.Count.EqualTo(1));
            Assert.That(harness.LastLease!.Disposed, Is.Zero);
            Assert.That(harness.Hid.Writes.Count, Is.GreaterThan(writesBefore),
                "starting the stream writes the gate and state reports");
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x03), Is.True,
                "the gate status report opens the physical microphone");
            Assert.That(state, Is.Not.Null, "the stream start re-applies the route and level");
            Assert.That(state![11] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectAuto));
            Assert.That(state[10], Is.EqualTo(64));
        });
    }

    [Test]
    public void Start_ReappliesConfiguredRouteAndLevel()
    {
        using Harness harness = new Harness(new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        int writesBefore = harness.Hid.Writes.Count;

        harness.Endpoint.RaiseActive(true);

        byte[]? state = FindMicrophoneState(harness.Hid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Hid.Writes.Count, Is.GreaterThan(writesBefore),
                "starting the stream writes the gate and state reports");
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x03), Is.True);
            Assert.That(state, Is.Not.Null, "the stream start sends the embedded-state report");
            Assert.That(state![11] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal));
            Assert.That(state[10], Is.EqualTo(32));
            Assert.That(state[11] & 0x30, Is.EqualTo((byte)AudioControl.OutputPathSpeaker),
                "re-applying the route preserves the output path");
        });
    }

    [Test]
    public void Start_WhenRouteDisabled_DoesNotStream()
    {
        using Harness harness = new Harness(new MicrophoneSettings
        {
            Source = MicrophoneSource.Disabled
        });

        harness.Endpoint.RaiseActive(true);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases, Is.Empty);
            Assert.That(harness.Endpoint.Frames, Is.Empty);
        });
    }

    [Test]
    public void Start_WhenPassthroughOff_DoesNotStream()
    {
        using Harness harness = new Harness(passthroughEnabled: false);

        harness.Endpoint.RaiseActive(true);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases, Is.Empty);
            Assert.That(harness.Endpoint.Frames, Is.Empty);
        });
    }

    [Test]
    public void DisabledToAuto_WhileEndpointOpen_Starts()
    {
        using Harness harness = new Harness(new MicrophoneSettings
        {
            Source = MicrophoneSource.Disabled
        });
        harness.Endpoint.RaiseActive(true);

        harness.Bridge.ApplySettings(new MicrophoneSettings
        {
            Source = MicrophoneSource.Auto
        }, true);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases, Has.Count.EqualTo(1));
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x03), Is.True);
            Assert.That(FindMicrophoneState(harness.Hid), Is.Not.Null,
                "starting after a route change re-applies the state");
        });
    }

    [Test]
    public void AutoToDisabled_StopsAndClears()
    {
        using Harness harness = new Harness();
        harness.Endpoint.RaiseActive(true);
        int clearsBefore = harness.Endpoint.ClearCount;

        harness.Bridge.ApplySettings(new MicrophoneSettings
        {
            Source = MicrophoneSource.Disabled
        }, true);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases[0].Disposed, Is.EqualTo(1));
            Assert.That(harness.Endpoint.ClearCount, Is.GreaterThan(clearsBefore));
            Assert.That(harness.Hid.LastWrite![0], Is.EqualTo(0x32));
            Assert.That(harness.Hid.LastWrite![4], Is.EqualTo(0x02));
        });
    }

    [Test]
    public void PassthroughOff_StopsAndClears()
    {
        using Harness harness = new Harness();
        harness.Endpoint.RaiseActive(true);
        int clearsBefore = harness.Endpoint.ClearCount;

        harness.Bridge.ApplySettings(new MicrophoneSettings(), false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases[0].Disposed, Is.EqualTo(1));
            Assert.That(harness.Endpoint.ClearCount, Is.GreaterThan(clearsBefore));
            Assert.That(harness.Hid.LastWrite![0], Is.EqualTo(0x32));
            Assert.That(harness.Hid.LastWrite![4], Is.EqualTo(0x02));
        });
    }

    [Test]
    public void Stop_WhenEndpointClearThrows_StillClosesTheGateAndReleasesTheLease()
    {
        using Harness harness = new Harness();
        harness.Endpoint.RaiseActive(true);
        harness.Endpoint.ThrowOnClear = true;

        Assert.That(
            () => harness.Bridge.ApplySettings(new MicrophoneSettings
            {
                Source = MicrophoneSource.Disabled
            }, true),
            Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x02), Is.True,
                "the physical microphone gate closes even when the persona flush fails");
            Assert.That(harness.Leases[0].Disposed, Is.EqualTo(1), "the timer lease is still released");
        });
    }

    [Test]
    public void Start_WhenEndpointClearThrows_RollsBackAndAllowsRetry()
    {
        using Harness harness = new Harness();
        harness.Endpoint.ThrowOnClear = true;

        Assert.That(() => harness.Endpoint.RaiseActive(true), Throws.TypeOf<InvalidOperationException>());

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases, Has.Count.EqualTo(1));
            Assert.That(harness.Leases[0].Disposed, Is.EqualTo(1), "the failed start releases its lease");
            Assert.That(harness.Bridge.QueuedSamples, Is.Zero);
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x03), Is.False, "the failed start leaves the gate closed");
        });

        harness.Endpoint.ThrowOnClear = false;
        harness.Bridge.ApplySettings(new MicrophoneSettings(), true);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases, Has.Count.EqualTo(2), "a retry can start a new stream");
            Assert.That(harness.Leases[1].Disposed, Is.Zero);
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x03), Is.True, "the retry opens the gate");
        });
    }

    [Test]
    public void Dispose_KeepsTheConfiguredMicrophoneRouteAndLevel()
    {
        using Harness harness = new Harness(new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        harness.Bridge.Dispose();

        harness.Device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathHeadphones | AudioControl.MicSelectInternal,
            MicVolume = 7
        });

        byte[] write = harness.Hid.Writes.Last(report => report[0] == 0x31);
        Assert.Multiple(() =>
        {
            Assert.That(write[10] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal),
                "disposing the bridge leaves the configured route in place");
            Assert.That(write[9], Is.EqualTo(32), "disposing the bridge leaves the configured level in place");
        });
    }

    [Test]
    public void Dispose_ClosesTheGateAndReleasesTheLease()
    {
        Harness harness = new Harness();
        harness.Endpoint.RaiseActive(true);
        Assert.That(HasMicrophoneStatus(harness.Hid, 0x03), Is.True, "precondition: the gate is open");

        harness.Bridge.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(HasMicrophoneStatus(harness.Hid, 0x02), Is.True,
                "disposing the bridge closes the physical microphone gate");
            Assert.That(harness.Leases[0].Disposed, Is.EqualTo(1), "the timer lease is released");
        });

        harness.Dispose();
    }

    [Test]
    public void PacketServed_AfterPrebuffer_PushesOneTargetFrame()
    {
        using Harness harness = new Harness();
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        FeedFrames(harness.Bridge, encoder, PrebufferFrames, LoudTone);

        harness.Endpoint.RaiseServed();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Endpoint.Frames, Has.Count.EqualTo(1));
            Assert.That(harness.Endpoint.Frames[0], Has.Length.EqualTo(192));
            Assert.That(harness.Bridge.QueuedSamples, Is.EqualTo(PrebufferFrames * ToneSamples - 48));
        });
    }

    [Test]
    public void PhysicalMicrophoneReport_DecodesIntoTargetPcmFrame()
    {
        using Harness harness = new Harness();
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);

        for (int i = 0; i < PrebufferFrames; i++)
        {
            byte[] report = CreateMicrophoneReport(encoder.EncodeTone(LoudTone));
            harness.Device.ProcessInputReport(report, report.Length);
        }

        // The decoder's codec delay makes the first few milliseconds silent; serve past
        // it so the assertion measures the decoded tone rather than warm-up.
        for (int i = 0; i < 12; i++)
        {
            harness.Endpoint.RaiseServed();
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Endpoint.Frames, Has.Count.EqualTo(12));
            Assert.That(harness.Endpoint.Frames, Has.All.Length.EqualTo(192));
            Assert.That(harness.Endpoint.Frames.Any(frame => MaxAmplitude(frame) > 1000), Is.True,
                "a physical HasMic packet reaches the persona as audible PCM");
        });
    }

    [Test]
    public void PhysicalMicrophoneReport_DecodesIntoDualShock4PcmFrame()
    {
        using Harness harness = new Harness(format: AudioFunctionFormat.DualShock4);
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);

        for (int i = 0; i < PrebufferFrames; i++)
        {
            byte[] report = CreateMicrophoneReport(encoder.EncodeTone(LoudTone));
            harness.Device.ProcessInputReport(report, report.Length);
        }

        for (int i = 0; i < 12; i++)
        {
            harness.Endpoint.RaiseServed();
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Endpoint.Frames, Has.Count.EqualTo(12));
            Assert.That(harness.Endpoint.Frames, Has.All.Length.EqualTo(32));
            Assert.That(harness.Endpoint.Frames.Any(frame => MaxAmplitude(frame) > 1000), Is.True,
                "the physical microphone reaches the DualShock 4 target as 16 kHz mono PCM");
        });
    }

    [Test]
    public void Prebuffer_NoFramesBeforeThreshold()
    {
        using Harness harness = new Harness();
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        FeedFrames(harness.Bridge, encoder, PrebufferFrames - 1, LoudTone);

        harness.Endpoint.RaiseServed();

        Assert.That(harness.Endpoint.Frames, Is.Empty);
    }

    [Test]
    public void Underrun_RebuildsPrebuffer()
    {
        using Harness harness = new Harness();
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        FeedFrames(harness.Bridge, encoder, PrebufferFrames, LoudTone);
        for (int i = 0; i < PrebufferFrames * ToneSamples / 48; i++)
        {
            harness.Endpoint.RaiseServed();
        }

        int framesAfterDrain = harness.Endpoint.Frames.Count;
        harness.Endpoint.RaiseServed();
        int framesAfterUnderrun = harness.Endpoint.Frames.Count;
        FeedFrames(harness.Bridge, encoder, PrebufferFrames, LoudTone);
        harness.Endpoint.RaiseServed();

        Assert.Multiple(() =>
        {
            Assert.That(framesAfterDrain, Is.EqualTo(PrebufferFrames * ToneSamples / 48));
            Assert.That(framesAfterUnderrun, Is.EqualTo(framesAfterDrain));
            Assert.That(harness.Endpoint.Frames, Has.Count.EqualTo(framesAfterUnderrun + 1));
        });
    }

    [Test]
    public void Overflow_DropsOldest()
    {
        using Harness harness = new Harness();
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        FeedFrames(harness.Bridge, encoder, QueueCapacitySamples / ToneSamples, 0);

        harness.Bridge.HandleMicrophoneFrame(encoder.EncodeTone(LoudTone));

        Assert.That(harness.Bridge.QueuedSamples, Is.EqualTo(QueueCapacitySamples));
        for (int i = 0; i < QueueCapacitySamples / 48; i++)
        {
            harness.Endpoint.RaiseServed();
        }

        Assert.Multiple(() =>
        {
            Assert.That(harness.Endpoint.Frames, Has.Count.EqualTo(QueueCapacitySamples / 48));
            Assert.That(MaxAmplitude(harness.Endpoint.Frames[0]), Is.LessThan(1000), "oldest silent audio dropped");
            Assert.That(MaxAmplitude(harness.Endpoint.Frames[^1]), Is.GreaterThan(4000), "newest audio kept");
        });
    }

    [Test]
    public void DualSenseTarget_Produces192ByteFrames()
    {
        using Harness harness = new Harness(format: AudioFunctionFormat.DualSense);
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        FeedFrames(harness.Bridge, encoder, PrebufferFrames, LoudTone);

        harness.Endpoint.RaiseServed();

        Assert.That(harness.Endpoint.Frames[0], Has.Length.EqualTo(192));
    }

    [Test]
    public void DualShock4Target_Produces32ByteFrames()
    {
        using Harness harness = new Harness(format: AudioFunctionFormat.DualShock4);
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        FeedFrames(harness.Bridge, encoder, PrebufferFrames, LoudTone);

        harness.Endpoint.RaiseServed();

        Assert.That(harness.Endpoint.Frames[0], Has.Length.EqualTo(32));
    }

    [Test]
    public void ApplySettings_MapsRouteAndLevel()
    {
        using Harness harness = new Harness();

        harness.Bridge.ApplySettings(new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        }, true);

        byte[] write = harness.Hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[0], Is.EqualTo(0x32));
            Assert.That(write[2], Is.EqualTo(0x90));
            Assert.That(write[3], Is.EqualTo(0x3F));
            Assert.That(write[11] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal));
            Assert.That(write[10], Is.EqualTo(32));
        });
    }

    [Test]
    public void Dispose_ReleasesTimerAndUnsubscribes()
    {
        Harness harness = new Harness();
        harness.Endpoint.RaiseActive(true);
        int framesBefore = harness.Endpoint.Frames.Count;

        harness.Bridge.Dispose();
        harness.Bridge.Dispose();
        harness.Endpoint.RaiseServed();
        harness.Endpoint.RaiseActive(false);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Leases[0].Disposed, Is.EqualTo(1));
            Assert.That(harness.Endpoint.Frames, Has.Count.EqualTo(framesBefore));
            Assert.That(harness.Bridge.QueuedSamples, Is.Zero);
        });

        harness.Dispose();
    }

    [Test]
    public void ConcurrentSettingsServeAndDispose_DoesNotThrow()
    {
        using Harness harness = new Harness();
        using TestEncoder encoder = new TestEncoder();
        harness.Endpoint.RaiseActive(true);
        byte[] frame = encoder.EncodeTone(LoudTone);
        using CancellationTokenSource cancellation = new CancellationTokenSource();

        Task[] workers =
        [
            Task.Run(() =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    harness.Bridge.HandleMicrophoneFrame(frame);
                }
            }),
            Task.Run(() =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    harness.Endpoint.RaiseServed();
                }
            }),
            Task.Run(() =>
            {
                int iteration = 0;
                while (!cancellation.IsCancellationRequested)
                {
                    harness.Bridge.ApplySettings(new MicrophoneSettings
                    {
                        Source = iteration % 2 == 0 ? MicrophoneSource.Auto : MicrophoneSource.Headphone,
                        Level = iteration % 101
                    }, true);
                    iteration++;
                }
            })
        ];

        Thread.Sleep(50);
        harness.Bridge.Dispose();
        cancellation.Cancel();

        Assert.That(() => Task.WaitAll(workers, TimeSpan.FromSeconds(5)), Throws.Nothing);
    }

    [Test]
    public void DecodeFailure_DropsFrame()
    {
        using Harness harness = new Harness();
        harness.Endpoint.RaiseActive(true);

        harness.Bridge.HandleMicrophoneFrame([0xFF, 0xFF, 0xFF]);
        harness.Endpoint.RaiseServed();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Bridge.QueuedSamples, Is.Zero);
            Assert.That(harness.Endpoint.Frames, Is.Empty);
        });
    }

    private static bool HasMicrophoneStatus(RecordingHidDevice hid, byte mask) =>
        hid.Writes.Any(write => write.Length > 4
                                && write[0] == 0x32
                                && write[2] == 0x91
                                && write[4] == mask);

    private static byte[]? FindMicrophoneState(RecordingHidDevice hid) =>
        hid.Writes.LastOrDefault(write => write.Length > 11
                                          && write[0] == 0x32
                                          && write[2] == 0x90
                                          && write[3] == 0x3F);

    private static byte[] CreateMicrophoneReport(byte[] opusFrame)
    {
        byte[] report = new byte[78];
        report[0] = 0x31;
        report[1] = 0x02; // HasMic, HasHID clear
        Array.Copy(opusFrame, 0, report, 3, Math.Min(opusFrame.Length, 71));

        uint crc = DualSenseCRC32.Compute(report, 0, 74, DualSenseCRC32.InputReportSeed);
        report[74] = (byte)crc;
        report[75] = (byte)(crc >> 8);
        report[76] = (byte)(crc >> 16);
        report[77] = (byte)(crc >> 24);
        return report;
    }

    private static void FeedFrames(
        VirtualControllerMicrophone bridge,
        TestEncoder encoder,
        int count,
        short amplitude)
    {
        for (int i = 0; i < count; i++)
        {
            bridge.HandleMicrophoneFrame(encoder.EncodeTone(amplitude));
        }
    }

    private static int MaxAmplitude(byte[] frame)
    {
        int maximum = 0;
        for (int i = 0; i + 1 < frame.Length; i += 2)
        {
            int sample = Math.Abs((short)(frame[i] | (frame[i + 1] << 8)));
            maximum = Math.Max(maximum, sample);
        }

        return maximum;
    }
}