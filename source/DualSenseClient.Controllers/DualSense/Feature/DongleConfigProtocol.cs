using System;

namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Reads, decodes, encodes, and writes DS5Dongle configuration bodies over its vendor
/// feature reports.
/// </summary>
internal static class DongleConfigProtocol
{
    /// <summary>
    /// Feature report returning the configuration body.
    /// </summary>
    public const byte GetConfigReportId = 0xF7;

    /// <summary>
    /// Feature report carrying configuration commands.
    /// </summary>
    public const byte SetConfigReportId = 0xF6;

    /// <summary>
    /// Command that updates the in-RAM configuration.
    /// </summary>
    public const byte UpdateConfigCommand = 0x01;

    /// <summary>
    /// Command that persists the in-RAM configuration to flash.
    /// </summary>
    public const byte SaveToFlashCommand = 0x02;

    /// <summary>
    /// Command that reconnects the USB device.
    /// </summary>
    public const byte ReconnectUsbCommand = 0x03;

    /// <summary>
    /// Size of the dongle's vendor feature reports including the report ID.
    /// </summary>
    public const int FeatureReportSize = 64;

    /// <summary>
    /// Data bytes after the report ID in a vendor feature report.
    /// </summary>
    public const int PayloadSize = FeatureReportSize - 1;

    /// <summary>
    /// Reads the current configuration snapshot.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    /// <returns>The snapshot, or <c>null</c> when the report is unavailable or malformed.</returns>
    public static DongleConfig? Read(IControllerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!device.TryGetFeatureReport(GetConfigReportId, FeatureReportSize, out byte[] report))
        {
            return null;
        }

        return TryDecode(report, out DongleConfig config) ? config : null;
    }

    /// <summary>
    /// Decodes a raw <c>0xF7</c> feature report.
    /// </summary>
    /// <param name="report">Raw feature report including the report ID.</param>
    /// <param name="config">Decoded configuration when the report is valid.</param>
    /// <returns><c>true</c> when the report is a valid dongle configuration.</returns>
    public static bool TryDecode(byte[] report, out DongleConfig config)
    {
        config = null!;
        if (report.Length < 1 + DongleConfig.MinimumBodyLength || report[0] != GetConfigReportId)
        {
            return false;
        }

        return TryDecodeBody(report.AsSpan(1), out config);
    }

    /// <summary>
    /// Decodes a configuration body without its report ID.
    /// </summary>
    /// <param name="body">Configuration body bytes.</param>
    /// <param name="config">Decoded configuration when the body is valid.</param>
    /// <returns><c>true</c> when the body is a valid dongle configuration.</returns>
    public static bool TryDecodeBody(ReadOnlySpan<byte> body, out DongleConfig config)
    {
        config = null!;
        if (body.Length < DongleConfig.MinimumBodyLength)
        {
            return false;
        }

        byte version = body[0];
        if (version != DongleConfig.CurrentVersion)
        {
            return false;
        }

        // Intermediate v5 firmware revisions used 20-23 byte bodies whose bytes 14-16
        // changed meaning more than once. Refuse them instead of reinterpreting values
        // and writing a corrupt layout back.
        if (body.Length != DongleConfig.MinimumBodyLength && body.Length < DongleConfig.ModernBodyLength)
        {
            return false;
        }

        float hapticsGain = BitConverter.ToSingle(body.Slice(1, 4));
        if (!float.IsFinite(hapticsGain))
        {
            return false;
        }

        bool modern = body.Length >= DongleConfig.ModernBodyLength;
        int knownLength = modern
            ? body.Length >= DongleConfig.SleepOnDockBodyLength
                ? DongleConfig.SleepOnDockBodyLength
                : DongleConfig.ModernBodyLength
            : DongleConfig.MinimumBodyLength;
        byte[] trailingBytes = body.Length > knownLength ? body[knownLength..].ToArray() : [];

        DonglePollingRateMode pollingRateMode;
        if (modern)
        {
            pollingRateMode = (DonglePollingRateMode)(body[10] <= 3 ? body[10] : (byte)DonglePollingRateMode.Hz500);
        }
        else
        {
            pollingRateMode = body[10] switch
            {
                (byte)DonglePollingRateMode.Hz250 => DonglePollingRateMode.Hz250,
                (byte)DonglePollingRateMode.Hz500 => DonglePollingRateMode.Hz500,
                2 => DonglePollingRateMode.RealTime,
                _ => DonglePollingRateMode.Hz500
            };
        }

        config = new DongleConfig
        {
            BodyLength = body.Length,
            Version = version,
            HapticsGain = Math.Clamp(hapticsGain, 1.0f, 2.0f),
            SpeakerVolume = body[5] <= 127 ? body[5] : (byte)100,
            HeadsetVolume = body[6] <= 127 ? body[6] : (byte)100,
            SpeakerGain = body[7] <= 7 ? body[7] : (byte)2,
            InactiveTimeMinutes = body[8] <= 60 ? body[8] : (byte)30,
            DisablePicoLed = body[9] == 1,
            PollingRateMode = pollingRateMode,
            AudioBufferLength = body[11] is >= 16 and <= 128
                ? body[11]
                : modern
                    ? (byte)48
                    : (byte)64,
            ControllerMode = (DongleControllerMode)(body[12] <= 2 ? body[12] : (byte)DongleControllerMode.Auto),
            EnableUsbSerialNumber = body[13] == 1,
            EnableKeyboard = modern && body[14] == 1,
            Microphone = modern
                ? (DongleAudioDevice)(body[15] <= 3 ? body[15] : (byte)DongleAudioDevice.Auto)
                : DongleAudioDevice.Auto,
            Speaker = modern
                ? (DongleAudioDevice)(body[16] <= 3 ? body[16] : (byte)DongleAudioDevice.Auto)
                : DongleAudioDevice.Auto,
            EnableWake = body[17] == 1,
            TriggerReduce = body[18] <= (modern ? (byte)10 : (byte)7) ? body[18] : (byte)0,
            LockVolume = modern && body[19] <= (byte)DongleVolumeLockMode.SystemVolume
                ? (DongleVolumeLockMode)body[19]
                : DongleVolumeLockMode.Off,
            StatusGpioPin = modern && DongleConfig.IsValidStatusGpioPin(body[20])
                ? body[20]
                : DongleConfig.DisabledStatusGpioPin,
            StatusGpioMode = modern && body[21] == (byte)DongleStatusGpioMode.ConnectPulse
                ? DongleStatusGpioMode.ConnectPulse
                : DongleStatusGpioMode.HighWhileConnected,
            PacketMode = modern && body[22] == (byte)DonglePacketMode.Single
                ? DonglePacketMode.Single
                : DonglePacketMode.Double,
            DisconnectOnDock = modern && body[23] == 1,
            SleepOnDock = body.Length >= DongleConfig.SleepOnDockBodyLength && body[24] == 1,
            LegacyPsShortcutEnabled = !modern && body[14] == 1,
            LegacyDisableMicrophone = !modern && body[15] == 1,
            LegacyDisableSpeaker = !modern && body[16] == 1,
            TrailingBytes = trailingBytes
        };

        return true;
    }

    /// <summary>
    /// Encodes a configuration into the body length the device reported.
    /// </summary>
    /// <param name="config">Configuration to encode.</param>
    /// <returns>The encoded body, preserving unknown trailing bytes.</returns>
    public static byte[] Encode(DongleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        int bodyLength = config.BodyLength >= DongleConfig.ModernBodyLength
            ? config.BodyLength
            : DongleConfig.MinimumBodyLength;

        byte[] body = new byte[bodyLength];
        body[0] = config.Version == 0 ? DongleConfig.CurrentVersion : config.Version;

        float hapticsGain = float.IsFinite(config.HapticsGain)
            ? Math.Clamp(config.HapticsGain, 1.0f, 2.0f)
            : 1.0f;
        BitConverter.TryWriteBytes(body.AsSpan(1, 4), hapticsGain);

        body[5] = Math.Min(config.SpeakerVolume, (byte)127);
        body[6] = Math.Min(config.HeadsetVolume, (byte)127);
        body[7] = Math.Min(config.SpeakerGain, (byte)7);
        body[8] = Math.Min(config.InactiveTimeMinutes, (byte)60);
        body[9] = (byte)(config.DisablePicoLed ? 1 : 0);

        bool modern = bodyLength >= DongleConfig.ModernBodyLength;
        body[10] = modern
            ? (byte)Math.Clamp((int)config.PollingRateMode, 0, 3)
            : config.PollingRateMode switch
            {
                DonglePollingRateMode.Hz250 => (byte)DonglePollingRateMode.Hz250,
                DonglePollingRateMode.Hz500 => (byte)DonglePollingRateMode.Hz500,
                DonglePollingRateMode.RealTime => (byte)2,
                _ => (byte)DonglePollingRateMode.Hz500
            };
        body[11] = (byte)Math.Clamp((int)config.AudioBufferLength, 16, 128);
        body[12] = (byte)Math.Clamp((int)config.ControllerMode, 0, 2);
        body[13] = (byte)(config.EnableUsbSerialNumber ? 1 : 0);

        if (modern)
        {
            body[14] = (byte)(config.EnableKeyboard ? 1 : 0);
            body[15] = (byte)Math.Clamp((int)config.Microphone, 0, 3);
            body[16] = (byte)Math.Clamp((int)config.Speaker, 0, 3);
            body[17] = (byte)(config.EnableWake ? 1 : 0);
            body[18] = Math.Min(config.TriggerReduce, (byte)10);
            int lockVolume = (int)config.LockVolume;
            body[19] = (byte)(lockVolume is >= 0 and <= (int)DongleVolumeLockMode.SystemVolume ? lockVolume : 0);
            body[20] = DongleConfig.IsValidStatusGpioPin(config.StatusGpioPin)
                ? config.StatusGpioPin
                : DongleConfig.DisabledStatusGpioPin;
            body[21] = (byte)Math.Clamp((int)config.StatusGpioMode, 0, 1);
            body[22] = (byte)Math.Clamp((int)config.PacketMode, 0, 1);
            body[23] = (byte)(config.DisconnectOnDock ? 1 : 0);
            if (body.Length > 24)
            {
                body[24] = (byte)(config.SleepOnDock ? 1 : 0);
            }
        }
        else
        {
            body[14] = (byte)(config.LegacyPsShortcutEnabled ? 1 : 0);
            body[15] = (byte)(config.LegacyDisableMicrophone ? 1 : 0);
            body[16] = (byte)(config.LegacyDisableSpeaker ? 1 : 0);
            body[17] = (byte)(config.EnableWake ? 1 : 0);
            body[18] = Math.Min(config.TriggerReduce, (byte)7);
        }

        int knownLength = modern
            ? body.Length >= DongleConfig.SleepOnDockBodyLength
                ? DongleConfig.SleepOnDockBodyLength
                : DongleConfig.ModernBodyLength
            : DongleConfig.MinimumBodyLength;
        if (body.Length > knownLength && config.TrailingBytes.Length > 0)
        {
            int copyLength = Math.Min(config.TrailingBytes.Length, body.Length - knownLength);
            config.TrailingBytes.AsSpan(0, copyLength).CopyTo(body.AsSpan(knownLength));
        }

        return body;
    }

    /// <summary>
    /// Builds a 64-byte feature report for a configuration command.
    /// </summary>
    /// <param name="command">Command byte (<see cref="UpdateConfigCommand"/>, <see cref="SaveToFlashCommand"/>, or <see cref="ReconnectUsbCommand"/>).</param>
    /// <param name="body">Optional configuration body placed after the command byte.</param>
    /// <returns>The padded feature report, starting with report ID <c>0xF6</c>.</returns>
    public static byte[] BuildCommand(byte command, ReadOnlySpan<byte> body)
    {
        byte[] report = new byte[FeatureReportSize];
        report[0] = SetConfigReportId;
        report[1] = command;

        int copyLength = Math.Min(body.Length, PayloadSize - 1);
        if (copyLength > 0)
        {
            body[..copyLength].CopyTo(report.AsSpan(2));
        }

        return report;
    }

    /// <summary>
    /// Sends an in-RAM configuration update.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    /// <param name="config">Configuration to write.</param>
    public static void SendUpdate(IControllerDevice device, DongleConfig config)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] report = BuildCommand(UpdateConfigCommand, Encode(config));
        device.SendFeatureReport(report, 0, report.Length);
    }

    /// <summary>
    /// Sends the save-to-flash command.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    public static void SendSave(IControllerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] report = BuildCommand(SaveToFlashCommand, []);
        device.SendFeatureReport(report, 0, report.Length);
    }

    /// <summary>
    /// Sends the USB reconnect command.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    public static void SendReconnect(IControllerDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        byte[] report = BuildCommand(ReconnectUsbCommand, []);
        device.SendFeatureReport(report, 0, report.Length);
    }

    /// <summary>
    /// Returns whether two configurations encode to the same bytes.
    /// </summary>
    /// <param name="left">First configuration.</param>
    /// <param name="right">Second configuration.</param>
    public static bool AreEquivalent(DongleConfig left, DongleConfig right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return Encode(left).AsSpan().SequenceEqual(Encode(right));
    }
}