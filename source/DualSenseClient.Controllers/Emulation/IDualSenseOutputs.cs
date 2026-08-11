using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Receives feedback commands from a virtual controller and applies them to the
/// physical DualSense. Abstracted so the virtual controllers can be unit-tested
/// against a fake instead of real hardware.
/// </summary>
public interface IDualSenseOutputs
{
    /// <summary>
    /// Sets the classic rumble motors on the physical controller.
    /// </summary>
    /// <param name="left">Left (low-frequency) motor strength (0-255).</param>
    /// <param name="right">Right (high-frequency) motor strength (0-255).</param>
    void SetVibration(byte left, byte right);

    /// <summary>
    /// Sends an arbitrary output state (lightbar, player LEDs, trigger effects,
    /// mic LED, brightness) to the physical controller.
    /// </summary>
    /// <param name="payload">The output state to send.</param>
    void SendOutputState(SetStateData payload);
}

/// <summary>
/// Default <see cref="IDualSenseOutputs"/> implementation forwarding to a physical
/// <see cref="DualSenseDevice"/>. Writes are serialized because the physical
/// controller keeps a rolling Bluetooth sequence counter that must not be written
/// from native callback threads and the UI thread at the same time.
/// </summary>
public sealed class DualSenseDeviceOutputs : IDualSenseOutputs
{
    /// <summary>
    /// Guards feedback writes to the physical device.
    /// </summary>
    private readonly Lock _writeLock = new Lock();

    /// <summary>
    /// The physical controller receiving the feedback.
    /// </summary>
    private readonly DualSenseDevice _device;

    /// <summary>
    /// Creates a new adapter around the given physical controller.
    /// </summary>
    public DualSenseDeviceOutputs(DualSenseDevice device) => _device = device;

    /// <inheritdoc/>
    public void SetVibration(byte left, byte right)
    {
        lock (_writeLock)
        {
            _device.SetVibration(left, right);
        }
    }

    /// <inheritdoc/>
    public void SendOutputState(SetStateData payload)
    {
        lock (_writeLock)
        {
            _device.SendOutputState(payload);
        }
    }
}