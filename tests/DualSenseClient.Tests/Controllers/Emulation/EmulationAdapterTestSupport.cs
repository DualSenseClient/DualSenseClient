using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Shared fakes and physical-report builders for the Emulation virtual-controller adapter tests.
/// The builders produce physical DualSense reports in the shape the client parses (report id at
/// offset 0, the 63-byte payload from offset 1), so every adapter test drives the real parsing and
/// mapping pipeline instead of a bespoke state carrier.
/// </summary>
internal static class EmulationAdapterTestSupport
{
    /// <summary>Report id of the USB input report 0x01 shared by the DualSense and DualShock 4.</summary>
    internal const byte InputReportId = 0x01;

    /// <summary>Size of the USB input report: the report id byte plus the 63-byte payload.</summary>
    internal const int InputReportSize = 64;

    /// <summary>Size of the payload the physical input reports carry (report id excluded).</summary>
    internal const int InputPayloadSize = 63;

    /// <summary>
    /// Creates a neutral physical payload: centered sticks, the released D-pad hat (0x08) and the
    /// battery byte 0x2A (level 10, power state Complete).
    /// </summary>
    /// <returns>A fresh 63-byte payload the caller may mutate.</returns>
    internal static byte[] NeutralPayload()
    {
        byte[] payload = new byte[InputPayloadSize];
        payload[0] = 128;
        payload[1] = 128;
        payload[2] = 128;
        payload[3] = 128;
        payload[7] = 0x08;
        payload[52] = 0x2A;
        return payload;
    }

    /// <summary>
    /// Wraps a 63-byte physical payload into a parsed input report, optionally attaching the exact
    /// 64-byte USB image the client would pass through on the DualSense raw path.
    /// </summary>
    /// <param name="payload">Physical payload; not copied.</param>
    /// <param name="usbRaw64">Raw USB image to attach, or <see langword="null"/>.</param>
    /// <returns>The parsed report the adapters consume.</returns>
    internal static InputReport Report(byte[] payload, byte[]? usbRaw64 = null)
    {
        byte[] buffer = new byte[InputReportSize];
        buffer[0] = InputReportId;
        Buffer.BlockCopy(payload, 0, buffer, 1, InputPayloadSize);
        return new InputReport(buffer, 1, usbRaw64);
    }

    /// <summary>
    /// Creates a filled 64-byte raw USB input report with the report id at offset 0.
    /// </summary>
    /// <param name="fill">Byte every payload position is filled with.</param>
    /// <returns>A raw report ready to attach to an <see cref="InputReport"/>.</returns>
    internal static byte[] RawReport(byte fill)
    {
        byte[] raw = new byte[InputReportSize];
        raw.AsSpan().Fill(fill);
        raw[0] = InputReportId;
        return raw;
    }

    /// <summary>
    /// Pulls one input report from the persona and asserts it produced the expected number of bytes.
    /// </summary>
    /// <param name="device">Persona to pull from.</param>
    /// <param name="expectedSize">Expected report size in bytes.</param>
    /// <returns>The served report.</returns>
    internal static byte[] Pull(VirtualUsbDevice device, int expectedSize)
    {
        byte[] report = new byte[expectedSize];
        int written = device.ProvideInputReport(report);
        Assert.That(written, Is.EqualTo(expectedSize));
        return report;
    }

    /// <summary>
    /// Replaces the D-pad nibble (the low nibble of payload byte 7) while keeping the face buttons.
    /// </summary>
    /// <param name="payload">Payload to mutate.</param>
    /// <param name="hat">Hat value to write (masked to four bits).</param>
    internal static void SetDpad(byte[] payload, byte hat) => payload[7] = (byte)((payload[7] & 0xF0) | (hat & 0x0F));
}

/// <summary>
/// <see cref="IDualSenseOutputs"/> test double recording every feedback call in call order, so the
/// output-routing tests can assert both the payload and the order in which the adapter sends it.
/// </summary>
internal sealed class RecordingDualSenseOutputs : IDualSenseOutputs
{
    /// <summary>Every vibration command, in call order.</summary>
    internal List<(byte Left, byte Right)> Vibrations { get; } = [];

    /// <summary>Every output-state payload, in call order.</summary>
    internal List<SetStateData> OutputStates { get; } = [];

    /// <inheritdoc/>
    public void SetVibration(byte left, byte right) => Vibrations.Add((left, right));

    /// <inheritdoc/>
    public void SendOutputState(SetStateData payload) => OutputStates.Add(payload);
}