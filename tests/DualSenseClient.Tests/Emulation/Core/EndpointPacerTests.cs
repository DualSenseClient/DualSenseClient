using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

/// <summary>
/// Tests of <see cref="EndpointPacer"/>: tick cadence, start/stop lifecycle, fault containment and
/// the skip-don't-queue rule for slow ticks, driven deterministically by <see cref="ManualTimeProvider"/>.
/// </summary>
[TestFixture]
public sealed class EndpointPacerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(4);

    [Test]
    public void Start_AdvanceThreeIntervals_FiresExactlyThreeTicks()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        int ticks = 0;

        pacer.Start(() => ticks++);

        timeProvider.Advance(3 * Interval);

        Assert.That(ticks, Is.EqualTo(3));
    }

    [Test]
    public void Start_AdvanceHalfInterval_FiresNoTicks()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        int ticks = 0;
        pacer.Start(() => ticks++);

        timeProvider.Advance(Interval / 2);

        Assert.That(ticks, Is.Zero);
        Assert.That(pacer.IsRunning, Is.True);

        timeProvider.Advance(Interval / 2); // exactly one interval has passed now

        Assert.That(ticks, Is.EqualTo(1));
    }

    [Test]
    public void Stop_WhileRunning_SuppressesFurtherTicks()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        int ticks = 0;
        pacer.Start(() => ticks++);
        timeProvider.Advance(2 * Interval);
        Assert.That(ticks, Is.EqualTo(2));

        pacer.Stop();

        Assert.That(pacer.IsRunning, Is.False);
        timeProvider.Advance(10 * Interval);
        Assert.That(ticks, Is.EqualTo(2));
    }

    [Test]
    public void Stop_WithoutStart_DoesNothing()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);

        pacer.Stop();

        Assert.That(pacer.IsRunning, Is.False);
        timeProvider.Advance(Interval);
    }

    [Test]
    public void Start_AfterStop_TicksAgain()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        int ticks = 0;
        pacer.Start(() => ticks++);
        timeProvider.Advance(Interval);
        pacer.Stop();

        pacer.Start(() => ticks++);
        timeProvider.Advance(Interval);

        Assert.That(pacer.IsRunning, Is.True);
        Assert.That(ticks, Is.EqualTo(2));
    }

    [Test]
    public void TickHandlerThrows_PacerKeepsTicking()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        int invocations = 0;
        pacer.Start(() =>
        {
            invocations++;
            if (invocations == 1)
            {
                throw new InvalidOperationException("Synthetic tick handler failure");
            }
        });

        Assert.That(() => timeProvider.Advance(3 * Interval), Throws.Nothing);

        Assert.That(invocations, Is.EqualTo(3));
        Assert.That(pacer.IsRunning, Is.True);
    }

    [Test]
    public void TickHandlerAdvancesTime_OverlappingTick_IsSkippedNotQueued()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        int ticks = 0;
        int depth = 0;
        int maxDepth = 0;
        pacer.Start(() =>
        {
            depth++;
            maxDepth = Math.Max(maxDepth, depth);
            ticks++;
            if (ticks == 1)
            {
                // Advancing from inside a tick makes the next tick due while this one is still running.
                timeProvider.Advance(Interval);
            }

            depth--;
        });

        timeProvider.Advance(Interval);

        Assert.That(ticks, Is.EqualTo(1));
        Assert.That(maxDepth, Is.EqualTo(1));
    }

    [Test]
    public void Start_WhileAlreadyRunning_ThrowsInvalidOperationException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);
        pacer.Start(DoNothing);

        Assert.That(() => pacer.Start(DoNothing), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Start_NullHandler_ThrowsArgumentNullException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        EndpointPacer pacer = new EndpointPacer(timeProvider, Interval);

        Assert.That(() => pacer.Start(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_NullTimeProvider_ThrowsArgumentNullException() =>
        Assert.That(() => new EndpointPacer(null!, Interval), Throws.TypeOf<ArgumentNullException>());

    [Test]
    public void Constructor_NonPositiveInterval_ThrowsArgumentOutOfRangeException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();

        Assert.That(() => new EndpointPacer(timeProvider, TimeSpan.Zero), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new EndpointPacer(timeProvider, TimeSpan.FromMilliseconds(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static void DoNothing()
    {
    }
}