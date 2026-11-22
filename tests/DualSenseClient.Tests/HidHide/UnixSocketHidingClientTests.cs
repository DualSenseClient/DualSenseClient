using DualSenseClient.HidHide;
using DualSenseClient.Tests.HidHide.TestSupport;

namespace DualSenseClient.Tests.HidHide;

/// <summary>
/// Tests of the Unix socket hiding client against a fake helper.
/// </summary>
[TestFixture]
[Platform("Linux")]
public sealed class UnixSocketHidingClientTests
{
    /// <summary>
    /// Temporary socket directory.
    /// </summary>
    private string _tempDir = null!;

    /// <summary>
    /// Socket path under the temporary directory.
    /// </summary>
    private string _socketPath = null!;

    /// <summary>
    /// Creates an isolated socket directory.
    /// </summary>
    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UnixSocketHidingClientTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _socketPath = Path.Combine(_tempDir, "hiding.sock");
    }

    /// <summary>
    /// Removes the temporary directory.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A protocol-1 handshake succeeds and sends the hello line.
    /// </summary>
    [Test]
    public void TryConnect_SendsHelloAndAcceptsProtocolOne()
    {
        using FakeHidingServer server = FakeHidingServer.Start(
            _socketPath,
            line => line == "HELLO 1" ? "OK 1 1.0.0 revoke=hidraw,evdev fallback=rebind" : "ERR EINVAL unexpected");
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);

        Assert.That(client.TryConnect(out string error), Is.True);
        Assert.That(error, Is.Empty);
        Assert.That(server.Requests, Does.Contain("HELLO 1"));
    }

    /// <summary>
    /// A protocol-2 helper is rejected with a protocol error.
    /// </summary>
    [Test]
    public void TryConnect_RejectsProtocolTwo()
    {
        using FakeHidingServer server = FakeHidingServer.Start(_socketPath, _ => "OK 2 2.0.0");
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);

        Assert.That(client.TryConnect(out string error), Is.False);
        Assert.That(error, Does.Contain("protocol"));
    }

    /// <summary>
    /// Hiding sends HIDE and accepts OK.
    /// </summary>
    [Test]
    public void TrySetHidden_SendsHideAndParsesOk()
    {
        using FakeHidingServer server = FakeHidingServer.Start(_socketPath, line =>
            line == "HELLO 1" ? "OK 1 1.0.0" :
            line.StartsWith("HIDE ") ? "OK" :
            "ERR EINVAL unexpected");
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);
        Assume.That(client.TryConnect(out _), Is.True);

        Assert.That(client.TrySetHidden("d0:bc:c1:17:55:63", true, out string error), Is.True);
        Assert.That(error, Is.Empty);
        Assert.That(server.Requests, Does.Contain("HIDE d0:bc:c1:17:55:63"));
    }

    /// <summary>
    /// An ERR response is surfaced as a failed request.
    /// </summary>
    [Test]
    public void TrySetHidden_ParsesErr()
    {
        using FakeHidingServer server = FakeHidingServer.Start(_socketPath, line =>
            line == "HELLO 1" ? "OK 1 1.0.0" : "ERR EACCES denied");
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);
        Assume.That(client.TryConnect(out _), Is.True);

        Assert.That(client.TrySetHidden("d0:bc:c1:17:55:63", true, out string error), Is.False);
        Assert.That(error, Does.Contain("EACCES"));
    }

    /// <summary>
    /// Status responses are parsed for hidden and visible states.
    /// </summary>
    [Test]
    public void TryGetStatus_ParsesHiddenAndVisible()
    {
        int statusCalls = 0;
        using FakeHidingServer server = FakeHidingServer.Start(_socketPath, line =>
        {
            if (line == "HELLO 1")
            {
                return "OK 1 1.0.0";
            }

            if (line.StartsWith("STATUS "))
            {
                return Interlocked.Increment(ref statusCalls) == 1 ? "HIDDEN" : "VISIBLE";
            }

            return "ERR EINVAL unexpected";
        });
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);
        Assume.That(client.TryConnect(out _), Is.True);

        Assert.That(client.TryGetStatus("d0:bc:c1:17:55:63", out bool hidden, out _), Is.True);
        Assert.That(hidden, Is.True);
        Assert.That(client.TryGetStatus("d0:bc:c1:17:55:63", out hidden, out _), Is.True);
        Assert.That(hidden, Is.False);
    }

    /// <summary>
    /// Failed connects must close their sockets instead of leaking descriptors.
    /// </summary>
    [Test]
    public void TryConnect_WhenSocketMissing_DoesNotLeakFileDescriptors()
    {
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);
        int before = Directory.GetFiles("/proc/self/fd").Length;

        for (int i = 0; i < 50; i++)
        {
            Assert.That(client.TryConnect(out _), Is.False);
        }

        int after = Directory.GetFiles("/proc/self/fd").Length;
        Assert.That(after - before, Is.LessThanOrEqualTo(2));
    }

    /// <summary>
    /// A dropped connection is retried once with a fresh handshake.
    /// </summary>
    [Test]
    public void Request_WhenServerDisconnects_ReconnectsOnNextCall()
    {
        using FakeHidingServer server = FakeHidingServer.Start(_socketPath, line =>
            line.StartsWith("STATUS ") ? "HIDDEN" : "OK 1 1.0.0");
        using UnixSocketHidingClient client = new UnixSocketHidingClient(_socketPath);

        Assert.That(client.TryConnect(out _), Is.True);
        Assert.That(client.TryGetStatus("d0:bc:c1:17:55:63", out bool first, out _), Is.True);
        Assert.That(first, Is.True);

        server.CloseConnection();

        Assert.That(client.TryGetStatus("d0:bc:c1:17:55:63", out bool second, out _), Is.True);
        Assert.That(second, Is.True);
        Assert.That(server.ConnectionCount, Is.EqualTo(2));
    }
}