using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// Virtual USB device test double: serves the simple test device's descriptor set by default and
/// a configurable interrupt IN report, and records every interrupt OUT payload. Reports can be
/// queued so that each pacer tick observes a fresh payload, and the report can be set to a
/// zero-length array to simulate a device with nothing to report.
/// </summary>
public sealed class TestVirtualUsbDevice : VirtualUsbDevice
{
    /// <summary>Report served when neither a queued report nor another configured report is set.</summary>
    public static readonly byte[] DefaultInputReport = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];

    private readonly object _lock = new object();

    private readonly Queue<byte[]> _queuedReports = new Queue<byte[]>();

    private readonly List<byte[]> _outputReports = new List<byte[]>();

    private byte[] _inputReport = DefaultInputReport;

    /// <summary>Initializes a device serving the simple test device's descriptor set.</summary>
    public TestVirtualUsbDevice()
        : base(TestDevices.CreateSimpleDevice())
    {
    }

    /// <summary>Initializes a device serving <paramref name="descriptors"/>.</summary>
    /// <param name="descriptors">Descriptor set of the device.</param>
    public TestVirtualUsbDevice(UsbDescriptorSet descriptors)
        : base(descriptors)
    {
    }

    /// <summary>Report served once the queued reports are exhausted; empty to report nothing.</summary>
    public byte[] InputReport
    {
        get
        {
            lock (_lock)
            {
                return _inputReport;
            }
        }

        set
        {
            lock (_lock)
            {
                _inputReport = value;
            }
        }
    }

    /// <summary>Snapshot of the payloads received through interrupt OUT transfers, in order.</summary>
    public IReadOnlyList<byte[]> OutputReports
    {
        get
        {
            lock (_lock)
            {
                return _outputReports.ToArray();
            }
        }
    }

    /// <summary>Queues <paramref name="report"/> to be served by the next input report request.</summary>
    /// <param name="report">Report bytes served once, before the configured report is used again.</param>
    public void EnqueueInputReport(byte[] report)
    {
        lock (_lock)
        {
            _queuedReports.Enqueue(report);
        }
    }

    /// <inheritdoc/>
    public override int ProvideInputReport(Span<byte> destination)
    {
        lock (_lock)
        {
            byte[] report = _queuedReports.Count > 0 ? _queuedReports.Dequeue() : _inputReport;
            int count = Math.Min(report.Length, destination.Length);
            report.AsSpan(0, count).CopyTo(destination);
            return count;
        }
    }

    /// <inheritdoc/>
    public override void OnOutputReport(ReadOnlySpan<byte> data)
    {
        lock (_lock)
        {
            _outputReports.Add(data.ToArray());
        }
    }
}