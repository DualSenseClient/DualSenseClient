using System.Net;
using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.Emulation.Devices.Xbox360;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation;

/// <summary>
/// Tests of the <see cref="EmulationServer"/> facade: bus id assignment and listing, the attach and
/// detach calls forwarded to the attach strategy, the per-device attach bookkeeping and the bus id
/// reuse after a removal. The server binds loopback port 0, no client ever connects, and every
/// attach decision goes through the fake strategy, so no traffic leaves the machine and no usbip.exe
/// run happens.
/// </summary>
[TestFixture]
public sealed class EmulationServerTests
{
    [Test]
    public void AddDualShock4_FirstDevice_ListsBusIdOneAndAttachUsesBoundEndPoint()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1"
        }));

        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(strategy.AttachCalls, Has.Count.EqualTo(1));
        Assert.That(strategy.AttachCalls[0].Host, Is.EqualTo("127.0.0.1"));
        Assert.That(strategy.AttachCalls[0].Port, Is.EqualTo(server.BoundEndPoint.Port));
        Assert.That(strategy.AttachCalls[0].BusId, Is.EqualTo("1-1"));
    }

    [Test]
    public void AddDualShock4_SecondDevice_AssignsNextBusIdAndListsBoth()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device first = server.AddDualShock4();
        DualShock4Device second = server.AddDualShock4();

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "2-1"
        }));
        Assert.That(server.TryAttach(second, out string? error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(strategy.AttachCalls[0].BusId, Is.EqualTo("2-1"));
    }

    [Test]
    public void AddDualShock4_BeforeStart_ListsDevice()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        DualShock4Device device = server.AddDualShock4();

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1"
        }));
        Assert.That(device, Is.Not.Null);
    }

    [Test]
    public void AddDualShock4_CustomIdentity_ServesIdentityDescriptors()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        DualShock4Device reference = server.AddDualShock4();
        DualShock4Device custom = server.AddDualShock4(new DualShock4Identity
        {
            IdVendor = 0x0F0F,
            IdProduct = 0x1234
        });

        Assert.That(reference.Descriptors.IdVendor, Is.EqualTo(0x054C));
        Assert.That(reference.Descriptors.IdProduct, Is.EqualTo(0x09CC));
        Assert.That(custom.Descriptors.IdVendor, Is.EqualTo(0x0F0F));
        Assert.That(custom.Descriptors.IdProduct, Is.EqualTo(0x1234));
    }

    [Test]
    public void AddDualSense_BaseAndEdge_RegistersEachPersonaWithItsProductId()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        DualSenseDevice baseDevice = server.AddDualSense();
        DualSenseDevice edgeDevice = server.AddDualSense(true);

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "2-1"
        }));
        Assert.That(baseDevice.Descriptors.IdVendor, Is.EqualTo(0x054C));
        Assert.That(baseDevice.Descriptors.IdProduct, Is.EqualTo(0x0CE6));
        Assert.That(edgeDevice.Descriptors.IdVendor, Is.EqualTo(0x054C));
        Assert.That(edgeDevice.Descriptors.IdProduct, Is.EqualTo(0x0DF2)); // the Edge factory pins the Edge product id
    }

    [Test]
    public void AddDualSense_CustomIdentity_ServesIdentityDescriptors()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        DualSenseDevice device = server.AddDualSense(identity: new DualSenseIdentity
        {
            IdVendor = 0x0F0F,
            IdProduct = 0x1234
        });

        Assert.That(device.Descriptors.IdVendor, Is.EqualTo(0x0F0F));
        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(0x1234));
        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1"
        }));
    }

    [Test]
    public void AddXbox360_DefaultIdentity_ServesMicrosoftDescriptors()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        Xbox360Device device = server.AddXbox360();

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1"
        }));
        Assert.That(device.Descriptors.IdVendor, Is.EqualTo(0x045E));
        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(0x028E));
    }

    [Test]
    public void AddXbox360_CustomIdentity_ServesIdentityDescriptors()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        Xbox360Device device = server.AddXbox360(new Xbox360Identity
        {
            IdVendor = 0x0F0F,
            IdProduct = 0x1234
        });

        Assert.That(device.Descriptors.IdVendor, Is.EqualTo(0x0F0F));
        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(0x1234));
    }

    [Test]
    public void AddDualShock4_VariantV1_PinsV1ProductIdAndKeepsIdentityVendorId()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        DualShock4Device device = server.AddDualShock4(DualShock4Variant.V1, new DualShock4Identity
        {
            IdVendor = 0x0F0F
        });

        Assert.That(device.Descriptors.IdVendor, Is.EqualTo(0x0F0F)); // identity-driven
        Assert.That(device.Descriptors.IdProduct, Is.EqualTo(0x05C4)); // the V1 variant pins the reference v1 id
    }

    [Test]
    public void AddPersonas_AllVariants_AssignBusIdsInRegistrationOrder()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);

        server.AddDualShock4();
        server.AddDualShock4(DualShock4Variant.V1);
        server.AddDualSense();
        server.AddDualSense(true);
        server.AddXbox360();

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "2-1", "3-1", "4-1", "5-1"
        }));
    }

    [Test]
    public void TryAttach_DifferentPersonas_ForwardsEachBusIdToTheBoundEndPoint()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device dualShock4 = server.AddDualShock4();
        DualSenseDevice dualSense = server.AddDualSense();
        Xbox360Device xbox360 = server.AddXbox360();

        Assert.That(server.TryAttach(dualShock4, out string? _), Is.True);
        Assert.That(server.TryAttach(dualSense, out string? _), Is.True);
        Assert.That(server.TryAttach(xbox360, out string? _), Is.True);

        Assert.That(strategy.AttachCalls, Has.Count.EqualTo(3));
        Assert.That(strategy.AttachCalls[0].BusId, Is.EqualTo("1-1"));
        Assert.That(strategy.AttachCalls[1].BusId, Is.EqualTo("2-1"));
        Assert.That(strategy.AttachCalls[2].BusId, Is.EqualTo("3-1"));
        foreach (RecordedAttachCall call in strategy.AttachCalls)
        {
            Assert.That(call.Port, Is.EqualTo(server.BoundEndPoint.Port)); // every persona attaches through the one bound endpoint
        }
    }

    [Test]
    public void TryDetach_NonDualShock4Persona_ForwardsBusIdAndResetsAttachState()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        Xbox360Device device = server.AddXbox360();
        Assert.That(server.TryAttach(device, out string? _), Is.True);

        bool detached = server.TryDetach(device, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(strategy.DetachCalls, Is.EqualTo(new[]
        {
            "1-1"
        }));
    }

    [Test]
    public void TryAttach_StrategyFails_ReturnsFalseAndSurfacesStrategyError()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "usbip.exe attach failed with exit code 1: the remote computer refused the network connection"
        };
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();

        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Is.EqualTo(strategy.AttachError));
    }

    [Test]
    public void TryAttach_FailedAttach_DoesNotMarkDeviceAttached()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "attach failed"
        };
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();

        server.TryAttach(device, out string? _);
        strategy.AttachResult = true;
        strategy.AttachError = null;
        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(strategy.AttachCalls, Has.Count.EqualTo(2)); // a failed attach leaves the device retryable
    }

    [Test]
    public void TryAttach_DeviceNotAdded_ReturnsFalseWithoutCallingStrategy()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = new DualShock4Device(new DualShock4Identity());

        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("AddDualShock4"));
        Assert.That(strategy.AttachCalls, Is.Empty);
    }

    [Test]
    public void TryAttach_AlreadyAttachedDevice_ReturnsFalseWithoutCallingStrategyAgain()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();
        Assert.That(server.TryAttach(device, out string? _), Is.True);

        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("1-1"));
        Assert.That(strategy.AttachCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryAttach_ServerNotStarted_ThrowsInvalidOperationException()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        DualShock4Device device = server.AddDualShock4();

        Assert.Throws<InvalidOperationException>(() => server.TryAttach(device, out string? _));
        Assert.That(strategy.AttachCalls, Is.Empty);
    }

    [Test]
    public void TryDetach_AttachedDevice_ForwardsBusIdAndResetsAttachState()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();
        Assert.That(server.TryAttach(device, out string? _), Is.True);

        bool detached = server.TryDetach(device, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(strategy.DetachCalls, Is.EqualTo(new[]
        {
            "1-1"
        }));

        // The attach state was reset: a second detach asks nothing of the strategy.
        Assert.That(server.TryDetach(device, out string? secondError), Is.False);
        Assert.That(secondError, Is.Not.Null);
        Assert.That(strategy.DetachCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryDetach_DeviceNotAttached_ReturnsFalseWithoutCallingStrategy()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();

        bool detached = server.TryDetach(device, out string? error);

        Assert.That(detached, Is.False);
        Assert.That(error, Does.Contain("1-1"));
        Assert.That(strategy.DetachCalls, Is.Empty);
    }

    [Test]
    public void TryDetach_StrategyFails_ReturnsFalseAndSurfacesStrategyError()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy
        {
            DetachResult = false,
            DetachError = "usbip.exe detach failed with exit code 1"
        };
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device device = server.AddDualShock4();
        Assert.That(server.TryAttach(device, out string? _), Is.True);

        bool detached = server.TryDetach(device, out string? error);

        Assert.That(detached, Is.False);
        Assert.That(error, Is.EqualTo(strategy.DetachError));
    }

    [Test]
    public void RemoveDevice_RegisteredDevice_FreesBusIdForReuse()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        DualShock4Device first = server.AddDualShock4();
        DualShock4Device second = server.AddDualShock4();
        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1", "2-1"
        }));

        bool removed = server.RemoveDevice(first);

        Assert.That(removed, Is.True);
        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "2-1"
        }));

        DualShock4Device third = server.AddDualShock4();

        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "2-1", "1-1"
        }));
        Assert.That(server.TryAttach(third, out string? error), Is.True);
        Assert.That(error, Is.Null);
        Assert.That(strategy.AttachCalls[0].BusId, Is.EqualTo("1-1")); // the freed bus id was reassigned
        Assert.That(second, Is.Not.EqualTo(third));
    }

    [Test]
    public void RemoveDevice_UnknownDevice_ReturnsFalseAndLeavesRegistrationsUntouched()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        using EmulationServer server = CreateServer(strategy);
        server.Start();
        server.AddDualShock4();
        DualShock4Device stranger = new DualShock4Device(new DualShock4Identity());

        bool removed = server.RemoveDevice(stranger);

        Assert.That(removed, Is.False);
        Assert.That(server.BusIds, Is.EqualTo(new[]
        {
            "1-1"
        }));
    }

    [Test]
    public void AddDualShock4_DisposedServer_ThrowsObjectDisposedException()
    {
        FakeAttachStrategy strategy = new FakeAttachStrategy();
        EmulationServer server = CreateServer(strategy);
        server.Dispose();

        Assert.Throws<ObjectDisposedException>(() => server.AddDualShock4());
    }

    [Test]
    public void TryAttach_ExplicitStrategyAndFactory_ExplicitStrategyWinsAndFactoryIsNeverInvoked()
    {
        int factoryCalls = 0;
        FakeAttachStrategy explicitStrategy = new FakeAttachStrategy();
        FakeAttachStrategy factoryStrategy = new FakeAttachStrategy();
        using EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            TimeProvider = new ManualTimeProvider(),
            AttachStrategy = explicitStrategy,
            AttachStrategyFactory = () =>
            {
                factoryCalls++;
                return factoryStrategy;
            }
        });
        server.Start();
        DualShock4Device device = server.AddDualShock4();

        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(factoryCalls, Is.Zero); // an explicit strategy wins before the factory is consulted
        Assert.That(explicitStrategy.AttachCalls, Has.Count.EqualTo(1));
        Assert.That(factoryStrategy.AttachCalls, Is.Empty);
    }

    [Test]
    public void TryAttach_FactoryConfigured_AttachesThroughFactoryStrategyAndResolvesItOnce()
    {
        int factoryCalls = 0;
        FakeAttachStrategy factoryStrategy = new FakeAttachStrategy();
        using EmulationServer server = new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            TimeProvider = new ManualTimeProvider(),
            AttachStrategyFactory = () =>
            {
                factoryCalls++;
                return factoryStrategy;
            }
        });
        server.Start();
        DualShock4Device device = server.AddDualShock4();

        bool attached = server.TryAttach(device, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(factoryCalls, Is.EqualTo(1)); // one resolution per server, at construction
        Assert.That(factoryStrategy.AttachCalls, Has.Count.EqualTo(1));
        Assert.That(factoryStrategy.AttachCalls[0].Host, Is.EqualTo("127.0.0.1"));
        Assert.That(factoryStrategy.AttachCalls[0].Port, Is.EqualTo(server.BoundEndPoint.Port));
        Assert.That(factoryStrategy.AttachCalls[0].BusId, Is.EqualTo("1-1"));
    }

    [Test]
    public void Constructor_FactoryReturnsNull_ThrowsInvalidOperationExceptionNamingTheFactory()
    {
        EmulationServerOptions options = new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            TimeProvider = new ManualTimeProvider(),
            AttachStrategyFactory = () => null!
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new EmulationServer(options))!;

        Assert.That(exception.Message, Does.Contain("AttachStrategyFactory"));
    }

    /// <summary>
    /// Creates the facade under test: loopback port 0 (the port is read back from
    /// <see cref="EmulationServer.BoundEndPoint"/>), a manual time provider so no pacer ever ticks,
    /// and the given fake attach strategy.
    /// </summary>
    /// <param name="strategy">Fake attach strategy recording every attach and detach call.</param>
    /// <returns>The facade; the caller owns its lifetime.</returns>
    private static EmulationServer CreateServer(FakeAttachStrategy strategy)
    {
        return new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            TimeProvider = new ManualTimeProvider(),
            AttachStrategy = strategy
        });
    }

    /// <summary>
    /// <see cref="IUsbIpAttachStrategy"/> test double: records every attach and detach call and
    /// answers with the configured result, so the facade's bookkeeping is tested with no usbip.exe.
    /// </summary>
    private sealed class FakeAttachStrategy : IUsbIpAttachStrategy
    {
        private readonly List<RecordedAttachCall> _attachCalls = new List<RecordedAttachCall>();

        private readonly List<string> _detachCalls = new List<string>();

        /// <summary>Result returned by every attach call; defaults to success.</summary>
        public bool AttachResult { get; set; } = true;

        /// <summary>Error returned by every attach call; <see langword="null"/> on success.</summary>
        public string? AttachError { get; set; }

        /// <summary>Result returned by every detach call; defaults to success.</summary>
        public bool DetachResult { get; set; } = true;

        /// <summary>Error returned by every detach call; <see langword="null"/> on success.</summary>
        public string? DetachError { get; set; }

        /// <summary>Arguments of every attach call, in invocation order.</summary>
        public IReadOnlyList<RecordedAttachCall> AttachCalls
        {
            get
            {
                return _attachCalls;
            }
        }

        /// <summary>Bus ids of every detach call, in invocation order.</summary>
        public IReadOnlyList<string> DetachCalls
        {
            get
            {
                return _detachCalls;
            }
        }

        /// <summary>Records one attach call and answers with the configured result.</summary>
        /// <param name="host">Host the facade forwarded.</param>
        /// <param name="port">Port the facade forwarded.</param>
        /// <param name="busId">Bus id the facade forwarded.</param>
        /// <param name="error">Configured attach error.</param>
        /// <returns>The configured attach result.</returns>
        public bool TryAttach(string host, int port, string busId, out string? error)
        {
            _attachCalls.Add(new RecordedAttachCall(host, port, busId));
            error = AttachError;
            return AttachResult;
        }

        /// <summary>Records one detach call and answers with the configured result.</summary>
        /// <param name="busId">Bus id the facade forwarded.</param>
        /// <param name="error">Configured detach error.</param>
        /// <returns>The configured detach result.</returns>
        public bool TryDetach(string busId, out string? error)
        {
            _detachCalls.Add(busId);
            error = DetachError;
            return DetachResult;
        }
    }

    /// <summary>One attach call captured by <see cref="FakeAttachStrategy"/>.</summary>
    private readonly struct RecordedAttachCall
    {
        /// <summary>
        /// Initializes a record of one attach call with the forwarded host, port and bus id.
        /// </summary>
        /// <param name="host">Host the facade forwarded.</param>
        /// <param name="port">Port the facade forwarded.</param>
        /// <param name="busId">Bus id the facade forwarded.</param>
        public RecordedAttachCall(string host, int port, string busId)
        {
            Host = host;
            Port = port;
            BusId = busId;
        }

        /// <summary>Host the facade forwarded.</summary>
        public string Host { get; }

        /// <summary>Port the facade forwarded.</summary>
        public int Port { get; }

        /// <summary>Bus id the facade forwarded.</summary>
        public string BusId { get; }
    }
}