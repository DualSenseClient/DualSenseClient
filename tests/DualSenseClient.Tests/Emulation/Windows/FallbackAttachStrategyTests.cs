using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.Windows;

namespace DualSenseClient.Tests.Emulation.Windows;

/// <summary>
/// Tests of <see cref="FallbackAttachStrategy"/>: the attach composition (primary first, fallback
/// only after a primary failure, warning carrying the primary error, aggregated error when both
/// fail) and the winner-aware detach (route the detach to the strategy that attached the device,
/// probe primary-then-fallback without a known winner, keep the winner when a detach fails). All
/// runs go through <see cref="FakeAttachStrategy"/>, so the tests are hermetic and need no USB/IP
/// backend.
/// </summary>
[TestFixture]
public sealed class FallbackAttachStrategyTests
{
    private const string Host = "127.0.0.1";

    private const int Port = 3240;

    private const string BusId = "1-1";

    [Test]
    public void TryAttach_PrimarySucceeds_AttachesThroughPrimaryAndNeverCallsFallback()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(primary.AttachCallCount, Is.EqualTo(1));
        Assert.That(primary.AttachCalls[0].Host, Is.EqualTo(Host));
        Assert.That(primary.AttachCalls[0].Port, Is.EqualTo(Port));
        Assert.That(primary.AttachCalls[0].BusId, Is.EqualTo(BusId));
        Assert.That(fallback.AttachCallCount, Is.EqualTo(0), "The primary succeeded, so the fallback must never be called.");
    }

    [Test]
    public void TryAttach_PrimaryFails_LogsWarningWithPrimaryErrorAndUsesFallback()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "native boom"
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true
        };
        RecordingLogger logger = new RecordingLogger();
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(fallback.AttachCallCount, Is.EqualTo(1));
        Assert.That(fallback.AttachCalls[0].Host, Is.EqualTo(Host));
        Assert.That(fallback.AttachCalls[0].Port, Is.EqualTo(Port));
        Assert.That(fallback.AttachCalls[0].BusId, Is.EqualTo(BusId));
        Assert.That(
            logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning
                                        && entry.Message.Contains("native boom")
                                        && entry.Message.Contains("fallback")),
            Is.True,
            "The warning logged before the fallback must carry the primary error and announce the fallback.");
    }

    [Test]
    public void TryAttach_BothFail_ReturnsAggregatedErrorInMandatedShape()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "native boom"
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "cli boom"
        };
        RecordingLogger logger = new RecordingLogger();
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Is.EqualTo("native: native boom; usbip.exe: cli boom"));
        Assert.That(fallback.AttachCallCount, Is.EqualTo(1));
        Assert.That(
            logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning && entry.Message == error),
            Is.True,
            "The aggregated failure must be logged as a warning.");
    }

    [Test]
    public void TryAttach_BothFail_RecordsNoWinner()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "native boom",
            DetachResult = false,
            DetachError = "native detach boom"
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "cli boom",
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.False);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True, "A failed attach must not record a winner, so the detach must probe primary-then-fallback.");
        Assert.That(error, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(1));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(1));
    }

    [Test]
    public void TryDetach_DeviceAttachedByPrimary_RoutesDetachToPrimaryOnly()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = true,
            DetachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true,
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(1));
        Assert.That(primary.DetachCalls[0].BusId, Is.EqualTo(BusId));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(0), "The primary attached the device, so only the primary may detach it.");
    }

    [Test]
    public void TryDetach_DeviceAttachedByFallback_RoutesDetachToFallbackOnly()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "native boom",
            DetachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true,
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(fallback.DetachCallCount, Is.EqualTo(1));
        Assert.That(fallback.DetachCalls[0].BusId, Is.EqualTo(BusId));
        Assert.That(primary.DetachCallCount, Is.EqualTo(0), "The fallback attached the device, so only the fallback may detach it.");
    }

    [Test]
    public void TryDetach_NoPriorAttach_UsesPrimaryThenFallback()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            DetachResult = false,
            DetachError = "native detach boom"
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(1));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(1));
    }

    [Test]
    public void TryDetach_NoPriorAttach_PrimarySucceeds_NeverCallsFallback()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            DetachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(1));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(0), "The primary detach succeeded, so the fallback must never be called.");
    }

    [Test]
    public void TryDetach_NoPriorAttach_BothFail_ReturnsPrefixedAggregatedError()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            DetachResult = false,
            DetachError = "native detach boom"
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            DetachResult = false,
            DetachError = "cli detach boom"
        };
        RecordingLogger logger = new RecordingLogger();
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback, logger);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.False);
        Assert.That(
            error,
            Is.EqualTo("detach for bus id '1-1' failed: native: native detach boom; usbip.exe: cli detach boom"),
            "The aggregated detach failure must name the operation and keep the mandated native/usbip.exe shape.");
        Assert.That(
            logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning && entry.Message == error),
            Is.True,
            "The aggregated failure must be logged as a warning.");
    }

    [Test]
    public void TryDetach_NoPriorAttach_PrimarySucceeds_RecordsNoWinner()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            DetachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        bool firstDetach = strategy.TryDetach(BusId, out string? firstError);

        primary.DetachResult = false;
        primary.DetachError = "native detach boom";
        bool secondDetach = strategy.TryDetach(BusId, out string? secondError);

        Assert.That(firstDetach, Is.True);
        Assert.That(firstError, Is.Null);
        Assert.That(
            secondDetach,
            Is.True,
            "A successful detach without a winner must not record one; the second detach must probe primary-then-fallback again.");
        Assert.That(secondError, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(2));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(1));
    }

    [Test]
    public void TryDetach_PrimaryWinnerFails_NamesPrimaryAndKeepsWinnerForRetry()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true,
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        primary.DetachResult = false;
        primary.DetachError = "native detach boom";
        bool firstAttempt = strategy.TryDetach(BusId, out string? error);

        primary.DetachResult = true;
        bool retry = strategy.TryDetach(BusId, out string? retryError);

        Assert.That(firstAttempt, Is.False);
        Assert.That(error, Is.EqualTo("native detach for bus id '1-1' failed: native detach boom"));
        Assert.That(retry, Is.True);
        Assert.That(retryError, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(2));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(0), "The fallback must never be consulted while the primary is the winner.");
    }

    [Test]
    public void TryDetach_FallbackWinnerFails_NamesFallbackAndKeepsWinnerForRetry()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "native boom",
            DetachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        fallback.DetachResult = false;
        fallback.DetachError = "cli detach boom";
        bool firstAttempt = strategy.TryDetach(BusId, out string? error);

        fallback.DetachResult = true;
        bool retry = strategy.TryDetach(BusId, out string? retryError);

        Assert.That(firstAttempt, Is.False);
        Assert.That(error, Is.EqualTo("usbip.exe detach for bus id '1-1' failed: cli detach boom"));
        Assert.That(retry, Is.True);
        Assert.That(retryError, Is.Null);
        Assert.That(fallback.DetachCallCount, Is.EqualTo(2));
        Assert.That(primary.DetachCallCount, Is.EqualTo(0), "The primary must never be consulted while the fallback is the winner.");
    }

    [Test]
    public void TryAttach_AfterSuccessfulDetach_RecordsTheNewWinner()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = false,
            AttachError = "native boom",
            DetachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true,
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True, "The fallback must attach while the primary fails.");
        Assert.That(strategy.TryDetach(BusId, out string? _), Is.True, "The fallback that attached must detach.");

        primary.AttachResult = true;
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(1), "After the re-attach the primary is the winner and must detach.");
        Assert.That(fallback.DetachCallCount, Is.EqualTo(1), "Only the first (fallback-won) detach may reach the fallback.");
    }

    [Test]
    public void TryAttach_PrimaryThrows_FallsBackAndReturnsTrueInsteadOfThrowing()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachExceptionToThrow = new InvalidOperationException("synthetic primary fault")
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachResult = true
        };
        RecordingLogger logger = new RecordingLogger();
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback, logger);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(fallback.AttachCallCount, Is.EqualTo(1));
        Assert.That(
            logger.Entries.Any(entry => entry.Level == EmulationLogLevel.Warning && entry.Message.Contains("synthetic primary fault")),
            Is.True,
            "A throwing primary must be reported like any other primary failure.");
    }

    [Test]
    public void TryAttach_BothThrow_ReturnsFalseWithAggregatedFaultsInsteadOfThrowing()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachExceptionToThrow = new InvalidOperationException("synthetic primary fault")
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            AttachExceptionToThrow = new InvalidOperationException("synthetic fallback fault")
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);

        bool attached = strategy.TryAttach(Host, Port, BusId, out string? error);

        Assert.That(attached, Is.False);
        Assert.That(error, Does.Contain("native:"));
        Assert.That(error, Does.Contain("usbip.exe:"));
        Assert.That(error, Does.Contain("synthetic primary fault"));
        Assert.That(error, Does.Contain("synthetic fallback fault"));
    }

    [Test]
    public void TryDetach_WinnerThrows_ReturnsFalseWithNamedErrorAndKeepsWinner()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            AttachResult = true
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            DetachResult = true
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);
        Assert.That(strategy.TryAttach(Host, Port, BusId, out string? _), Is.True);

        primary.DetachExceptionToThrow = new InvalidOperationException("synthetic detach fault");
        bool firstAttempt = strategy.TryDetach(BusId, out string? error);

        primary.DetachExceptionToThrow = null;
        primary.DetachResult = true;
        bool retry = strategy.TryDetach(BusId, out string? retryError);

        Assert.That(firstAttempt, Is.False);
        Assert.That(error, Does.Contain("native detach for bus id '1-1'"));
        Assert.That(error, Does.Contain("synthetic detach fault"));
        Assert.That(retry, Is.True);
        Assert.That(retryError, Is.Null);
        Assert.That(primary.DetachCallCount, Is.EqualTo(2));
        Assert.That(fallback.DetachCallCount, Is.EqualTo(0));
    }

    [Test]
    public void TryDetach_NoPriorAttach_BothThrow_ReturnsFalseWithAggregatedFaultsInsteadOfThrowing()
    {
        FakeAttachStrategy primary = new FakeAttachStrategy
        {
            DetachExceptionToThrow = new InvalidOperationException("synthetic primary fault")
        };
        FakeAttachStrategy fallback = new FakeAttachStrategy
        {
            DetachExceptionToThrow = new InvalidOperationException("synthetic fallback fault")
        };
        FallbackAttachStrategy strategy = CreateStrategy(primary, fallback);

        bool detached = strategy.TryDetach(BusId, out string? error);

        Assert.That(detached, Is.False);
        Assert.That(error, Does.Contain("detach for bus id '1-1' failed:"));
        Assert.That(error, Does.Contain("native:"));
        Assert.That(error, Does.Contain("usbip.exe:"));
        Assert.That(error, Does.Contain("synthetic primary fault"));
        Assert.That(error, Does.Contain("synthetic fallback fault"));
    }

    private static FallbackAttachStrategy CreateStrategy(FakeAttachStrategy primary, FakeAttachStrategy fallback) =>
        CreateStrategy(primary, fallback, new RecordingLogger());

    private static FallbackAttachStrategy CreateStrategy(FakeAttachStrategy primary, FakeAttachStrategy fallback, RecordingLogger logger) =>
        new FallbackAttachStrategy(primary, fallback, logger);
}