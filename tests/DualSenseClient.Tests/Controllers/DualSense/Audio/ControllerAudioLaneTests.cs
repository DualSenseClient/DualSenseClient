using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio;

[TestFixture]
public sealed class ControllerAudioLaneTests
{
    [Test]
    public void HigherPriority_PreemptsLower_AndLowerResumesAfterLeave()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object forwarder = new object();
        object specialAction = new object();

        Assert.That(lane.TryEnter(forwarder, AudioPriority.Forwarder), Is.True);
        Assert.That(lane.TryEnter(specialAction, AudioPriority.SpecialAction), Is.True);
        Assert.That(lane.IsActive(forwarder), Is.False);

        lane.Leave(specialAction);

        Assert.That(lane.IsActive(forwarder), Is.True);
        Assert.That(lane.ActivePriority, Is.EqualTo(AudioPriority.Forwarder));
    }

    [Test]
    public void SendAudio_InactiveOwner_IsDropped()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object forwarder = new object();
        object player = new object();
        lane.TryEnter(forwarder, AudioPriority.Forwarder);
        lane.TryEnter(player, AudioPriority.Player);

        bool sent = lane.SendAudio(forwarder, new byte[200], BluetoothAudioRoute.Speaker);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.False);
            Assert.That(sink.AudioReports, Is.Zero);
        });
    }

    [Test]
    public void SetAudioLevel_InactiveOwner_DoesNotChange()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object forwarder = new object();
        object player = new object();
        lane.TryEnter(forwarder, AudioPriority.Forwarder);
        lane.TryEnter(player, AudioPriority.Player);

        lane.SetAudioLevel(forwarder, 0.9f);

        Assert.That(lane.AudioLevel, Is.EqualTo(0f));
    }

    [Test]
    public void ManualRumble_PreemptsTheAudioLane_AndReleasesIt()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object forwarder = new object();
        object manual = new object();
        lane.TryEnter(forwarder, AudioPriority.Forwarder);

        Assert.That(lane.TryEnter(manual, AudioPriority.ManualRumble), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(lane.IsActive(manual), Is.True);
            Assert.That(lane.SendAudio(forwarder, new byte[200], BluetoothAudioRoute.Speaker), Is.False,
                "the forwarder must stop streaming while manual rumble owns the lane");
            Assert.That(sink.AudioReports, Is.Zero);
        });

        lane.Leave(manual);
        Assert.That(lane.IsActive(forwarder), Is.True);
    }

    [Test]
    public void SamePriority_LatestOwnerWins()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object first = new object();
        object second = new object();

        Assert.That(lane.TryEnter(first, AudioPriority.Player), Is.True);
        Assert.That(lane.TryEnter(second, AudioPriority.Player), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(lane.IsActive(first), Is.False);
            Assert.That(lane.IsActive(second), Is.True);
        });
    }

    [Test]
    public void SendHaptics_InactiveOwner_IsDropped()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object forwarder = new object();
        object player = new object();
        lane.TryEnter(forwarder, AudioPriority.Forwarder);
        lane.TryEnter(player, AudioPriority.Player);

        bool sent = lane.SendHaptics(forwarder, new byte[64]);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.False);
            Assert.That(sink.HapticsReports, Is.Zero);
        });
    }

    [Test]
    public void SendHaptics_ActiveOwner_Sends()
    {
        RecordingAudioSink sink = new RecordingAudioSink();
        ControllerAudioLane lane = new ControllerAudioLane(sink);
        object player = new object();
        lane.TryEnter(player, AudioPriority.Player);

        bool sent = lane.SendHaptics(player, new byte[64]);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.True);
            Assert.That(sink.HapticsReports, Is.EqualTo(1));
        });
    }

    private sealed class RecordingAudioSink : IControllerAudioSink
    {
        public int AudioReports { get; private set; }

        public int HapticsReports { get; private set; }

        public ConnectionType ConnectionType
        {
            get
            {
                return ConnectionType.Bluetooth;
            }
        }

        public bool HeadphonesConnected
        {
            get
            {
                return false;
            }
        }

        public void SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume)
        {
        }

        public void ResetBluetoothAudioStream()
        {
        }

        public void SendBluetoothAudioPrime(SetStateData state)
        {
        }

        public void SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm,
            BluetoothAudioRoute route) =>
            AudioReports++;

        public void SendBluetoothAudioAndHapticsDouble(ReadOnlySpan<byte> opusFrames, ReadOnlySpan<byte> hapticsPcm,
            BluetoothAudioRoute route) =>
            AudioReports++;

        public void SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route) => AudioReports++;

        public void SendBluetoothHaptics(ReadOnlySpan<byte> hapticsPcm) => HapticsReports++;
    }
}