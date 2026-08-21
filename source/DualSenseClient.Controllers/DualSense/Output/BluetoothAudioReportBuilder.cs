using System.Buffers.Binary;
using DualSenseClient.Controllers.DualSense.Utilities;

namespace DualSenseClient.Controllers.DualSense.Output;

/// <summary>
/// Builds Bluetooth-only audio and haptics reports for a DualSense:
/// the report <c>0x35</c> speaker/headset lane (334 bytes, Opus payload), the combined
/// report <c>0x36</c> that carries state, audio and haptics packets together (398 bytes),
/// the report <c>0x32</c> voice-coil haptics lane (142 bytes, s8 stereo PCM), plus the
/// <c>0x32</c> init-prime report used to open the audio stream.
/// </summary>
/// <remarks>
/// <para>
/// Both report families end in a 4-byte CRC32 using the seeded form (seed
/// <c>0xA2</c>, see <see cref="DualSenseCRC32"/>). The sequence tag advances with a
/// stride of 16 (only the low nibble is transmitted) and the interval packet counter is
/// a free-running byte that increments once per report.
/// </para>
/// <para>
/// Layouts follow the vDS/DS5Dongle reference implementation: each report opens with
/// the sized packet-<c>0x11</c> session block (<c>0x91 0x07</c>, mic-disabled sections,
/// five 0x40 buffer-length bytes, interval counter), followed by typed sub-blocks in
/// vDS order — <see cref="BuildCombinedReport"/> carries state, then haptics, then the
/// speaker/headset Opus lane. The builder is not thread-safe:
/// drive it from a single report-writer thread and reset <see cref="Reset"/> when a
/// stream starts.
/// </para>
/// <para>
/// The per-tick builders (<see cref="BuildHapticsReport"/>, <see cref="BuildAudioReport"/>
/// and <see cref="BuildCombinedReport"/>) write into a reusable buffer per report type, so
/// the streaming path allocates nothing. Callers must not retain the returned array across
/// builds — it is only valid until the next call. <see cref="BuildInitPrime"/> is excluded
/// (once per stream) and still allocates a fresh report.
/// </para>
/// </remarks>
public sealed class BluetoothAudioReportBuilder
{
    /// <summary>
    /// Total size of the haptics report <c>0x32</c> including the report ID and CRC.
    /// </summary>
    public const int HapticsReportSize = 142;

    /// <summary>
    /// Total size of the audio report <c>0x35</c> including the report ID and CRC.
    /// </summary>
    public const int AudioReportSize = 334;

    /// <summary>
    /// Total size of the combined report <c>0x36</c> including the report ID and CRC.
    /// </summary>
    public const int CombinedReportSize = 398;

    /// <summary>
    /// Haptics payload size: 64 bytes = 32 stereo s8 frames at 3 kHz (10.67 ms).
    /// </summary>
    public const int HapticsPayloadSize = 64;

    /// <summary>
    /// Audio payload size: one Opus frame (nominal 10 ms at 48 kHz, 160 kbps CBR).
    /// </summary>
    public const int AudioPayloadSize = 200;

    /// <summary>
    /// CRC covers everything before the trailing 4 bytes in both report families.
    /// </summary>
    private const int CrcSize = 4;

    /// <summary>
    /// Rolling report sequence tag. Only the low nibble is transmitted, so it advances
    /// by 16 each report and wraps after every 16th report.
    /// </summary>
    private byte _reportSequence;

    /// <summary>
    /// Free-running interval packet counter, incremented once per built report.
    /// </summary>
    private byte _packetCounter;

    /// <summary>
    /// Reusable buffer for the <c>0x32</c> haptics report, returned by
    /// <see cref="BuildHapticsReport"/>. See the class remarks for the reuse contract.
    /// </summary>
    private readonly byte[] _hapticsReport = new byte[HapticsReportSize];

    /// <summary>
    /// Reusable buffer for the <c>0x35</c> audio report, returned by
    /// <see cref="BuildAudioReport"/>. See the class remarks for the reuse contract.
    /// </summary>
    private readonly byte[] _audioReport = new byte[AudioReportSize];

    /// <summary>
    /// Reusable buffer for the <c>0x36</c> combined report, returned by
    /// <see cref="BuildCombinedReport"/>. See the class remarks for the reuse contract.
    /// </summary>
    private readonly byte[] _combinedReport = new byte[CombinedReportSize];

    /// <summary>
    /// Builds the init-prime report <c>0x32</c> (142 bytes) that opens the audio/haptics
    /// stream. Carries a state sub-packet (packet <c>0x10</c>, length 63) whose first 47
    /// bytes are the given output state; the remaining bytes stay zero, matching the
    /// reference implementation.
    /// </summary>
    /// <param name="state">The 47-byte output state to embed.</param>
    public byte[] BuildInitPrime(SetStateData state)
    {
        byte[] report = new byte[HapticsReportSize];
        report[0] = 0x32;
        report[1] = 0x10;
        report[2] = 0x90;
        report[3] = 0x3F;
        state.CopyTo(report, 4);
        WriteCrc(report);
        return report;
    }

    /// <summary>
    /// Builds a voice-coil haptics report <c>0x32</c> (142 bytes) carrying 64 bytes of
    /// interleaved s8 stereo PCM (32 frames at 3 kHz, 10.67 ms).
    /// </summary>
    /// <param name="hapticsPcm">64 bytes of s8 stereo haptics material.</param>
    public byte[] BuildHapticsReport(ReadOnlySpan<byte> hapticsPcm)
    {
        if (hapticsPcm.Length != HapticsPayloadSize)
        {
            throw new ArgumentException($"Haptics payload must be {HapticsPayloadSize} bytes.", nameof(hapticsPcm));
        }

        byte[] report = _hapticsReport;
        report[0] = 0x32;
        report[1] = (byte)((_reportSequence & 0x0F) << 4);
        _reportSequence = (byte)((_reportSequence + 1) & 0x0F);

        // Packet 0x11 session block (sized flag set): mic-disabled sections and five
        // 0x40 buffer-length bytes, per the vDS reference.
        report[2] = 0x91;
        report[3] = 0x07;
        report[4] = 0xFE;
        report[5] = 0x40;
        report[6] = 0x40;
        report[7] = 0x40;
        report[8] = 0x40;
        report[9] = 0x40;
        report[10] = _packetCounter++;

        // Packet 0x12 haptics block (sized flag set).
        report[11] = 0x92;
        report[12] = HapticsPayloadSize;
        hapticsPcm.CopyTo(report.AsSpan(13));

        WriteCrc(report);
        return report;
    }

    /// <summary>
    /// Builds a speaker/headset audio report <c>0x35</c> (334 bytes) carrying one
    /// 200-byte Opus frame (nominal 10 ms at 48 kHz, 160 kbps CBR).
    /// </summary>
    /// <param name="opusFrame">A 200-byte Opus frame.</param>
    /// <param name="route">The output route: <see cref="BluetoothAudioRoute.Speaker"/> or
    /// <see cref="BluetoothAudioRoute.Headset"/>.</param>
    public byte[] BuildAudioReport(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route)
    {
        if (opusFrame.Length != AudioPayloadSize)
        {
            throw new ArgumentException($"Opus frame must be {AudioPayloadSize} bytes.", nameof(opusFrame));
        }

        byte[] report = _audioReport;
        report[0] = 0x35;
        report[1] = (byte)((_reportSequence & 0x0F) << 4);
        _reportSequence = (byte)((_reportSequence + 1) & 0x0F);

        // Packet 0x11 session block (sized flag set): mic-disabled sections and five
        // 0x40 buffer-length bytes, per the vDS reference.
        report[2] = 0x91;
        report[3] = 0x07;
        report[4] = 0xFE;
        report[5] = 0x40;
        report[6] = 0x40;
        report[7] = 0x40;
        report[8] = 0x40;
        report[9] = 0x40;
        report[10] = _packetCounter++;

        // Packet 0x13/0x16 route block (sized flag set).
        report[11] = (byte)route;
        report[12] = AudioPayloadSize;
        opusFrame.CopyTo(report.AsSpan(13));

        // Bytes 213-329 remain zero padding.
        WriteCrc(report);
        return report;
    }

    /// <summary>
    /// Builds a combined report <c>0x36</c> (398 bytes) carrying the 63-byte state
    /// sub-packet (packet <c>0x10</c>), the session sub-packet, one 200-byte Opus frame
    /// and the 64-byte voice-coil haptics packet in a single report. The controller only
    /// accepts haptics and audio together inside one report; sending a separate <c>0x32</c>
    /// haptics report interleaved with the audio lane corrupts the audio stream (the
    /// per-report sequence and interval counters advance twice per tick).
    /// </summary>
    /// <remarks>
    /// Layout follows the vDS reference: [session][state][haptics][speaker]. The session
    /// body uses the packet-<c>0x11</c> marker form (<c>0x91 0x07 0xFE 40 40 40 40 40 cnt</c>);
    /// the state sub-packet carries the same 47-byte output state as the <c>0x32</c>
    /// init-prime with zeroed tail bytes; the haptics sub-packet precedes the speaker
    /// lane so both audio blocks end before the trailing padding.
    /// </remarks>
    /// <param name="state">The 47-byte output state to embed.</param>
    /// <param name="opusFrame">A 200-byte Opus frame.</param>
    /// <param name="hapticsPcm">64 bytes of s8 stereo haptics material.</param>
    /// <param name="route">The output route: <see cref="BluetoothAudioRoute.Speaker"/> or
    /// <see cref="BluetoothAudioRoute.Headset"/>.</param>
    public byte[] BuildCombinedReport(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route)
    {
        if (opusFrame.Length != AudioPayloadSize)
        {
            throw new ArgumentException($"Opus frame must be {AudioPayloadSize} bytes.", nameof(opusFrame));
        }

        if (hapticsPcm.Length != HapticsPayloadSize)
        {
            throw new ArgumentException($"Haptics payload must be {HapticsPayloadSize} bytes.", nameof(hapticsPcm));
        }

        byte[] report = _combinedReport;
        report[0] = 0x36;
        report[1] = (byte)((_reportSequence & 0x0F) << 4);
        _reportSequence = (byte)((_reportSequence + 1) & 0x0F);

        // Packet 0x11 session block first, per the vDS reference (sized flag set):
        // mic-disabled sections and five 0x40 buffer-length bytes.
        report[2] = 0x91;
        report[3] = 0x07;
        report[4] = 0xFE;
        report[5] = 0x40;
        report[6] = 0x40;
        report[7] = 0x40;
        report[8] = 0x40;
        report[9] = 0x40;
        report[10] = _packetCounter++;

        // Packet 0x10 state block (63 bytes: 47-byte output state + zeroed tail).
        report[11] = 0x90;
        report[12] = 0x3F;
        state.CopyTo(report, 13);

        // Packet 0x12 haptics block (sized flag set).
        report[76] = 0x92;
        report[77] = HapticsPayloadSize;
        hapticsPcm.CopyTo(report.AsSpan(78));

        // Packet 0x13/0x16 route block (sized flag set).
        report[142] = (byte)route;
        report[143] = AudioPayloadSize;
        opusFrame.CopyTo(report.AsSpan(144));

        // Bytes 344-393 remain zero padding.
        WriteCrc(report);
        return report;
    }

    /// <summary>
    /// Resets the rolling sequence tag and interval packet counter. Call once at the
    /// start of each stream.
    /// </summary>
    public void Reset()
    {
        _reportSequence = 0;
        _packetCounter = 0;
    }

    /// <summary>
    /// Writes the seeded CRC32 over every byte before the trailing CRC field.
    /// </summary>
    private static void WriteCrc(byte[] report)
    {
        uint crc = DualSenseCRC32.Compute(report, 0, report.Length - CrcSize);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(report.Length - CrcSize, CrcSize), crc);
    }
}