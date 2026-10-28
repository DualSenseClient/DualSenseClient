using System.Diagnostics;
using DualSenseClient.Controllers.DualSense.Lightbar;

namespace DualSenseClient.Tests.Controllers.DualSense.Lightbar;

public class LightbarEffectClockTests
{
    [Test]
    public void Elapsed_AdvancesWithTimestamps()
    {
        LightbarEffectClock clock = new LightbarEffectClock();
        clock.Reset(Ticks(0));

        Assert.That(clock.GetElapsedSeconds(Ticks(2)), Is.EqualTo(2.0).Within(1e-6));
    }

    [Test]
    public void Suspended_FreezesElapsed()
    {
        LightbarEffectClock clock = new LightbarEffectClock();
        clock.Reset(Ticks(0));
        clock.Suspended = true;
        clock.GetElapsedSeconds(Ticks(2));

        Assert.That(clock.GetElapsedSeconds(Ticks(5)), Is.EqualTo(2.0).Within(1e-6));
    }

    [Test]
    public void Resume_ContinuesFromFrozenPhase()
    {
        LightbarEffectClock clock = new LightbarEffectClock();
        clock.Reset(Ticks(0));
        clock.Suspended = true;
        clock.GetElapsedSeconds(Ticks(2));
        clock.GetElapsedSeconds(Ticks(5));
        clock.Suspended = false;
        clock.GetElapsedSeconds(Ticks(5));

        Assert.That(clock.GetElapsedSeconds(Ticks(6)), Is.EqualTo(3.0).Within(1e-6));
    }

    [Test]
    public void Reset_ReturnsToZero()
    {
        LightbarEffectClock clock = new LightbarEffectClock();
        clock.Reset(Ticks(0));
        clock.GetElapsedSeconds(Ticks(3));

        clock.Reset(Ticks(10));

        Assert.That(clock.GetElapsedSeconds(Ticks(10)), Is.Zero.Within(1e-6));
    }

    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);
}