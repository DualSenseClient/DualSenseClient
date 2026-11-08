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

    private static TouchpadMouseProcessor CreateProcessor(out RecordingMouseInjector injector)
    {
        injector = new RecordingMouseInjector();
        TouchpadMouseProcessor processor = new TouchpadMouseProcessor(injector);
        processor.UpdateOptions(new TouchpadMouseOptions(true, 1.0, 1.0, false));
        return processor;
    }
}