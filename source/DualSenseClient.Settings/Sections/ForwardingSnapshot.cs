namespace DualSenseClient.Settings.Sections;

/// <summary>
/// An immutable, normalized view of <see cref="ForwardSettings"/> used while applying
/// forwarding changes to a running lane. The persisted model stays mutable; this value
/// prevents torn reads and gives the lane something stable to compare.
/// </summary>
/// <param name="AudioOutput">The physical controller output route.</param>
/// <param name="Volume">Speaker volume (0-255).</param>
/// <param name="AudioBufferLength">Controller-side buffer length (16-128).</param>
/// <param name="PacketMode">Bluetooth packet shape.</param>
/// <param name="Haptics">Audio-derived haptic strength (0-200).</param>
/// <param name="ForceAudioHaptics">Whether game haptics are replaced by audio haptics.</param>
/// <param name="Rumble">Whether classic rumble is forwarded.</param>
/// <param name="Triggers">Whether adaptive trigger effects are forwarded.</param>
/// <param name="Lightbar">Whether lightbar output is forwarded.</param>
/// <param name="PlayerLeds">Whether player LEDs are forwarded.</param>
/// <param name="HapticsEnabled">Whether game haptics are forwarded.</param>
/// <param name="AudioEnabled">Whether controller-endpoint audio is forwarded.</param>
/// <param name="AudioSource">The forwarded audio source.</param>
/// <param name="AudioApp">The captured application executable path.</param>
public readonly record struct ForwardingSnapshot(
    EmulationAudioOutput AudioOutput,
    byte Volume,
    byte AudioBufferLength,
    BluetoothAudioPacketMode PacketMode,
    int Haptics,
    bool ForceAudioHaptics,
    bool Rumble,
    bool Triggers,
    bool Lightbar,
    bool PlayerLeds,
    bool HapticsEnabled,
    bool AudioEnabled,
    EmulationAudioSource AudioSource,
    string AudioApp)
{
    /// <summary>
    /// Creates a normalized snapshot from the persisted forwarding settings.
    /// </summary>
    /// <param name="settings">The persisted settings.</param>
    /// <returns>The normalized snapshot.</returns>
    public static ForwardingSnapshot From(ForwardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ForwardingSnapshot(
            Enum.IsDefined(settings.AudioOutput) ? settings.AudioOutput : EmulationAudioOutput.Auto,
            (byte)Math.Clamp(settings.Volume, 0, 255),
            (byte)Math.Clamp(settings.AudioBufferLength, 16, 128),
            Enum.IsDefined(settings.PacketMode) ? settings.PacketMode : BluetoothAudioPacketMode.Single,
            Math.Clamp(settings.Haptics, 0, 200),
            settings.ForceAudioHaptics,
            settings.Rumble,
            settings.Triggers,
            settings.Lightbar,
            settings.PlayerLeds,
            settings.HapticsEnabled,
            settings.AudioEnabled,
            Enum.IsDefined(settings.AudioSource) ? settings.AudioSource : EmulationAudioSource.ControllerEndpoint,
            settings.AudioApp?.Trim() ?? string.Empty);
    }
}