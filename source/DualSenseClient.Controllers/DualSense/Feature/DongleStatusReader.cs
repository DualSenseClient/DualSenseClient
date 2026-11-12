using System.Text;

namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Reads the DS5Dongle bridge status from its vendor feature reports.
/// </summary>
internal static class DongleStatusReader
{
    /// <summary>
    /// Feature report returning the dongle firmware version string.
    /// </summary>
    private const byte FirmwareReportId = 0xF8;

    /// <summary>
    /// Feature report returning the Bluetooth RSSI and audio-stream flags.
    /// </summary>
    private const byte StatusReportId = 0xF9;

    /// <summary>
    /// Size of the dongle's 63-byte vendor feature reports plus the report ID.
    /// </summary>
    private const int FeatureReportSize = 64;

    /// <summary>
    /// Marks the audio flags byte as valid; firmware without it leaves the byte zero.
    /// </summary>
    private const byte AudioFlagsValidMask = 0x80;

    /// <summary>
    /// Set when the controller microphone stream is active.
    /// </summary>
    private const byte MicrophoneStreamingMask = 0x01;

    /// <summary>
    /// Set when the controller speaker stream is active.
    /// </summary>
    private const byte SpeakerActiveMask = 0x02;

    /// <summary>
    /// Reads the current dongle status snapshot.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    /// <returns>The snapshot, or <c>null</c> when neither report answered.</returns>
    public static DongleStatus? Read(IControllerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        string firmwareVersion = ReadFirmwareVersion(device);
        bool hasStatus = TryReadStatus(
            device,
            out sbyte? rssiDb,
            out bool hasAudioFlags,
            out bool microphoneStreaming,
            out bool speakerActive);

        if (firmwareVersion.Length == 0 && !hasStatus)
        {
            return null;
        }

        return new DongleStatus(firmwareVersion, rssiDb, hasAudioFlags, microphoneStreaming, speakerActive);
    }

    /// <summary>
    /// Reads and validates the firmware version report.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    /// <returns>The trimmed version string, or an empty string when unavailable.</returns>
    private static string ReadFirmwareVersion(IControllerDevice device)
    {
        if (!device.TryGetFeatureReport(FirmwareReportId, FeatureReportSize, out byte[] report)
            || report.Length < 2
            || report[0] != FirmwareReportId)
        {
            return string.Empty;
        }

        int length = 1;
        while (length < report.Length && report[length] != 0)
        {
            length++;
        }

        for (int i = 1; i < length; i++)
        {
            if (report[i] < 0x20 || report[i] > 0x7E)
            {
                return string.Empty;
            }
        }

        return Encoding.ASCII.GetString(report, 1, length - 1).Trim();
    }

    /// <summary>
    /// Reads and validates the link/audio status report.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    /// <param name="rssiDb">The RSSI in dB relative to the ideal range, or <c>null</c> when the dongle has no reading.</param>
    /// <param name="hasAudioFlags">Whether the firmware supplied the audio flags byte.</param>
    /// <param name="microphoneStreaming">Whether the microphone stream is active.</param>
    /// <param name="speakerActive">Whether the speaker stream is active.</param>
    /// <returns><c>true</c> when the report contained a usable value.</returns>
    private static bool TryReadStatus(
        IControllerDevice device,
        out sbyte? rssiDb,
        out bool hasAudioFlags,
        out bool microphoneStreaming,
        out bool speakerActive)
    {
        rssiDb = null;
        hasAudioFlags = false;
        microphoneStreaming = false;
        speakerActive = false;

        if (!device.TryGetFeatureReport(StatusReportId, FeatureReportSize, out byte[] report)
            || report.Length < 2
            || report[0] != StatusReportId)
        {
            return false;
        }

        // Classic Bluetooth RSSI is dB relative to the receiver's ideal range:
        // 0 is ideal, negative is weaker. 0x80 (-128) is the newer firmware's
        // "no reading" sentinel; positive values are not valid RSSI readings.
        sbyte rssi = (sbyte)report[1];
        if (rssi == 0 || (rssi < 0 && rssi != sbyte.MinValue))
        {
            rssiDb = rssi;
        }

        if (report.Length >= 3 && (report[2] & AudioFlagsValidMask) != 0)
        {
            hasAudioFlags = true;
            microphoneStreaming = (report[2] & MicrophoneStreamingMask) != 0;
            speakerActive = (report[2] & SpeakerActiveMask) != 0;
        }

        return rssiDb.HasValue || hasAudioFlags;
    }
}