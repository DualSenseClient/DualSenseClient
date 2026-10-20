using DualSenseClient.Tests.Emulation.TestSupport;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.UsbIp;

/// <summary>
/// Tests of <see cref="IsoScheduler"/>: one isochronous packet per frame, FIFO transfer handoff
/// without catch-up bursts, cancellation and clearing without completions, packet fault
/// containment and stop/dispose behavior, driven deterministically by <see cref="ManualTimeProvider"/>.
/// </summary>
[TestFixture]
public sealed class IsoSchedulerTests
{
    private static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(1);

    [Test]
    public void Scheduler_TwoPacketTransfer_CompletesOnePacketPerFrame()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<int> packetIndices = new List<int>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            packetIndices.Add(packetIndex);
            IsoPacketDescriptor descriptor = transfer.IsoPackets![packetIndex];
            transfer.Data!.AsSpan((int)descriptor.Offset, (int)descriptor.Length).Fill((byte)(packetIndex + 1));
            return (int)descriptor.Length;
        });
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 8, 8);
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            scheduler.Start();
            scheduler.Enqueue(transfer);

            timeProvider.Advance(FrameDuration);

            Assert.That(packetIndices, Is.EqualTo(new[]
            {
                0
            }));
            Assert.That(completions, Is.Zero);
            Assert.That(scheduler.PendingCount, Is.EqualTo(1));
            Assert.That(transfer.IsoPackets![0].ActualLength, Is.EqualTo(8u));
            Assert.That(transfer.IsoPackets[1].ActualLength, Is.Zero);
            Assert.That(transfer.Data![0..8], Is.All.EqualTo((byte)1));
            Assert.That(transfer.Data[8..16], Is.All.EqualTo((byte)0));

            timeProvider.Advance(FrameDuration);

            Assert.That(packetIndices, Is.EqualTo(new[]
            {
                0, 1
            }));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(transfer.IsoPackets[1].ActualLength, Is.EqualTo(8u));
            Assert.That(transfer.ActualLength, Is.EqualTo(16));
            Assert.That(transfer.Data[8..16], Is.All.EqualTo((byte)2));
        }
    }

    [Test]
    public void Scheduler_CompleteAfterLastPacket_RaisesTransferCompletedOnce()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
            (int)transfer.IsoPackets![packetIndex].Length);
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 4, 4, 4);
            List<UsbTransfer> completions = new List<UsbTransfer>();
            scheduler.TransferCompleted += completedTransfer => completions.Add(completedTransfer);
            scheduler.Start();
            scheduler.Enqueue(transfer);

            timeProvider.Advance(3 * FrameDuration);

            Assert.That(completions, Has.Count.EqualTo(1));
            Assert.That(completions[0], Is.SameAs(transfer));
            Assert.That(transfer.ActualLength, Is.EqualTo(12));
            Assert.That(transfer.ErrorCount, Is.Zero);

            timeProvider.Advance(5 * FrameDuration);

            Assert.That(completions, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Scheduler_NextTransferStartsOnFollowingFrame()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<(uint SeqNum, int PacketIndex)> calls = new List<(uint SeqNum, int PacketIndex)>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            calls.Add((transfer.SeqNum, packetIndex));
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            List<uint> completions = new List<uint>();
            scheduler.TransferCompleted += completedTransfer => completions.Add(completedTransfer.SeqNum);
            UsbTransfer first = CreateTransfer(1, 4);
            UsbTransfer second = CreateTransfer(2, 4, 4);
            scheduler.Start();
            scheduler.Enqueue(first);
            scheduler.Enqueue(second);

            timeProvider.Advance(FrameDuration);

            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0], Is.EqualTo((1u, 0)));
            Assert.That(completions, Is.EqualTo(new uint[]
            {
                1
            }));

            timeProvider.Advance(FrameDuration);

            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(calls[1], Is.EqualTo((2u, 0)));
            Assert.That(completions, Is.EqualTo(new uint[]
            {
                1
            }));

            timeProvider.Advance(FrameDuration);

            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(calls[2], Is.EqualTo((2u, 1)));
            Assert.That(completions, Is.EqualTo(new uint[]
            {
                1, 2
            }));
        }
    }

    [Test]
    public void Scheduler_TryCancel_PendingTransfer_ReturnsItAndNeverCompletes()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<uint> produced = new List<uint>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            produced.Add(transfer.SeqNum);
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            List<uint> completions = new List<uint>();
            scheduler.TransferCompleted += completedTransfer => completions.Add(completedTransfer.SeqNum);
            UsbTransfer first = CreateTransfer(1, 4, 4);
            UsbTransfer pending = CreateTransfer(2, 4, 4);
            scheduler.Start();
            scheduler.Enqueue(first);
            scheduler.Enqueue(pending);
            timeProvider.Advance(FrameDuration);

            bool cancelled = scheduler.TryCancel(pending.SeqNum, out UsbTransfer cancelledTransfer);

            Assert.That(cancelled, Is.True);
            Assert.That(cancelledTransfer, Is.SameAs(pending));
            Assert.That(scheduler.PendingCount, Is.EqualTo(1));
            Assert.That(scheduler.TryCancel(99, out _), Is.False);

            timeProvider.Advance(3 * FrameDuration);

            Assert.That(completions, Is.EqualTo(new uint[]
            {
                1
            }));
            Assert.That(produced, Is.EqualTo(new uint[]
            {
                1, 1
            }));
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(pending.IsoPackets![0].ActualLength, Is.Zero);
        }
    }

    [Test]
    public void Scheduler_TryCancel_CurrentTransfer_ReturnsItAndNeverCompletes()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<uint> produced = new List<uint>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            produced.Add(transfer.SeqNum);
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            List<uint> completions = new List<uint>();
            scheduler.TransferCompleted += completedTransfer => completions.Add(completedTransfer.SeqNum);
            UsbTransfer current = CreateTransfer(1, 4, 4);
            UsbTransfer next = CreateTransfer(2, 4);
            scheduler.Start();
            scheduler.Enqueue(current);
            scheduler.Enqueue(next);
            timeProvider.Advance(FrameDuration);
            Assert.That(produced, Is.EqualTo(new uint[]
            {
                1
            }));

            bool cancelled = scheduler.TryCancel(current.SeqNum, out UsbTransfer cancelledTransfer);

            Assert.That(cancelled, Is.True);
            Assert.That(cancelledTransfer, Is.SameAs(current));
            Assert.That(scheduler.PendingCount, Is.EqualTo(1));

            timeProvider.Advance(FrameDuration);

            Assert.That(produced, Is.EqualTo(new uint[]
            {
                1, 2
            }));
            Assert.That(completions, Is.EqualTo(new uint[]
            {
                2
            }));
            Assert.That(current.IsoPackets![1].ActualLength, Is.Zero);

            timeProvider.Advance(FrameDuration);

            Assert.That(completions, Is.EqualTo(new uint[]
            {
                2
            }));
            Assert.That(scheduler.PendingCount, Is.Zero);
        }
    }

    [Test]
    public void Scheduler_Clear_DropsAllWithoutCompletion()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        int produced = 0;
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            produced++;
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            scheduler.Start();
            scheduler.Enqueue(CreateTransfer(1, 4, 4));
            scheduler.Enqueue(CreateTransfer(2, 4, 4));
            timeProvider.Advance(FrameDuration);
            Assert.That(produced, Is.EqualTo(1));

            scheduler.Clear();

            Assert.That(scheduler.PendingCount, Is.Zero);
            timeProvider.Advance(3 * FrameDuration);
            Assert.That(completions, Is.Zero);
            Assert.That(produced, Is.EqualTo(1));
        }
    }

    [Test]
    public void Scheduler_ProducePacketThrows_ContainsAndRecordsPacketError()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            if (packetIndex == 0)
            {
                throw new InvalidOperationException("Synthetic packet producer failure");
            }

            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 8, 4);
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            scheduler.Start();
            scheduler.Enqueue(transfer);

            Assert.That(() => timeProvider.Advance(2 * FrameDuration), Throws.Nothing);

            Assert.That(transfer.IsoPackets![0].Status, Is.EqualTo(UsbIpStatus.Stall));
            Assert.That(transfer.IsoPackets[0].ActualLength, Is.Zero);
            Assert.That(transfer.IsoPackets[1].Status, Is.Zero);
            Assert.That(transfer.IsoPackets[1].ActualLength, Is.EqualTo(4u));
            Assert.That(transfer.ErrorCount, Is.EqualTo(1u));
            Assert.That(transfer.ActualLength, Is.EqualTo(4));
            Assert.That(transfer.Status, Is.Zero);
            Assert.That(completions, Is.EqualTo(1));
        }
    }

    [Test]
    public void Scheduler_ProducePacketReturnsNegative_RecordsPacketError()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
            packetIndex == 0 ? -1 : (int)transfer.IsoPackets![packetIndex].Length);
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 8, 4);
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            scheduler.Start();
            scheduler.Enqueue(transfer);

            timeProvider.Advance(2 * FrameDuration);

            Assert.That(transfer.IsoPackets![0].Status, Is.EqualTo(UsbIpStatus.Stall));
            Assert.That(transfer.IsoPackets[0].ActualLength, Is.Zero);
            Assert.That(transfer.IsoPackets[1].ActualLength, Is.EqualTo(4u));
            Assert.That(transfer.ErrorCount, Is.EqualTo(1u));
            Assert.That(transfer.ActualLength, Is.EqualTo(4));
            Assert.That(completions, Is.EqualTo(1));
        }
    }

    [Test]
    public void Scheduler_ProducePacketReturnsTooMany_ClampsToPacketLength()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
            (int)transfer.IsoPackets![packetIndex].Length + 10);
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 8, 4);
            scheduler.Start();
            scheduler.Enqueue(transfer);

            timeProvider.Advance(2 * FrameDuration);

            Assert.That(transfer.IsoPackets![0].ActualLength, Is.EqualTo(8u));
            Assert.That(transfer.IsoPackets[1].ActualLength, Is.EqualTo(4u));
            Assert.That(transfer.ActualLength, Is.EqualTo(12));
            Assert.That(transfer.ErrorCount, Is.Zero);
        }
    }

    [Test]
    public void Scheduler_StopWithPendingTransfer_NoCompletionAfterStop()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<int> packetIndices = new List<int>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            packetIndices.Add(packetIndex);
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            UsbTransfer transfer = CreateTransfer(1, 4, 4);
            scheduler.Start();
            scheduler.Enqueue(transfer);
            timeProvider.Advance(FrameDuration);

            scheduler.Stop();
            scheduler.Stop();

            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(timeProvider.TimerCount, Is.Zero);
            timeProvider.Advance(5 * FrameDuration);
            Assert.That(completions, Is.Zero);
            Assert.That(packetIndices, Is.EqualTo(new[]
            {
                0
            }));
            Assert.That(transfer.IsoPackets![0].ActualLength, Is.EqualTo(4u));
            Assert.That(transfer.IsoPackets[1].ActualLength, Is.Zero);
            Assert.That(
                () => scheduler.Enqueue(CreateTransfer(2, 4)),
                Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Scheduler_Dispose_StopsClockAndDisposesTimer()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
            (int)transfer.IsoPackets![packetIndex].Length);
        int completions = 0;
        scheduler.TransferCompleted += _ => completions++;
        UsbTransfer transfer = CreateTransfer(1, 4, 4);
        scheduler.Start();
        scheduler.Enqueue(transfer);
        timeProvider.Advance(FrameDuration);

        scheduler.Dispose();
        scheduler.Dispose();

        Assert.That(timeProvider.TimerCount, Is.Zero);
        Assert.That(scheduler.PendingCount, Is.Zero);
        timeProvider.Advance(5 * FrameDuration);
        Assert.That(completions, Is.Zero);
        Assert.That(transfer.IsoPackets![1].ActualLength, Is.Zero);
    }

    [Test]
    public void Scheduler_EnqueueBeforeStart_FirstPacketOnFirstTick()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
            (int)transfer.IsoPackets![packetIndex].Length);
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 4);
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            scheduler.Enqueue(transfer);

            Assert.That(scheduler.PendingCount, Is.EqualTo(1));
            timeProvider.Advance(2 * FrameDuration);
            Assert.That(completions, Is.Zero);

            scheduler.Start();
            timeProvider.Advance(FrameDuration);

            Assert.That(completions, Is.EqualTo(1));
            Assert.That(scheduler.PendingCount, Is.Zero);
        }
    }

    [Test]
    public void Scheduler_NoQueuedTransfer_DoesNothingAndNextTransferStartsOnArrivalFrame()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<(uint SeqNum, int PacketIndex)> calls = new List<(uint SeqNum, int PacketIndex)>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            calls.Add((transfer.SeqNum, packetIndex));
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            List<uint> completions = new List<uint>();
            scheduler.TransferCompleted += completedTransfer => completions.Add(completedTransfer.SeqNum);
            scheduler.Start();
            timeProvider.Advance(25 * FrameDuration);

            Assert.That(calls, Is.Empty);
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(timeProvider.TimerCount, Is.EqualTo(1));

            UsbTransfer transfer = CreateTransfer(7, 4, 4);
            scheduler.Enqueue(transfer);

            Assert.That(calls, Is.Empty); // arrival alone schedules nothing until the next frame
            Assert.That(timeProvider.TimerCount, Is.EqualTo(1));

            timeProvider.Advance(FrameDuration);

            Assert.That(calls, Is.EqualTo(new[]
            {
                (7u, 0)
            }));
            Assert.That(completions, Is.Empty);
            Assert.That(scheduler.PendingCount, Is.EqualTo(1));

            timeProvider.Advance(FrameDuration);

            Assert.That(calls, Is.EqualTo(new[]
            {
                (7u, 0), (7u, 1)
            }));
            Assert.That(completions, Is.EqualTo(new uint[]
            {
                7
            }));
            Assert.That(scheduler.PendingCount, Is.Zero);

            timeProvider.Advance(10 * FrameDuration);

            Assert.That(calls, Has.Count.EqualTo(2)); // idle frames after the transfer schedule no catch-up work
            Assert.That(completions, Is.EqualTo(new uint[]
            {
                7
            }));
            Assert.That(timeProvider.TimerCount, Is.EqualTo(1));
        }
    }

    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(0x00FFFFFFu)]
    [TestCase(uint.MaxValue)]
    public void Scheduler_StartFrameAnyValue_SchedulesFromArrivalFrame(uint startFrame)
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
            (int)transfer.IsoPackets![packetIndex].Length);
        using (scheduler)
        {
            UsbTransfer transfer = CreateTransfer(1, 4, 4);
            transfer.StartFrame = startFrame;
            int completions = 0;
            scheduler.TransferCompleted += _ => completions++;
            scheduler.Start();
            scheduler.Enqueue(transfer);

            timeProvider.Advance(FrameDuration);

            Assert.That(completions, Is.Zero); // every value is clamped to arrival, including a future one
            Assert.That(transfer.IsoPackets![0].ActualLength, Is.EqualTo(4u));

            timeProvider.Advance(FrameDuration);

            Assert.That(completions, Is.EqualTo(1));
            Assert.That(transfer.StartFrame, Is.EqualTo(startFrame)); // the submitted value is preserved for the RET echo
        }
    }

    [Test]
    public void Scheduler_ManyQueuedTransfers_CompletesInFifoOrderWithoutCap()
    {
        const int TransferCount = 512;
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        List<uint> produced = new List<uint>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            produced.Add(transfer.SeqNum);
            return (int)transfer.IsoPackets![packetIndex].Length;
        });
        using (scheduler)
        {
            List<uint> completions = new List<uint>();
            scheduler.TransferCompleted += completedTransfer => completions.Add(completedTransfer.SeqNum);
            scheduler.Start();
            for (int i = 1; i <= TransferCount; i++)
            {
                scheduler.Enqueue(CreateTransfer((uint)i, 4));
            }

            Assert.That(scheduler.PendingCount, Is.EqualTo(TransferCount));
            Assert.That(produced, Is.Empty);

            timeProvider.Advance(TransferCount * FrameDuration);

            List<uint> expected = new List<uint>();
            for (int i = 1; i <= TransferCount; i++)
            {
                expected.Add((uint)i);
            }

            Assert.That(produced, Is.EqualTo(expected));
            Assert.That(completions, Is.EqualTo(expected));
            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(timeProvider.TimerCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void Scheduler_LongRun_AlternatingProducerStates_StaysBoundedWithoutDuplicateTimersOrPostStopCompletions()
    {
        const int FrameCount = 10_000;
        const int QueueDepth = 2;
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        int fullPackets = 0;
        int zeroPackets = 0;
        int partialPackets = 0;
        int failedPackets = 0;
        int producerCalls = 0;
        List<CompletionRecord> completions = new List<CompletionRecord>();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) =>
        {
            producerCalls++;
            IsoPacketDescriptor descriptor = transfer.IsoPackets![packetIndex];
            int packetLength = (int)descriptor.Length;
            int mode = (int)((transfer.SeqNum + (uint)packetIndex) % 4);
            switch (mode)
            {
                case 0:
                    fullPackets++;
                    transfer.Data!.AsSpan((int)descriptor.Offset, packetLength).Fill(0x5A);
                    return packetLength;
                case 1:
                    zeroPackets++;
                    transfer.Data!.AsSpan((int)descriptor.Offset, packetLength).Clear();
                    return 0;
                case 2:
                    partialPackets++;
                    transfer.Data!.AsSpan((int)descriptor.Offset, packetLength / 2).Fill(0x3C);
                    return packetLength / 2;
                default:
                    failedPackets++;
                    throw new InvalidOperationException("Synthetic long-run packet failure");
            }
        });
        using (scheduler)
        {
            scheduler.TransferCompleted += transfer =>
            {
                int stallCount = 0;
                int actualLength = 0;
                for (int i = 0; i < transfer.IsoPackets!.Length; i++)
                {
                    IsoPacketDescriptor packet = transfer.IsoPackets[i];
                    actualLength += (int)packet.ActualLength;
                    if (packet.Status == UsbIpStatus.Stall)
                    {
                        stallCount++;
                    }
                }

                completions.Add(new CompletionRecord(transfer.SeqNum, transfer.Status, transfer.ErrorCount, actualLength, stallCount));
            };
            scheduler.Start();
            uint nextSeqNum = 1;
            int enqueued = 0;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 0; frame < FrameCount; frame++)
            {
                if (scheduler.PendingCount < QueueDepth)
                {
                    scheduler.Enqueue(CreateTransfer(nextSeqNum, 4, 4));
                    nextSeqNum++;
                    enqueued++;
                }

                int completionsBefore = completions.Count;
                timeProvider.Advance(FrameDuration);

                Assert.That(completions.Count - completionsBefore, Is.LessThanOrEqualTo(1));
                Assert.That(timeProvider.TimerCount, Is.EqualTo(1), "The scheduler must own exactly one timer for the whole run.");
                Assert.That(scheduler.PendingCount, Is.LessThanOrEqualTo(QueueDepth), "Pending work must stay bounded by the host queue depth.");
                if (completions.Count > completionsBefore)
                {
                    CompletionRecord completion = completions[^1];
                    int expectedActualLength = 0;
                    int expectedStalls = 0;
                    for (int packetIndex = 0; packetIndex < 2; packetIndex++)
                    {
                        int mode = (int)((completion.SeqNum + (uint)packetIndex) % 4);
                        expectedActualLength += mode == 0 ? 4 : mode == 2 ? 2 : 0;
                        expectedStalls += mode == 3 ? 1 : 0;
                    }

                    Assert.That(completion.Status, Is.Zero);
                    Assert.That(completion.ErrorCount, Is.EqualTo((uint)expectedStalls));
                    Assert.That(completion.StallCount, Is.EqualTo(expectedStalls));
                    Assert.That(completion.ActualLength, Is.EqualTo(expectedActualLength));
                }
            }

            stopwatch.Stop();
            Assert.That(producerCalls, Is.EqualTo(FrameCount)); // one packet produced per simulated frame
            int completionsBeforeStop = completions.Count;
            int producerCallsBeforeStop = producerCalls;
            int pendingAtStop = scheduler.PendingCount;
            Assert.That(completions, Has.Count.EqualTo(enqueued - pendingAtStop));

            scheduler.Stop();

            Assert.That(scheduler.PendingCount, Is.Zero);
            Assert.That(timeProvider.TimerCount, Is.Zero);
            timeProvider.Advance(100 * FrameDuration);

            Assert.That(completions, Has.Count.EqualTo(completionsBeforeStop));
            Assert.That(producerCalls, Is.EqualTo(producerCallsBeforeStop));
            Assert.That(fullPackets, Is.GreaterThan(1000));
            Assert.That(zeroPackets, Is.GreaterThan(1000));
            Assert.That(partialPackets, Is.GreaterThan(1000));
            Assert.That(failedPackets, Is.GreaterThan(1000));
            TestContext.Out.WriteLine(
                $"Long-run: {FrameCount} frames, {enqueued} URBs, {completionsBeforeStop} completions, " +
                $"{producerCalls} packets in {stopwatch.ElapsedMilliseconds} ms.");
        }
    }

    [Test]
    public void Scheduler_StartTwice_ThrowsInvalidOperationException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) => 0);
        using (scheduler)
        {
            scheduler.Start();

            Assert.That(() => scheduler.Start(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    public void Scheduler_ConstructorInvalidArguments_Throw()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();

        Assert.That(
            () => new IsoScheduler(null!, FrameDuration, (transfer, packetIndex) => 0),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(
            () => new IsoScheduler(timeProvider, FrameDuration, null!),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(
            () => new IsoScheduler(timeProvider, TimeSpan.Zero, (transfer, packetIndex) => 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Scheduler_EnqueueNullTransfer_ThrowsArgumentNullException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) => 0);
        using (scheduler)
        {
            Assert.That(() => scheduler.Enqueue(null!), Throws.TypeOf<ArgumentNullException>());
        }
    }

    [Test]
    public void Scheduler_EnqueueNullIsoPackets_ThrowsArgumentException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) => 0);
        using (scheduler)
        {
            UsbTransfer transfer = new UsbTransfer
            {
                SeqNum = 1,
                NumberOfPackets = 1,
                MaxTransferLength = 8,
                IsoPackets = null
            };

            Assert.That(() => scheduler.Enqueue(transfer), Throws.TypeOf<ArgumentException>());
        }
    }

    [Test]
    public void Scheduler_EnqueueZeroPackets_ThrowsArgumentException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) => 0);
        using (scheduler)
        {
            UsbTransfer transfer = new UsbTransfer
            {
                SeqNum = 1,
                NumberOfPackets = 0,
                MaxTransferLength = 8,
                IsoPackets = Array.Empty<IsoPacketDescriptor>()
            };

            Assert.That(() => scheduler.Enqueue(transfer), Throws.TypeOf<ArgumentException>());
        }
    }

    [Test]
    public void Scheduler_EnqueuePacketCountMismatch_ThrowsArgumentException()
    {
        ManualTimeProvider timeProvider = new ManualTimeProvider();
        IsoScheduler scheduler = new IsoScheduler(timeProvider, FrameDuration, (transfer, packetIndex) => 0);
        using (scheduler)
        {
            UsbTransfer transfer = new UsbTransfer
            {
                SeqNum = 1,
                NumberOfPackets = 2,
                MaxTransferLength = 8,
                IsoPackets = new IsoPacketDescriptor[1]
            };

            Assert.That(() => scheduler.Enqueue(transfer), Throws.TypeOf<ArgumentException>());
        }
    }

    /// <summary>One completion observed by the long-run test: the state of the whole transfer and its failed packets.</summary>
    private readonly struct CompletionRecord
    {
        /// <summary>Records the completion state of one long-run transfer.</summary>
        /// <param name="seqNum">Sequence number of the completed transfer.</param>
        /// <param name="status">Transfer-level status.</param>
        /// <param name="errorCount">Number of failed packets reported by the scheduler.</param>
        /// <param name="actualLength">Sum of the packet actual lengths.</param>
        /// <param name="stallCount">Number of packet descriptors marked as stalled.</param>
        public CompletionRecord(uint seqNum, int status, uint errorCount, int actualLength, int stallCount)
        {
            SeqNum = seqNum;
            Status = status;
            ErrorCount = errorCount;
            ActualLength = actualLength;
            StallCount = stallCount;
        }

        /// <summary>Sequence number of the completed transfer.</summary>
        public uint SeqNum { get; }

        /// <summary>Transfer-level status.</summary>
        public int Status { get; }

        /// <summary>Number of failed packets reported by the scheduler.</summary>
        public uint ErrorCount { get; }

        /// <summary>Sum of the packet actual lengths.</summary>
        public int ActualLength { get; }

        /// <summary>Number of packet descriptors marked as stalled.</summary>
        public int StallCount { get; }
    }

    private static UsbTransfer CreateTransfer(uint seqNum, params uint[] packetLengths)
    {
        IsoPacketDescriptor[] packets = new IsoPacketDescriptor[packetLengths.Length];
        uint offset = 0;
        for (int i = 0; i < packetLengths.Length; i++)
        {
            packets[i] = new IsoPacketDescriptor
            {
                Offset = offset,
                Length = packetLengths[i],
                ActualLength = 0,
                Status = 0
            };
            offset += packetLengths[i];
        }

        return new UsbTransfer
        {
            SeqNum = seqNum,
            Direction = UsbIpDirection.In,
            EndpointAddress = 0x82,
            MaxTransferLength = (int)offset,
            NumberOfPackets = (uint)packetLengths.Length,
            IsoPackets = packets
        };
    }
}