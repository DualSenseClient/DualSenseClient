using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.Devices;

/// <summary>
/// Concrete controller implementation for the Sony DualSense (PS5) controller.
/// Opens and communicates with the DualSense over USB or Bluetooth via SDL3 HID.
/// </summary>
public sealed class DualSenseDevice : ControllerDevice
{
    // Fields
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseDevice");
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly Task _readTask;

    /// <inheritdoc/>
    public override ControllerType ControllerType => ControllerType.DualSense;

    /// <inheritdoc/>
    public override int MaxOutputReportLength => ConnectionType switch
    {
        ConnectionType.Bluetooth => 78,
        ConnectionType.Usb => 63,
        ConnectionType.Unknown => throw new ArgumentOutOfRangeException($"Unknown connection type: {ConnectionType}"),
        _ => throw new ArgumentOutOfRangeException($"Unknown connection type: {ConnectionType}")
    };

    /// <summary>
    /// Current state of input
    /// </summary>
    public InputReport InputReport { get; private set; } = null!;

    /// <summary>
    /// Creates a new DualSense controller wrapper around an already-opened HID device.
    /// </summary>
    /// <param name="device">The opened HID device for this controller.</param>
    /// <param name="info">The device info that was used to discover and open the device.</param>
    public DualSenseDevice(IHidDevice device, IHidDeviceInfo info) : base(device, info)
    {
        _readTask = Task.Run(() => ReadLoop(_cts.Token));
    }

    /// <summary>
    /// Background loop that continuously reads HID input reports from the controller.
    /// Runs on a background task for the lifetime of the controller connection.
    /// </summary>
    /// <param name="ct">Cancellation token to signal when the loop should stop.</param>
    private async Task ReadLoop(CancellationToken ct)
    {
        _log.Debug("Read Loop Start");
        byte[] buffer = new byte[MaxOutputReportLength];
        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                int result = await ReadInputAsync(buffer, 0, buffer.Length, ct);
                if (result <= 0)
                {
                    _log.Warning($"Read returned {result} bytes, disconnecting");
                    break;
                }

                ProcessInputReport(buffer);
            }
            catch (HidException)
            {
                _log.Error("SDL_hid_read_timeout failed");
                break;
            }
            catch (OperationCanceledException)
            {
                _log.Debug("Read Loop Cancelled");
                break;
            }
            catch (Exception ex)
            {
                _log.LogExceptionDetails(ex);
                break;
            }
        }

        _log.Debug("Read Loop End");
    }

    /// <summary>
    /// Routes a raw HID report to the correct parser based on connection type and report ID.
    /// Strips the protocol header bytes before forwarding to the input parser.
    /// </summary>
    /// <param name="data">Raw HID input report buffer.</param>
    private void ProcessInputReport(byte[] data)
    {
        byte reportId = data[0];
        if (ConnectionType == ConnectionType.Bluetooth)
        {
            switch (reportId)
            {
                case 0x31:
                    InputReport = new InputReport(data, 2);
                    break;
                case 0x01:
                    _log.Warning("Controller is in simple Bluetooth state");
                    // TODO: Send a default output in order to fix this
                    break;
                default:
                    _log.Warning($"Unknown Bluetooth report ID: 0x{reportId:X2}");
                    break;
            }
        }
        else
        {
            if (reportId != 0x01)
            {
                _log.Warning($"Invalid USB report ID: 0x{reportId:X2} (expected 0x01)");
                return;
            }
            InputReport = new InputReport(data, 1);
        }
    }
}