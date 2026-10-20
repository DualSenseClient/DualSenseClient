using System.Buffers.Binary;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.Emulation.Devices.Xbox360;

namespace DualSenseClient.Tests.Emulation.Conformance;

/// <summary>
/// Persona conformance sweep across all five M1 personas (DualShock 4 v2 and v1, DualSense, DualSense
/// Edge and Xbox 360): one parameterized fixture that pins the invariants every persona must satisfy
/// regardless of its family. Every case constructs a fresh device and drives only the type-agnostic
/// <see cref="VirtualUsbDevice"/> seams (<see cref="VirtualUsbDevice.Descriptors"/>,
/// <see cref="VirtualUsbDevice.ProvideInputReport"/>, <see cref="VirtualUsbDevice.HandleControlRequest"/>
/// and <see cref="VirtualUsbDevice.OnOutputReport"/>), so the sweep observes the served bytes instead
/// of any persona's internal helpers and never depends on the per-persona fixtures.
/// The sweep covers: the device descriptor copy (bLength/type, identity, single configuration), the
/// configuration blob (a raw descriptor walk that must land exactly on the declared wTotalLength,
/// the declared interface count, alternate setting 0 for every interface number, and endpoint
/// address uniqueness per interface alternate setting), the HID
/// report descriptor declaration (<c>wDescriptorLength</c> in the HID class descriptor equals the
/// served bytes, and GET_DESCRIPTOR(REPORT) serves exactly them; the vendor-class Xbox 360 persona
/// declares and serves none), the input report size (64/64/64/64/20), the feature surface (the
/// DualSense personas answer every feature report id their HID report descriptor declares at the
/// declared length and stall an undeclared id; the DualShock 4 personas serve their listed anchors
/// and stall the rest of their declared surface; the Xbox 360 persona has no HID class path at
/// all), and output-report tolerance (a known id-prefixed payload raises the persona's
/// event exactly once, while an unknown id or a truncated payload raises nothing and never throws).
/// The feature ids and their declared sizes are proven from the descriptor bytes with a small local
/// HID item walker (report-type Feature main items attributed to the current Report ID/Size/Count
/// globals), not from a transcribed list.
/// </summary>
[TestFixture]
public sealed class PersonaConformanceTests
{
    /// <summary>Value returned for a stalled (unsupported) control request (STALL).</summary>
    private const int Stall = -1;

    /// <summary>Standard request code of GET_DESCRIPTOR (USB 2.0 specification, section 9.4.3).</summary>
    private const byte RequestGetDescriptor = 0x06;

    /// <summary>HID class request code of GET_REPORT (HID 1.11, section 7.2.1).</summary>
    private const byte RequestGetReport = 0x01;

    /// <summary>Device-to-host standard interface request (direction IN, type standard, recipient interface).</summary>
    private const byte StandardInterfaceIn = 0x81;

    /// <summary>Device-to-host class interface request (direction IN, type class, recipient interface).</summary>
    private const byte ClassInterfaceIn = 0xA1;

    /// <summary>Report type value of a feature report in wValue's high byte.</summary>
    private const byte ReportTypeFeature = 0x03;

    /// <summary>wValue of a GET_DESCRIPTOR(REPORT, index 0) request for the HID report descriptor.</summary>
    private const ushort HidReportDescriptorValue = 0x2200;

    /// <summary>Undeclared feature report probe: the DS4 pairing-info id, which no DualSense descriptor declares.</summary>
    private const byte UndeclaredFeatureReportId = 0x12;

    /// <summary>Size in bytes of the device descriptor (USB 2.0 specification, section 9.6.1).</summary>
    private const int DeviceDescriptorSize = 18;

    /// <summary>Size in bytes of the configuration descriptor header (USB 2.0 specification, section 9.6.3).</summary>
    private const int ConfigDescriptorSize = 9;

    /// <summary>Size in bytes of an endpoint descriptor (USB 2.0 specification, section 9.6.6).</summary>
    private const int EndpointDescriptorSize = 7;

    /// <summary>Size in bytes of an interface descriptor (USB 2.0 specification, section 9.6.5).</summary>
    private const int InterfaceDescriptorSize = 9;

    /// <summary>Size in bytes of a HID class descriptor (USB HID 1.11, section 6.2.1).</summary>
    private const int HidClassDescriptorSize = 9;

    /// <summary>Size in bytes of the bLength/bDescriptorType header every descriptor starts with.</summary>
    private const int DescriptorHeaderSize = 2;

    /// <summary>Offset of wTotalLength inside the configuration descriptor.</summary>
    private const int ConfigTotalLengthOffset = 2;

    /// <summary>Offset of bNumInterfaces inside the configuration descriptor.</summary>
    private const int ConfigNumInterfacesOffset = 4;

    /// <summary>Offset of idVendor inside the device descriptor.</summary>
    private const int DeviceVendorOffset = 8;

    /// <summary>Offset of idProduct inside the device descriptor.</summary>
    private const int DeviceProductOffset = 10;

    /// <summary>Offset of wDescriptorLength inside a HID class descriptor (after bcdHID, bCountryCode, bNumDescriptors and bDescriptorType).</summary>
    private const int HidClassReportLengthOffset = 7;

    /// <summary>Descriptor type value of the device descriptor (DEVICE).</summary>
    private const byte DescriptorTypeDevice = 0x01;

    /// <summary>Descriptor type value of the configuration descriptor (CONFIGURATION).</summary>
    private const byte DescriptorTypeConfiguration = 0x02;

    /// <summary>Descriptor type value of an interface descriptor (INTERFACE).</summary>
    private const byte DescriptorTypeInterface = 0x04;

    /// <summary>Descriptor type value of an endpoint descriptor (ENDPOINT).</summary>
    private const byte DescriptorTypeEndpoint = 0x05;

    /// <summary>Descriptor type value of a HID class descriptor (HID).</summary>
    private const byte DescriptorTypeHid = 0x21;

    /// <summary>HID item type of a Main item (prefix bits 3..2 = 0b00).</summary>
    private const int HidMainItemType = 0;

    /// <summary>HID item type of a Global item (prefix bits 3..2 = 0b01).</summary>
    private const int HidGlobalItemType = 1;

    /// <summary>HID Main item tag of a Feature item (0b1011).</summary>
    private const int HidFeatureTag = 11;

    /// <summary>HID Global item tag of Report Size (0b0111).</summary>
    private const int HidReportSizeTag = 7;

    /// <summary>HID Global item tag of Report ID (0b1000).</summary>
    private const int HidReportIdTag = 8;

    /// <summary>HID Global item tag of Report Count (0b1001).</summary>
    private const int HidReportCountTag = 9;

    /// <summary>Prefix of a HID long item; the persona descriptors use none.</summary>
    private const byte HidLongItemPrefix = 0xFE;

    /// <summary>
    /// The five M1 personas the sweep runs over, with the facts the cases assert. Fresh prototypes:
    /// every test constructs its own device from <see cref="Persona.Create"/> so no counter, state
    /// or subscription leaks between cases.
    /// </summary>
    private static readonly Persona[] Personas =
    [
        new Persona("DualShock 4 v2", 0x054C, 0x09CC, 64, OutputKind.DualShock4, FeatureExpectation.DeclaredAnchors)
        {
            Create = () => new DualShock4Device(new DualShock4Identity(), null, DualShock4Variant.V2),
            FeatureAnchors =
            [
                (0x02, 37), // calibration (Report Count 36)
                (0x12, 16), // pairing (Report Count 15)
                (0x81, 7), // controller MAC (Report Count 6)
                (0xA3, 49) // firmware (Report Count 48)
            ]
        },
        new Persona("DualShock 4 v1", 0x054C, 0x05C4, 64, OutputKind.DualShock4, FeatureExpectation.DeclaredAnchors)
        {
            Create = () => new DualShock4Device(new DualShock4Identity(), null, DualShock4Variant.V1),
            FeatureAnchors =
            [
                (0x02, 37), // calibration (Report Count 36)
                (0x12, 16), // pairing (Report Count 15)
                (0x81, 7), // controller MAC (Report Count 6)
                (0xA3, 49) // firmware (Report Count 48)
            ]
        },
        new Persona("DualSense", 0x054C, 0x0CE6, 64, OutputKind.DualSense, FeatureExpectation.DeclaredTable)
        {
            Create = () => new DualSenseDevice(new DualSenseIdentity()),
            FeatureAnchors =
            [
                (0x05, 41), // calibration (Report Count 40)
                (0x09, 20), // pairing (Report Count 19)
                (0x20, 64), // firmware (Report Count 63)
                (0x81, 64), // command response (Report Count 63)
                (0xF2, 16) // lightbar setup (Report Count 15)
            ]
        },
        new Persona("DualSense Edge", 0x054C, 0x0DF2, 64, OutputKind.DualSense, FeatureExpectation.DeclaredTable)
        {
            Create = () => new DualSenseDevice(new DualSenseIdentity(), true),
            FeatureAnchors =
            [
                (0x05, 41), // calibration (Report Count 40)
                (0x09, 20), // pairing (Report Count 19)
                (0x20, 64), // firmware (Report Count 63)
                (0x65, 64), // Edge handshake (Report Count 63)
                (0x81, 64), // command response (Report Count 63)
                (0xF2, 53) // lightbar setup widened to Report Count 52
            ]
        },
        new Persona("Xbox 360", 0x045E, 0x028E, 20, OutputKind.Xbox360, FeatureExpectation.NonHid)
        {
            Create = () => new Xbox360Device(new Xbox360Identity())
        }
    ];

    /// <summary>Cases of every persona.</summary>
    private static IEnumerable<TestCaseData> AllPersonaCases()
    {
        foreach (Persona persona in Personas)
        {
            yield return new TestCaseData(persona).SetArgDisplayNames(persona.Name);
        }
    }

    /// <summary>Cases of the personas whose feature surface is served from their descriptor's declared table.</summary>
    private static IEnumerable<TestCaseData> TablePersonaCases()
    {
        foreach (Persona persona in Personas)
        {
            if (persona.FeatureExpectation == FeatureExpectation.DeclaredTable)
            {
                yield return new TestCaseData(persona).SetArgDisplayNames(persona.Name);
            }
        }
    }

    /// <summary>Cases of the personas that declare a HID feature surface and serve its listed anchors.</summary>
    private static IEnumerable<TestCaseData> AnchorPersonaCases()
    {
        foreach (Persona persona in Personas)
        {
            if (persona.FeatureExpectation == FeatureExpectation.DeclaredAnchors)
            {
                yield return new TestCaseData(persona).SetArgDisplayNames(persona.Name);
            }
        }
    }

    /// <summary>Cases of the vendor-class persona without any HID class path.</summary>
    private static IEnumerable<TestCaseData> NonHidPersonaCases()
    {
        foreach (Persona persona in Personas)
        {
            if (persona.FeatureExpectation == FeatureExpectation.NonHid)
            {
                yield return new TestCaseData(persona).SetArgDisplayNames(persona.Name);
            }
        }
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void DescriptorSet_DeviceIdentity_MatchesThePersona(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();

        UsbDescriptorSet descriptors = device.Descriptors;
        Assert.That(descriptors, Is.Not.Null);

        ReadOnlySpan<byte> deviceDescriptor = descriptors.DeviceDescriptor.Span;
        Assert.That(deviceDescriptor.Length, Is.EqualTo(DeviceDescriptorSize));
        Assert.That(deviceDescriptor[0], Is.EqualTo(DeviceDescriptorSize)); // bLength
        Assert.That(deviceDescriptor[1], Is.EqualTo(DescriptorTypeDevice));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(deviceDescriptor[DeviceVendorOffset..]), Is.EqualTo(persona.IdVendor));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(deviceDescriptor[DeviceProductOffset..]), Is.EqualTo(persona.IdProduct));
        Assert.That(descriptors.IdVendor, Is.EqualTo(persona.IdVendor));
        Assert.That(descriptors.IdProduct, Is.EqualTo(persona.IdProduct));
        Assert.That(descriptors.NumConfigurations, Is.EqualTo((byte)1));
        Assert.That(descriptors.ConfigurationValue, Is.EqualTo((byte)1));
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void DescriptorSet_ConfigBlob_WalksExactlyToItsDeclaredTotalLength(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        ReadOnlySpan<byte> config = device.Descriptors.ConfigDescriptor.Span;

        Assert.That(config.Length, Is.GreaterThanOrEqualTo(ConfigDescriptorSize));
        Assert.That(config[0], Is.EqualTo(ConfigDescriptorSize)); // bLength
        Assert.That(config[1], Is.EqualTo(DescriptorTypeConfiguration));
        int declaredTotalLength = BinaryPrimitives.ReadUInt16LittleEndian(config[ConfigTotalLengthOffset..]);
        Assert.That(declaredTotalLength, Is.EqualTo(config.Length)); // the served blob is exactly wTotalLength, no slack

        List<RawDescriptor> chain = WalkDescriptorChain(config);
        Assert.That(chain, Has.Count.GreaterThanOrEqualTo(3)); // configuration + interface(s) + endpoints

        // Every distinct interface number must declare alternate setting 0, and the configuration's
        // bNumInterfaces counts the distinct interface numbers rather than the interface descriptors
        // (an interface may declare several alternate settings).
        HashSet<byte> interfaceNumbers = [];
        HashSet<byte> numbersWithAltZero = [];
        int currentNumber = -1;
        int currentAlternateSetting = -1;
        List<(byte InterfaceNumber, byte AlternateSetting, byte Address)> endpointAddresses = [];
        List<byte> altZeroEndpointAddresses = [];
        foreach (RawDescriptor descriptor in chain)
        {
            if (descriptor.Type == DescriptorTypeInterface)
            {
                Assert.That(descriptor.Length, Is.GreaterThanOrEqualTo(InterfaceDescriptorSize), $"interface at offset {descriptor.Offset} is too short");
                currentNumber = config[descriptor.Offset + 2];
                currentAlternateSetting = config[descriptor.Offset + 3];
                interfaceNumbers.Add((byte)currentNumber);
                if (currentAlternateSetting == 0)
                {
                    numbersWithAltZero.Add((byte)currentNumber);
                }
            }
            else if (descriptor.Type == DescriptorTypeEndpoint)
            {
                Assert.That(descriptor.Length, Is.GreaterThanOrEqualTo(EndpointDescriptorSize), $"endpoint at offset {descriptor.Offset} is too short");
                Assert.That(currentNumber, Is.GreaterThanOrEqualTo(0), $"endpoint at offset {descriptor.Offset} appears before any interface");
                byte address = config[descriptor.Offset + 2];
                endpointAddresses.Add(((byte)currentNumber, (byte)currentAlternateSetting, address));
                if (currentAlternateSetting == 0)
                {
                    altZeroEndpointAddresses.Add(address);
                }
            }
        }

        Assert.That(interfaceNumbers, Is.Not.Empty);
        Assert.That(interfaceNumbers, Is.SubsetOf(numbersWithAltZero), "every interface number must declare alternate setting 0");
        Assert.That(config[ConfigNumInterfacesOffset], Is.EqualTo((byte)interfaceNumbers.Count));

        // Endpoint addresses are unique per interface number and alternate setting: two endpoints of
        // the same active setting sharing an address would make the configuration ambiguous.
        Assert.That(endpointAddresses, Is.Not.Empty);
        Assert.That(endpointAddresses, Is.Unique);

        // All alternate setting 0 interfaces are active simultaneously, so an address reused across
        // interface numbers would make TryResolveEndpoint ambiguous. Only mutually exclusive
        // alternate settings may reuse an address.
        Assert.That(altZeroEndpointAddresses, Is.Not.Empty);
        Assert.That(altZeroEndpointAddresses, Is.Unique);
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void DescriptorSet_HidReportDescriptor_MatchesItsDeclaredLength(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        UsbDescriptorSet descriptors = device.Descriptors;
        ReadOnlyMemory<byte>? hidReportDescriptor = descriptors.HidReportDescriptor;

        if (hidReportDescriptor is null)
        {
            // The Xbox 360 persona is vendor-class, not HID: no report descriptor exists, so both
            // the standard descriptor path and the HID class path stall.
            Assert.That(persona.FeatureExpectation, Is.EqualTo(FeatureExpectation.NonHid));
            Assert.That(Send(device, StandardInterfaceIn, RequestGetDescriptor, HidReportDescriptorValue, 64).Count, Is.EqualTo(Stall));
            Assert.That(Send(device, ClassInterfaceIn, RequestGetReport, (ReportTypeFeature << 8) | 0x01, 8).Count, Is.EqualTo(Stall));
            return;
        }

        // The HID class descriptor of the configuration blob declares wDescriptorLength ...
        List<RawDescriptor> chain = WalkDescriptorChain(descriptors.ConfigDescriptor.Span);
        RawDescriptor hidClassDescriptor = chain.Single(descriptor => descriptor.Type == DescriptorTypeHid);
        Assert.That(hidClassDescriptor.Length, Is.EqualTo(HidClassDescriptorSize));
        int declaredLength =
            BinaryPrimitives.ReadUInt16LittleEndian(descriptors.ConfigDescriptor.Span[(hidClassDescriptor.Offset + HidClassReportLengthOffset)..]);
        Assert.That(declaredLength, Is.EqualTo(hidReportDescriptor.Value.Length));

        // ... and the engine serves exactly those bytes for GET_DESCRIPTOR(REPORT, interface 0).
        ControlResult served = Send(device, StandardInterfaceIn, RequestGetDescriptor, HidReportDescriptorValue, (ushort)declaredLength);
        Assert.That(served.Count, Is.EqualTo(declaredLength));
        Assert.That(served.Data, Is.EqualTo(hidReportDescriptor.Value.ToArray()));
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void InputReport_IsServedAtThePersonaSize(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        byte[] oversized = new byte[persona.InputReportSize + 64];

        int count = device.ProvideInputReport(oversized);

        Assert.That(count, Is.EqualTo(persona.InputReportSize));
    }

    [TestCaseSource(nameof(TablePersonaCases))]
    public void FeatureReports_DeclaredSurfaceServesExactlyTheDescriptorLengths(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        List<FeatureReport> declared = ExtractFeatureReports(device.Descriptors.HidReportDescriptor!.Value.Span);
        Assert.That(declared, Is.Not.Empty);

        // Every feature report id the HID report descriptor declares answers at the length the
        // descriptor declares (report id byte plus the payload bits), with the report id first. The
        // requests ask for more than the declared size, so a served length equal to the declared
        // size cannot be a clamp of a larger body.
        foreach (FeatureReport feature in declared)
        {
            ControlResult result = GetFeature(device, feature.ReportId, feature.Size + 32);
            Assert.That(result.Count, Is.EqualTo(feature.Size), $"feature report 0x{feature.ReportId:X2} served length");
            Assert.That(result.Data[0], Is.EqualTo(feature.ReportId), $"feature report 0x{feature.ReportId:X2} report id byte");
        }

        // The anchors (ids with a body the persona fills, including the Edge-widened 0xF2) agree
        // with the lengths the persona's own feature-report tests pin.
        foreach ((byte reportId, int anchorSize) in persona.FeatureAnchors)
        {
            FeatureReport feature = declared.Single(report => report.ReportId == reportId);
            Assert.That(feature.Size, Is.EqualTo(anchorSize), $"anchor 0x{reportId:X2} declared size");
            Assert.That(GetFeature(device, reportId, anchorSize + 32).Count, Is.EqualTo(anchorSize), $"anchor 0x{reportId:X2} served length");
        }
    }

    [TestCaseSource(nameof(TablePersonaCases))]
    public void FeatureReports_UndeclaredReportIdStalls(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        List<FeatureReport> declared = ExtractFeatureReports(device.Descriptors.HidReportDescriptor!.Value.Span);

        // 0x12 is the DS4 pairing-info id, which no DualSense descriptor declares; the walk below
        // proves the probe id really is undeclared instead of assuming it.
        Assert.That(declared.Any(feature => feature.ReportId == UndeclaredFeatureReportId), Is.False);
        ControlResult result = GetFeature(device, UndeclaredFeatureReportId, 64);
        Assert.That(result.Count, Is.EqualTo(Stall));
    }

    [TestCaseSource(nameof(AnchorPersonaCases))]
    public void FeatureReports_DeclaredSurfaceServesTheAnchorsAndStallsTheRest(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        List<FeatureReport> declared = ExtractFeatureReports(device.Descriptors.HidReportDescriptor!.Value.Span);
        Assert.That(declared, Is.Not.Empty);
        Assert.That(persona.FeatureAnchors, Is.Not.Empty);

        foreach (FeatureReport feature in declared)
        {
            if (persona.FeatureAnchors.Any(anchor => anchor.ReportId == feature.ReportId))
            {
                continue;
            }

            ControlResult result = GetFeature(device, feature.ReportId, feature.Size);
            Assert.That(result.Count, Is.EqualTo(Stall), $"feature report 0x{feature.ReportId:X2}");
        }

        foreach ((byte reportId, int size) in persona.FeatureAnchors)
        {
            FeatureReport feature = declared.Single(item => item.ReportId == reportId);
            Assert.That(feature.Size, Is.EqualTo(size), $"anchor 0x{reportId:X2} declared size");
            ControlResult result = GetFeature(device, reportId, size + 32);
            Assert.That(result.Count, Is.EqualTo(size), $"anchor 0x{reportId:X2} served length");
            Assert.That(result.Data[0], Is.EqualTo(reportId), $"anchor 0x{reportId:X2} report id byte");
        }

        Assert.That(GetFeature(device, 0x99, 64).Count, Is.EqualTo(Stall));
    }

    [TestCaseSource(nameof(NonHidPersonaCases))]
    public void FeatureReports_Xbox360HasNoClassFeaturePath(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();

        // The persona is vendor-class, not HID: it declares no report descriptor and the engine
        // stalls the HID class GET_REPORT probe outright.
        Assert.That(device.Descriptors.HidReportDescriptor, Is.Null);
        ControlResult result = GetFeature(device, 0x01, 8);
        Assert.That(result.Count, Is.EqualTo(Stall));
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void OutputReports_KnownPayloadRaisesTheEventExactlyOnce(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        int received = 0;
        SubscribeOutput(device, () => received++);

        byte[] payload = CreateKnownOutputPayload(persona.OutputKind);
        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(received, Is.EqualTo(1));
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void OutputReports_UnknownReportIdRaisesNothing(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        int received = 0;
        SubscribeOutput(device, () => received++);

        byte[] payload = CreateUnknownOutputPayload(persona.OutputKind);
        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(received, Is.Zero);
    }

    [TestCaseSource(nameof(AllPersonaCases))]
    public void OutputReports_TruncatedPayloadRaisesNothing(Persona persona)
    {
        VirtualUsbDevice device = persona.Create();
        int received = 0;
        SubscribeOutput(device, () => received++);

        byte[] payload = CreateTruncatedOutputPayload(persona.OutputKind);
        Assert.That(() => device.OnOutputReport(payload), Throws.Nothing);

        Assert.That(received, Is.Zero);
    }

    /// <summary>
    /// Walks a raw USB descriptor chain (bLength/bDescriptorType headers) and returns every
    /// descriptor; the walk asserts each declared length fits the remaining bytes and the chain
    /// lands exactly on the end of <paramref name="blob"/>.
    /// </summary>
    /// <param name="blob">Raw descriptor blob to walk.</param>
    /// <returns>Every descriptor of the chain in order.</returns>
    private static List<RawDescriptor> WalkDescriptorChain(ReadOnlySpan<byte> blob)
    {
        List<RawDescriptor> chain = [];
        int offset = 0;
        while (offset < blob.Length)
        {
            Assert.That(blob.Length - offset, Is.GreaterThanOrEqualTo(DescriptorHeaderSize), $"descriptor at offset {offset} has no header");
            int length = blob[offset];
            Assert.That(length, Is.GreaterThanOrEqualTo(DescriptorHeaderSize), $"descriptor at offset {offset} declares bLength {length}");
            Assert.That(length, Is.LessThanOrEqualTo(blob.Length - offset), $"descriptor at offset {offset} overruns the blob");
            chain.Add(new RawDescriptor(offset, (byte)length, blob[offset + 1]));
            offset += length;
        }

        return chain;
    }

    /// <summary>
    /// Extracts the feature reports a raw HID report descriptor declares: a local walk of the HID
    /// item stream that tracks the current Report ID, Report Size and Report Count Global items and
    /// records one entry per report-type Feature Main item (tag 0b1011) with the total report
    /// length, the report id byte plus the payload bits the descriptor declares. Long items and
    /// item overruns are rejected so a mis-walked descriptor cannot pass silently.
    /// </summary>
    /// <param name="hidReportDescriptor">Raw HID report descriptor bytes.</param>
    /// <returns>The declared feature reports in descriptor order.</returns>
    private static List<FeatureReport> ExtractFeatureReports(ReadOnlySpan<byte> hidReportDescriptor)
    {
        List<FeatureReport> features = [];
        int reportId = 0;
        int reportSizeBits = 0;
        int reportCount = 0;
        int offset = 0;
        while (offset < hidReportDescriptor.Length)
        {
            byte prefix = hidReportDescriptor[offset];
            Assert.That(prefix, Is.Not.EqualTo(HidLongItemPrefix), $"long HID item at offset {offset}");
            int dataBytes = prefix & 0x03;
            if (dataBytes == 3)
            {
                dataBytes = 4;
            }

            Assert.That(offset + 1 + dataBytes, Is.LessThanOrEqualTo(hidReportDescriptor.Length),
                $"HID item at offset {offset} overruns the report descriptor");

            int itemType = (prefix >> 2) & 0x03;
            int itemTag = (prefix >> 4) & 0x0F;
            int value = ReadHidItemValue(hidReportDescriptor.Slice(offset + 1, dataBytes));
            if (itemType == HidGlobalItemType)
            {
                switch (itemTag)
                {
                    case HidReportIdTag:
                        reportId = value;
                        break;

                    case HidReportSizeTag:
                        reportSizeBits = value;
                        break;

                    case HidReportCountTag:
                        reportCount = value;
                        break;
                }
            }
            else if (itemType == HidMainItemType && itemTag == HidFeatureTag)
            {
                Assert.That(reportId, Is.Not.Zero, $"feature block at offset {offset} declares no report id");
                Assert.That(reportSizeBits > 0 && reportSizeBits % 8 == 0, $"feature block at offset {offset} declares {reportSizeBits}-bit fields");
                Assert.That(reportCount, Is.GreaterThan(0), $"feature block at offset {offset} declares no report count");
                features.Add(new FeatureReport((byte)reportId, 1 + reportSizeBits * reportCount / 8));
            }

            offset += 1 + dataBytes;
        }

        return features;
    }

    /// <summary>Reads the little-endian data of one HID short item (zero to four bytes).</summary>
    /// <param name="data">Item data bytes.</param>
    /// <returns>The item's unsigned value.</returns>
    private static int ReadHidItemValue(ReadOnlySpan<byte> data)
    {
        int value = 0;
        for (int i = 0; i < data.Length; i++)
        {
            value |= data[i] << (8 * i);
        }

        return value;
    }

    /// <summary>Sends one control transfer through the public entry point of <paramref name="device"/>; the response buffer matches the requested length.</summary>
    /// <param name="device">Device the transfer is addressed to.</param>
    /// <param name="requestType">bmRequestType of the setup packet.</param>
    /// <param name="request">bRequest of the setup packet.</param>
    /// <param name="value">wValue of the setup packet.</param>
    /// <param name="length">wLength of the setup packet.</param>
    /// <param name="outData">Data stage of a host-to-device transfer; none for device-to-host requests.</param>
    /// <returns>The result count and the response buffer.</returns>
    private static ControlResult Send(VirtualUsbDevice device, byte requestType, byte request, ushort value, ushort length, byte[]? outData = null)
    {
        byte[] buffer = new byte[length];
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = requestType,
            Request = request,
            Value = value,
            Index = 0,
            Length = length
        };

        int count = device.HandleControlRequest(setup, outData, buffer);
        return new ControlResult(count, buffer);
    }

    /// <summary>Reads one feature report of <paramref name="reportId"/> through a HID class GET_REPORT control transfer.</summary>
    /// <param name="device">Device the report is read from.</param>
    /// <param name="reportId">Report id of the requested feature report.</param>
    /// <param name="length">Requested transfer length (wLength).</param>
    /// <returns>The result count and the response buffer.</returns>
    private static ControlResult GetFeature(VirtualUsbDevice device, byte reportId, int length) =>
        Send(device, ClassInterfaceIn, RequestGetReport, (ushort)((ReportTypeFeature << 8) | reportId), (ushort)length);

    /// <summary>
    /// Subscribes a counting probe to the persona's own output-report event, whatever its report
    /// type is; the probe counts events, not their content (the per-persona tests pin the fields).
    /// </summary>
    /// <param name="device">Persona device to subscribe to.</param>
    /// <param name="onReport">Callback invoked once per raised event.</param>
    /// <exception cref="InvalidOperationException">The device is not one of the known personas.</exception>
    private static void SubscribeOutput(VirtualUsbDevice device, Action onReport)
    {
        switch (device)
        {
            case DualShock4Device dualShock4:
                dualShock4.OutputReportReceived += _ => onReport();
                break;

            case DualSenseDevice dualSense:
                dualSense.OutputReportReceived += _ => onReport();
                break;

            case Xbox360Device xbox360:
                xbox360.OutputReportReceived += _ => onReport();
                break;

            default:
                throw new InvalidOperationException($"The conformance sweep has no output probe for {device.GetType().Name}.");
        }
    }

    /// <summary>
    /// Creates the known-good output payload of <paramref name="kind"/> (the shape the persona's own
    /// output tests pin): the DS4 32-byte USB report 0x05, the DualSense 48-byte USB report 0x02 and
    /// the Xbox 360 8-byte rumble packet (id 0x00, size 0x08).
    /// </summary>
    /// <param name="kind">Output report family of the persona.</param>
    /// <returns>The payload bytes.</returns>
    private static byte[] CreateKnownOutputPayload(OutputKind kind) => kind switch
    {
        OutputKind.DualShock4 => CreatePayload(32, 0x05, 0x07),
        OutputKind.DualSense => CreatePayload(48, 0x02, 0x03),
        OutputKind.Xbox360 => [0x00, 0x08, 0x00, 0x64, 0x28, 0x00, 0x00, 0x00],
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Creates a full-length output payload with an id no persona decoder recognizes (the DS4 shape
    /// with report id 0x06, the DualSense shape with report id 0x03, the Xbox 360 8-byte shape with
    /// packet id 0x42).
    /// </summary>
    /// <param name="kind">Output report family of the persona.</param>
    /// <returns>The payload bytes.</returns>
    private static byte[] CreateUnknownOutputPayload(OutputKind kind) => kind switch
    {
        OutputKind.DualShock4 => CreatePayload(32, 0x06, 0x07),
        OutputKind.DualSense => CreatePayload(48, 0x03, 0x03),
        OutputKind.Xbox360 => [0x42, 0x08, 0x00, 0x64, 0x28, 0x00, 0x00, 0x00],
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Creates each payload one byte short of its decoder's documented minimum (the DS4 11-byte
    /// minimum, the DualSense 48-byte minimum, the Xbox 360 8-byte minimum); the leading bytes still
    /// carry the known report id, so only the length makes them unrecognizable.
    /// </summary>
    /// <param name="kind">Output report family of the persona.</param>
    /// <returns>The payload bytes.</returns>
    private static byte[] CreateTruncatedOutputPayload(OutputKind kind) => kind switch
    {
        OutputKind.DualShock4 => CreatePayload(10, 0x05, 0x07),
        OutputKind.DualSense => CreatePayload(47, 0x02, 0x03),
        OutputKind.Xbox360 => [0x00, 0x08, 0x00, 0x64, 0x28, 0x00, 0x00],
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Creates a zero payload of <paramref name="length"/> bytes with the report id and a flag byte set.</summary>
    /// <param name="length">Total payload length.</param>
    /// <param name="reportId">Report id (first byte).</param>
    /// <param name="flags">Enable-flags byte (second byte).</param>
    /// <returns>The payload bytes.</returns>
    private static byte[] CreatePayload(int length, byte reportId, byte flags)
    {
        byte[] payload = new byte[length];
        payload[0] = reportId;
        payload[1] = flags;
        return payload;
    }

    /// <summary>One descriptor of a raw descriptor chain walk: its offset, bLength and bDescriptorType.</summary>
    /// <param name="Offset">Offset of the descriptor in its blob.</param>
    /// <param name="Length">Declared bLength of the descriptor.</param>
    /// <param name="Type">bDescriptorType of the descriptor.</param>
    private readonly record struct RawDescriptor(int Offset, byte Length, byte Type);

    /// <summary>One feature report a HID report descriptor declares: its report id and total length (report id byte plus payload).</summary>
    /// <param name="ReportId">Report id of the feature report.</param>
    /// <param name="Size">Total declared length in bytes.</param>
    private readonly record struct FeatureReport(byte ReportId, int Size);

    /// <summary>Result of one control transfer: the result count (-1 for STALL) and the response buffer.</summary>
    /// <param name="Count">Number of bytes written, or -1 for STALL.</param>
    /// <param name="Data">The response buffer of the transfer.</param>
    private readonly record struct ControlResult(int Count, byte[] Data);

    /// <summary>Output-report family of a persona, selecting the independent payload shapes the tolerance cases use.</summary>
    public enum OutputKind
    {
        /// <summary>DualShock 4 output report 0x05 (32 bytes).</summary>
        DualShock4,

        /// <summary>DualSense output report 0x02 (48 bytes).</summary>
        DualSense,

        /// <summary>Xbox 360 rumble packet (8 bytes).</summary>
        Xbox360
    }

    /// <summary>Feature-surface behavior a persona's device currently implements.</summary>
    public enum FeatureExpectation
    {
        /// <summary>Every descriptor-declared feature report id is served at its declared length and an undeclared id stalls (DualSense, Edge).</summary>
        DeclaredTable,

        /// <summary>The descriptor declares a feature surface; the device serves its listed anchors and stalls every other id (DualShock 4 v1/v2).</summary>
        DeclaredAnchors,

        /// <summary>The persona is vendor-class, not HID: no report descriptor and no class feature path (Xbox 360).</summary>
        NonHid
    }

    /// <summary>
    /// One persona of the sweep: the construction prototype, the identity the device descriptor must
    /// carry, the input report size and the per-family output shapes. Each test invokes
    /// <see cref="Create"/> so devices never share state between cases.
    /// </summary>
    public sealed class Persona
    {
        /// <summary>Initializes a persona case.</summary>
        /// <param name="name">Display name of the persona.</param>
        /// <param name="idVendor">Vendor id the device descriptor carries.</param>
        /// <param name="idProduct">Product id the device descriptor carries.</param>
        /// <param name="inputReportSize">Size in bytes of one input report.</param>
        /// <param name="outputKind">Output-report family of the persona.</param>
        /// <param name="featureExpectation">Feature-surface behavior the device currently implements.</param>
        public Persona(string name, ushort idVendor, ushort idProduct, int inputReportSize, OutputKind outputKind, FeatureExpectation featureExpectation)
        {
            Name = name;
            IdVendor = idVendor;
            IdProduct = idProduct;
            InputReportSize = inputReportSize;
            OutputKind = outputKind;
            FeatureExpectation = featureExpectation;
        }

        /// <summary>Display name of the persona.</summary>
        public string Name { get; }

        /// <summary>Vendor id the device descriptor must carry.</summary>
        public ushort IdVendor { get; }

        /// <summary>Product id the device descriptor must carry.</summary>
        public ushort IdProduct { get; }

        /// <summary>Size in bytes of one input report (64 for the HID personas, 20 for the Xbox 360).</summary>
        public int InputReportSize { get; }

        /// <summary>Output-report family of the persona.</summary>
        public OutputKind OutputKind { get; }

        /// <summary>Feature-surface behavior the device currently implements.</summary>
        public FeatureExpectation FeatureExpectation { get; }

        /// <summary>Creates a fresh device of the persona.</summary>
        public required Func<VirtualUsbDevice> Create { get; init; }

        /// <summary>Anchor feature reports (id and pinned length) of the table-serving personas; empty for the others.</summary>
        public (byte ReportId, int Size)[] FeatureAnchors { get; init; } = [];

        /// <summary>Display name of the persona, used in test case names.</summary>
        /// <returns>The persona name.</returns>
        public override string ToString() => Name;
    }
}