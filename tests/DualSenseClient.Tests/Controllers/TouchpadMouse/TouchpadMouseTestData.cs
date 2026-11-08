using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.TouchpadMouse;

/// <summary>
/// Builds <see cref="TouchpadState"/> values from active touch points so processor tests can
/// describe gestures as coordinates instead of raw report bytes.
/// </summary>
internal static class TouchpadMouseTestData
{
    /// <summary>
    /// Builds a touchpad state containing up to two active points. An omitted point reports as
    /// inactive, matching a finger that is not touching the pad.
    /// </summary>
    /// <param name="touch1">First active point, or <see langword="null"/> when inactive.</param>
    /// <param name="touch2">Second active point, or <see langword="null"/> when inactive.</param>
    /// <param name="tracking1">Tracking identifier of the first point.</param>
    /// <param name="tracking2">Tracking identifier of the second point.</param>
    /// <returns>The parsed touchpad state.</returns>
    public static TouchpadState State(
        (ushort X, ushort Y)? touch1 = null,
        (ushort X, ushort Y)? touch2 = null,
        byte tracking1 = 1,
        byte tracking2 = 2)
    {
        byte[] buffer = new byte[40];
        buffer[32] = 0x80; // Touch1 inactive
        buffer[36] = 0x80; // Touch2 inactive
        if (touch1 is { } a)
        {
            WritePoint(buffer, 32, a.X, a.Y, tracking1);
        }

        if (touch2 is { } b)
        {
            WritePoint(buffer, 36, b.X, b.Y, tracking2);
        }

        return new TouchpadState(buffer, 0);
    }

    /// <summary>
    /// Writes one active point using the DualSense touchpad byte layout.
    /// </summary>
    /// <param name="buffer">The backing report buffer.</param>
    /// <param name="offset">The point's byte offset (32 for touch 1, 36 for touch 2).</param>
    /// <param name="x">Horizontal position.</param>
    /// <param name="y">Vertical position.</param>
    /// <param name="trackingId">Tracking identifier.</param>
    private static void WritePoint(byte[] buffer, int offset, ushort x, ushort y, byte trackingId)
    {
        buffer[offset] = (byte)(trackingId & 0x7F);
        buffer[offset + 1] = (byte)(x & 0xFF);
        buffer[offset + 2] = (byte)(((y & 0x0F) << 4) | ((x >> 8) & 0x0F));
        buffer[offset + 3] = (byte)(y >> 4);
    }
}