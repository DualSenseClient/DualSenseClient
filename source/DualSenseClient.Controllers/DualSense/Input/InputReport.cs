namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Parsed DualSense input report.
/// Provides typed access to each input section (buttons, sticks, IMU, touchpad, etc.).
/// </summary>
public readonly struct InputReport
{
    /// <summary>
    /// Button and stick state (bytes 0-9).
    /// </summary>
    public InputState Input { get; }

    /// <summary>
    /// Gyroscope and accelerometer data (bytes 15-31).
    /// </summary>
    public MotionState Motion { get; }

    /// <summary>
    /// Touchpad touch points (bytes 32-39).
    /// </summary>
    public TouchpadState Touchpad { get; }

    /// <summary>
    /// Adaptive trigger status and host timestamp echo (bytes 41-47).
    /// </summary>
    public AdaptiveTriggerStatus AdaptiveTriggers { get; }

    /// <summary>
    /// Device timestamp (bytes 48-51, uint32 LE).
    /// </summary>
    public DeviceTimestamp DeviceTimestamp { get; }

    /// <summary>
    /// Battery level and power state (byte 52).
    /// </summary>
    public BatteryState Battery { get; }

    /// <summary>
    /// Connection and audio status (byte 53).
    /// </summary>
    public ConnectionStatus Connection { get; }

    /// <summary>
    /// Creates a new parsed input report. All sub-state values are parsed
    /// from the buffer at construction time.
    /// </summary>
    /// <param name="buffer">The full HID input report buffer.</param>
    /// <param name="offset">Offset to the 63-byte data payload (1 for USB, 2 for Bluetooth).</param>
    public InputReport(byte[] buffer, int offset)
    {
        Input = new InputState(buffer, offset);
        Motion = new MotionState(buffer, offset);
        Touchpad = new TouchpadState(buffer, offset);
        AdaptiveTriggers = new AdaptiveTriggerStatus(buffer, offset);
        DeviceTimestamp = new DeviceTimestamp(buffer, offset);
        Battery = new BatteryState(buffer[offset + 52]);
        Connection = new ConnectionStatus(buffer[offset + 53]);
    }
}