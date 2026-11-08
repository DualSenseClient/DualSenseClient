using DualSenseClient.Controllers.TouchpadMouse;

namespace DualSenseClient.Tests.Controllers.TouchpadMouse;

public class TouchpadMouseProcessorTests
{
    [Test]
    public void Process_OneFingerMoves_EmitsRelativeMovement()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 5);
        processor.Process(TouchpadMouseTestData.State((110, 106)), false, 15);

        Assert.That(injector.Moves, Has.Count.EqualTo(1));
        Assert.That(injector.Moves[0], Is.EqualTo((10, 6)));
    }

    [Test]
    public void Process_RestingFinger_EmitsNothing()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 5);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 15);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 25);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 35);

        Assert.That(injector.Moves, Is.Empty);
    }

    [Test]
    public void Process_TinyJitter_EmitsNothing()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 5);
        processor.Process(TouchpadMouseTestData.State((101, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 15);
        processor.Process(TouchpadMouseTestData.State((101, 100)), false, 20);

        Assert.That(injector.Moves, Is.Empty);
    }

    [Test]
    public void Process_SingleFingerOnSecondSlot_MovesPointer()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State(touch2: (300, 300)), false, 5);
        processor.Process(TouchpadMouseTestData.State(touch2: (310, 306)), false, 15);

        Assert.That(injector.Moves, Has.Count.EqualTo(1));
        Assert.That(injector.Moves[0], Is.EqualTo((10, 6)));
    }

    [Test]
    public void Process_TwoFingersDown_DoesNotMovePointer()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100), (200, 100)), false, 5);
        processor.Process(TouchpadMouseTestData.State((110, 106), (210, 106)), false, 15);

        Assert.That(injector.Moves, Is.Empty);
    }

    [Test]
    public void Process_FingerCountChange_DoesNotJump()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 5);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 15);
        processor.Process(TouchpadMouseTestData.State((700, 700)), false, 25);

        Assert.That(injector.Moves, Is.Empty);
    }

    [Test]
    public void Process_SecondFingerLift_ResumesPointerFromRemainingFinger()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 5);
        processor.Process(TouchpadMouseTestData.State((700, 700)), false, 15);
        processor.Process(TouchpadMouseTestData.State((710, 700)), false, 25);

        Assert.That(injector.Moves, Has.Count.EqualTo(1));
        Assert.That(injector.Moves[0].Dx, Is.EqualTo(10));
    }

    [Test]
    public void Process_HighReportRate_PreservesTotalMovement()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 5);
        for (int i = 1; i <= 100; i++)
        {
            processor.Process(TouchpadMouseTestData.State(((ushort)(100 + i), 100)), false, 5 + i);
        }

        Assert.That(injector.Moves.Sum(m => m.Dx), Is.InRange(95, 110));
    }

    [Test]
    public void Process_TrackingIdChange_RebasesWithoutJump()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100), tracking1: 1), false, 5);
        processor.Process(TouchpadMouseTestData.State((110, 100), tracking1: 1), false, 15);
        int movesBeforeIdChange = injector.Moves.Count;

        processor.Process(TouchpadMouseTestData.State((300, 300), tracking1: 2), false, 25);
        Assert.That(injector.Moves, Has.Count.EqualTo(movesBeforeIdChange));

        processor.Process(TouchpadMouseTestData.State((305, 300), tracking1: 2), false, 35);
        Assert.That(injector.Moves, Has.Count.EqualTo(movesBeforeIdChange + 1));
        Assert.That(injector.Moves[^1].Dx, Is.EqualTo(5));
    }

    [Test]
    public void Process_OneFingerTap_EmitsLeftClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State(), false, 110);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));
    }

    [Test]
    public void Process_LongTouch_DoesNotClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State(), false, 400);

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Process_MovedTouch_DoesNotClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State((200, 100)), false, 60);
        processor.Process(TouchpadMouseTestData.State(), false, 70);

        // The move itself is expected; only the click is forbidden.
        Assert.That(injector.Moves, Is.Not.Empty);
        AssertNoClicks(injector);
    }

    [Test]
    public void Process_TwoFingerTap_EmitsRightClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100), (200, 200)), false, 50);
        processor.Process(TouchpadMouseTestData.State(), false, 110);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "RightDown", "RightUp"
        }));
    }

    [Test]
    public void Process_TwoFingerTap_SecondFingerLate_DoesNotClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100), (200, 200)), false, 170);
        processor.Process(TouchpadMouseTestData.State(), false, 190);

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Process_TwoFingerTap_StaggeredLift_EmitsRightClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100), (200, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 80);
        processor.Process(TouchpadMouseTestData.State(), false, 100);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "RightDown", "RightUp"
        }));
    }

    [Test]
    public void Process_TrackingIdChange_DoesNotClickOnLift()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100), tracking1: 1), false, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100), tracking1: 2), false, 50);
        processor.Process(TouchpadMouseTestData.State(), false, 100);

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Process_FirstReportWithActiveTouch_DoesNotClickOnLift()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State(), false, 50);

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Process_TouchStartingWhilePressed_DoesNotClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), true, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), true, 10);
        processor.Process(TouchpadMouseTestData.State(), true, 50);

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Process_PadPress_EmitsLeftDownAndReleaseEmitsLeftUp()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State(), true, 10);
        processor.Process(TouchpadMouseTestData.State(), false, 100);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));
    }

    [Test]
    public void Process_PressWhileMoving_MovesAndDrags()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State(), true, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100)), true, 20);
        processor.Process(TouchpadMouseTestData.State((105, 100)), true, 30);
        processor.Process(TouchpadMouseTestData.State((105, 100)), false, 40);
        processor.Process(TouchpadMouseTestData.State(), false, 50);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "Move(5,0)", "LeftUp"
        }));
    }

    [Test]
    public void Process_PhysicalClickDuringTap_DoesNotEmitTapClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100)), true, 30);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 40);

        // The physical press must click immediately, before the finger lifts.
        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));

        processor.Process(TouchpadMouseTestData.State(), false, 60);

        // The lift must not add a second tap click.
        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));
    }

    [Test]
    public void Process_TouchStartingWhilePressed_IsNeverATap()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State(), true, 10);
        processor.Process(TouchpadMouseTestData.State((100, 100)), true, 20);
        processor.Process(TouchpadMouseTestData.State(), true, 50);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown"
        }));

        processor.Process(TouchpadMouseTestData.State(), false, 60);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));
    }

    [Test]
    public void Process_AttachWhilePressed_DoesNotInjectDown()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State((100, 100)), true, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 50);

        Assert.That(injector.Calls, Is.Empty);
    }

    [Test]
    public void Process_FailedLeftDown_DoesNotEmitUp()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        injector.FailLeftDown = true;
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State(), true, 10);
        processor.Process(TouchpadMouseTestData.State(), false, 100);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown"
        }));
    }

    [Test]
    public void Reset_ReleasesHeldLeftButton()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State(), true, 10);
        processor.Reset();

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));
    }

    [Test]
    public void Process_TwoFingerSlideDown_EmitsNegativeWheel()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        processor.Process(TouchpadMouseTestData.State((500, 550), (600, 550)), false, 20);

        Assert.That(injector.Wheels, Is.EqualTo(new[]
        {
            -55
        }));
        Assert.That(injector.Moves, Is.Empty);
    }

    [Test]
    public void Process_NaturalScrolling_EmitsPositiveWheel()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.UpdateOptions(new TouchpadMouseOptions(true, 1.0, 1.0, true));
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        processor.Process(TouchpadMouseTestData.State((500, 550), (600, 550)), false, 20);

        Assert.That(injector.Wheels, Is.EqualTo(new[]
        {
            55
        }));
    }

    [Test]
    public void Process_TwoFingerSlideRight_EmitsPositiveHorizontalWheel()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (500, 600)), false, 10);
        processor.Process(TouchpadMouseTestData.State((550, 500), (550, 600)), false, 20);

        Assert.That(injector.HorizontalWheels, Is.EqualTo(new[]
        {
            55
        }));
        Assert.That(injector.Wheels, Is.Empty);
    }

    [Test]
    public void Process_NaturalHorizontal_FlipsDirection()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.UpdateOptions(new TouchpadMouseOptions(true, 1.0, 1.0, true));
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (500, 600)), false, 10);
        processor.Process(TouchpadMouseTestData.State((550, 500), (550, 600)), false, 20);

        Assert.That(injector.HorizontalWheels, Is.EqualTo(new[]
        {
            -55
        }));
    }

    [Test]
    public void Process_MovementBelowScrollThreshold_DoesNotScroll()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        processor.Process(TouchpadMouseTestData.State((500, 510), (600, 510)), false, 20);

        Assert.That(injector.Wheels, Is.Empty);
    }

    [Test]
    public void Process_ScrollSpeed_ScalesWheelOutput()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.UpdateOptions(new TouchpadMouseOptions(true, 1.0, 2.0, false));
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        processor.Process(TouchpadMouseTestData.State((500, 550), (600, 550)), false, 20);

        Assert.That(injector.Wheels.Sum(), Is.EqualTo(-111));
    }

    [Test]
    public void Process_LiftAfterScroll_DoesNotRightClick()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        processor.Process(TouchpadMouseTestData.State((500, 550), (600, 550)), false, 20);
        processor.Process(TouchpadMouseTestData.State(), false, 30);

        Assert.That(injector.Calls, Does.Not.Contain("RightDown"));
        Assert.That(injector.Calls, Does.Not.Contain("RightUp"));
    }

    [Test]
    public void Process_OneFingerRestJitter_StillLeftClicks()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100)), false, 10);
        for (int i = 0; i < 100; i++)
        {
            ushort jitter = (ushort)(i % 2);
            processor.Process(TouchpadMouseTestData.State(((ushort)(100 + jitter), 100)), false, 10 + i);
        }

        processor.Process(TouchpadMouseTestData.State(), false, 150);

        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "LeftDown", "LeftUp"
        }));
        Assert.That(injector.Moves, Is.Empty);
    }

    [Test]
    public void Process_TwoFingersRestingJitter_DoesNotScrollAndStillRightClicks()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((100, 100), (200, 200)), false, 10);
        for (int i = 0; i < 100; i++)
        {
            ushort jitter = (ushort)(i % 2);
            processor.Process(
                TouchpadMouseTestData.State(((ushort)(100 + jitter), 100), ((ushort)(200 + jitter), 200)),
                false,
                10 + i);
        }

        processor.Process(TouchpadMouseTestData.State(), false, 150);

        Assert.That(injector.Wheels, Is.Empty);
        Assert.That(injector.Calls, Is.EqualTo(new[]
        {
            "RightDown", "RightUp"
        }));
    }

    [Test]
    public void Process_SubUnitScrollMovement_AccumulatesWheel()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        for (int i = 1; i <= 30; i++)
        {
            ushort y = (ushort)(500 + i);
            processor.Process(TouchpadMouseTestData.State((500, y), (600, y)), false, 10 + i);
        }

        Assert.That(injector.Wheels, Is.Not.Empty);
    }

    [Test]
    public void Process_SecondFingerLift_StopsWheelOutput()
    {
        TouchpadMouseProcessor processor = CreateProcessor(out RecordingMouseInjector injector);
        processor.Process(TouchpadMouseTestData.State(), false, 0);
        processor.Process(TouchpadMouseTestData.State((500, 500), (600, 500)), false, 10);
        for (int i = 1; i <= 3; i++)
        {
            ushort y = (ushort)(500 + i * 10);
            processor.Process(TouchpadMouseTestData.State((500, y), (600, y)), false, 10 + i * 10);
        }

        Assert.That(injector.Wheels, Is.Not.Empty);
        int wheelsBeforeLift = injector.Wheels.Count;

        processor.Process(TouchpadMouseTestData.State((500, 530)), false, 50);
        for (int i = 1; i <= 3; i++)
        {
            ushort y = (ushort)(530 + i * 10);
            processor.Process(TouchpadMouseTestData.State((500, y)), false, 50 + i * 10);
        }

        Assert.That(injector.Wheels, Has.Count.EqualTo(wheelsBeforeLift));
    }

    private static void AssertNoClicks(RecordingMouseInjector injector)
    {
        Assert.That(
            injector.Calls,
            Has.None.EqualTo("LeftDown")
                .And.None.EqualTo("LeftUp")
                .And.None.EqualTo("RightDown")
                .And.None.EqualTo("RightUp"));
    }

    private static TouchpadMouseProcessor CreateProcessor(out RecordingMouseInjector injector)
    {
        injector = new RecordingMouseInjector();
        TouchpadMouseProcessor processor = new TouchpadMouseProcessor(injector);
        processor.UpdateOptions(new TouchpadMouseOptions(true, 1.0, 1.0, false));
        return processor;
    }
}