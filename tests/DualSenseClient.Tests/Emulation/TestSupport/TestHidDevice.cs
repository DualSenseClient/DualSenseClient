using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.TestSupport;

/// <summary>
/// HID device test double: serves the simple test device's descriptor set through
/// <see cref="VirtualHidDevice"/>, so class requests of the HID interface reach the report
/// engine, and records every output report pushed to <see cref="VirtualUsbDevice.OnOutputReport"/>
/// (both the interrupt OUT path and the control SET_REPORT path).
/// </summary>
public sealed class TestHidDevice : VirtualHidDevice
{
    private readonly object _lock = new object();

    private readonly List<byte[]> _outputReports = new List<byte[]>();

    private byte[] _inputReport = TestVirtualUsbDevice.DefaultInputReport;

    /// <summary>Initializes a device serving the simple test device's HID descriptor set.</summary>
    public TestHidDevice()
        : base(TestDevices.CreateSimpleDevice())
    {
    }

    /// <summary>Snapshot of the payloads received through output-report transfers, in order.</summary>
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

    /// <inheritdoc/>
    public override int ProvideInputReport(Span<byte> destination)
    {
        lock (_lock)
        {
            int count = Math.Min(_inputReport.Length, destination.Length);
            _inputReport.AsSpan(0, count).CopyTo(destination);
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