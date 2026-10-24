using System.Buffers.Binary;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation.Devices.Xbox360;
using ButtonMappingEntry = DualSenseClient.Settings.Sections.ButtonMappingEntry;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Input-side tests of <see cref="VirtualXbox360Controller"/>: the adapter maps the same
/// <see cref="VirtualInputMapper"/> pipeline result onto the persona's 20-byte XInput packet. The
/// expected packets are stated independently here (the mapper's flag values are the wire button
/// bits), so the adapter's button/D-pad translation, the Y-axis inversion and the trigger bytes are
/// pinned against the mapper's semantics. The persona has no battery or connection state, so the
/// inherited no-ops are kept.
/// </summary>
[TestFixture]
public sealed class VirtualXbox360ControllerTests
{
    /// <summary>Packet offset of the little-endian button word.</summary>
    private const int OffsetButtons = 2;

    /// <summary>Packet offset of the left analog trigger.</summary>
    private const int OffsetLeftTrigger = 4;

    /// <summary>Packet offset of the left stick X word (sticks follow at +2 bytes each).</summary>
    private const int OffsetLeftStickX = 6;

    private static VirtualXbox360Controller CreateController(Xbox360Device device, RecordingDualSenseOutputs outputs) =>
        new VirtualXbox360Controller(device, outputs);

    private static ButtonMappingEntry Entry(string target, params string[] keys)
    {
        return new ButtonMappingEntry
        {
            Keys = [.. keys],
            Targets = [target]
        };
    }

    [Test]
    public void PushInput_EveryStickByte_MapsThroughTheAxisHelpers()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        VirtualXbox360Controller controller = CreateController(device, new RecordingDualSenseOutputs());

        for (int value = 0; value < 256; value++)
        {
            byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
            payload[0] = (byte)value;
            payload[1] = (byte)value;
            payload[2] = (byte)value;
            payload[3] = (byte)value;
            controller.PushInput(EmulationAdapterTestSupport.Report(payload));

            byte[] packet = EmulationAdapterTestSupport.Pull(device, 20);
            Assert.Multiple(() =>
            {
                Assert.That(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(OffsetLeftStickX)), Is.EqualTo(VirtualInputMapper.X360Axis((byte)value)));
                Assert.That(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(OffsetLeftStickX + 2)),
                    Is.EqualTo(VirtualInputMapper.X360AxisInverted((byte)value)));
                Assert.That(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(OffsetLeftStickX + 4)), Is.EqualTo(VirtualInputMapper.X360Axis((byte)value)));
                Assert.That(BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(OffsetLeftStickX + 6)),
                    Is.EqualTo(VirtualInputMapper.X360AxisInverted((byte)value)));
            });
        }
    }

    [TestCase(Xbox360Buttons.DPadUp)]
    [TestCase(Xbox360Buttons.DPadDown)]
    [TestCase(Xbox360Buttons.DPadLeft)]
    [TestCase(Xbox360Buttons.DPadRight)]
    [TestCase(Xbox360Buttons.Start)]
    [TestCase(Xbox360Buttons.Back)]
    [TestCase(Xbox360Buttons.LeftThumb)]
    [TestCase(Xbox360Buttons.RightThumb)]
    [TestCase(Xbox360Buttons.LeftShoulder)]
    [TestCase(Xbox360Buttons.RightShoulder)]
    [TestCase(Xbox360Buttons.Guide)]
    [TestCase(Xbox360Buttons.A)]
    [TestCase(Xbox360Buttons.B)]
    [TestCase(Xbox360Buttons.X)]
    [TestCase(Xbox360Buttons.Y)]
    public void PushInput_EveryMappedButtonBit_SetsOnlyItsWireBit(Xbox360Buttons target)
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        VirtualXbox360Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        controller.ButtonMappings = VirtualInputMapper.Xbox360Table([Entry(target.ToString(), "Cross")]);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[7] |= 0x20; // Cross is the only pressed source

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] packet = EmulationAdapterTestSupport.Pull(device, 20);
        Assert.Multiple(() =>
        {
            Assert.That(packet[0], Is.EqualTo(0x00));
            Assert.That(packet[1], Is.EqualTo(0x14));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(OffsetButtons)), Is.EqualTo((ushort)target));
            Assert.That(packet.AsSpan(14).ToArray(), Is.EqualTo(new byte[6]), "the packet tail stays zero");
        });
    }

    [Test]
    public void PushInput_RichReport_ProducesTheExpectedXInputPacket()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        VirtualXbox360Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 0; // X360Axis -> short.MinValue
        payload[1] = 0; // X360AxisInverted -> short.MaxValue
        payload[2] = 255; // X360Axis -> 32512
        payload[3] = 255; // X360AxisInverted -> -32512
        payload[4] = 200;
        payload[5] = 100;
        payload[7] = 0x08 | 0xF0; // every face button
        payload[8] = 0x01 | 0x02 | 0x40 | 0x80 | 0x10 | 0x20; // L1 R1 L3 R3 Create Options
        payload[9] = 0x01; // PS -> Guide

        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        byte[] expected = new byte[20];
        expected[0] = 0x00;
        expected[1] = 0x14;
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(OffsetButtons), 0xF7F0);
        expected[OffsetLeftTrigger] = 200;
        expected[OffsetLeftTrigger + 1] = 100;
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(OffsetLeftStickX), VirtualInputMapper.X360Axis(0));
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(OffsetLeftStickX + 2), VirtualInputMapper.X360AxisInverted(0));
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(OffsetLeftStickX + 4), VirtualInputMapper.X360Axis(255));
        BinaryPrimitives.WriteInt16LittleEndian(expected.AsSpan(OffsetLeftStickX + 6), VirtualInputMapper.X360AxisInverted(255));

        Assert.That(EmulationAdapterTestSupport.Pull(device, 20), Is.EqualTo(expected));
    }

    [Test]
    public void PushInput_AttachedRawImage_IsIgnored()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        VirtualXbox360Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] raw = EmulationAdapterTestSupport.RawReport(0x5A);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 128;
        payload[1] = 128;

        controller.PushInput(EmulationAdapterTestSupport.Report(payload, raw));

        byte[] packet = EmulationAdapterTestSupport.Pull(device, 20);
        Assert.Multiple(() =>
        {
            Assert.That(packet[0], Is.EqualTo(0x00));
            Assert.That(packet, Is.Not.EqualTo(raw.AsSpan(0, 20).ToArray()));
        });
    }

    [Test]
    public void PushBatteryAndConnectionStatus_AreNoOps()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        VirtualXbox360Controller controller = CreateController(device, new RecordingDualSenseOutputs());
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[4] = 120;
        controller.PushInput(EmulationAdapterTestSupport.Report(payload));
        byte[] before = EmulationAdapterTestSupport.Pull(device, 20);

        Assert.That(() => controller.PushBattery(new BatteryState(0x2A)), Throws.Nothing);
        Assert.That(() => controller.PushConnectionStatus(new ConnectionStatus(0xFF)), Throws.Nothing);
        byte[] after = EmulationAdapterTestSupport.Pull(device, 20);

        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public void Construction_ExposesXbox360Mode()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        VirtualXbox360Controller controller = CreateController(device, new RecordingDualSenseOutputs());

        Assert.That(controller.Mode, Is.EqualTo(EmulationMode.Xbox360));
    }

    [Test]
    public void Dispose_StopsPushesAndOutputDeliveryWithoutRemovingTheDevice()
    {
        Xbox360Device device = new Xbox360Device(new Xbox360Identity());
        RecordingDualSenseOutputs outputs = new RecordingDualSenseOutputs();
        VirtualXbox360Controller controller = CreateController(device, outputs);
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[4] = 200;
        controller.PushInput(EmulationAdapterTestSupport.Report(payload));

        controller.Dispose();
        byte[] ignored = EmulationAdapterTestSupport.NeutralPayload();
        ignored[4] = 10;
        controller.PushInput(EmulationAdapterTestSupport.Report(ignored));
        device.OnOutputReport(new byte[]
        {
            0x00, 0x08, 0x00, 0x11, 0x22, 0x00, 0x00, 0x00
        });

        byte[] packet = EmulationAdapterTestSupport.Pull(device, 20);
        Assert.Multiple(() =>
        {
            Assert.That(packet[OffsetLeftTrigger], Is.EqualTo(200), "a disposed adapter ignores later input pushes");
            Assert.That(outputs.Vibrations, Is.Empty);
            Assert.That(() => controller.Dispose(), Throws.Nothing);
        });
    }
}