using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.GUI.Models.Items;

namespace DualSenseClient.Tests.GUI.Models.Items;

public class DongleConfigItemTests
{
    [Test]
    public void Load_ModernConfig_MapsEveryProperty()
    {
        DongleConfig config = CreateModernConfig();

        DongleConfigItem item = new DongleConfigItem(config);

        Assert.Multiple(() =>
        {
            Assert.That(item.IsLegacy, Is.False);
            Assert.That(item.SupportsModernSettings, Is.True);
            Assert.That(item.SupportsGpio, Is.True);
            Assert.That(item.HapticsGain, Is.EqualTo(1.5));
            Assert.That(item.SpeakerGain, Is.EqualTo(2));
            Assert.That(item.TriggerReduce, Is.EqualTo(4));
            Assert.That(item.AudioBufferLength, Is.EqualTo(64));
            Assert.That(item.InactiveTimeMinutes, Is.EqualTo(30));
            Assert.That(item.LockVolumeIndex, Is.EqualTo((int)DongleVolumeLockMode.GameAndSoftware));
            Assert.That(item.DisablePicoLed, Is.False);
            Assert.That(item.EnableWake, Is.True);
            Assert.That(item.DisconnectOnDock, Is.True);
            Assert.That(item.EnableUsbSerialNumber, Is.False);
            Assert.That(item.EnableKeyboard, Is.True);
            Assert.That(item.SinglePacketMode, Is.True);
            Assert.That(item.PollingRateIndex, Is.EqualTo((int)DonglePollingRateMode.Hz500));
            Assert.That(item.ControllerModeIndex, Is.EqualTo((int)DongleControllerMode.Auto));
            Assert.That(item.MicrophoneIndex, Is.EqualTo((int)DongleAudioDevice.Headphone));
            Assert.That(item.SpeakerIndex, Is.EqualTo((int)DongleAudioDevice.Disabled));
            Assert.That(item.StatusGpioPinIndex, Is.EqualTo(4));
            Assert.That(item.StatusGpioModeIndex, Is.EqualTo((int)DongleStatusGpioMode.ConnectPulse));
        });
    }

    [Test]
    public void Load_LegacyConfig_MarksLegacyAndHidesModernSettings()
    {
        DongleConfig config = DongleConfig.CreateDefault(19) with
        {
            LegacyPsShortcutEnabled = true,
            LegacyDisableMicrophone = true,
            LegacyDisableSpeaker = false
        };

        DongleConfigItem item = new DongleConfigItem(config);

        Assert.Multiple(() =>
        {
            Assert.That(item.IsLegacy, Is.True);
            Assert.That(item.SupportsModernSettings, Is.False);
            Assert.That(item.SupportsGpio, Is.False);
            Assert.That(item.LegacyPsShortcutEnabled, Is.True);
            Assert.That(item.LegacyDisableMicrophone, Is.True);
            Assert.That(item.LegacyDisableSpeaker, Is.False);
        });
    }

    [Test]
    public void Load_LegacyConfig_MapsRealTimePollingToLegacyIndexTwo()
    {
        DongleConfig config = DongleConfig.CreateDefault(19) with
        {
            PollingRateMode = DonglePollingRateMode.RealTime
        };

        DongleConfigItem item = new DongleConfigItem(config);

        Assert.Multiple(() =>
        {
            Assert.That(item.LegacyPollingRateIndex, Is.EqualTo(2));
            Assert.That(item.PollingRateIndex, Is.EqualTo(1));
            Assert.That(item.ToConfig().PollingRateMode, Is.EqualTo(DonglePollingRateMode.RealTime));
        });
    }

    [Test]
    public void ToConfig_UsesOnlyTheVisiblePollingIndex()
    {
        DongleConfigItem legacy = new DongleConfigItem(DongleConfig.CreateDefault(19))
        {
            PollingRateIndex = 3,
            LegacyPollingRateIndex = 0
        };
        DongleConfigItem modern = new DongleConfigItem(DongleConfig.CreateDefault(25))
        {
            PollingRateIndex = 3,
            LegacyPollingRateIndex = 2
        };

        Assert.Multiple(() =>
        {
            Assert.That(legacy.ToConfig().PollingRateMode, Is.EqualTo(DonglePollingRateMode.Hz250));
            Assert.That(modern.ToConfig().PollingRateMode, Is.EqualTo(DonglePollingRateMode.RealTime));
        });
    }

    [Test]
    public void ToConfig_InvalidPollingIndex_FallsBackTo500Hz()
    {
        DongleConfigItem legacy = new DongleConfigItem(DongleConfig.CreateDefault(19))
        {
            LegacyPollingRateIndex = -1
        };
        DongleConfigItem modern = new DongleConfigItem(DongleConfig.CreateDefault(25))
        {
            PollingRateIndex = -1
        };

        Assert.Multiple(() =>
        {
            Assert.That(legacy.ToConfig().PollingRateMode, Is.EqualTo(DonglePollingRateMode.Hz500));
            Assert.That(modern.ToConfig().PollingRateMode, Is.EqualTo(DonglePollingRateMode.Hz500));
        });
    }

    [Test]
    public void ChangingProperty_IncrementsRevision()
    {
        DongleConfigItem item = new DongleConfigItem(CreateModernConfig());
        int before = item.Revision;

        item.HapticsGain = 1.75;

        Assert.That(item.Revision, Is.EqualTo(before + 1));
    }

    [Test]
    public void Load_DoesNotIncrementRevision()
    {
        DongleConfigItem item = new DongleConfigItem(CreateModernConfig());
        int before = item.Revision;

        item.Load(DongleConfig.CreateDefault(25));

        Assert.That(item.Revision, Is.EqualTo(before));
    }

    [Test]
    public void ChangingProperty_RaisesChanged()
    {
        DongleConfigItem item = new DongleConfigItem(CreateModernConfig());
        int changes = 0;
        item.Changed += (_, _) => changes++;

        item.HapticsGain = 1.75;

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Load_DoesNotRaiseChanged()
    {
        DongleConfigItem item = new DongleConfigItem(CreateModernConfig());
        int changes = 0;
        item.Changed += (_, _) => changes++;

        item.Load(DongleConfig.CreateDefault(25));

        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void ToConfig_MapsIndexPropertiesBackToEnums()
    {
        DongleConfigItem item = new DongleConfigItem(CreateModernConfig())
        {
            PollingRateIndex = (int)DonglePollingRateMode.RealTime,
            ControllerModeIndex = (int)DongleControllerMode.DualSenseEdge,
            MicrophoneIndex = (int)DongleAudioDevice.Headphone,
            SpeakerIndex = (int)DongleAudioDevice.Disabled,
            LockVolumeIndex = (int)DongleVolumeLockMode.SystemVolume,
            StatusGpioPinIndex = 1,
            StatusGpioModeIndex = (int)DongleStatusGpioMode.ConnectPulse,
            SinglePacketMode = true
        };

        DongleConfig config = item.ToConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.PollingRateMode, Is.EqualTo(DonglePollingRateMode.RealTime));
            Assert.That(config.ControllerMode, Is.EqualTo(DongleControllerMode.DualSenseEdge));
            Assert.That(config.Microphone, Is.EqualTo(DongleAudioDevice.Headphone));
            Assert.That(config.Speaker, Is.EqualTo(DongleAudioDevice.Disabled));
            Assert.That(config.LockVolume, Is.EqualTo(DongleVolumeLockMode.SystemVolume));
            Assert.That(config.StatusGpioPin, Is.EqualTo(2));
            Assert.That(config.StatusGpioMode, Is.EqualTo(DongleStatusGpioMode.ConnectPulse));
            Assert.That(config.PacketMode, Is.EqualTo(DonglePacketMode.Single));
        });
    }

    [Test]
    public void StatusGpioPinIndex_MapsDisabledAndBoardPins()
    {
        DongleConfig disabled = DongleConfig.CreateDefault(25) with
        {
            StatusGpioPin = DongleConfig.DisabledStatusGpioPin
        };
        DongleConfig pinFive = DongleConfig.CreateDefault(25) with
        {
            StatusGpioPin = 5
        };
        DongleConfigItem disabledItem = new DongleConfigItem(disabled);
        DongleConfigItem pinFiveItem = new DongleConfigItem(pinFive);

        Assert.Multiple(() =>
        {
            Assert.That(disabledItem.StatusGpioPinIndex, Is.Zero);
            Assert.That(disabledItem.HasGpioPin, Is.False);
            Assert.That(pinFiveItem.StatusGpioPinIndex, Is.EqualTo(4));
            Assert.That(pinFiveItem.HasGpioPin, Is.True);
            Assert.That(pinFiveItem.ToConfig().StatusGpioPin, Is.EqualTo(5));
        });

        disabledItem.StatusGpioPinIndex = DongleConfigItem.GpioPinValues.Count - 1;
        Assert.That(disabledItem.ToConfig().StatusGpioPin, Is.EqualTo(28));
    }

    [Test]
    public void ToConfig_PreservesUnshownAndTrailingFields()
    {
        DongleConfig template = CreateModernConfig() with
        {
            SpeakerVolume = 77,
            HeadsetVolume = 88,
            SleepOnDock = true,
            TrailingBytes = [0xAA, 0xBB]
        };
        DongleConfigItem item = new DongleConfigItem(template);

        item.HapticsGain = 1.75;
        DongleConfig config = item.ToConfig();

        Assert.Multiple(() =>
        {
            Assert.That(config.SpeakerVolume, Is.EqualTo(77));
            Assert.That(config.HeadsetVolume, Is.EqualTo(88));
            Assert.That(config.SleepOnDock, Is.True);
            Assert.That(config.TrailingBytes, Is.EqualTo(new byte[]
            {
                0xAA, 0xBB
            }));
            Assert.That(config.HapticsGain, Is.EqualTo(1.75f));
        });
    }

    [Test]
    public void TriggerReduce_LegacyLayoutUsesFirmwareMaximumOfSeven()
    {
        DongleConfigItem item = new DongleConfigItem(DongleConfig.CreateDefault(19) with
        {
            TriggerReduce = 10
        });

        Assert.Multiple(() =>
        {
            Assert.That(item.TriggerReduceMax, Is.EqualTo(7));
            Assert.That(item.TriggerReduce, Is.EqualTo(7));
        });

        item.TriggerReduce = 10;
        Assert.That(item.ToConfig().TriggerReduce, Is.EqualTo(7));
    }

    [Test]
    public void TriggerReduce_ModernLayoutAllowsTen()
    {
        DongleConfigItem item = new DongleConfigItem(DongleConfig.CreateDefault(25));

        Assert.That(item.TriggerReduceMax, Is.EqualTo(10));

        item.TriggerReduce = 10;
        Assert.That(item.ToConfig().TriggerReduce, Is.EqualTo(10));
    }

    [Test]
    public void ShowSinglePacketHint_TrueBelow32WithDoublePackets()
    {
        DongleConfigItem item = new DongleConfigItem(CreateModernConfig() with
        {
            PacketMode = DonglePacketMode.Double,
            AudioBufferLength = 16
        });

        Assert.That(item.ShowSinglePacketHint, Is.True);

        item.AudioBufferLength = 32;
        Assert.That(item.ShowSinglePacketHint, Is.False);

        item.AudioBufferLength = 16;
        item.SinglePacketMode = true;
        Assert.That(item.ShowSinglePacketHint, Is.False);

        item.SinglePacketMode = false;
        item.AudioBufferLength = 48;
        Assert.That(item.ShowSinglePacketHint, Is.False);
    }

    [Test]
    public void GpioPinValues_MatchesPico2WPinList()
    {
        int[] expected =
        [
            DongleConfig.DisabledStatusGpioPin,
            2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22,
            26, 27, 28
        ];

        Assert.That(DongleConfigItem.GpioPinValues, Is.EqualTo(expected));
    }

    private static DongleConfig CreateModernConfig()
    {
        return DongleConfig.CreateDefault(25) with
        {
            HapticsGain = 1.5f,
            SpeakerGain = 2,
            TriggerReduce = 4,
            AudioBufferLength = 64,
            InactiveTimeMinutes = 30,
            LockVolume = DongleVolumeLockMode.GameAndSoftware,
            DisablePicoLed = false,
            EnableWake = true,
            DisconnectOnDock = true,
            EnableUsbSerialNumber = false,
            EnableKeyboard = true,
            PacketMode = DonglePacketMode.Single,
            PollingRateMode = DonglePollingRateMode.Hz500,
            ControllerMode = DongleControllerMode.Auto,
            Microphone = DongleAudioDevice.Headphone,
            Speaker = DongleAudioDevice.Disabled,
            StatusGpioPin = 5,
            StatusGpioMode = DongleStatusGpioMode.ConnectPulse
        };
    }
}