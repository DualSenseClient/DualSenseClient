namespace DualSenseClient.Controllers.DualSense.Input;

/// <summary>
/// Connection and audio status from the DualSense controller (byte 53).
/// </summary>
public readonly struct ConnectionStatus : IEquatable<ConnectionStatus>
{
    /// <summary>
    /// Raw connection status byte from the controller.
    /// </summary>
    public byte Raw { get; }

    /// <summary>
    /// Initializes a new connection status from a raw report byte.
    /// </summary>
    public ConnectionStatus(byte raw)
    {
        Raw = raw;
    }

    /// <summary>
    /// Headphones are connected to the controller.
    /// </summary>
    public bool Headphone => (Raw & 0x01) != 0;

    /// <summary>
    /// Microphone is connected to the controller.
    /// </summary>
    public bool Mic => (Raw & 0x02) != 0;

    /// <summary>
    /// Microphone is muted.
    /// </summary>
    public bool MicMuted => (Raw & 0x04) != 0;

    /// <summary>
    /// USB data connection is active.
    /// </summary>
    public bool UsbData => (Raw & 0x08) != 0;

    /// <summary>
    /// USB power is connected.
    /// </summary>
    public bool UsbPower => (Raw & 0x10) != 0;

    /// <summary>
    /// Returns true if the raw connection byte is equal.
    /// </summary>
    public bool Equals(ConnectionStatus other) => Raw == other.Raw;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ConnectionStatus other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Raw.GetHashCode();

    /// <summary>
    /// Returns true if the two connection statuses are equal.
    /// </summary>
    public static bool operator ==(ConnectionStatus left, ConnectionStatus right) => left.Equals(right);

    /// <summary>
    /// Returns true if the two connection statuses are not equal.
    /// </summary>
    public static bool operator !=(ConnectionStatus left, ConnectionStatus right) => !left.Equals(right);
}