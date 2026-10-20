using System.Buffers.Binary;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Devices;

/// <summary>
/// Pins the DualShock 4 audio lanes wired into the persona device: the two-channel speaker ISO OUT
/// packet is forwarded verbatim, the microphone ISO IN endpoint drains the UAC1 microphone ring, the
/// speaker reset event fires once per effective alternate-setting change, a microphone
/// alternate-setting change drops queued capture audio, and class requests are routed to the audio
/// function only for audio interfaces. The persona has no realtime-haptics lane, so no such event
/// exists.
/// </summary>
[TestFixture]
public sealed class DualShock4AudioLaneTests
{
    /// <summary>Isochronous OUT endpoint address of the speaker path (AS OUT interface 1, alternate 1).</summary>
    private const byte AudioOutEndpointAddress = 0x01;

    /// <summary>Isochronous IN endpoint address of the microphone path (AS IN interface 2, alternate 1).</summary>
    private const byte AudioInEndpointAddress = 0x82;

    /// <summary>Interface number of the speaker audio streaming interface (AS OUT).</summary>
    private const byte SpeakerInterfaceNumber = 1;

    /// <summary>Interface number of the microphone audio streaming interface (AS IN).</summary>
    private const byte MicrophoneInterfaceNumber = 2;

    /// <summary>Interface number of the HID function in the composite configuration.</summary>
    private const byte HidInterfaceNumber = 3;

    /// <summary>Feature-unit ID of the speaker path (the audio control interface's unit 2).</summary>
    private const ushort SpeakerFeatureUnitId = 2;

    /// <summary>UAC1 feature-unit volume control selector.</summary>
    private const ushort VolumeSelector = 2;

    /// <summary>Class-specific SET_CUR request code (UAC1).</summary>
    private const byte RequestSetCur = 0x01;

    /// <summary>HID class GET_IDLE request code (HID 1.11, section 7.2.4).</summary>
    private const byte RequestGetIdle = 0x02;

    /// <summary>Class-specific GET_CUR request code (UAC1).</summary>
    private const byte RequestGetCur = 0x81;

    /// <summary>Standard SET_INTERFACE request code (USB 2.0, section 9.4.10).</summary>
    private const byte RequestSetInterface = 0x0B;

    /// <summary>Standard SET_CONFIGURATION request code (USB 2.0, section 9.4.7).</summary>
    private const byte RequestSetConfiguration = 0x09;

    /// <summary>Host-to-device standard request addressed to an interface (bmRequestType 0x01).</summary>
    private const byte StandardInterfaceOut = 0x01;

    /// <summary>Host-to-device standard request addressed to the device (bmRequestType 0x00).</summary>
    private const byte StandardDeviceOut = 0x00;

    /// <summary>Host-to-device class request addressed to an interface (bmRequestType 0x21).</summary>
    private const byte ClassInterfaceOut = 0x21;

    /// <summary>Device-to-host class request addressed to an interface (bmRequestType 0xA1).</summary>
    private const byte ClassInterfaceIn = 0xA1;

    /// <summary>Nominal speaker packet length: 32 one-millisecond frames of two 16-bit channels.</summary>
    private const int SpeakerPacketBytes = 128;

    /// <summary>Bytes of one microphone PCM frame: 16 one-millisecond samples of one 16-bit channel.</summary>
    private const int MicrophoneFrameBytes = 32;

    /// <summary>Number of samples in one microphone PCM frame.</summary>
    private const int MicrophoneSamples = 16;

    /// <summary>wMaxPacketSize of the microphone endpoint (34 bytes; 32 nominal plus two spare).</summary>
    private const int MicrophoneMaxPacketBytes = 34;

    [Test]
    public void OnIsoPacketReceived_128BytePacket_RaisesExactAudioOut()
    {
        DualShock4Device device = CreateDevice();
        byte[] packet = CreateSpeakerPacket(SpeakerPacketBytes);
        byte[]? audio = null;
        device.AudioOutReceived += data =>
        {
            // The event memory is only valid during the callback; copy it like a real subscriber must.
            audio = data.ToArray();
        };

        device.OnIsoPacketReceived(AudioOutEndpointAddress, packet);

        Assert.Multiple(() =>
        {
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio, Has.Length.EqualTo(SpeakerPacketBytes)); // 32 frames of two interleaved 16-bit channels
            Assert.That(audio, Is.EqualTo(packet));
        });
    }

    [Test]
    public void OnIsoPacketReceived_DoesNotRaiseHaptics()
    {
        DualShock4Device device = CreateDevice();
        int audioCalls = 0;
        device.AudioOutReceived += _ => audioCalls++;

        string[] eventNames = typeof(DualShock4Device).GetEvents().Select(eventInfo => eventInfo.Name).ToArray();
        Assert.That(typeof(DualShock4Device).GetEvent("RealtimeHapticsReceived"), Is.Null);
        Assert.That(eventNames, Has.None.Matches(".*Haptics.*")); // the DS4 persona has no haptics lane at all

        device.OnIsoPacketReceived(AudioOutEndpointAddress, CreateSpeakerPacket(SpeakerPacketBytes));

        Assert.That(audioCalls, Is.EqualTo(1)); // only the audio event fires
    }

    [Test]
    public void SetMicrophonePcm_32Bytes_ProvideIsoPacketReturns16Samples()
    {
        DualShock4Device device = CreateDevice();
        byte[] frame = CreateMicrophoneFrame();
        device.SetMicrophonePcm(frame);
        byte[] destination = new byte[MicrophoneMaxPacketBytes];

        int count = device.ProvideIsoPacket(AudioInEndpointAddress, destination);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(MicrophoneFrameBytes));
            Assert.That(count / sizeof(short), Is.EqualTo(MicrophoneSamples)); // 16 one-channel samples
            Assert.That(destination[..MicrophoneFrameBytes], Is.EqualTo(frame));
            Assert.That(destination[MicrophoneFrameBytes..], Is.All.Zero);
        });
    }

    [Test]
    public void ProvideIsoPacket_EmptyQueue_ReturnsZerosAndNominalLength()
    {
        DualShock4Device device = CreateDevice();
        byte[] destination = new byte[MicrophoneMaxPacketBytes];
        Array.Fill(destination, (byte)0xFF);

        int count = device.ProvideIsoPacket(AudioInEndpointAddress, destination);

        Assert.That(count, Is.EqualTo(MicrophoneFrameBytes));
        Assert.That(destination, Is.All.Zero); // the nominal zeros plus the unused two-byte tail
    }

    [TestCase(0)]
    [TestCase(31)]
    [TestCase(33)]
    public void SetMicrophonePcm_InvalidLength_Throws(int length)
    {
        DualShock4Device device = CreateDevice();

        Assert.That(() => device.SetMicrophonePcm(new byte[length]), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void OnInterfaceChanged_SpeakerAltChanges_RaisesResetExactlyOnce()
    {
        DualShock4Device device = CreateDevice();
        int resets = 0;
        device.SpeakerResetReceived += () => resets++;

        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);
        Assert.That(resets, Is.EqualTo(1));
        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);
        Assert.That(resets, Is.EqualTo(1)); // a write of the active alternate setting changes nothing

        // SET_CONFIGURATION(0) returns the speaker to alternate setting 0 through the base class's
        // interface hook; OnConfigurationChanged must reset its generation without raising a second reset.
        Assert.That(SendSetConfiguration(device, 0), Is.Zero);
        Assert.That(resets, Is.EqualTo(2));
        Assert.That(SendSetConfiguration(device, 0), Is.Zero);
        Assert.That(resets, Is.EqualTo(2));

        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 0), Is.Zero);
        Assert.That(resets, Is.EqualTo(2)); // already at alternate setting 0
        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);
        Assert.That(resets, Is.EqualTo(3));
        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 0), Is.Zero);
        Assert.That(resets, Is.EqualTo(4));
    }

    [Test]
    public void OnInterfaceChanged_MicAltChange_ClearsQueue()
    {
        DualShock4Device device = CreateDevice();
        device.SetMicrophonePcm(CreateMicrophoneFrame());

        Assert.That(SendSetInterface(device, MicrophoneInterfaceNumber, 1), Is.Zero);
        byte[] destination = new byte[MicrophoneMaxPacketBytes];
        int count = device.ProvideIsoPacket(AudioInEndpointAddress, destination);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(MicrophoneFrameBytes));
            Assert.That(destination, Is.All.Zero); // the queued frame is gone; only the nominal zero packet remains
        });
    }

    [Test]
    public void OnConfigurationChanged_ConfigZero_ClearsMicrophoneQueue()
    {
        DualShock4Device device = CreateDevice();
        device.SetMicrophonePcm(CreateMicrophoneFrame());

        // SET_CONFIGURATION(0) must drop the queued capture frame, not just reset the interface state.
        Assert.That(SendSetConfiguration(device, 0), Is.Zero);

        byte[] destination = new byte[MicrophoneMaxPacketBytes];
        int count = device.ProvideIsoPacket(AudioInEndpointAddress, destination);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(MicrophoneFrameBytes));
            Assert.That(destination, Is.All.Zero); // the queued frame is gone; only the nominal zero packet remains
        });
    }

    [Test]
    public void OnClassRequest_AudioInterface_RoutesToVolumeState()
    {
        DualShock4Device device = CreateDevice();
        ControlResult initial = SendClassRequest(device, ClassInterfaceIn, RequestGetCur, VolumeSelector, SpeakerFeatureUnitId, 0, 2);
        Assert.That(initial.Count, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadInt16LittleEndian(initial.Data), Is.EqualTo(-256)); // -1 dB speaker default

        short volumeQ8 = -2560; // -10 dB
        byte[] volume = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(volume, volumeQ8);
        ControlResult set = SendClassRequest(device, ClassInterfaceOut, RequestSetCur, VolumeSelector, SpeakerFeatureUnitId, 0, 2, volume);
        Assert.That(set.Count, Is.Zero);

        ControlResult updated = SendClassRequest(device, ClassInterfaceIn, RequestGetCur, VolumeSelector, SpeakerFeatureUnitId, 0, 2);
        Assert.That(updated.Count, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadInt16LittleEndian(updated.Data), Is.EqualTo(volumeQ8));

        // The HID interface is not an audio interface: GET_IDLE still reaches VirtualHidDevice.
        ControlResult idle = Send(device, ClassInterfaceIn, RequestGetIdle, 0, HidInterfaceNumber, 1);
        Assert.That(idle.Count, Is.EqualTo(1));
        Assert.That(idle.Data[0], Is.Zero);
    }

    [Test]
    public void OnClassRequest_StreamingInterface_RoutesToVolumeState()
    {
        DualShock4Device device = CreateDevice();
        short volumeQ8 = -1280; // -5 dB
        byte[] volume = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(volume, volumeQ8);

        // The feature unit belongs to the audio control interface, but every audio-class interface must
        // route to the audio function; address the same unit through the speaker streaming interface 1.
        ControlResult set = SendClassRequest(
            device,
            ClassInterfaceOut,
            RequestSetCur,
            VolumeSelector,
            SpeakerFeatureUnitId,
            0,
            2,
            volume,
            SpeakerInterfaceNumber);
        Assert.That(set.Count, Is.Zero);

        ControlResult updated = SendClassRequest(
            device,
            ClassInterfaceIn,
            RequestGetCur,
            VolumeSelector,
            SpeakerFeatureUnitId,
            0,
            2,
            interfaceNumber: SpeakerInterfaceNumber);
        Assert.That(updated.Count, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadInt16LittleEndian(updated.Data), Is.EqualTo(volumeQ8));
    }

    [Test]
    public void AudioLanes_OtherEndpoints_AreNoOps()
    {
        DualShock4Device device = CreateDevice();
        int audioCalls = 0;
        device.AudioOutReceived += _ => audioCalls++;

        device.OnIsoPacketReceived(AudioInEndpointAddress, CreateSpeakerPacket(SpeakerPacketBytes));
        byte[] destination = new byte[MicrophoneMaxPacketBytes];
        int count = device.ProvideIsoPacket(AudioOutEndpointAddress, destination);

        Assert.Multiple(() =>
        {
            Assert.That(audioCalls, Is.Zero); // only endpoint 0x01 has an OUT lane
            Assert.That(count, Is.Zero); // only endpoint 0x82 has an IN lane
        });
    }

    [Test]
    public void OnIsoPacketReceived_ThrowingSubscriber_DoesNotBlockTheRemainingSubscribers()
    {
        RecordingLogger logger = new RecordingLogger();
        DualShock4Device device = new DualShock4Device(new DualShock4Identity(), logger);
        int audioCalls = 0;
        device.AudioOutReceived += _ => throw new InvalidOperationException("audio subscriber fault");
        device.AudioOutReceived += _ => audioCalls++;

        Assert.That(
            () => device.OnIsoPacketReceived(AudioOutEndpointAddress, new byte[SpeakerPacketBytes]),
            Throws.Nothing);

        Assert.Multiple(() =>
        {
            Assert.That(audioCalls, Is.EqualTo(1)); // the second subscriber still observed the packet
            Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1)); // the fault was logged at Error
        });
    }

    [Test]
    public void SpeakerResetReceived_ThrowingSubscriber_IsLoggedAndOthersRun()
    {
        RecordingLogger logger = new RecordingLogger();
        DualShock4Device device = new DualShock4Device(new DualShock4Identity(), logger);
        int resets = 0;
        device.SpeakerResetReceived += () => throw new InvalidOperationException("speaker reset subscriber fault");
        device.SpeakerResetReceived += () => resets++;

        Assert.That(SendSetInterface(device, SpeakerInterfaceNumber, 1), Is.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(resets, Is.EqualTo(1)); // the second subscriber still observed the reset
            Assert.That(logger.ErrorEntries, Has.Count.EqualTo(1)); // the fault was logged at Error
        });
    }

    /// <summary>Creates the persona device with the reference identity.</summary>
    private static DualShock4Device CreateDevice() => new DualShock4Device(new DualShock4Identity());

    /// <summary>
    /// Creates a deterministic speaker packet: the two interleaved 16-bit channels of every 4-byte
    /// frame carry distinctive byte patterns.
    /// </summary>
    private static byte[] CreateSpeakerPacket(int length)
    {
        byte[] packet = new byte[length];
        for (int i = 0; i < packet.Length; i++)
        {
            packet[i] = (byte)(i * 7 + 3);
        }

        return packet;
    }

    /// <summary>Creates a deterministic 32-byte microphone frame (one interleaved 16-bit channel).</summary>
    private static byte[] CreateMicrophoneFrame()
    {
        byte[] frame = new byte[MicrophoneFrameBytes];
        for (int i = 0; i < frame.Length; i++)
        {
            frame[i] = (byte)(i * 5 + 1);
        }

        return frame;
    }

    /// <summary>Sends a standard SET_INTERFACE request and returns its result count.</summary>
    private static int SendSetInterface(DualShock4Device device, byte interfaceNumber, byte alternateSetting) =>
        Send(device, StandardInterfaceOut, RequestSetInterface, alternateSetting, interfaceNumber, 0).Count;

    /// <summary>Sends a standard SET_CONFIGURATION request and returns its result count.</summary>
    private static int SendSetConfiguration(DualShock4Device device, byte configurationValue) =>
        Send(device, StandardDeviceOut, RequestSetConfiguration, configurationValue, 0, 0).Count;

    /// <summary>
    /// Sends a class request whose wValue is selector/channel and whose wIndex is feature-unit/interface
    /// (entity id in the high byte, interface number in the low byte), then returns the response count
    /// and buffer.
    /// </summary>
    private static ControlResult SendClassRequest(
        DualShock4Device device,
        byte requestType,
        byte request,
        ushort selector,
        ushort entityId,
        ushort channel,
        ushort length,
        byte[]? outData = null,
        byte interfaceNumber = 0) =>
        Send(
            device,
            requestType,
            request,
            (ushort)((selector << 8) | channel),
            (ushort)((entityId << 8) | interfaceNumber),
            length,
            outData);

    /// <summary>Sends one control transfer through the public entry point of <paramref name="device"/>.</summary>
    private static ControlResult Send(
        DualShock4Device device,
        byte requestType,
        byte request,
        ushort value,
        ushort index,
        ushort length,
        byte[]? outData = null)
    {
        byte[] buffer = new byte[length];
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = requestType,
            Request = request,
            Value = value,
            Index = index,
            Length = length
        };

        int count = device.HandleControlRequest(setup, outData, buffer);
        return new ControlResult(count, buffer);
    }

    /// <summary>Control result of one transfer: response count and response buffer.</summary>
    private readonly record struct ControlResult(int Count, byte[] Data);
}