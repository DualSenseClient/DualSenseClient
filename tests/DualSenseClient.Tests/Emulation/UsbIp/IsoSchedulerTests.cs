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