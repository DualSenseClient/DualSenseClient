using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

// The physical controller and an Emulation persona are both named DualSenseDevice in their
// namespaces; the aliases keep the two roles explicit in the fixture.
using PhysicalDualSense = DualSenseClient.Controllers.Devices.DualSenseDevice;
using PersonaDualSense = DualSenseClient.Emulation.Devices.DualSense.DualSenseDevice;
using PersonaDualShock4 = DualSenseClient.Emulation.Devices.DualShock4.DualShock4Device;
using PersonaXbox360 = DualSenseClient.Emulation.Devices.Xbox360.Xbox360Device;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Tests of <see cref="EmulationService"/>: profiles whose stored
/// <see cref="EmulationSettings.Mode"/> is enabled create their virtual controllers through the
/// Emulation server and the adapters, the shared Emulation server follows the last entry (start
/// lazily, stop on Off/removal/dispose) and a mode or variant change rebuilds the entry.
/// </summary>
/// <remarks>
/// The tests drive the real server facade (<see cref="EmulationServer"/>) with a fake attach
/// strategy (the P2 test pattern) and a fake HID enumerator, so no native library, no USB/IP
/// driver and no real attachment is involved.
/// </remarks>
[TestFixture]
public sealed class EmulationServiceUsbIpTests
{
    /// <summary>HID path the fake enumerator serves for the attached virtual device.</summary>
    private const string VirtualDevicePath = @"\\?\hid#emulation-virtual#1";

    /// <summary>Report id of the DualSense pairing information (carries the controller MAC).</summary>
    private const byte PairingReportId = 0x09;

    /// <summary>Size of the pairing information report.</summary>
    private const int PairingReportSize = 20;

    /// <summary>Report id of the Bluetooth microphone state and status reports.</summary>
    private const byte MicrophoneReportId = 0x32;

    /// <summary>Byte offset of the microphone mask in a <c>0x32</c> report.</summary>
    private const int MicrophoneMaskOffset = 4;

    /// <summary>Microphone mask carried while the physical microphone gate is open.</summary>
    private const byte MicrophoneStreamingMask = 0x03;

    /// <summary>Microphone mask carried while the physical microphone gate is closed.</summary>
    private const byte MicrophoneStoppedMask = 0x02;

    /// <summary>Standard SET_INTERFACE request code (USB 2.0, section 9.4.10).</summary>
    private const byte RequestSetInterface = 0x0B;

    /// <summary>Host-to-device standard request addressed to an interface (bmRequestType 0x01).</summary>
    private const byte StandardInterfaceOut = 0x01;

    /// <summary>Interface number of the microphone audio streaming interface (AS IN).</summary>
    private const byte MicrophoneInterfaceNumber = 2;

    private string _tempDir = null!;
    private ControllerTracker _tracker = null!;
    private FakeHidEnumerator _enumerator = null!;
    private ControllerInfoService _controllerInfo = null!;
    private SpecialActionEngineRegistry _specialActions = null!;
    private FakeAttachStrategy _attachStrategy = null!;
    private RecordingLogger _emulationLogger = null!;
    private List<EmulationServer> _servers = null!;
    private RecordingVirtualControllerFactory _virtualFactory = null!;
    private EmulationService _service = null!;
    private readonly List<PhysicalDualSense> _devices = new List<PhysicalDualSense>();
    private readonly Dictionary<PhysicalDualSense, StubHidDevice> _hids = new Dictionary<PhysicalDualSense, StubHidDevice>();

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"EmulationServiceUsbIpTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tracker = new ControllerTracker();
        _enumerator = new FakeHidEnumerator();
        _controllerInfo = new ControllerInfoService(controllersPath: Path.Combine(_tempDir, "Config", "controllers.json"));
        _specialActions = new SpecialActionEngineRegistry();
        _attachStrategy = new FakeAttachStrategy
        {
            AttachResult = true,
            DetachResult = true
        };
        _emulationLogger = new RecordingLogger();
        _servers = new List<EmulationServer>();
        _virtualFactory = new RecordingVirtualControllerFactory();

        // The service fixture injects a null audio engine: no SoundFlow device is ever created.
        // The Emulation controller-endpoint path still creates its forwarder and capture, but
        // CreateAudioForwarder leaves the USB render target disabled, so the forwarder starts
        // without a transport; no test here drives the direct loopback captures.
        _service = CreateService(CreateEmulationServer);

        // The fake enumerator presents the virtual device exactly while the fake attach strategy
        // has it attached: it appears after the attach (never in the pre-creation snapshot) and
        // disappears once the teardown detached it again.
        _enumerator.VirtualDevicesProvider = () => _attachStrategy.AttachCallCount > _attachStrategy.DetachCallCount
            ? _enumerator.VirtualDevices
            : [];
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();
        foreach (PhysicalDualSense device in _devices)
        {
            device.Dispose();
        }

        _specialActions?.Dispose();
        _tracker?.Dispose();
        _enumerator?.Dispose();

        foreach (EmulationServer server in _servers)
        {
            server.Dispose();
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

    /// <summary>Builds the service under test around the given Emulation server factory.</summary>
    private EmulationService CreateService(Func<EmulationServer> emulationServerFactory) =>
        new EmulationService(_tracker, _enumerator, _controllerInfo,
            null!, _specialActions, new AudioApplicationService(), emulationServerFactory, _virtualFactory);

    /// <summary>
    /// Creates a fresh server per call on a free loopback port: the service must never reuse a
    /// server it tore down.
    /// </summary>
    private EmulationServer CreateEmulationServer() => CreateEmulationServer(new IPEndPoint(IPAddress.Loopback, 0));

    /// <summary>
    /// Creates a fresh server bound to <paramref name="listenEndPoint"/> per call and registers it
    /// for teardown disposal, so tests can pick a fixed port or force a start failure (busy port).
    /// </summary>
    private EmulationServer CreateEmulationServer(IPEndPoint listenEndPoint)
    {
        EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = listenEndPoint,
            AttachStrategyFactory = () => _attachStrategy,
            Logger = _emulationLogger
        });
        _servers.Add(server);
        return server;
    }

    private PhysicalDualSense CreateDevice(string key, ConnectionType busType = ConnectionType.Usb)
    {
        StubHidDevice hid = new StubHidDevice();
        PhysicalDualSense device = new PhysicalDualSense(hid,
            new StubHidDeviceInfo($@"\\?\hid#emulation-test#{key}")
            {
                BusType = busType
            });
        _devices.Add(device);
        _hids.Add(device, hid);
        return device;
    }

    /// <summary>Returns the recording HID stub of a device created by <see cref="CreateDevice"/>.</summary>
    private StubHidDevice GetHid(PhysicalDualSense device) => _hids[device];

    private void SetStoredSettings(PhysicalDualSense device, EmulationMode mode,
        DualSenseVariant dsVariant = DualSenseVariant.Standard, DualShock4Variant ds4Variant = DualShock4Variant.V2,
        EmulationAudioSource audioSource = EmulationAudioSource.ControllerEndpoint,
        MicrophoneSettings? microphone = null)
    {
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, new EmulationSettings
        {
            Mode = mode,
            Variant = new VariantSettings
            {
                DualSense = dsVariant,
                DualShock4 = ds4Variant
            },
            Forward = new ForwardSettings
            {
                AudioSource = audioSource
            },
            Microphone = microphone ?? new MicrophoneSettings()
        });
    }

    private static StubHidDeviceInfo CreateVirtualDeviceInfo(string path, ushort vendorId, ushort productId) =>
        new StubHidDeviceInfo(path, vendorId, productId, "Emulation Virtual Controller");

    private void WaitForRunning(PhysicalDualSense device) => WaitUntil(() => _service.GetStatus(device).Running, "the virtual controller to start running");

    private void WaitForFailure(PhysicalDualSense device)
    {
        WaitUntil(() => _service.GetStatus(device) is { Running: false, IsCreating: false, Detail: not null },
            "the creation to fail");
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(15);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out after {timeout.TotalSeconds:0} s waiting for {what}");
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Returns the private per-device <c>VirtualControllerEntry</c> the service holds for
    /// <paramref name="device"/>. The entry type is a private nested class, so its audio state is
    /// read and driven through reflection exactly like the service's own code would.
    /// </summary>
    private object GetEntry(PhysicalDualSense device)
    {
        FieldInfo entriesField = typeof(EmulationService).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)!;
        System.Collections.IDictionary entries = (System.Collections.IDictionary)entriesField.GetValue(_service)!;
        object? entry = entries[device];
        Assert.That(entry, Is.Not.Null, "the service tracks an entry for the device");
        return entry!;
    }

    /// <summary>Reads one instance field of the private entry, forwarder or capture.</summary>
    private static T ReadField<T>(object target, string fieldName)
    {
        Type? type = target.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }

        return (T)field!.GetValue(target)!;
    }

    /// <summary>Writes one instance field of the private entry.</summary>
    private static void WriteField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType()
            .GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(target, value);
    }

    /// <summary>Invokes one private instance method of the service with the private entry.</summary>
    private static void InvokeService(object service, string methodName, params object?[] args)
    {
        MethodInfo method = service.GetType()
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, args);
    }

    /// <summary>
    /// Builds one nominal 384-byte Emulation DualSense speaker packet: 48 whole 8-byte frames, so
    /// both the front audio lane and the rear realtime-haptics lane see complete frames.
    /// </summary>
    private static byte[] CreateSpeakerPacket()
    {
        byte[] packet = new byte[384];
        for (int i = 0; i < packet.Length; i++)
        {
            packet[i] = (byte)(i * 7);
        }

        return packet;
    }

    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, DualShock4Variant.V2, (ushort)0x054C, (ushort)0x0CE6)]
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Edge, DualShock4Variant.V2, (ushort)0x054C, (ushort)0x0DF2)]
    [TestCase(EmulationMode.DualShock4, DualSenseVariant.Standard, DualShock4Variant.V1, (ushort)0x054C, (ushort)0x05C4)]
    [TestCase(EmulationMode.DualShock4, DualSenseVariant.Standard, DualShock4Variant.V2, (ushort)0x054C, (ushort)0x09CC)]
    [TestCase(EmulationMode.Xbox360, DualSenseVariant.Standard, DualShock4Variant.V2, (ushort)0x045E, (ushort)0x028E)]
    public void Creation_CreatesTheModePersonaAdapterAndAttachesIt(EmulationMode mode, DualSenseVariant dsVariant,
        DualShock4Variant ds4Variant, ushort vendorId, ushort productId)
    {
        PhysicalDualSense device = CreateDevice("mode");
        SetStoredSettings(device, mode, dsVariant, ds4Variant);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, vendorId, productId));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        Type expectedPersona = mode switch
        {
            EmulationMode.DualSense => typeof(PersonaDualSense),
            EmulationMode.DualShock4 => typeof(PersonaDualShock4),
            _ => typeof(PersonaXbox360)
        };
        Type expectedAdapter = mode switch
        {
            EmulationMode.DualSense => typeof(VirtualDualSenseController),
            EmulationMode.DualShock4 => typeof(VirtualDualShock4Controller),
            _ => typeof(VirtualXbox360Controller)
        };
        EmulationStatus status = _service.GetStatus(device);

        Assert.Multiple(() =>
        {
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(1));
            Assert.That(_virtualFactory.Creations[0].Mode, Is.EqualTo(mode));
            Assert.That(_virtualFactory.Creations[0].Edge, Is.EqualTo(dsVariant == DualSenseVariant.Edge));
            Assert.That(_virtualFactory.Creations[0].Creation, Is.Not.Null);
            Assert.That(_virtualFactory.Creations[0].Creation!.Persona, Is.TypeOf(expectedPersona));
            Assert.That(_virtualFactory.Creations[0].Creation!.Controller, Is.TypeOf(expectedAdapter));
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(1), "the fake attach strategy saw the attach");
            Assert.That(_attachStrategy.AttachCalls[0].BusId, Is.EqualTo("1-1"));
            Assert.That(status.Running, Is.True);
            Assert.That(status.Mode, Is.EqualTo(mode));
            Assert.That(status.Ds4Variant, Is.EqualTo(mode == EmulationMode.DualShock4 ? ds4Variant : null));
            Assert.That(status.VirtualDevicePath, Is.EqualTo(mode == EmulationMode.Xbox360 ? null : VirtualDevicePath));
        });

        if (mode != EmulationMode.Xbox360)
        {
            Assert.That(_enumerator.ExcludedPaths, Does.Contain(VirtualDevicePath),
                "the discovered path is excluded from enumeration");
        }
    }

    [Test]
    public void Creation_DualSensePersona_ServesTheOwnershipMacOverThePairingReport()
    {
        PhysicalDualSense device = CreateDevice("mac");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        PersonaDualSense persona = (PersonaDualSense)_virtualFactory.Creations[0].Creation!.Persona;
        byte[] pairing = GetFeatureReport(persona, PairingReportId, PairingReportSize);

        // The pairing report serves the MAC in reverse address order (reference behaviour), so
        // the ownership prefix "02:D5" lands at the end of the six address bytes.
        Assert.Multiple(() =>
        {
            Assert.That(pairing[5], Is.EqualTo(0xD5));
            Assert.That(pairing[6], Is.EqualTo(0x02));
        });
    }

    [Test]
    public void Creation_AttachFailure_ReportsTheAdaptedFailureStatusAndRetriesWithoutLeftovers()
    {
        _attachStrategy.AttachResult = false;
        _attachStrategy.AttachError = "synthetic attach failure";
        PhysicalDualSense device = CreateDevice("attach-failure");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForFailure(device);

        EmulationStatus status = _service.GetStatus(device);
        Assert.Multiple(() =>
        {
            Assert.That(status.Running, Is.False);
            Assert.That(status.Detail, Is.EqualTo(
                "The emulation server could not create the virtual device. Check the emulation logs; USB/IP must be installed for auto-attachment"));
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(2), "creation is retried once");
            Assert.That(_attachStrategy.AttachCalls[0].BusId, Is.EqualTo("1-1"));
            Assert.That(_attachStrategy.AttachCalls[1].BusId, Is.EqualTo("1-1"),
                "the failed persona was unregistered again, freeing its bus id for the retry");
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(2), "every attempt uses a fresh persona");
        });
    }

    [Test]
    public void Creation_AmbiguousDiscovery_DetachesAndUnregistersThePersona()
    {
        PhysicalDualSense device = CreateDevice("ambiguous");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(@"\\?\hid#emulation-virtual#2", 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForFailure(device);

        EmulationStatus status = _service.GetStatus(device);
        Assert.Multiple(() =>
        {
            Assert.That(status.Running, Is.False);
            Assert.That(status.Detail, Is.EqualTo(
                "The virtual device did not appear in HID enumeration. Check the emulation logs; USB/IP must be installed for auto-attachment"));
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(1));
            Assert.That(_attachStrategy.DetachCallCount, Is.EqualTo(1), "the attached persona is detached again");
            Assert.That(_enumerator.ExcludedPaths, Has.Count.EqualTo(2), "both ambiguous candidates were excluded on sighting");
        });
    }

    [Test]
    public void Creation_ServerStartFailure_ReportsTheStartFailureStatus()
    {
        // A listener holding the endpoint makes the service's server factory fail to bind:
        // ExclusiveAddressUse makes the second bind fail even when both sockets are local.
        TcpListener blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.ExclusiveAddressUse = true;
        blocker.Start();
        try
        {
            _service.Dispose();
            _service = CreateService(() => CreateEmulationServer((IPEndPoint)blocker.LocalEndpoint));

            PhysicalDualSense device = CreateDevice("server-start-failure");
            SetStoredSettings(device, EmulationMode.DualSense);
            _tracker.TrackController(device);

            _service.Start();
            WaitForFailure(device);

            EmulationStatus status = _service.GetStatus(device);
            Assert.Multiple(() =>
            {
                Assert.That(status.Running, Is.False);
                Assert.That(status.Detail, Is.EqualTo("Could not start the Emulation server"));
                Assert.That(_virtualFactory.Creations, Is.Empty, "a server that cannot start creates no persona");
            });
        }
        finally
        {
            blocker.Stop();
        }
    }

    [Test]
    public void Refresh_EngineOff_TearsDownTheEmulationServerAndReEntryCreatesAFreshOne()
    {
        PhysicalDualSense device = CreateDevice("switch");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);
        EmulationServer firstServer = _servers.Single();

        SetStoredSettings(device, EmulationMode.Off);
        _service.Refresh();

        EmulationStatus offStatus = _service.GetStatus(device);
        Assert.Multiple(() =>
        {
            Assert.That(offStatus.Mode, Is.EqualTo(EmulationMode.Off));
            Assert.That(offStatus.Running, Is.False);
            Assert.That(offStatus.Detail, Is.EqualTo("Emulation is disabled for this controller"));
            Assert.That(_attachStrategy.DetachCallCount, Is.EqualTo(1), "the persona is detached when the entry leaves");
            Assert.That(() => firstServer.AddDualSense(), Throws.TypeOf<ObjectDisposedException>(),
                "the last Emulation entry leaving tears the server down");
            Assert.That(_emulationLogger.Entries.Select(entry => entry.Message), Has.Some.Contains("USB/IP server stopped."));
        });

        // Re-entry creates a fresh server instance (a torn-down server is never reused).
        SetStoredSettings(device, EmulationMode.DualSense);
        _service.Refresh();
        WaitForRunning(device);

        Assert.Multiple(() =>
        {
            Assert.That(_servers, Has.Count.EqualTo(2));
            Assert.That(_servers[1], Is.Not.SameAs(firstServer));
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(2));
            Assert.That(_service.GetStatus(device).Running, Is.True);
        });
    }

    [Test]
    public void TemporaryModeOverride_CreatesWithoutChangingStoredSettings()
    {
        PhysicalDualSense device = CreateDevice("temporary");
        SetStoredSettings(device, EmulationMode.Off);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        Assert.That(_service.GetStatus(device).Running, Is.False, "the stored Off setting does not create");

        // The temporary override changes the mode only; the stored Off setting must survive.
        _service.SetTemporaryOverride(device, new EmulationOverride(EmulationMode.DualSense));
        _service.Refresh();
        WaitForRunning(device);

        Assert.Multiple(() =>
        {
            Assert.That(_service.GetStatus(device).Mode, Is.EqualTo(EmulationMode.DualSense));
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(1),
                "the temporary override creates the overridden mode");
            Assert.That(_virtualFactory.Creations[0].Mode, Is.EqualTo(EmulationMode.DualSense));
            Assert.That(_servers, Has.Count.EqualTo(1), "the creation went through the Emulation server");
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Refresh_ModeChangeWithinEmulation_RebuildsTheAdapterOnTheSameServer()
    {
        PhysicalDualSense device = CreateDevice("mode-change");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        SetStoredSettings(device, EmulationMode.DualShock4, ds4Variant: DualShock4Variant.V2);
        _enumerator.VirtualDevices.Clear();
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x09CC));
        _service.Refresh();
        WaitUntil(() => _service.GetStatus(device) is { Running: true, Mode: EmulationMode.DualShock4 }, "the rebuilt adapter to run");

        Assert.Multiple(() =>
        {
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(2));
            Assert.That(_virtualFactory.Creations[0].Creation!.Persona, Is.TypeOf<PersonaDualSense>());
            Assert.That(_virtualFactory.Creations[1].Creation!.Persona, Is.TypeOf<PersonaDualShock4>());
            Assert.That(_virtualFactory.Creations[1].Creation!.Controller, Is.TypeOf<VirtualDualShock4Controller>());
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(2));
            Assert.That(_attachStrategy.DetachCallCount, Is.EqualTo(1));
            Assert.That(_servers, Has.Count.EqualTo(1), "the shared server survives a mode rebuild");
            Assert.That(() => _servers[0].AddDualSense(), Throws.Nothing, "the shared server is still alive");
        });
    }

    [Test]
    public void Dispose_DetachesAndDisposesTheServerAndTheAdapter()
    {
        PhysicalDualSense device = CreateDevice("dispose");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);
        EmulationServer server = _servers.Single();
        VirtualDualSenseController adapter = (VirtualDualSenseController)_virtualFactory.Creations[0].Creation!.Controller;
        PersonaDualSense persona = (PersonaDualSense)_virtualFactory.Creations[0].Creation!.Persona;

        _service.Dispose();

        // A disposed adapter ignores input pushes: the persona keeps serving its last state.
        byte[] payload = EmulationAdapterTestSupport.NeutralPayload();
        payload[0] = 99;
        adapter.PushInput(EmulationAdapterTestSupport.Report(payload));

        Assert.Multiple(() =>
        {
            Assert.That(_attachStrategy.DetachCallCount, Is.EqualTo(1), "the persona is detached on service dispose");
            Assert.That(() => server.AddDualSense(), Throws.TypeOf<ObjectDisposedException>(), "the server is disposed");
            Assert.That(EmulationAdapterTestSupport.Pull(persona, 64)[1], Is.EqualTo(128), "the disposed adapter stops pushing input");
        });
    }

    [Test]
    public void Start_OffProfile_DoesNotTouchTheEmulationServer()
    {
        PhysicalDualSense offDevice = CreateDevice("off");
        SetStoredSettings(offDevice, EmulationMode.Off);
        PhysicalDualSense emulationDevice = CreateDevice("emulation");
        SetStoredSettings(emulationDevice, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(offDevice);
        _tracker.TrackController(emulationDevice);

        _service.Start();
        WaitForRunning(emulationDevice);

        Assert.Multiple(() =>
        {
            Assert.That(_service.GetStatus(offDevice).Detail, Is.EqualTo("Emulation is disabled for this controller"));
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(1), "only the enabled profile created a persona");
            Assert.That(_servers, Has.Count.EqualTo(1), "the Off profile did not create an Emulation server");
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoEmulationEntries_ShareOneServer_AndItStopsWhenTheLastOneLeaves()
    {
        PhysicalDualSense firstDevice = CreateDevice("first");
        PhysicalDualSense secondDevice = CreateDevice("second");
        SetStoredSettings(firstDevice, EmulationMode.DualSense);
        SetStoredSettings(secondDevice, EmulationMode.DualSense);
        _enumerator.VirtualDevicesProvider = () => _attachStrategy.AttachCallCount switch
        {
            0 => [],
            1 => [CreateVirtualDeviceInfo(@"\\?\hid#emulation-virtual#1", 0x054C, 0x0CE6)],
            _ =>
            [
                CreateVirtualDeviceInfo(@"\\?\hid#emulation-virtual#1", 0x054C, 0x0CE6),
                CreateVirtualDeviceInfo(@"\\?\hid#emulation-virtual#2", 0x054C, 0x0CE6)
            ]
        };
        _tracker.TrackController(firstDevice);
        _tracker.TrackController(secondDevice);

        _service.Start();
        WaitForRunning(firstDevice);
        WaitForRunning(secondDevice);

        Assert.Multiple(() =>
        {
            Assert.That(_servers, Has.Count.EqualTo(1), "both entries share one server");
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(2));
            Assert.That(_attachStrategy.AttachCalls.Select(call => call.BusId), Is.EqualTo(new[]
            {
                "1-1", "2-1"
            }));
        });

        // Both entries leave before a single Refresh: the loop disposes the first entry while
        // the second entry's persona is still attached, so the shared server may only stop
        // after every persona was detached.
        EmulationServer server = _servers.Single();
        SetStoredSettings(firstDevice, EmulationMode.Off);
        SetStoredSettings(secondDevice, EmulationMode.Off);
        _service.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(_service.GetStatus(firstDevice).Mode, Is.EqualTo(EmulationMode.Off));
            Assert.That(_service.GetStatus(secondDevice).Mode, Is.EqualTo(EmulationMode.Off));
            Assert.That(_attachStrategy.DetachCallCount, Is.EqualTo(2),
                "both personas are detached before the shared server stops");
            Assert.That(() => server.AddDualSense(), Throws.TypeOf<ObjectDisposedException>(),
                "the last persona detaching stops the shared server");
            Assert.That(_emulationLogger.Entries.Select(entry => entry.Message), Has.Some.Contains("USB/IP server stopped."));
        });
    }

    [Test]
    public void Refresh_DuringAnInFlightCreation_KeepsTheServerUntilThePersonaIsDetached()
    {
        PhysicalDualSense device = CreateDevice("in-flight");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);
        EmulationServer server = _servers.Single();

        // Park the mode-change creation after its persona attached but before it committed:
        // the discovery enumeration blocks on this hook until the test releases it again.
        ManualResetEventSlim discoveryReached = new ManualResetEventSlim();
        ManualResetEventSlim discoveryReleased = new ManualResetEventSlim();
        _enumerator.VirtualDevices.Clear();
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(@"\\?\hid#emulation-virtual#2", 0x054C, 0x09CC));
        _enumerator.OnEnumerate = () =>
        {
            if (_attachStrategy.AttachCallCount > _attachStrategy.DetachCallCount)
            {
                discoveryReached.Set();
                discoveryReleased.Wait();
            }
        };

        SetStoredSettings(device, EmulationMode.DualShock4);
        _service.Refresh();
        try
        {
            Assert.That(discoveryReached.Wait(TimeSpan.FromSeconds(15)), Is.True,
                "the queued creation reached discovery with its persona attached");

            // The entry leaves while the persona is attached but not committed to it: the
            // shared server must survive until the creation detached its persona.
            SetStoredSettings(device, EmulationMode.Off);
            _service.Refresh();
            Assert.That(_emulationLogger.Entries.Select(entry => entry.Message), Has.None.Contains("USB/IP server stopped."),
                "the in-flight persona keeps the server alive across the entry teardown");
        }
        finally
        {
            discoveryReleased.Set();
        }

        WaitUntil(() => _attachStrategy.DetachCallCount == 2, "the stale creation to detach its persona");
        WaitUntil(() => _emulationLogger.Entries.Select(entry => entry.Message).Contains("USB/IP server stopped."),
            "the server to stop once the in-flight persona was detached");
        Assert.That(() => server.AddDualSense(), Throws.TypeOf<ObjectDisposedException>());
    }

    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, (ushort)0x0CE6, typeof(EmulationDualSenseAudioCapture))]
    [TestCase(EmulationMode.DualShock4, DualSenseVariant.Standard, (ushort)0x09CC, typeof(EmulationDualShock4AudioCapture))]
    public void Creation_ControllerEndpoint_StoresThePersonaCaptureAndKeepsTheForwarder(
        EmulationMode mode, DualSenseVariant dsVariant, ushort productId, Type expectedCapture)
    {
        PhysicalDualSense device = CreateDevice($"audio-{mode}");
        SetStoredSettings(device, mode, dsVariant);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, productId));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        IDisposable? capture = ReadField<IDisposable?>(entry, "Capture");
        DualSenseAudioForwarder? forwarder = ReadField<DualSenseAudioForwarder?>(entry, "Forwarder");

        Assert.Multiple(() =>
        {
            Assert.That(capture, Is.Not.Null, "the controller endpoint has a capture");
            Assert.That(capture, Is.TypeOf(expectedCapture));
            Assert.That(capture, Is.AssignableTo<IVirtualAudioCapture>(), "the capture takes part in source cutover");
            Assert.That(forwarder, Is.Not.Null, "the capture has a sink to feed");
            Assert.That(forwarder?.IsDisposed, Is.False);
            Assert.That(ReadField<bool>(entry, "AudioCallbacksWired"), Is.True,
                "the game-output/haptics callbacks are wired on the forwarder");
        });
    }

    [Test]
    public void Creation_ControllerEndpoint_RoutesSpeakerHapticsIntoTheForwarderUntilUnwired()
    {
        PhysicalDualSense device = CreateDevice("audio-haptics");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        DualSenseAudioForwarder forwarder = ReadField<DualSenseAudioForwarder>(entry, "Forwarder");
        PersonaDualSense persona = (PersonaDualSense)_virtualFactory.Creations[0].Creation!.Persona;
        Assert.That(ReadField<long>(forwarder, "_lastGameHapticsTimestamp"), Is.Zero,
            "a freshly created forwarder has not seen game haptics yet");

        // Each 384-byte speaker packet carries 48 whole frames. Eleven packets fill the
        // forwarder's 512-frame haptics block, so the timestamp proves the lane reached it.
        for (int i = 0; i < 11; i++)
        {
            persona.OnIsoPacketReceived(0x01, CreateSpeakerPacket());
        }

        long seenHaptics = ReadField<long>(forwarder, "_lastGameHapticsTimestamp");
        Assert.That(seenHaptics, Is.Not.Zero, "the haptics lane reached the forwarder");

        InvokeService(_service, "UnwireGameCallbacksLocked", entry);
        for (int i = 0; i < 11; i++)
        {
            persona.OnIsoPacketReceived(0x01, CreateSpeakerPacket());
        }

        Assert.Multiple(() =>
        {
            Assert.That(ReadField<long>(forwarder, "_lastGameHapticsTimestamp"), Is.EqualTo(seenHaptics),
                "an unwired lane no longer updates the forwarder");
            Assert.That(ReadField<bool>(entry, "AudioCallbacksWired"), Is.False);
        });
    }

    [Test]
    public void DeactivateDirectAudioLocked_RebuildsTheControllerCaptureAndKeepsTheLiveForwarder()
    {
        PhysicalDualSense device = CreateDevice("audio-direct");
        SetStoredSettings(device, EmulationMode.DualSense,
            audioSource: EmulationAudioSource.SystemOutput);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        DualSenseAudioForwarder? forwarder = ReadField<DualSenseAudioForwarder?>(entry, "Forwarder");
        Assert.Multiple(() =>
        {
            Assert.That(ReadField<IDisposable?>(entry, "Capture"), Is.Null,
                "a direct source forwards straight to the physical controller");
            Assert.That(forwarder, Is.Not.Null, "the direct source created a forwarder");
            Assert.That(forwarder?.IsDisposed, Is.False);
        });

        // Simulate a successfully started direct capture: the OS loopback itself is not faked, only
        // the state the service would own after Start succeeded.
        WriteField(entry, "DirectAudioActive", true);
        InvokeService(_service, "DeactivateDirectAudioLocked", entry);

        IDisposable? capture = ReadField<IDisposable?>(entry, "Capture");
        DualSenseAudioForwarder? rebuiltForwarder = ReadField<DualSenseAudioForwarder?>(entry, "Forwarder");
        Assert.Multiple(() =>
        {
            Assert.That(capture, Is.TypeOf<EmulationDualSenseAudioCapture>(),
                "the missing controller capture is rebuilt on the switch back");
            Assert.That((capture as IVirtualAudioCapture)?.IsSuspended, Is.False, "the rebuilt capture feeds again");
            Assert.That(rebuiltForwarder, Is.SameAs(forwarder),
                "the live forwarder is reused instead of being torn down");
            Assert.That(rebuiltForwarder?.IsDisposed, Is.False);
            Assert.That(ReadField<bool>(entry, "AudioCallbacksWired"), Is.True);
        });
    }

    [Test]
    public void Refresh_ForwardingSettings_AreAppliedToTheLiveAdapterAndForwarder()
    {
        PhysicalDualSense device = CreateDevice("audio-forwarding-live");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        IVirtualController adapter = ReadField<IVirtualController>(entry, "Virtual");
        DualSenseAudioForwarder forwarder = ReadField<DualSenseAudioForwarder>(entry, "Forwarder");

        EmulationSettings settings = _controllerInfo.GetEmulationSettings(null, device.Info.Path);
        settings.Forward.Rumble = false;
        settings.Forward.Triggers = false;
        settings.Forward.Lightbar = false;
        settings.Forward.PlayerLeds = false;
        settings.Forward.HapticsEnabled = false;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(1),
                "forwarding changes must not recreate the virtual device");
            Assert.That(ReadField<bool>(adapter, "_rumbleForwarding"), Is.False);
            Assert.That(ReadField<bool>(adapter, "_triggerForwarding"), Is.False);
            Assert.That(ReadField<bool>(adapter, "_lightbarForwarding"), Is.False);
            Assert.That(ReadField<bool>(adapter, "_playerLedsForwarding"), Is.False);
            Assert.That(ReadField<bool>(adapter, "_hapticsForwarding"), Is.False);
            Assert.That(ReadField<bool>(forwarder, "_gameRumbleEnabled"), Is.False);
            Assert.That(ReadField<bool>(forwarder, "_gameTriggersEnabled"), Is.False);
            Assert.That(ReadField<bool>(forwarder, "_gameLightbarEnabled"), Is.False);
            Assert.That(ReadField<bool>(forwarder, "_gamePlayerLedsEnabled"), Is.False);
            Assert.That(ReadField<bool>(forwarder, "_gameHapticsEnabled"), Is.False);
        });
    }

    [Test]
    public void Refresh_AudioPassthroughDisabled_StopsTheControllerCaptureAndResumesWhenEnabled()
    {
        PhysicalDualSense device = CreateDevice("audio-toggle", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        DualSenseAudioForwarder forwarder = ReadField<DualSenseAudioForwarder>(entry, "Forwarder");
        Assert.That(ReadField<IDisposable?>(entry, "Capture"), Is.Not.Null);

        EmulationSettings settings = _controllerInfo.GetEmulationSettings(null, device.Info.Path);
        settings.Forward.AudioEnabled = false;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(ReadField<IDisposable?>(entry, "Capture"), Is.Null, "the controller-endpoint capture must stop");
            Assert.That(ReadField<DualSenseAudioForwarder?>(entry, "Forwarder"), Is.SameAs(forwarder),
                "the forwarder stays: it is the only Bluetooth lane for game haptics");
            Assert.That(forwarder.IsDisposed, Is.False);
            Assert.That(ReadField<bool>(entry, "AudioCallbacksWired"), Is.True,
                "game haptics stay wired to the forwarder");
        });

        // The game keeps sending realtime haptics: with audio off they must still reach the forwarder.
        PersonaDualSense persona = (PersonaDualSense)_virtualFactory.Creations[0].Creation!.Persona;
        for (int i = 0; i < 11; i++)
        {
            persona.OnIsoPacketReceived(0x01, CreateSpeakerPacket());
        }

        Assert.That(ReadField<long>(forwarder, "_lastGameHapticsTimestamp"), Is.Not.Zero,
            "disabling audio passthrough must not unwire the game-haptics lane");

        settings.Forward.AudioEnabled = true;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(ReadField<IDisposable?>(entry, "Capture"), Is.TypeOf<EmulationDualSenseAudioCapture>(),
                "re-enabling must rebuild the controller-endpoint capture");
            Assert.That(ReadField<DualSenseAudioForwarder?>(entry, "Forwarder"), Is.SameAs(forwarder));
        });
    }

    [Test]
    public void Refresh_AudioPassthroughAndGameHapticsDisabled_TearsDownTheForwarder()
    {
        PhysicalDualSense device = CreateDevice("audio-toggle-haptics-off", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        EmulationSettings settings = _controllerInfo.GetEmulationSettings(null, device.Info.Path);
        settings.Forward.AudioEnabled = false;
        settings.Forward.HapticsEnabled = false;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(ReadField<IDisposable?>(entry, "Capture"), Is.Null);
            Assert.That(ReadField<DualSenseAudioForwarder?>(entry, "Forwarder"), Is.Null,
                "nothing needs the forwarder once audio and game haptics are both off");
            Assert.That(ReadField<bool>(entry, "AudioCallbacksWired"), Is.False);
        });
    }

    [Test]
    public void BluetoothDualSenseEntry_CreatesMicrophoneBridge()
    {
        PhysicalDualSense device = CreateDevice("microphone-dualsense", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        StubHidDevice hid = GetHid(device);
        Assert.That(ReadField<VirtualControllerMicrophone?>(entry, "Microphone"), Is.Not.Null,
            "a Bluetooth DualSense entry owns a microphone bridge");

        SetMicrophoneInterface(_virtualFactory.Creations[0].Creation!.Persona, 1);

        Assert.That(HasMicrophoneStatusWrite(hid, MicrophoneStreamingMask), Is.True,
            "opening the persona's microphone starts the bridge and opens the physical microphone gate");
    }

    [Test]
    public void BluetoothDualShock4Entry_CreatesMicrophoneBridge()
    {
        PhysicalDualSense device = CreateDevice("microphone-dualshock4", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.DualShock4);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x09CC));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        StubHidDevice hid = GetHid(device);
        Assert.Multiple(() =>
        {
            Assert.That(_virtualFactory.Creations[0].Creation!.Persona, Is.TypeOf<PersonaDualShock4>());
            Assert.That(ReadField<VirtualControllerMicrophone?>(entry, "Microphone"), Is.Not.Null,
                "a Bluetooth DualShock 4 entry owns a microphone bridge");
        });

        SetMicrophoneInterface(_virtualFactory.Creations[0].Creation!.Persona, 1);

        Assert.That(HasMicrophoneStatusWrite(hid, MicrophoneStreamingMask), Is.True,
            "opening the persona's microphone starts the bridge and opens the physical microphone gate");
    }

    [Test]
    public void UsbEntry_DoesNotCreateMicrophoneBridge()
    {
        PhysicalDualSense device = CreateDevice("microphone-usb");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        StubHidDevice hid = GetHid(device);
        SetMicrophoneInterface(_virtualFactory.Creations[0].Creation!.Persona, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ReadField<VirtualControllerMicrophone?>(entry, "Microphone"), Is.Null,
                "a wired controller has no Bluetooth microphone to forward");
            Assert.That(HasMicrophoneStatusWrite(hid, MicrophoneStreamingMask), Is.False,
                "no microphone status report is written for a USB entry");
        });
    }

    [Test]
    public void UsbEntry_AppliesMicrophoneRouteAndLevel()
    {
        PhysicalDualSense device = CreateDevice("microphone-usb-route");
        SetStoredSettings(device, EmulationMode.DualSense, microphone: new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        Assert.That(HasUsbMicrophoneStateWrite(GetHid(device), AudioControl.MicSelectExternal, 32), Is.True,
            "a wired controller receives the route and level through the USB output report");
    }

    [Test]
    public void UsbEntry_AppliesMicrophoneRouteAndLevel_WhenEmulationOff()
    {
        PhysicalDualSense device = CreateDevice("microphone-usb-off-mode");
        SetStoredSettings(device, EmulationMode.Off, microphone: new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        _tracker.TrackController(device);

        _service.Start();

        Assert.That(HasUsbMicrophoneStateWrite(GetHid(device), AudioControl.MicSelectExternal, 32), Is.True,
            "the USB microphone settings apply without virtual controller emulation");
    }

    [Test]
    public void BluetoothEntry_AppliesMicrophoneRouteAndLevel_WhenEmulationOff()
    {
        PhysicalDualSense device = CreateDevice("microphone-bluetooth-off-mode", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.Off, microphone: new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        _tracker.TrackController(device);

        _service.Start();

        Assert.That(HasBluetoothMicrophoneStateWrite(GetHid(device), AudioControl.MicSelectExternal, 32), Is.True,
            "the Bluetooth microphone settings apply without virtual controller emulation");
    }

    [Test]
    public void UsbEntry_AppliesMicrophoneRouteAndLevel_WhenCreationFails()
    {
        PhysicalDualSense device = CreateDevice("microphone-usb-create-failure");
        SetStoredSettings(device, EmulationMode.DualSense, microphone: new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        _tracker.TrackController(device);

        _service.Start();
        WaitForFailure(device);

        Assert.That(HasUsbMicrophoneStateWrite(GetHid(device), AudioControl.MicSelectExternal, 32), Is.True,
            "the physical microphone settings still apply when the virtual controller cannot start");
    }

    [Test]
    public void Refresh_UsbAppliesMicrophoneRouteAndLevel()
    {
        PhysicalDualSense device = CreateDevice("microphone-usb-refresh");
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        EmulationSettings settings = _controllerInfo.GetEmulationSettings(null, device.Info.Path);
        settings.Microphone.Source = MicrophoneSource.Headphone;
        settings.Microphone.Level = 50;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();

        Assert.That(HasUsbMicrophoneStateWrite(GetHid(device), AudioControl.MicSelectExternal, 32), Is.True,
            "a live settings change reaches the wired controller without recreating the virtual device");
    }

    [Test]
    public void Refresh_UsbKeepsOverridingMicrophoneWhenEmulationTurnsOff()
    {
        PhysicalDualSense device = CreateDevice("microphone-usb-off");
        SetStoredSettings(device, EmulationMode.DualSense, microphone: new MicrophoneSettings
        {
            Source = MicrophoneSource.Headphone,
            Level = 50
        });
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        EmulationSettings settings = _controllerInfo.GetEmulationSettings(null, device.Info.Path);
        settings.Mode = EmulationMode.Off;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();

        StubHidDevice hid = GetHid(device);
        device.SendOutputState(new SetStateData
        {
            ValidFlag0 = ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume,
            AudioControl = AudioControl.OutputPathHeadphones | AudioControl.MicSelectInternal,
            MicVolume = 7
        });

        byte[] write = hid.LastWrite!;
        Assert.Multiple(() =>
        {
            Assert.That(write[0], Is.EqualTo(0x02));
            Assert.That(write[8] & 0x03, Is.EqualTo((byte)AudioControl.MicSelectExternal),
                "the USB microphone route stays overridden when emulation turns off");
            Assert.That(write[7], Is.EqualTo(32), "the USB microphone level stays overridden when emulation turns off");
        });
    }

    [Test]
    public void Xbox360Entry_DoesNotCreateMicrophoneBridge()
    {
        PhysicalDualSense device = CreateDevice("microphone-xbox360", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.Xbox360);
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        object entry = GetEntry(device);
        StubHidDevice hid = GetHid(device);
        Assert.Multiple(() =>
        {
            Assert.That(_virtualFactory.Creations[0].Creation!.Persona, Is.TypeOf<PersonaXbox360>());
            Assert.That(ReadField<VirtualControllerMicrophone?>(entry, "Microphone"), Is.Null,
                "the Xbox 360 persona has no microphone endpoint");
            Assert.That(HasMicrophoneStatusWrite(hid, MicrophoneStreamingMask), Is.False,
                "no microphone gate is opened for an Xbox 360 entry");
            Assert.That(HasBluetoothMicrophoneStateWrite(hid, AudioControl.MicSelectAuto, 64), Is.True,
                "the physical microphone route and level still apply");
        });
    }

    [Test]
    public void DisposeEntry_StopsBridge()
    {
        PhysicalDualSense device = CreateDevice("microphone-dispose", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        StubHidDevice hid = GetHid(device);
        SetMicrophoneInterface(_virtualFactory.Creations[0].Creation!.Persona, 1);
        Assert.That(HasMicrophoneStatusWrite(hid, MicrophoneStreamingMask), Is.True,
            "precondition: the bridge opened the physical microphone gate");

        _service.Dispose();

        Assert.That(HasMicrophoneStatusWrite(hid, MicrophoneStoppedMask), Is.True,
            "disposing the entry stops the bridge and closes the physical microphone gate");
    }

    [Test]
    public void Refresh_MicrophonePassthrough_TogglesTheLiveBridgeWithoutRecreating()
    {
        PhysicalDualSense device = CreateDevice("microphone-live", ConnectionType.Bluetooth);
        SetStoredSettings(device, EmulationMode.DualSense);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        StubHidDevice hid = GetHid(device);
        SetMicrophoneInterface(_virtualFactory.Creations[0].Creation!.Persona, 1);
        WaitForMicrophoneStatus(hid, MicrophoneStreamingMask, 1);

        EmulationSettings settings = _controllerInfo.GetEmulationSettings(null, device.Info.Path);
        int stoppedBefore = CountMicrophoneStatusWrites(hid, MicrophoneStoppedMask);
        settings.Forward.MicrophoneEnabled = false;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();
        WaitUntil(() => CountMicrophoneStatusWrites(hid, MicrophoneStoppedMask) > stoppedBefore,
            "the live bridge to close the physical microphone gate when passthrough is disabled");

        int streamingBefore = CountMicrophoneStatusWrites(hid, MicrophoneStreamingMask);
        settings.Forward.MicrophoneEnabled = true;
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, settings);
        _service.Refresh();
        WaitUntil(() => CountMicrophoneStatusWrites(hid, MicrophoneStreamingMask) > streamingBefore,
            "the live bridge to reopen the physical microphone gate when passthrough is enabled again");

        Assert.That(_virtualFactory.Creations, Has.Count.EqualTo(1),
            "the passthrough toggle applies to the live bridge without recreating the virtual device");
    }

    /// <summary>
    /// Sets the microphone streaming interface alternate setting through the same standard
    /// SET_INTERFACE request the host sends.
    /// </summary>
    private static void SetMicrophoneInterface(VirtualUsbDevice persona, byte alternateSetting)
    {
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = StandardInterfaceOut,
            Request = RequestSetInterface,
            Value = alternateSetting,
            Index = MicrophoneInterfaceNumber,
            Length = 0
        };
        Assert.That(persona.HandleControlRequest(setup, [], []), Is.Zero,
            "the persona accepts the microphone alternate setting");
    }

    /// <summary>Whether the physical controller recorded a Bluetooth microphone state with the given route and level.</summary>
    private static bool HasBluetoothMicrophoneStateWrite(StubHidDevice hid, AudioControl source, byte level) =>
        hid.Writes.Any(write => write.Length > 11
                                && write[0] == MicrophoneReportId
                                && write[2] == 0x90
                                && (write[11] & 0x03) == (byte)source
                                && write[10] == level);

    /// <summary>Whether the physical controller recorded a USB output report with the given microphone route and level.</summary>
    private static bool HasUsbMicrophoneStateWrite(StubHidDevice hid, AudioControl source, byte level) =>
        hid.Writes.Any(write => write.Length > 8
                                && write[0] == 0x02
                                && (write[1] & (byte)(ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume))
                                == (byte)(ValidFlags.AllowAudioControl | ValidFlags.AllowMicVolume)
                                && (write[8] & 0x03) == (byte)source
                                && write[7] == level);

    /// <summary>Whether the physical controller's HID stub recorded a microphone status with the given mask.</summary>
    private static bool HasMicrophoneStatusWrite(StubHidDevice hid, byte mask) =>
        CountMicrophoneStatusWrites(hid, mask) > 0;

    /// <summary>Counts the recorded microphone status reports that carry the given mask.</summary>
    private static int CountMicrophoneStatusWrites(StubHidDevice hid, byte mask) =>
        hid.Writes.Count(write => write.Length > MicrophoneMaskOffset
                                  && write[0] == MicrophoneReportId
                                  && write[2] == 0x91
                                  && write[MicrophoneMaskOffset] == mask);

    /// <summary>Waits until the physical controller recorded at least the given number of status writes.</summary>
    private static void WaitForMicrophoneStatus(StubHidDevice hid, byte mask, int minimumCount) =>
        WaitUntil(() => CountMicrophoneStatusWrites(hid, mask) >= minimumCount,
            $"the physical controller to record {minimumCount} microphone status write(s) with mask 0x{mask:X2}");

    /// <summary>
    /// Reads one feature report of the persona through a GET_REPORT control transfer, the same
    /// public entry point the persona conformance tests use.
    /// </summary>
    private static byte[] GetFeatureReport(PersonaDualSense device, byte reportId, int length)
    {
        byte[] buffer = new byte[length];
        UsbSetupPacket setup = new UsbSetupPacket
        {
            RequestType = 0xA1,
            Request = 0x01,
            Value = (ushort)(0x0300 | reportId),
            Index = 3, // the audio function occupies interfaces 0-2; HID is interface 3
            Length = (ushort)length
        };
        int count = device.HandleControlRequest(setup, [], buffer);
        Assert.That(count, Is.EqualTo(length), "the persona must answer the pairing feature report");
        return buffer;
    }

    /// <summary>
    /// <see cref="IVirtualControllerFactory"/> test double: delegates to the real factory (so
    /// the real personas and adapters are created) and records every creation.
    /// </summary>
    private sealed class RecordingVirtualControllerFactory : IVirtualControllerFactory
    {
        private readonly object _lock = new object();
        private readonly VirtualControllerFactory _inner = new VirtualControllerFactory();
        private readonly List<RecordedVirtualCreation> _creations = new List<RecordedVirtualCreation>();

        public IReadOnlyList<RecordedVirtualCreation> Creations
        {
            get
            {
                lock (_lock)
                {
                    return _creations.ToArray();
                }
            }
        }

        public VirtualControllerCreation? Create(EmulationServer server, EmulationMode mode, IDualSenseOutputs outputs, bool vibrationV2,
            bool edge = false, DualShock4Variant ds4Variant = DualShock4Variant.V2)
        {
            VirtualControllerCreation? creation = _inner.Create(server, mode, outputs, vibrationV2, edge, ds4Variant);
            lock (_lock)
            {
                _creations.Add(new RecordedVirtualCreation(mode, edge, ds4Variant, creation));
            }

            return creation;
        }
    }

    /// <summary>One creation recorded by <see cref="RecordingVirtualControllerFactory"/>.</summary>
    private sealed class RecordedVirtualCreation
    {
        public RecordedVirtualCreation(EmulationMode mode, bool edge, DualShock4Variant ds4Variant, VirtualControllerCreation? creation)
        {
            Mode = mode;
            Edge = edge;
            Ds4Variant = ds4Variant;
            Creation = creation;
        }

        public EmulationMode Mode { get; }

        public bool Edge { get; }

        public DualShock4Variant Ds4Variant { get; }

        public VirtualControllerCreation? Creation { get; }
    }

    /// <summary>
    /// <see cref="IHidDeviceEnumerator"/> test double: returns the configured physical devices
    /// plus whatever <see cref="VirtualDevicesProvider"/> yields for the requested VID/PID.
    /// Exclusions are recorded, never applied.
    /// </summary>
    private sealed class FakeHidEnumerator : IHidDeviceEnumerator
    {
        private readonly object _lock = new object();
        private readonly List<string> _excluded = new List<string>();

        /// <summary>Devices always present in enumeration.</summary>
        public List<IHidDeviceInfo> Devices { get; } = new List<IHidDeviceInfo>();

        /// <summary>Pool the default <see cref="VirtualDevicesProvider"/> serves while attached.</summary>
        public List<IHidDeviceInfo> VirtualDevices { get; } = new List<IHidDeviceInfo>();

        /// <summary>Returns the virtual devices to present in the current enumeration.</summary>
        public Func<IReadOnlyList<IHidDeviceInfo>> VirtualDevicesProvider { get; set; } = () => [];

        /// <summary>
        /// Invoked on every enumeration before the result is built; lets a test park the
        /// enumerating thread (e.g. to inspect the service mid-discovery).
        /// </summary>
        public Action? OnEnumerate { get; set; }

        public IReadOnlyList<string> ExcludedPaths
        {
            get
            {
                lock (_lock)
                {
                    return _excluded.ToArray();
                }
            }
        }

        public IReadOnlyList<IHidDeviceInfo> Enumerate(ushort? vendorId = null, ushort? productId = null) =>
            EnumerateIncludingExcluded(vendorId, productId);

        public IReadOnlyList<IHidDeviceInfo> Enumerate(IEnumerable<(ushort VendorId, ushort ProductId)> deviceIds)
        {
            List<IHidDeviceInfo> result = new List<IHidDeviceInfo>();
            foreach ((ushort vendorId, ushort productId) in deviceIds)
            {
                result.AddRange(EnumerateIncludingExcluded(vendorId, productId));
            }

            return result;
        }

        public IReadOnlyList<IHidDeviceInfo> EnumerateIncludingExcluded(ushort? vendorId = null, ushort? productId = null)
        {
            OnEnumerate?.Invoke();
            List<IHidDeviceInfo> result = Filter(Devices, vendorId, productId);
            result.AddRange(Filter(VirtualDevicesProvider().ToList(), vendorId, productId));
            return result;
        }

        private static List<IHidDeviceInfo> Filter(List<IHidDeviceInfo> devices, ushort? vendorId, ushort? productId) =>
            devices.Where(info => (vendorId is null || info.VendorId == vendorId)
                                  && (productId is null || info.ProductId == productId)).ToList();

        public IHidDevice OpenDevice(string path) => throw new NotSupportedException("The engine-switch tests never open HID devices.");

        public void StartWatching(int intervalMs = 1000)
        {
        }

        public void StopWatching()
        {
        }

        public void ExcludeDevice(string path)
        {
            lock (_lock)
            {
                _excluded.Add(path);
            }
        }

        public void RemoveExcludedDevice(string path)
        {
            lock (_lock)
            {
                _excluded.RemoveAll(excluded => string.Equals(excluded, path, StringComparison.OrdinalIgnoreCase));
            }
        }

        public event EventHandler<DeviceConnectionEventArgs>? DeviceConnected
        {
            add { }
            remove { }
        }

        public event EventHandler<DeviceConnectionEventArgs>? DeviceDisconnected
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Stub HID device of the physical controller; reads always time out.</summary>
    private sealed class StubHidDevice : IHidDevice
    {
        /// <summary>Reports written to the device, in call order; safe to read while the creator and persona threads write.</summary>
        private readonly ConcurrentQueue<byte[]> _writes = new ConcurrentQueue<byte[]>();

        /// <summary>A snapshot of every report written to the device, in call order.</summary>
        public IReadOnlyCollection<byte[]> Writes
        {
            get
            {
                return _writes.ToArray();
            }
        }

        /// <summary>The last report written to the device, or <c>null</c> when none was written.</summary>
        public byte[]? LastWrite
        {
            get
            {
                return _writes.LastOrDefault();
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
            _writes.Enqueue(buffer[offset..(offset + count)]);
            return count;
        }

        public byte[] GetFeatureReport(byte reportId, int bufferSize = 64) => [];

        public void SendFeatureReport(byte[] buffer, int offset, int count)
        {
        }

        public string GetProductName() => "Test DualSense";

        public void Dispose()
        {
        }
    }

    /// <summary>Stub HID device info; the path identifies the controller's stored settings.</summary>
    private sealed class StubHidDeviceInfo : IHidDeviceInfo
    {
        public StubHidDeviceInfo(string path, ushort vendorId = 0x054C, ushort productId = 0x0CE6, string productName = "Test DualSense")
        {
            Path = path;
            VendorId = vendorId;
            ProductId = productId;
            ProductName = productName;
        }

        public string Path { get; }

        public ushort VendorId { get; }

        public ushort ProductId { get; }

        public string ProductName { get; }

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
                return 3;
            }
        }

        public ushort UsagePage
        {
            get
            {
                return 0x01;
            }
        }

        public HidUsageId Usage
        {
            get
            {
                return HidUsageId.GamePad;
            }
        }

        public ConnectionType BusType { get; init; } = ConnectionType.Usb;
    }
}