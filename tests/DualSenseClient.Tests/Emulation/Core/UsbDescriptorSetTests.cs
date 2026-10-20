using System.Buffers.Binary;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

[TestFixture]
public sealed class UsbDescriptorSetTests
{
    [Test]
    public void Create_SimpleDevice_ParsesDeviceIdentifiers()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateSimpleDevice();

        Assert.That(descriptors.IdVendor, Is.EqualTo(TestDevices.SimpleIdVendor));
        Assert.That(descriptors.IdProduct, Is.EqualTo(TestDevices.SimpleIdProduct));
        Assert.That(descriptors.BcdDevice, Is.EqualTo(TestDevices.SimpleBcdDevice));
        Assert.That(descriptors.DeviceClass, Is.Zero);
        Assert.That(descriptors.DeviceSubClass, Is.Zero);
        Assert.That(descriptors.DeviceProtocol, Is.Zero);
        Assert.That(descriptors.NumConfigurations, Is.EqualTo((byte)1));
        Assert.That(descriptors.ConfigurationValue, Is.EqualTo(TestDevices.SimpleConfigurationValue));
    }

    [Test]
    public void Create_SimpleDevice_ExposesDescriptorBlobs()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateSimpleDevice();

        Assert.That(descriptors.DeviceDescriptor.ToArray(), Is.EqualTo(TestDevices.CreateSimpleDeviceDescriptor()));
        Assert.That(descriptors.ConfigDescriptor.ToArray(), Is.EqualTo(TestDevices.CreateSimpleConfigDescriptor()));
        Assert.That(descriptors.HidReportDescriptor, Is.Not.Null);
        Assert.That(descriptors.HidReportDescriptor.Value.ToArray(), Is.EqualTo(TestDevices.CreateSimpleHidReportDescriptor()));
    }

    [TestCase(0x00, UsbEndpointType.Control)]
    [TestCase(0x01, UsbEndpointType.Isochronous)]
    [TestCase(0x02, UsbEndpointType.Bulk)]
    [TestCase(0x03, UsbEndpointType.Interrupt)]
    [TestCase(0x0D, UsbEndpointType.Isochronous)]
    public void UsbEndpointInfo_Type_DecodesTransferTypeBits(byte attributes, UsbEndpointType expected)
    {
        UsbEndpointInfo endpoint = new UsbEndpointInfo
        {
            Attributes = attributes
        };

        Assert.That(endpoint.Type, Is.EqualTo(expected));
    }

    [Test]
    public void Create_SimpleDevice_ParsesHidInterface()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateSimpleDevice();

        Assert.That(descriptors.Interfaces, Has.Count.EqualTo(1));
        UsbInterfaceInfo interfaceInfo = descriptors.Interfaces[0];
        Assert.That(interfaceInfo.Number, Is.Zero);
        Assert.That(interfaceInfo.Class, Is.EqualTo(TestDevices.SimpleInterfaceClass));
        Assert.That(interfaceInfo.SubClass, Is.Zero);
        Assert.That(interfaceInfo.Protocol, Is.Zero);
    }

    [Test]
    public void Create_SimpleDevice_ParsesInterruptEndpointPair()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateSimpleDevice();
        IReadOnlyList<UsbEndpointInfo> endpoints = descriptors.Interfaces[0].Endpoints;

        Assert.That(endpoints, Has.Count.EqualTo(2));

        UsbEndpointInfo interruptIn = endpoints[0];
        Assert.That(interruptIn.Address, Is.EqualTo(TestDevices.SimpleInterruptInAddress));
        Assert.That(interruptIn.Attributes, Is.EqualTo((byte)0x03));
        Assert.That(interruptIn.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(interruptIn.MaxPacketSize, Is.EqualTo(TestDevices.SimpleInterruptInPacketSize));
        Assert.That(interruptIn.Interval, Is.EqualTo(TestDevices.SimpleInterruptInInterval));

        UsbEndpointInfo interruptOut = endpoints[1];
        Assert.That(interruptOut.Address, Is.EqualTo(TestDevices.SimpleInterruptOutAddress));
        Assert.That(interruptOut.Attributes, Is.EqualTo((byte)0x03));
        Assert.That(interruptOut.Type, Is.EqualTo(UsbEndpointType.Interrupt));
        Assert.That(interruptOut.MaxPacketSize, Is.EqualTo(TestDevices.SimpleInterruptOutPacketSize));
        Assert.That(interruptOut.Interval, Is.EqualTo(TestDevices.SimpleInterruptOutInterval));
    }

    [Test]
    public void WalkConfiguration_TwoAlternates_CollectsBothAndTheirEndpoints()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateAlternateSettingDevice();

        Assert.That(descriptors.Interfaces, Has.Count.EqualTo(2));

        UsbInterfaceInfo defaultSetting = descriptors.Interfaces[0];
        Assert.That(defaultSetting.Number, Is.Zero);
        Assert.That(defaultSetting.AlternateSetting, Is.Zero);
        Assert.That(defaultSetting.Endpoints, Has.Count.EqualTo(2));
        Assert.That(defaultSetting.Endpoints[0].Address, Is.EqualTo(TestDevices.SimpleInterruptInAddress));
        Assert.That(defaultSetting.Endpoints[1].Address, Is.EqualTo(TestDevices.SimpleInterruptOutAddress));

        UsbInterfaceInfo alternateSetting = descriptors.Interfaces[1];
        Assert.That(alternateSetting.Number, Is.Zero);
        Assert.That(alternateSetting.AlternateSetting, Is.EqualTo((byte)1));
        Assert.That(alternateSetting.Class, Is.EqualTo(TestDevices.SimpleInterfaceClass));
        Assert.That(alternateSetting.Endpoints, Has.Count.EqualTo(1));
        Assert.That(alternateSetting.Endpoints[0].Address, Is.EqualTo(TestDevices.AlternateInterruptInAddress));

        Assert.That(descriptors.InterfaceNumbers, Is.EqualTo(new byte[]
        {
            0
        }));
        Assert.That(descriptors.InterfaceCount, Is.EqualTo(1));
        Assert.That(descriptors.HasInterface(0), Is.True);
        Assert.That(descriptors.HasInterface(1), Is.False);
        Assert.That(descriptors.GetAlternates(0), Has.Count.EqualTo(2));
    }

    [Test]
    public void WalkConfiguration_AltInterfaceWithoutAltZero_Throws()
    {
        byte[] configDescriptor = TestDevices.CreateAlternateSettingConfigDescriptor();

        // The first interface descriptor (offset 9) declares bAlternateSetting 1 as well, so
        // interface number 0 never declares the mandatory alternate setting 0.
        configDescriptor[12] = 0x01;

        Assert.That(
            () => CreateSimpleDeviceWithConfig(configDescriptor),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void GetAlternates_UnknownInterface_ReturnsEmpty()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateAlternateSettingDevice();

        Assert.That(descriptors.GetAlternates(5), Is.Empty);
    }

    [Test]
    public void TryGetInterface_KnownAlt_ReturnsInterface()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateAlternateSettingDevice();

        bool found = descriptors.TryGetInterface(0, 1, out UsbInterfaceInfo interfaceInfo);

        Assert.That(found, Is.True);
        Assert.That(interfaceInfo.Number, Is.Zero);
        Assert.That(interfaceInfo.AlternateSetting, Is.EqualTo((byte)1));
        Assert.That(interfaceInfo.Endpoints, Has.Count.EqualTo(1));
        Assert.That(interfaceInfo.Endpoints[0].Address, Is.EqualTo(TestDevices.AlternateInterruptInAddress));
        Assert.That(descriptors.TryGetInterface(0, 2, out _), Is.False);
    }

    [Test]
    public void TryGetString_KnownIndexes_ReturnsConfiguredStrings()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateSimpleDevice();

        Assert.That(descriptors.TryGetString(1, out string manufacturer), Is.True);
        Assert.That(manufacturer, Is.EqualTo(TestDevices.SimpleManufacturer));
        Assert.That(descriptors.TryGetString(2, out string product), Is.True);
        Assert.That(product, Is.EqualTo(TestDevices.SimpleProduct));
        Assert.That(descriptors.TryGetString(3, out string serialNumber), Is.True);
        Assert.That(serialNumber, Is.EqualTo(TestDevices.SimpleSerialNumber));
    }

    [Test]
    public void TryGetString_UnknownIndexes_ReturnsFalseAndEmpty()
    {
        UsbDescriptorSet descriptors = TestDevices.CreateSimpleDevice();

        Assert.That(descriptors.TryGetString(0, out string language), Is.False);
        Assert.That(language, Is.Empty);
        Assert.That(descriptors.TryGetString(4, out string beyondEnd), Is.False);
        Assert.That(beyondEnd, Is.Empty);
        Assert.That(descriptors.TryGetString(int.MaxValue, out string overflow), Is.False);
        Assert.That(overflow, Is.Empty);
    }

    [Test]
    public void Create_WithoutHidReportDescriptor_ExposesNull()
    {
        UsbDescriptorSet descriptors = UsbDescriptorSet.Create(
            TestDevices.CreateSimpleDeviceDescriptor(),
            TestDevices.CreateSimpleConfigDescriptor(),
            null,
            TestDevices.CreateSimpleStrings());

        Assert.That(descriptors.HidReportDescriptor, Is.Null);
    }

    [Test]
    public void Create_TruncatedConfigDescriptor_ThrowsInvalidDataException()
    {
        byte[] configDescriptor = TestDevices.CreateSimpleConfigDescriptor();

        // Cut inside the last endpoint so the blob is shorter than its declared wTotalLength.
        Assert.That(
            () => CreateSimpleDeviceWithConfig(configDescriptor[..^4]),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Create_ConfigDescriptorWithZeroLengthDescriptor_ThrowsInvalidDataException()
    {
        byte[] configDescriptor = TestDevices.CreateSimpleConfigDescriptor();
        configDescriptor[9] = 0x00; // bLength of the interface descriptor

        Assert.That(
            () => CreateSimpleDeviceWithConfig(configDescriptor),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Create_ConfigDescriptorWithOverrunningDescriptor_ThrowsInvalidDataException()
    {
        byte[] configDescriptor = TestDevices.CreateSimpleConfigDescriptor();
        configDescriptor[9] = 0x30; // bLength 48 exceeds the 32 bytes left before wTotalLength

        Assert.That(
            () => CreateSimpleDeviceWithConfig(configDescriptor),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Create_ConfigDescriptorMissingTerminator_ThrowsInvalidDataException()
    {
        byte[] configDescriptor = TestDevices.CreateSimpleConfigDescriptor();
        byte[] withoutTerminator = [.. configDescriptor, 0x00, 0x00]; // two bytes no descriptor covers
        BinaryPrimitives.WriteUInt16LittleEndian(withoutTerminator.AsSpan(2), 0x002B); // wTotalLength: 43

        Assert.That(
            () => CreateSimpleDeviceWithConfig(withoutTerminator),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Create_ConfigDescriptorWithEndpointBeforeInterface_ThrowsInvalidDataException()
    {
        byte[] configDescriptor = TestDevices.CreateSimpleConfigDescriptor();
        configDescriptor[10] = 0x05; // interface descriptor type -> endpoint descriptor

        Assert.That(
            () => CreateSimpleDeviceWithConfig(configDescriptor),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Create_TruncatedDeviceDescriptor_ThrowsInvalidDataException()
    {
        byte[] deviceDescriptor = TestDevices.CreateSimpleDeviceDescriptor();

        Assert.That(
            () => CreateSimpleDevice(
                deviceDescriptor[..17], TestDevices.CreateSimpleConfigDescriptor()),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Create_DeviceDescriptorWithBadLength_ThrowsInvalidDataException()
    {
        byte[] deviceDescriptor = TestDevices.CreateSimpleDeviceDescriptor();
        deviceDescriptor[0] = 0x10; // bLength: 16 instead of 18

        Assert.That(
            () => CreateSimpleDevice(deviceDescriptor, TestDevices.CreateSimpleConfigDescriptor()),
            Throws.TypeOf<InvalidDataException>());
    }

    private static UsbDescriptorSet CreateSimpleDeviceWithConfig(byte[] configDescriptor) =>
        CreateSimpleDevice(TestDevices.CreateSimpleDeviceDescriptor(), configDescriptor);

    private static UsbDescriptorSet CreateSimpleDevice(byte[] deviceDescriptor, byte[] configDescriptor)
    {
        return UsbDescriptorSet.Create(
            deviceDescriptor,
            configDescriptor,
            TestDevices.CreateSimpleHidReportDescriptor(),
            TestDevices.CreateSimpleStrings());
    }
}