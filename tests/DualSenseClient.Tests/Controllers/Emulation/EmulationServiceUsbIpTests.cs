using System.Net;
using System.Net.Sockets;
using DualSenseClient.Controllers;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Controllers.Emulation.UsbIp;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Tests.Controllers.Emulation.UsbIp;
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
/// Engine-switch tests of <see cref="EmulationService"/>: profiles whose stored
/// <see cref="EmulationSettings.Engine"/> is <see cref="EmulationEngine.UsbIp"/> create their
/// virtual controllers through the Emulation server and the Task 3 adapters, profiles on the
/// default libVIIPER engine never touch the Emulation engine, the shared Emulation server follows
/// the last entry (start lazily, stop on engine switch/Off/removal/dispose) and an engine change
/// rebuilds the entry.
/// </summary>
/// <remarks>
/// The tests drive the real server facade (<see cref="EmulationServer"/>) with a fake attach
/// strategy (the P2 test pattern) and a fake HID enumerator, so no native library, no USB/IP
/// driver and no real attachment is involved. The libVIIPER branch of the service is static
/// P/Invoke and is not driven here; the live VIIPER↔Emulation switch is validated in the live
/// validation session.
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

    private string _tempDir = null!;
    private ControllerTracker _tracker = null!;
    private FakeHidEnumerator _enumerator = null!;
    private ControllerInfoService _controllerInfo = null!;
    private SpecialActionEngineRegistry _specialActions = null!;
    private FakeAttachStrategy _attachStrategy = null!;
    private RecordingLogger _emulationLogger = null!;
    private List<EmulationServer> _servers = null!;
    private RecordingEmulationControllerFactory _emulationFactory = null!;
    private EmulationService _service = null!;
    private readonly List<PhysicalDualSense> _devices = new List<PhysicalDualSense>();

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
        _emulationFactory = new RecordingEmulationControllerFactory();

        // The audio engine is only touched by the controller-endpoint capture and the direct
        // capture paths, neither of which any test here exercises (the Emulation path has no
        // controller-endpoint audio), so no SoundFlow engine is created.
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
        new EmulationService(_tracker, _enumerator, _controllerInfo, new VirtualControllerFactory(),
            null!, _specialActions, new AudioApplicationService(), emulationServerFactory, _emulationFactory);

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

    private PhysicalDualSense CreateDevice(string key)
    {
        PhysicalDualSense device = new PhysicalDualSense(new StubHidDevice(), new StubHidDeviceInfo($@"\\?\hid#emulation-test#{key}"));
        _devices.Add(device);
        return device;
    }

    private void SetStoredSettings(PhysicalDualSense device, EmulationMode mode, EmulationEngine engine,
        DualSenseVariant dsVariant = DualSenseVariant.Standard, DualShock4Variant ds4Variant = DualShock4Variant.V2)
    {
        _controllerInfo.SaveEmulationSettings(null, device.Info.Path, new EmulationSettings
        {
            Mode = mode,
            Engine = engine,
            Variant = new VariantSettings
            {
                DualSense = dsVariant,
                DualShock4 = ds4Variant
            }
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

    [TestCase(EmulationMode.DualSense, DualSenseVariant.Standard, DualShock4Variant.V2, (ushort)0x054C, (ushort)0x0CE6)]
    [TestCase(EmulationMode.DualSense, DualSenseVariant.Edge, DualShock4Variant.V2, (ushort)0x054C, (ushort)0x0DF2)]
    [TestCase(EmulationMode.DualShock4, DualSenseVariant.Standard, DualShock4Variant.V1, (ushort)0x054C, (ushort)0x05C4)]
    [TestCase(EmulationMode.DualShock4, DualSenseVariant.Standard, DualShock4Variant.V2, (ushort)0x054C, (ushort)0x09CC)]
    [TestCase(EmulationMode.Xbox360, DualSenseVariant.Standard, DualShock4Variant.V2, (ushort)0x045E, (ushort)0x028E)]
    public void Creation_EmulationEngine_CreatesTheModePersonaAdapterAndAttachesIt(EmulationMode mode, DualSenseVariant dsVariant,
        DualShock4Variant ds4Variant, ushort vendorId, ushort productId)
    {
        PhysicalDualSense device = CreateDevice("mode");
        SetStoredSettings(device, mode, EmulationEngine.UsbIp, dsVariant, ds4Variant);
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
            EmulationMode.DualSense => typeof(EmulationDualSenseController),
            EmulationMode.DualShock4 => typeof(EmulationDualShock4Controller),
            _ => typeof(EmulationXbox360Controller)
        };
        EmulationStatus status = _service.GetStatus(device);

        Assert.Multiple(() =>
        {
            Assert.That(_emulationFactory.Creations, Has.Count.EqualTo(1));
            Assert.That(_emulationFactory.Creations[0].Mode, Is.EqualTo(mode));
            Assert.That(_emulationFactory.Creations[0].Edge, Is.EqualTo(dsVariant == DualSenseVariant.Edge));
            Assert.That(_emulationFactory.Creations[0].Creation, Is.Not.Null);
            Assert.That(_emulationFactory.Creations[0].Creation!.Persona, Is.TypeOf(expectedPersona));
            Assert.That(_emulationFactory.Creations[0].Creation!.Controller, Is.TypeOf(expectedAdapter));
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
                "the discovered path is excluded exactly like the libVIIPER path's");
        }
    }

    [Test]
    public void Creation_DualSensePersona_ServesTheOwnershipMacOverThePairingReport()
    {
        PhysicalDualSense device = CreateDevice("mac");
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        PersonaDualSense persona = (PersonaDualSense)_emulationFactory.Creations[0].Creation!.Persona;
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
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
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
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(2), "creation is retried once, mirroring the libVIIPER path");
            Assert.That(_attachStrategy.AttachCalls[0].BusId, Is.EqualTo("1-1"));
            Assert.That(_attachStrategy.AttachCalls[1].BusId, Is.EqualTo("1-1"),
                "the failed persona was unregistered again, freeing its bus id for the retry");
            Assert.That(_emulationFactory.Creations, Has.Count.EqualTo(2), "every attempt uses a fresh persona");
        });
    }

    [Test]
    public void Creation_AmbiguousDiscovery_DetachesAndUnregistersThePersona()
    {
        PhysicalDualSense device = CreateDevice("ambiguous");
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
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
            SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
            _tracker.TrackController(device);

            _service.Start();
            WaitForFailure(device);

            EmulationStatus status = _service.GetStatus(device);
            Assert.Multiple(() =>
            {
                Assert.That(status.Running, Is.False);
                Assert.That(status.Detail, Is.EqualTo("Could not start the Emulation server"));
                Assert.That(_emulationFactory.Creations, Is.Empty, "a server that cannot start creates no persona");
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
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);
        EmulationServer firstServer = _servers.Single();

        SetStoredSettings(device, EmulationMode.Off, EmulationEngine.UsbIp);
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
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
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
    public void Refresh_EngineChangeWithModeOff_TearsDownAndReEntryCreatesAFreshServer()
    {
        PhysicalDualSense device = CreateDevice("engine-change");
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);
        EmulationServer firstServer = _servers.Single();

        // The stored settings now select the other engine and Off: the entry must leave the
        // Emulation engine (persona detached, server stopped) and surface the Off status.
        SetStoredSettings(device, EmulationMode.Off, EmulationEngine.Viiper);
        _service.Refresh();

        Assert.Multiple(() =>
        {
            EmulationStatus status = _service.GetStatus(device);
            Assert.That(status.Mode, Is.EqualTo(EmulationMode.Off));
            Assert.That(status.Running, Is.False);
            Assert.That(status.Detail, Is.EqualTo("Emulation is disabled for this controller"));
            Assert.That(_attachStrategy.DetachCallCount, Is.EqualTo(1), "the engine switch detaches the Emulation persona");
            Assert.That(() => firstServer.AddDualSense(), Throws.TypeOf<ObjectDisposedException>(),
                "the engine switch stops the Emulation server");
        });

        // Re-entry on the Emulation engine creates a fresh server instance, never the old one.
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _service.Refresh();
        WaitForRunning(device);

        Assert.Multiple(() =>
        {
            Assert.That(_servers, Has.Count.EqualTo(2));
            Assert.That(_servers[1], Is.Not.SameAs(firstServer));
            Assert.That(_service.GetStatus(device).Running, Is.True);
        });
    }

    [Test]
    public void TemporaryModeOverride_KeepsTheStoredEmulationEngine()
    {
        PhysicalDualSense device = CreateDevice("temporary");
        SetStoredSettings(device, EmulationMode.Off, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        Assert.That(_service.GetStatus(device).Running, Is.False, "the stored Off setting does not create");

        // The temporary override changes the mode only: the engine must still come from the
        // stored settings, so the override copy may not silently fall back to libVIIPER.
        _service.SetTemporaryEmulationMode(device, EmulationMode.DualSense);
        _service.Refresh();
        WaitForRunning(device);

        Assert.Multiple(() =>
        {
            Assert.That(_service.GetStatus(device).Mode, Is.EqualTo(EmulationMode.DualSense));
            Assert.That(_emulationFactory.Creations, Has.Count.EqualTo(1),
                "the temporary override creates on the Emulation engine the stored settings select");
            Assert.That(_emulationFactory.Creations[0].Mode, Is.EqualTo(EmulationMode.DualSense));
            Assert.That(_servers, Has.Count.EqualTo(1), "the creation went through the Emulation server");
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Refresh_ModeChangeWithinEmulation_RebuildsTheAdapterOnTheSameServer()
    {
        PhysicalDualSense device = CreateDevice("mode-change");
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);

        SetStoredSettings(device, EmulationMode.DualShock4, EmulationEngine.UsbIp, ds4Variant: DualShock4Variant.V2);
        _enumerator.VirtualDevices.Clear();
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x09CC));
        _service.Refresh();
        WaitUntil(() => _service.GetStatus(device) is { Running: true, Mode: EmulationMode.DualShock4 }, "the rebuilt adapter to run");

        Assert.Multiple(() =>
        {
            Assert.That(_emulationFactory.Creations, Has.Count.EqualTo(2));
            Assert.That(_emulationFactory.Creations[0].Creation!.Persona, Is.TypeOf<PersonaDualSense>());
            Assert.That(_emulationFactory.Creations[1].Creation!.Persona, Is.TypeOf<PersonaDualShock4>());
            Assert.That(_emulationFactory.Creations[1].Creation!.Controller, Is.TypeOf<EmulationDualShock4Controller>());
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
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(device);

        _service.Start();
        WaitForRunning(device);
        EmulationServer server = _servers.Single();
        EmulationDualSenseController adapter = (EmulationDualSenseController)_emulationFactory.Creations[0].Creation!.Controller;
        PersonaDualSense persona = (PersonaDualSense)_emulationFactory.Creations[0].Creation!.Persona;

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
    public void Start_ViiperProfile_DoesNotTouchTheEmulationEngine()
    {
        PhysicalDualSense viiperDevice = CreateDevice("viiper");
        SetStoredSettings(viiperDevice, EmulationMode.Off, EmulationEngine.Viiper);
        PhysicalDualSense emulationDevice = CreateDevice("emulation");
        SetStoredSettings(emulationDevice, EmulationMode.DualSense, EmulationEngine.UsbIp);
        _enumerator.VirtualDevices.Add(CreateVirtualDeviceInfo(VirtualDevicePath, 0x054C, 0x0CE6));
        _tracker.TrackController(viiperDevice);
        _tracker.TrackController(emulationDevice);

        _service.Start();
        WaitForRunning(emulationDevice);

        Assert.Multiple(() =>
        {
            Assert.That(_service.GetStatus(viiperDevice).Detail, Is.EqualTo("Emulation is disabled for this controller"));
            Assert.That(_emulationFactory.Creations, Has.Count.EqualTo(1), "only the Emulation profile created a persona");
            Assert.That(_servers, Has.Count.EqualTo(1), "the Viiper profile did not create an Emulation server");
            Assert.That(_attachStrategy.AttachCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void TwoEmulationEntries_ShareOneServer_AndItStopsWhenTheLastOneLeaves()
    {
        PhysicalDualSense firstDevice = CreateDevice("first");
        PhysicalDualSense secondDevice = CreateDevice("second");
        SetStoredSettings(firstDevice, EmulationMode.DualSense, EmulationEngine.UsbIp);
        SetStoredSettings(secondDevice, EmulationMode.DualSense, EmulationEngine.UsbIp);
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
            Assert.That(_emulationFactory.Creations, Has.Count.EqualTo(2));
            Assert.That(_attachStrategy.AttachCalls.Select(call => call.BusId), Is.EqualTo(new[]
            {
                "1-1", "2-1"
            }));
        });

        // Both entries leave before a single Refresh: the loop disposes the first entry while
        // the second entry's persona is still attached, so the shared server may only stop
        // after every persona was detached.
        EmulationServer server = _servers.Single();
        SetStoredSettings(firstDevice, EmulationMode.Off, EmulationEngine.UsbIp);
        SetStoredSettings(secondDevice, EmulationMode.Off, EmulationEngine.UsbIp);
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
        SetStoredSettings(device, EmulationMode.DualSense, EmulationEngine.UsbIp);
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

        SetStoredSettings(device, EmulationMode.DualShock4, EmulationEngine.UsbIp);
        _service.Refresh();
        try
        {
            Assert.That(discoveryReached.Wait(TimeSpan.FromSeconds(15)), Is.True,
                "the queued creation reached discovery with its persona attached");

            // The entry leaves while the persona is attached but not committed to it: the
            // shared server must survive until the creation detached its persona.
            SetStoredSettings(device, EmulationMode.Off, EmulationEngine.UsbIp);
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
            Index = 0,
            Length = (ushort)length
        };
        int count = device.HandleControlRequest(setup, [], buffer);
        Assert.That(count, Is.EqualTo(length), "the persona must answer the pairing feature report");
        return buffer;
    }

    /// <summary>
    /// <see cref="IEmulationControllerFactory"/> test double: delegates to the real factory (so
    /// the real personas and adapters are created) and records every creation.
    /// </summary>
    private sealed class RecordingEmulationControllerFactory : IEmulationControllerFactory
    {
        private readonly object _lock = new object();
        private readonly EmulationControllerFactory _inner = new EmulationControllerFactory();
        private readonly List<RecordedEmulationCreation> _creations = new List<RecordedEmulationCreation>();

        public IReadOnlyList<RecordedEmulationCreation> Creations
        {
            get
            {
                lock (_lock)
                {
                    return _creations.ToArray();
                }
            }
        }

        public EmulationControllerCreation? Create(EmulationServer server, EmulationMode mode, IDualSenseOutputs outputs, bool vibrationV2,
            bool edge = false, DualShock4Variant ds4Variant = DualShock4Variant.V2)
        {
            EmulationControllerCreation? creation = _inner.Create(server, mode, outputs, vibrationV2, edge, ds4Variant);
            lock (_lock)
            {
                _creations.Add(new RecordedEmulationCreation(mode, edge, ds4Variant, creation));
            }

            return creation;
        }
    }

    /// <summary>One creation recorded by <see cref="RecordingEmulationControllerFactory"/>.</summary>
    private sealed class RecordedEmulationCreation
    {
        public RecordedEmulationCreation(EmulationMode mode, bool edge, DualShock4Variant ds4Variant, EmulationControllerCreation? creation)
        {
            Mode = mode;
            Edge = edge;
            Ds4Variant = ds4Variant;
            Creation = creation;
        }

        public EmulationMode Mode { get; }

        public bool Edge { get; }

        public DualShock4Variant Ds4Variant { get; }

        public EmulationControllerCreation? Creation { get; }
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

        public int Write(byte[] buffer, int offset, int count) => 0;

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

        public ConnectionType BusType
        {
            get
            {
                return ConnectionType.Usb;
            }
        }
    }
}