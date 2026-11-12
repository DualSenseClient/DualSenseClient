namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Byte layout family reported by a DS5Dongle configuration body.
/// </summary>
public enum DongleConfigLayout
{
    /// <summary>
    /// Older firmware with the PS-shortcut/disable-mic/disable-speaker bytes but no
    /// lock-volume, GPIO, packet-mode, or dock fields.
    /// </summary>
    Legacy,

    /// <summary>
    /// Current firmware with the full website configuration layout.
    /// </summary>
    Modern
}

/// <summary>
/// DS5Dongle HID report polling cadence.
/// </summary>
public enum DonglePollingRateMode : byte
{
    /// <summary>
    /// 250 Hz report interval.
    /// </summary>
    Hz250 = 0,

    /// <summary>
    /// 500 Hz report interval.
    /// </summary>
    Hz500 = 1,

    /// <summary>
    /// 1000 Hz report interval.
    /// </summary>
    Hz1000 = 2,

    /// <summary>
    /// Forward each controller report as soon as it arrives.
    /// </summary>
    RealTime = 3
}

/// <summary>
/// Controller identity the bridge presents over USB.
/// </summary>
public enum DongleControllerMode : byte
{
    /// <summary>
    /// Present as a DualSense.
    /// </summary>
    DualSense = 0,

    /// <summary>
    /// Present as a DualSense Edge.
    /// </summary>
    DualSenseEdge = 1,

    /// <summary>
    /// Match the connected controller automatically.
    /// </summary>
    Auto = 2
}

/// <summary>
/// Audio device selection used by the bridge microphone and speaker routes.
/// </summary>
public enum DongleAudioDevice : byte
{
    /// <summary>
    /// Let the firmware choose the route.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Use the controller's built-in device.
    /// </summary>
    BuiltIn = 1,

    /// <summary>
    /// Use the controller's headset jack.
    /// </summary>
    Headphone = 2,

    /// <summary>
    /// Disable the route.
    /// </summary>
    Disabled = 3
}

/// <summary>
/// Behaviour of the optional status GPIO output.
/// </summary>
public enum DongleStatusGpioMode : byte
{
    /// <summary>
    /// Keep the output high while a controller is connected.
    /// </summary>
    HighWhileConnected = 0,

    /// <summary>
    /// Emit a 200 ms pulse when a controller connects.
    /// </summary>
    ConnectPulse = 1
}

/// <summary>
/// Source whose volume and mute changes the bridge ignores.
/// </summary>
public enum DongleVolumeLockMode : byte
{
    /// <summary>
    /// Accept volume and mute changes from every source.
    /// </summary>
    Off = 0,

    /// <summary>
    /// Ignore volume and mute changes in HID output reports from games and software.
    /// System USB Audio controls remain available.
    /// </summary>
    GameAndSoftware = 1,

    /// <summary>
    /// Ignore volume and mute changes through USB Audio, including the Windows device
    /// volume. Game and software HID controls remain available.
    /// </summary>
    SystemVolume = 2
}

/// <summary>
/// Number of audio frames sent in each Bluetooth audio packet.
/// </summary>
public enum DonglePacketMode : byte
{
    /// <summary>
    /// Send two audio frames per packet (firmware default).
    /// </summary>
    Double = 0,

    /// <summary>
    /// Send one audio frame per packet.
    /// </summary>
    Single = 1
}

/// <summary>
/// DS5Dongle configuration snapshot read from feature report <c>0xF7</c>.
/// </summary>
/// <remarks>
/// The record keeps the reported body length so encoding writes the same layout back.
/// Fields the current UI does not expose (speaker/headset volume, sleep on dock, legacy
/// toggles) and trailing bytes from newer firmware are preserved across edits.
/// </remarks>
public sealed record DongleConfig
{
    /// <summary>
    /// Smallest configuration body the bridge has ever written.
    /// </summary>
    public const int MinimumBodyLength = 19;

    /// <summary>
    /// Body length that first contains the full website configuration layout.
    /// </summary>
    public const int ModernBodyLength = 24;

    /// <summary>
    /// Body length that first contains the sleep-on-dock byte.
    /// </summary>
    public const int SleepOnDockBodyLength = 25;

    /// <summary>
    /// Configuration version written by current firmware.
    /// </summary>
    public const byte CurrentVersion = 5;

    /// <summary>
    /// Status GPIO value that disables the output.
    /// </summary>
    public const byte DisabledStatusGpioPin = 0xFF;

    /// <summary>
    /// Number of bytes in the reported configuration body.
    /// </summary>
    public int BodyLength { get; init; } = MinimumBodyLength;

    /// <summary>
    /// Configuration version byte.
    /// </summary>
    public byte Version { get; init; } = CurrentVersion;

    /// <summary>
    /// Haptic feedback gain multiplier (1.0-2.0).
    /// </summary>
    public float HapticsGain { get; init; } = 1.0f;

    /// <summary>
    /// Speaker volume stored by older firmware (0-127). Not shown by the configuration UI.
    /// </summary>
    public byte SpeakerVolume { get; init; } = 100;

    /// <summary>
    /// Headset volume stored by older firmware (0-127). Not shown by the configuration UI.
    /// </summary>
    public byte HeadsetVolume { get; init; } = 100;

    /// <summary>
    /// Speaker preamp gain (0 = auto, 1-7 fixed).
    /// </summary>
    public byte SpeakerGain { get; init; } = 2;

    /// <summary>
    /// Idle time before the bridge disconnects the controller, in minutes (0 disables).
    /// </summary>
    public byte InactiveTimeMinutes { get; init; } = 30;

    /// <summary>
    /// Whether the always-on Pico LED is disabled.
    /// </summary>
    public bool DisablePicoLed { get; init; }

    /// <summary>
    /// HID report polling cadence.
    /// </summary>
    public DonglePollingRateMode PollingRateMode { get; init; } = DonglePollingRateMode.Hz500;

    /// <summary>
    /// Controller-side audio buffer length (16-128).
    /// </summary>
    public byte AudioBufferLength { get; init; } = 48;

    /// <summary>
    /// Controller identity presented over USB.
    /// </summary>
    public DongleControllerMode ControllerMode { get; init; } = DongleControllerMode.Auto;

    /// <summary>
    /// Whether the USB serial number descriptor is enabled.
    /// </summary>
    public bool EnableUsbSerialNumber { get; init; }

    /// <summary>
    /// Whether the USB keyboard interface is exposed for shortcut actions.
    /// </summary>
    public bool EnableKeyboard { get; init; }

    /// <summary>
    /// Microphone route.
    /// </summary>
    public DongleAudioDevice Microphone { get; init; } = DongleAudioDevice.Auto;

    /// <summary>
    /// Speaker/headset route.
    /// </summary>
    public DongleAudioDevice Speaker { get; init; } = DongleAudioDevice.Auto;

    /// <summary>
    /// Whether a PS-button press wakes the host over USB remote wakeup.
    /// </summary>
    public bool EnableWake { get; init; }

    /// <summary>
    /// Adaptive-trigger feedback reduction (0 = auto, 1-10 stronger reduction).
    /// </summary>
    public byte TriggerReduce { get; init; }

    /// <summary>
    /// Source whose volume and mute changes are ignored.
    /// </summary>
    public DongleVolumeLockMode LockVolume { get; init; } = DongleVolumeLockMode.Off;

    /// <summary>
    /// Status GPIO pin (2-22 or 26-28), or <see cref="DisabledStatusGpioPin"/>.
    /// </summary>
    public byte StatusGpioPin { get; init; } = DisabledStatusGpioPin;

    /// <summary>
    /// Behaviour of the status GPIO output.
    /// </summary>
    public DongleStatusGpioMode StatusGpioMode { get; init; } = DongleStatusGpioMode.HighWhileConnected;

    /// <summary>
    /// Audio frames per Bluetooth packet.
    /// </summary>
    public DonglePacketMode PacketMode { get; init; } = DonglePacketMode.Double;

    /// <summary>
    /// Whether the bridge drops the Bluetooth link when the controller is docked.
    /// </summary>
    public bool DisconnectOnDock { get; init; }

    /// <summary>
    /// Whether the bridge suspends the host when the controller is docked.
    /// </summary>
    public bool SleepOnDock { get; init; }

    /// <summary>
    /// Legacy PS-button shortcut toggle at byte 14 on older firmware.
    /// </summary>
    public bool LegacyPsShortcutEnabled { get; init; }

    /// <summary>
    /// Legacy controller-microphone disable toggle at byte 15 on older firmware.
    /// </summary>
    public bool LegacyDisableMicrophone { get; init; }

    /// <summary>
    /// Legacy controller-speaker disable toggle at byte 16 on older firmware.
    /// </summary>
    public bool LegacyDisableSpeaker { get; init; }

    /// <summary>
    /// Bytes reported beyond the known layout, preserved for future firmware fields.
    /// </summary>
    public byte[] TrailingBytes { get; init; } = [];

    /// <summary>
    /// Layout family of the reported body length.
    /// </summary>
    public DongleConfigLayout Layout
    {
        get
        {
            return BodyLength >= ModernBodyLength ? DongleConfigLayout.Modern : DongleConfigLayout.Legacy;
        }
    }

    /// <summary>
    /// Whether the body carries the modern keyboard, audio-select, lock-volume, GPIO,
    /// packet-mode, and dock fields.
    /// </summary>
    public bool SupportsModernSettings
    {
        get
        {
            return Layout == DongleConfigLayout.Modern;
        }
    }

    /// <summary>
    /// Whether the body carries the sleep-on-dock byte.
    /// </summary>
    public bool SupportsSleepOnDock
    {
        get
        {
            return BodyLength >= SleepOnDockBodyLength;
        }
    }

    /// <summary>
    /// Returns whether this configuration encodes to the same bytes as another snapshot.
    /// </summary>
    /// <param name="other">Configuration to compare with.</param>
    public bool IsEquivalentTo(DongleConfig? other) => other is not null && DongleConfigProtocol.AreEquivalent(this, other);

    /// <summary>
    /// Returns whether a byte is a valid Pico 2 W status GPIO pin or the disabled marker.
    /// </summary>
    /// <param name="pin">Pin value to validate.</param>
    public static bool IsValidStatusGpioPin(byte pin)
    {
        return pin == DisabledStatusGpioPin
               || pin is >= 2 and <= 22
               || pin is >= 26 and <= 28;
    }

    /// <summary>
    /// Creates the bridge's default configuration for the given body length.
    /// </summary>
    /// <param name="bodyLength">Reported body length, preserved so the layout does not change.</param>
    public static DongleConfig CreateDefault(int bodyLength)
    {
        // Lengths between the original legacy body and the modern body belong to
        // intermediate firmware layouts with incompatible field meanings. Only the two
        // layouts this client understands are created; anything else falls back to legacy.
        int normalizedLength = bodyLength >= ModernBodyLength ? bodyLength : MinimumBodyLength;
        bool legacy = normalizedLength < ModernBodyLength;

        return new DongleConfig
        {
            BodyLength = normalizedLength,
            Version = CurrentVersion,
            HapticsGain = 1.0f,
            SpeakerVolume = 100,
            HeadsetVolume = 100,
            SpeakerGain = 2,
            InactiveTimeMinutes = 30,
            DisablePicoLed = false,
            PollingRateMode = DonglePollingRateMode.Hz500,
            AudioBufferLength = legacy ? (byte)64 : (byte)48,
            ControllerMode = DongleControllerMode.Auto,
            EnableUsbSerialNumber = false,
            EnableKeyboard = false,
            Microphone = DongleAudioDevice.Auto,
            Speaker = DongleAudioDevice.Auto,
            EnableWake = false,
            TriggerReduce = 0,
            LockVolume = DongleVolumeLockMode.Off,
            StatusGpioPin = DisabledStatusGpioPin,
            StatusGpioMode = DongleStatusGpioMode.HighWhileConnected,
            PacketMode = DonglePacketMode.Double,
            DisconnectOnDock = false,
            SleepOnDock = false,
            LegacyPsShortcutEnabled = false,
            LegacyDisableMicrophone = false,
            LegacyDisableSpeaker = false,
            TrailingBytes = []
        };
    }
}