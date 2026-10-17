using System.Collections.Concurrent;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Emulation.UsbIp;

namespace DualSenseClient.Tests.Emulation.Core;

[TestFixture]
public sealed class UsbTransferQueueTests
{
    [Test]
    public void EnqueueTryDequeue_ThreeTransfers_ReturnsInFifoOrder()
    {
        UsbTransferQueue queue = new UsbTransferQueue();
        UsbTransfer first = CreateTransfer(10);
        UsbTransfer second = CreateTransfer(20);
        UsbTransfer third = CreateTransfer(30);

        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(third);

        Assert.That(queue.Count, Is.EqualTo(3));
        Assert.That(queue.TryDequeue(out UsbTransfer dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(first));
        Assert.That(dequeued.SeqNum, Is.EqualTo(10));
        Assert.That(queue.TryDequeue(out dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(second));
        Assert.That(dequeued.SeqNum, Is.EqualTo(20));
        Assert.That(queue.TryDequeue(out dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(third));
        Assert.That(dequeued.SeqNum, Is.EqualTo(30));
        Assert.That(queue.Count, Is.Zero);
    }

    [Test]
    public void TryCancel_MiddleTransfer_RemovesItAndPreservesOrderOfRemaining()
    {
        UsbTransferQueue queue = new UsbTransferQueue();
        UsbTransfer first = CreateTransfer(10);
        UsbTransfer second = CreateTransfer(20);
        UsbTransfer third = CreateTransfer(30);
        queue.Enqueue(first);
        queue.Enqueue(second);
        queue.Enqueue(third);

        Assert.That(queue.TryCancel(20, out UsbTransfer cancelled), Is.True);

        Assert.That(cancelled, Is.SameAs(second));
        Assert.That(queue.Count, Is.EqualTo(2));
        Assert.That(queue.TryDequeue(out UsbTransfer dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(first));
        Assert.That(queue.TryDequeue(out dequeued), Is.True);
        Assert.That(dequeued, Is.SameAs(third));
        Assert.That(queue.TryDequeue(out dequeued), Is.False);
    }

    [Test]
    public void TryCancel_UnknownSeqNum_ReturnsFalseAndRemovesNothing()
    {
        UsbTransferQueue queue = new UsbTransferQueue();
        queue.Enqueue(CreateTransfer(10));

        Assert.That(queue.TryCancel(99, out UsbTransfer cancelled), Is.False);

        Assert.That(cancelled, Is.Null);
        Assert.That(queue.Count, Is.EqualTo(1));
    }

    [Test]
    public void TryCancel_AlreadyDequeuedSeqNum_ReturnsFalse()
    {
        UsbTransferQueue queue = new UsbTransferQueue();
        queue.Enqueue(CreateTransfer(10));
        Assert.That(queue.TryDequeue(out UsbTransfer dequeued), Is.True);

        Assert.That(queue.TryCancel(10, out UsbTransfer cancelled), Is.False);

        Assert.That(dequeued.SeqNum, Is.EqualTo(10));
        Assert.That(cancelled, Is.Null);
        Assert.That(queue.Count, Is.Zero);
    }

    [Test]
    public void Clear_QueuedTransfers_EmptiesCount()
    {
        UsbTransferQueue queue = new UsbTransferQueue();
        queue.Enqueue(CreateTransfer(10));
        queue.Enqueue(CreateTransfer(20));

        queue.Clear();

        Assert.That(queue.Count, Is.Zero);
        Assert.That(queue.TryDequeue(out UsbTransfer dequeued), Is.False);
        Assert.That(dequeued, Is.Null);
    }

    [Test]
    public void TryDequeue_EmptyQueue_ReturnsFalse()
    {
        UsbTransferQueue queue = new UsbTransferQueue();

        Assert.That(queue.TryDequeue(out UsbTransfer dequeued), Is.False);

        Assert.That(dequeued, Is.Null);
    }

    [Test]
    public void Enqueue_NullTransfer_ThrowsArgumentNullException()
    {
        UsbTransferQueue queue = new UsbTransferQueue();

        Assert.That(() => queue.Enqueue(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void EnqueueTryDequeue_ParallelProducersAndConsumers_TransfersEveryItemExactlyOnce()
    {
        const int ProducerCount = 4;
        const int ConsumerCount = 4;
        const int TransfersPerProducer = 250;
        const int TransferCount = ProducerCount * TransfersPerProducer;
        UsbTransferQueue queue = new UsbTransferQueue();
        ConcurrentDictionary<uint, byte> dequeuedSeqNums = new ConcurrentDictionary<uint, byte>();
        int dequeuedCount = 0;

        Parallel.Invoke(
            () => Parallel.For(0, ProducerCount, producer =>
            {
                uint firstSeqNum = (uint)(producer * TransfersPerProducer);
                for (int i = 0; i < TransfersPerProducer; i++)
                {
                    queue.Enqueue(CreateTransfer(firstSeqNum + (uint)i));
                }
            }),
            () => Parallel.For(0, ConsumerCount, _ =>
            {
                while (Volatile.Read(ref dequeuedCount) < TransferCount)
                {
                    if (queue.TryDequeue(out UsbTransfer transfer))
                    {
                        dequeuedSeqNums.TryAdd(transfer.SeqNum, 0);
                        Interlocked.Increment(ref dequeuedCount);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }));

        Assert.That(dequeuedCount, Is.EqualTo(TransferCount));
        Assert.That(dequeuedSeqNums.Count, Is.EqualTo(TransferCount));
        Assert.That(queue.Count, Is.Zero);
    }

    private static UsbTransfer CreateTransfer(uint seqNum)
    {
        return new UsbTransfer
        {
            SeqNum = seqNum,
            Direction = UsbIpDirection.Out,
            EndpointAddress = 0x02,
            MaxTransferLength = 8,
            Data = [0x01, 0x02, 0x03],
            Status = UsbIpStatus.Success,
            ActualLength = 3
        };
    }
}