using DualSenseClient.HidHide;

namespace DualSenseClient.Tests.HidHide;

/// <summary>
/// Tests of the line protocol shared by the Linux hiding service and helper.
/// </summary>
[TestFixture]
public sealed class HidingProtocolTests
{
    /// <summary>
    /// The hello request advertises the supported protocol version.
    /// </summary>
    [Test]
    public void HelloRequest_ReturnsVersionOne() => Assert.That(HidingProtocol.HelloRequest(), Is.EqualTo("HELLO 1"));

    /// <summary>
    /// The hide request carries the controller ID.
    /// </summary>
    [Test]
    public void HideRequest_ReturnsHideLine() => Assert.That(HidingProtocol.HideRequest("d0:bc:c1:17:55:63"), Is.EqualTo("HIDE d0:bc:c1:17:55:63"));

    /// <summary>
    /// The unhide request carries the controller ID.
    /// </summary>
    [Test]
    public void UnhideRequest_ReturnsUnhideLine() => Assert.That(HidingProtocol.UnhideRequest("d0:bc:c1:17:55:63"), Is.EqualTo("UNHIDE d0:bc:c1:17:55:63"));

    /// <summary>
    /// The status request carries the controller ID.
    /// </summary>
    [Test]
    public void StatusRequest_ReturnsStatusLine() => Assert.That(HidingProtocol.StatusRequest("d0:bc:c1:17:55:63"), Is.EqualTo("STATUS d0:bc:c1:17:55:63"));

    /// <summary>
    /// A successful handshake exposes the protocol and helper version.
    /// </summary>
    [Test]
    public void TryParseHelloResponse_AcceptsProtocolOne()
    {
        bool parsed = HidingProtocol.TryParseHelloResponse(
            "OK 1 1.0.0 revoke=hidraw,evdev fallback=rebind",
            out int version,
            out string helperVersion);

        Assert.That(parsed, Is.True);
        Assert.That(version, Is.EqualTo(1));
        Assert.That(helperVersion, Is.EqualTo("1.0.0"));
    }

    /// <summary>
    /// Malformed or failed handshakes are rejected.
    /// </summary>
    [Test]
    public void TryParseHelloResponse_RejectsMalformedLines()
    {
        Assert.That(HidingProtocol.TryParseHelloResponse("", out _, out _), Is.False);
        Assert.That(HidingProtocol.TryParseHelloResponse("OK", out _, out _), Is.False);
        Assert.That(HidingProtocol.TryParseHelloResponse("ERR EINVAL bad", out _, out _), Is.False);
        Assert.That(HidingProtocol.TryParseHelloResponse("OK one 1.0.0", out _, out _), Is.False);
    }

    /// <summary>
    /// Hidden and visible status responses are parsed.
    /// </summary>
    [Test]
    public void TryParseStatusResponse_ParsesHiddenAndVisible()
    {
        Assert.That(HidingProtocol.TryParseStatusResponse("HIDDEN", out bool hidden), Is.True);
        Assert.That(hidden, Is.True);
        Assert.That(HidingProtocol.TryParseStatusResponse("VISIBLE", out hidden), Is.True);
        Assert.That(hidden, Is.False);
        Assert.That(HidingProtocol.TryParseStatusResponse("OK", out _), Is.False);
    }

    /// <summary>
    /// OK is success; ERR carries the error text.
    /// </summary>
    [Test]
    public void TryParseCommandResponse_ParsesOkAndErr()
    {
        Assert.That(HidingProtocol.TryParseCommandResponse("OK", out string error), Is.True);
        Assert.That(error, Is.Empty);
        Assert.That(HidingProtocol.TryParseCommandResponse("ERR EACCES permission denied", out error), Is.False);
        Assert.That(error, Does.Contain("EACCES"));
        Assert.That(error, Does.Contain("permission denied"));
    }

    /// <summary>
    /// IDs are limited to the marker-safe charset and length.
    /// </summary>
    /// <param name="id">Candidate ID.</param>
    /// <param name="expected">Whether the ID is valid.</param>
    [TestCase("d0:bc:c1:17:55:63", true)]
    [TestCase("abc_DEF-1.2", true)]
    [TestCase("", false)]
    [TestCase(".", false)]
    [TestCase("..", false)]
    [TestCase("has space", false)]
    [TestCase("a/b", false)]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public void IsValidId_ValidatesCharsetAndLength(string id, bool expected) => Assert.That(HidingProtocol.IsValidId(id), Is.EqualTo(expected));
}