using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Core;

/// <summary>
/// Tests of <see cref="UsbFrameClock"/>: one timer tick per USB frame, start/stop lifecycle,
/// skip-don't-queue behavior for overlapping ticks, fault containment and timer disposal, driven
/// deterministically by <see cref="ManualTimeProvider"/>.
/// </summary>
[TestFixture]
public sealed class UsbFrameClockTests
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(1);

    [Test]
    public void FrameClock_AdvanceOneFrame_RaisesExactlyOneTick()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        using UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);
        int ticks = 0;
        clock.Start(() => ticks++);

        timeProvider.Advance(FrameDuration);

        Assert.That(ticks, Is.EqualTo(1));

        timeProvider.Advance(FrameDuration);

        Assert.That(ticks, Is.EqualTo(2));
    }

    [Test]
    public void FrameClock_Stop_StopsTicksAndIsIdempotent()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        using UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);
        int ticks = 0;
        clock.Start(() => ticks++);
        timeProvider.Advance(FrameDuration);

        clock.Stop();
        clock.Stop();

        Assert.That(clock.IsRunning, Is.False);
        Assert.That(timeProvider.TimerCount, Is.Zero);
        timeProvider.Advance(5 * FrameDuration);
        Assert.That(ticks, Is.EqualTo(1));
    }

    [Test]
    public void FrameClock_Dispose_StopsTicksAndDisposesTimer()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);
        int ticks = 0;
        clock.Start(() => ticks++);
        timeProvider.Advance(FrameDuration);

        clock.Dispose();
        clock.Dispose();

        Assert.That(clock.IsRunning, Is.False);
        Assert.That(timeProvider.TimerCount, Is.Zero);
        timeProvider.Advance(5 * FrameDuration);
        Assert.That(ticks, Is.EqualTo(1));
    }

    [Test]
    public void FrameClock_TickHandlerThrows_KeepsTicking()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        using UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);
        int ticks = 0;
        clock.Start(() =>
        {
            ticks++;
            if (ticks == 1)
            {
                throw new InvalidOperationException("Synthetic frame handler failure");
            }
        });

        Assert.That(() => timeProvider.Advance(3 * FrameDuration), Throws.Nothing);

        Assert.That(ticks, Is.EqualTo(3));
        Assert.That(clock.IsRunning, Is.True);
    }

    [Test]
    public void FrameClock_TickHandlerAdvancesTime_OverlappingTickIsSkipped()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        using UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);
        int ticks = 0;
        int depth = 0;
        int maxDepth = 0;
        clock.Start(() =>
        {
            depth++;
            maxDepth = Math.Max(maxDepth, depth);
            ticks++;
            if (ticks == 1)
            {
                // Advancing from inside a tick makes the next frame due while this one is still running.
                timeProvider.Advance(FrameDuration);
            }

            depth--;
        });

        timeProvider.Advance(FrameDuration);

        Assert.That(ticks, Is.EqualTo(1));
        Assert.That(maxDepth, Is.EqualTo(1));
    }

    [Test]
    public void FrameClock_StartWhileRunning_ThrowsInvalidOperationException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        using UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);
        clock.Start(DoNothing);

        Assert.That(() => clock.Start(DoNothing), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void FrameClock_StartNullHandler_ThrowsArgumentNullException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        using UsbFrameClock clock = new UsbFrameClock(timeProvider, FrameDuration);

        Assert.That(() => clock.Start(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void FrameClock_ConstructorNullTimeProvider_ThrowsArgumentNullException() =>
        Assert.That(() => new UsbFrameClock(null!, FrameDuration), Throws.TypeOf<ArgumentNullException>());

    [Test]
    public void FrameClock_ConstructorNonPositiveFrameDuration_ThrowsArgumentOutOfRangeException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();

        Assert.That(() => new UsbFrameClock(timeProvider, TimeSpan.Zero), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => new UsbFrameClock(timeProvider, TimeSpan.FromMilliseconds(-1)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static void DoNothing()
    {
    }
}