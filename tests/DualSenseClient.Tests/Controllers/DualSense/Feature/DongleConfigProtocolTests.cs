using DualSenseClient.Controllers.DualSense.Feature;

namespace DualSenseClient.Tests.Controllers.DualSense.Feature;

public class DongleConfigProtocolTests
{
    [Test]
    public void Decode_ModernBody_MapsEveryField()
    {
        byte[] report = BuildReport(CreateModernBody());

        bool decoded = DongleConfigProtocol.TryDecode(report, out DongleConfig config);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(config.BodyLength, Is.EqualTo(25));
            Assert.That(config.Layout, Is.EqualTo(DongleConfigLayout.Modern));
            Assert.That(config.SupportsModernSettings, Is.True);
            Assert.That(config.SupportsSleepOnDock, Is.True);
            Assert.That(config.Version, Is.EqualTo(5));
            Assert.That(config.HapticsGain, Is.EqualTo(1.5f));
            Assert.That(config.SpeakerVolume, Is.EqualTo(80));
            Assert.That(config.HeadsetVolume, Is.EqualTo(63));
            Assert.That(config.SpeakerGain, Is.EqualTo(2));
            Assert.That(config.InactiveTimeMinutes, Is.EqualTo(30));
            Assert.That(config.DisablePicoLed, Is.False);
            Assert.That(config.PollingRateMode, Is.EqualTo(DonglePollingRateMode.Hz500));
            Assert.That(config.AudioBufferLength, Is.EqualTo(64));
            Assert.That(config.ControllerMode, Is.EqualTo(DongleControllerMode.Auto));
            Assert.That(config.EnableUsbSerialNumber, Is.False);
            Assert.That(config.EnableKeyboard, Is.True);
            Assert.That(config.Microphone, Is.EqualTo(DongleAudioDevice.Headphone));
            Assert.That(config.Speaker, Is.EqualTo(DongleAudioDevice.Disabled));
            Assert.That(config.EnableWake, Is.True);
            Assert.That(config.TriggerReduce, Is.EqualTo(4));
            Assert.That(config.LockVolume, Is.EqualTo(DongleVolumeLockMode.GameAndSoftware));
            Assert.That(config.StatusGpioPin, Is.EqualTo(5));
            Assert.That(config.StatusGpioMode, Is.EqualTo(DongleStatusGpioMode.ConnectPulse));
            Assert.That(config.PacketMode, Is.EqualTo(DonglePacketMode.Single));
            Assert.That(config.DisconnectOnDock, Is.True);
            Assert.That(config.SleepOnDock, Is.True);
        });
    }

    [Test]
    public void Decode_LegacyBody_MapsLegacyFieldsAndHidesModernSettings()
    {
        byte[] report = BuildReport(CreateLegacyBody());

        bool decoded = DongleConfigProtocol.TryDecode(report, out DongleConfig config);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(config.BodyLength, Is.EqualTo(19));
            Assert.That(config.Layout, Is.EqualTo(DongleConfigLayout.Legacy));
            Assert.That(config.SupportsModernSettings, Is.False);
            Assert.That(config.SupportsSleepOnDock, Is.False);
            Assert.That(config.HapticsGain, Is.EqualTo(1.25f));
            Assert.That(config.SpeakerVolume, Is.EqualTo(100));
            Assert.That(config.HeadsetVolume, Is.EqualTo(90));
            Assert.That(config.SpeakerGain, Is.EqualTo(3));
            Assert.That(config.InactiveTimeMinutes, Is.EqualTo(15));
            Assert.That(config.DisablePicoLed, Is.True);
            Assert.That(config.PollingRateMode, Is.EqualTo(DonglePollingRateMode.Hz250));
            Assert.That(config.AudioBufferLength, Is.EqualTo(32));
            Assert.That(config.ControllerMode, Is.EqualTo(DongleControllerMode.DualSenseEdge));
            Assert.That(config.EnableUsbSerialNumber, Is.True);
            Assert.That(config.LegacyPsShortcutEnabled, Is.True);
            Assert.That(config.LegacyDisableMicrophone, Is.True);
            Assert.That(config.LegacyDisableSpeaker, Is.False);
            Assert.That(config.EnableWake, Is.True);
            Assert.That(config.TriggerReduce, Is.EqualTo(3));
            Assert.That(config.EnableKeyboard, Is.False);
            Assert.That(config.Microphone, Is.EqualTo(DongleAudioDevice.Auto));
            Assert.That(config.Speaker, Is.EqualTo(DongleAudioDevice.Auto));
            Assert.That(config.LockVolume, Is.EqualTo(DongleVolumeLockMode.Off));
            Assert.That(config.StatusGpioPin, Is.EqualTo(0xFF));
            Assert.That(config.PacketMode, Is.EqualTo(DonglePacketMode.Double));
            Assert.That(config.SleepOnDock, Is.False);
        });
    }

    [Test]
    public void Decode_SystemVolumeLock_MapsToSystemVolumeAndEncodesBackToTwo()
    {
        byte[] body = CreateModernBody();
        body[19] = (byte)DongleVolumeLockMode.SystemVolume;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(config.LockVolume, Is.EqualTo(DongleVolumeLockMode.SystemVolume));
            Assert.That(DongleConfigProtocol.Encode(config)[19], Is.EqualTo(2));
        });
    }

    [Test]
    public void Decode_InvalidVolumeLockMode_FallsBackToOff()
    {
        byte[] body = CreateModernBody();
        body[19] = 3;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config), Is.True);
        Assert.That(config.LockVolume, Is.EqualTo(DongleVolumeLockMode.Off));
    }

    [Test]
    public void Encode_InvalidVolumeLockMode_FallsBackToOff()
    {
        DongleConfig config = DongleConfig.CreateDefault(25) with
        {
            LockVolume = (DongleVolumeLockMode)3
        };

        Assert.That(DongleConfigProtocol.Encode(config)[19], Is.Zero);
    }

    [Test]
    public void Decode_LegacyTriggerReduceAboveSeven_FallsBackToZero()
    {
        byte[] body = CreateLegacyBody();
        body[18] = 8;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config), Is.True);
        Assert.That(config.TriggerReduce, Is.Zero);
    }

    [Test]
    public void Encode_LegacyTriggerReduce_ClampsToSeven()
    {
        DongleConfig config = DongleConfig.CreateDefault(19) with
        {
            TriggerReduce = 10
        };

        Assert.That(DongleConfigProtocol.Encode(config)[18], Is.EqualTo(7));
    }

    [Test]
    public void Decode_LegacyInvalidAudioBuffer_FallsBackTo64()
    {
        byte[] body = CreateLegacyBody();
        body[11] = 15;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config), Is.True);
        Assert.That(config.AudioBufferLength, Is.EqualTo(64));
    }

    [Test]
    public void Decode_ModernInvalidAudioBuffer_FallsBackTo48()
    {
        byte[] body = CreateModernBody();
        body[11] = 15;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config), Is.True);
        Assert.That(config.AudioBufferLength, Is.EqualTo(48));
    }

    [Test]
    public void Decode_LegacyRealTimePolling_MapsToRealTimeAndEncodesBackToTwo()
    {
        byte[] body = CreateLegacyBody();
        body[10] = 2;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(config.PollingRateMode, Is.EqualTo(DonglePollingRateMode.RealTime));
            Assert.That(DongleConfigProtocol.Encode(config)[10], Is.EqualTo(2));
        });
    }

    [Test]
    public void Decode_UnsupportedLegacyLengths_ReturnFalse()
    {
        foreach (int length in new[]
                 {
                     20, 21, 22, 23
                 })
        {
            byte[] body = new byte[length];
            body[0] = 5;
            BitConverter.GetBytes(1.0f).CopyTo(body, 1);

            Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out _), Is.False, $"length {length}");
        }
    }

    [Test]
    public void Decode_Version4Body_ReturnsFalse()
    {
        byte[] body = CreateLegacyBody();
        body[0] = 4;

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out _), Is.False);
    }

    [Test]
    public void CreateDefault_Legacy_UsesFirmwareLegacyBufferDefault()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DongleConfig.CreateDefault(19).AudioBufferLength, Is.EqualTo(64));
            Assert.That(DongleConfig.CreateDefault(25).AudioBufferLength, Is.EqualTo(48));
        });
    }

    [Test]
    public void CreateDefault_UnsupportedLengthNormalizesToLegacy() => Assert.That(DongleConfig.CreateDefault(22).BodyLength, Is.EqualTo(19));

    [Test]
    public void Decode_ModernBodyWithoutSleep_ReportsNoSleepField()
    {
        byte[] body = CreateModernBody()[..24];

        bool decoded = DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(config.SupportsModernSettings, Is.True);
            Assert.That(config.SupportsSleepOnDock, Is.False);
            Assert.That(config.SleepOnDock, Is.False);
        });
    }

    [Test]
    public void Decode_TrailingBytes_ArePreserved()
    {
        byte[] body = [.. CreateModernBody(), 0xAA, 0xBB];

        bool decoded = DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config);

        Assert.Multiple(() =>
        {
            Assert.That(decoded, Is.True);
            Assert.That(config.TrailingBytes, Is.EqualTo(new byte[]
            {
                0xAA, 0xBB
            }));
        });
    }

    [Test]
    public void Decode_ShortBody_ReturnsFalse()
    {
        byte[] body = CreateModernBody()[..18];

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(body), out _), Is.False);
    }

    [Test]
    public void Decode_WrongReportId_ReturnsFalse()
    {
        byte[] report = BuildReport(CreateModernBody());
        report[0] = 0xF8;

        Assert.That(DongleConfigProtocol.TryDecode(report, out _), Is.False);
    }

    [Test]
    public void Decode_NonFiniteHapticsGain_ReturnsFalse()
    {
        byte[] nanBody = CreateModernBody();
        BitConverter.GetBytes(float.NaN).CopyTo(nanBody, 1);

        byte[] infinityBody = CreateModernBody();
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(infinityBody, 1);

        Assert.Multiple(() =>
        {
            Assert.That(DongleConfigProtocol.TryDecode(BuildReport(nanBody), out _), Is.False);
            Assert.That(DongleConfigProtocol.TryDecode(BuildReport(infinityBody), out _), Is.False);
        });
    }

    [Test]
    public void Encode_ModernBody_RoundTripsEveryFieldAndSleepOnDock()
    {
        DongleConfigProtocol.TryDecode(BuildReport(CreateModernBody()), out DongleConfig config);

        byte[] encoded = DongleConfigProtocol.Encode(config);

        Assert.Multiple(() =>
        {
            Assert.That(encoded, Has.Length.EqualTo(25));
            Assert.That(encoded[14], Is.EqualTo(1));
            Assert.That(encoded[15], Is.EqualTo(2));
            Assert.That(encoded[16], Is.EqualTo(3));
            Assert.That(encoded[17], Is.EqualTo(1));
            Assert.That(encoded[18], Is.EqualTo(4));
            Assert.That(encoded[19], Is.EqualTo(1));
            Assert.That(encoded[20], Is.EqualTo(5));
            Assert.That(encoded[21], Is.EqualTo(1));
            Assert.That(encoded[22], Is.EqualTo(1));
            Assert.That(encoded[23], Is.EqualTo(1));
            Assert.That(encoded[24], Is.EqualTo(1));
        });

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(encoded), out DongleConfig roundTripped), Is.True);
        Assert.That(DongleConfigProtocol.AreEquivalent(config, roundTripped), Is.True);
    }

    [Test]
    public void Encode_LegacyBody_RoundTripsLegacyFields()
    {
        DongleConfigProtocol.TryDecode(BuildReport(CreateLegacyBody()), out DongleConfig config);

        byte[] encoded = DongleConfigProtocol.Encode(config);

        Assert.Multiple(() =>
        {
            Assert.That(encoded, Has.Length.EqualTo(19));
            Assert.That(encoded[14], Is.EqualTo(1));
            Assert.That(encoded[15], Is.EqualTo(1));
            Assert.That(encoded[16], Is.EqualTo(0));
            Assert.That(encoded[17], Is.EqualTo(1));
            Assert.That(encoded[18], Is.EqualTo(3));
        });

        Assert.That(DongleConfigProtocol.TryDecode(BuildReport(encoded), out DongleConfig roundTripped), Is.True);
        Assert.That(DongleConfigProtocol.AreEquivalent(config, roundTripped), Is.True);
    }

    [Test]
    public void Encode_PreservesTrailingBytes()
    {
        byte[] body = [.. CreateModernBody(), 0xAA, 0xBB];
        DongleConfigProtocol.TryDecode(BuildReport(body), out DongleConfig config);

        byte[] encoded = DongleConfigProtocol.Encode(config);

        Assert.Multiple(() =>
        {
            Assert.That(encoded, Has.Length.EqualTo(27));
            Assert.That(encoded[25], Is.EqualTo(0xAA));
            Assert.That(encoded[26], Is.EqualTo(0xBB));
        });
    }

    [Test]
    public void BuildCommand_Update_PadsTo64Bytes()
    {
        byte[] body = CreateModernBody();

        byte[] report = DongleConfigProtocol.BuildCommand(DongleConfigProtocol.UpdateConfigCommand, body);

        Assert.Multiple(() =>
        {
            Assert.That(report, Has.Length.EqualTo(64));
            Assert.That(report[0], Is.EqualTo(0xF6));
            Assert.That(report[1], Is.EqualTo(0x01));
            Assert.That(report.AsSpan(2, body.Length).SequenceEqual(body), Is.True);
            Assert.That(report.AsSpan(2 + body.Length).ToArray(), Is.All.Zero);
        });
    }

    [Test]
    public void BuildCommand_SaveAndReconnect_UseCommandOnly()
    {
        byte[] save = DongleConfigProtocol.BuildCommand(DongleConfigProtocol.SaveToFlashCommand, []);
        byte[] reconnect = DongleConfigProtocol.BuildCommand(DongleConfigProtocol.ReconnectUsbCommand, []);

        Assert.Multiple(() =>
        {
            Assert.That(save[0], Is.EqualTo(0xF6));
            Assert.That(save[1], Is.EqualTo(0x02));
            Assert.That(save.AsSpan(2).ToArray(), Is.All.Zero);
            Assert.That(reconnect[0], Is.EqualTo(0xF6));
            Assert.That(reconnect[1], Is.EqualTo(0x03));
            Assert.That(reconnect.AsSpan(2).ToArray(), Is.All.Zero);
        });
    }

    [Test]
    public void AreEquivalent_DetectsEveryChange()
    {
        DongleConfigProtocol.TryDecode(BuildReport(CreateModernBody()), out DongleConfig config);
        DongleConfig identical = config with
        {
        };

        Assert.Multiple(() =>
        {
            Assert.That(DongleConfigProtocol.AreEquivalent(config, identical), Is.True);
            Assert.That(DongleConfigProtocol.AreEquivalent(config, config with
            {
                HapticsGain = 1.75f
            }), Is.False);
            Assert.That(DongleConfigProtocol.AreEquivalent(config, config with
            {
                SleepOnDock = false
            }), Is.False);
            Assert.That(DongleConfigProtocol.AreEquivalent(config, config with
            {
                StatusGpioPin = 0xFF
            }), Is.False);
        });
    }

    private static byte[] CreateModernBody()
    {
        byte[] body = new byte[25];
        body[0] = 5;
        BitConverter.GetBytes(1.5f).CopyTo(body, 1);
        body[5] = 80;
        body[6] = 63;
        body[7] = 2;
        body[8] = 30;
        body[9] = 0;
        body[10] = (byte)DonglePollingRateMode.Hz500;
        body[11] = 64;
        body[12] = (byte)DongleControllerMode.Auto;
        body[13] = 0;
        body[14] = 1;
        body[15] = (byte)DongleAudioDevice.Headphone;
        body[16] = (byte)DongleAudioDevice.Disabled;
        body[17] = 1;
        body[18] = 4;
        body[19] = 1;
        body[20] = 5;
        body[21] = (byte)DongleStatusGpioMode.ConnectPulse;
        body[22] = (byte)DonglePacketMode.Single;
        body[23] = 1;
        body[24] = 1;
        return body;
    }

    private static byte[] CreateLegacyBody()
    {
        byte[] body = new byte[19];
        body[0] = 5;
        BitConverter.GetBytes(1.25f).CopyTo(body, 1);
        body[5] = 100;
        body[6] = 90;
        body[7] = 3;
        body[8] = 15;
        body[9] = 1;
        body[10] = (byte)DonglePollingRateMode.Hz250;
        body[11] = 32;
        body[12] = (byte)DongleControllerMode.DualSenseEdge;
        body[13] = 1;
        body[14] = 1;
        body[15] = 1;
        body[16] = 0;
        body[17] = 1;
        body[18] = 3;
        return body;
    }

    private static byte[] BuildReport(byte[] body)
    {
        byte[] report = new byte[body.Length + 1];
        report[0] = 0xF7;
        body.CopyTo(report, 1);
        return report;
    }
}