namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Snapshot of the DS5Dongle bridge status read from its vendor feature reports.
/// </summary>
public readonly struct DongleStatus
{
    /// <summary>
    /// Creates a bridge status snapshot.
    /// </summary>
    /// <param name="firmwareVersion">Dongle firmware version, or an empty string when unavailable.</param>
    /// <param name="rssiDb">Bluetooth link RSSI in dB relative to the ideal range, or <c>null</c> when unavailable.</param>
    /// <param name="hasAudioFlags">Whether the firmware reported the audio-stream flags byte.</param>
    /// <param name="microphoneStreaming">Whether the USB microphone stream is active.</param>
    /// <param name="speakerActive">Whether the USB speaker stream is active.</param>
    public DongleStatus(
        string firmwareVersion,
        sbyte? rssiDb,
        bool hasAudioFlags,
        bool microphoneStreaming,
        bool speakerActive)
    {
        FirmwareVersion = firmwareVersion ?? string.Empty;
        RssiDb = rssiDb;
        HasAudioFlags = hasAudioFlags;
        MicrophoneStreaming = microphoneStreaming;
        SpeakerActive = speakerActive;
    }

    /// <summary>
    /// Dongle firmware version, or an empty string when unavailable.
    /// </summary>
    public string FirmwareVersion { get; }

    /// <summary>
    /// Bluetooth link RSSI in dB relative to the receiver's ideal range (0 = ideal),
    /// or <c>null</c> when the dongle has no reading.
    /// </summary>
    public sbyte? RssiDb { get; }

    /// <summary>
    /// Whether the firmware reported the audio-stream flags byte.
    /// </summary>
    public bool HasAudioFlags { get; }

    /// <summary>
    /// Whether the USB microphone stream is currently active.
    /// </summary>
    public bool MicrophoneStreaming { get; }

    /// <summary>
    /// Whether the USB speaker stream is currently active.
    /// </summary>
    public bool SpeakerActive { get; }

    /// <summary>
    /// Whether the snapshot contains at least one usable value.
    /// </summary>
    public bool IsValid
    {
        get
        {
            return !string.IsNullOrEmpty(FirmwareVersion) || RssiDb.HasValue || HasAudioFlags;
        }
    }
}