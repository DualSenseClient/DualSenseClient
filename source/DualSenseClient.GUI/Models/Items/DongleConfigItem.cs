using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Controllers.DualSense.Feature;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Observable editor over a <see cref="DongleConfig"/> snapshot. The item exposes the
/// fields the dongle configuration page binds to and raises <see cref="Changed"/> when
/// the user edits one, so the page can apply the change to the bridge.
/// </summary>
/// <remarks>
/// The original snapshot is kept as <see cref="Template"/> so fields the UI does not show
/// (speaker/headset volume, sleep on dock, legacy toggles, trailing bytes) survive a
/// round-trip through <see cref="ToConfig"/>.
/// </remarks>
public sealed partial class DongleConfigItem : ObservableObject
{
    /// <summary>
    /// Pico 2 W pins usable by the status GPIO output, with the disabled marker first.
    /// </summary>
    public static IReadOnlyList<int> GpioPinValues { get; } = new int[]
    {
        DongleConfig.DisabledStatusGpioPin, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 26, 27, 28
    };

    /// <summary>
    /// Whether properties are being populated from a device snapshot rather than edited.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// The snapshot whose unshown fields and body layout are preserved by <see cref="ToConfig"/>.
    /// </summary>
    public DongleConfig Template { get; private set; }

    /// <summary>
    /// Haptic feedback gain multiplier (1.0-2.0).
    /// </summary>
    [ObservableProperty] private double _hapticsGain = 1.0;

    /// <summary>
    /// Speaker preamp gain (0 = auto, 1-7 fixed).
    /// </summary>
    [ObservableProperty] private int _speakerGain = 2;

    /// <summary>
    /// Adaptive-trigger feedback reduction (0 = auto, 1-10 stronger reduction).
    /// </summary>
    [ObservableProperty] private int _triggerReduce;

    /// <summary>
    /// Controller-side audio buffer length (16-128).
    /// </summary>
    [ObservableProperty] private int _audioBufferLength = 48;

    /// <summary>
    /// Idle time before the bridge disconnects the controller, in minutes (0 disables).
    /// </summary>
    [ObservableProperty] private int _inactiveTimeMinutes = 30;

    /// <summary>
    /// Selected volume-lock mode as an index into the page's localized option list.
    /// </summary>
    [ObservableProperty] private int _lockVolumeIndex;

    /// <summary>
    /// Whether the always-on Pico LED is disabled.
    /// </summary>
    [ObservableProperty] private bool _disablePicoLed;

    /// <summary>
    /// Whether a PS-button press wakes the host over USB remote wakeup.
    /// </summary>
    [ObservableProperty] private bool _enableWake;

    /// <summary>
    /// Whether the bridge drops the Bluetooth link when the controller is docked.
    /// </summary>
    [ObservableProperty] private bool _disconnectOnDock;

    /// <summary>
    /// Whether the USB serial number descriptor is enabled.
    /// </summary>
    [ObservableProperty] private bool _enableUsbSerialNumber;

    /// <summary>
    /// Whether the USB keyboard interface is exposed for shortcut actions.
    /// </summary>
    [ObservableProperty] private bool _enableKeyboard;

    /// <summary>
    /// Whether the bridge sends one audio frame per Bluetooth packet.
    /// </summary>
    [ObservableProperty] private bool _singlePacketMode;

    /// <summary>
    /// Legacy PS-button shortcut toggle at byte 14 on older firmware.
    /// </summary>
    [ObservableProperty] private bool _legacyPsShortcutEnabled;

    /// <summary>
    /// Legacy controller-microphone disable toggle at byte 15 on older firmware.
    /// </summary>
    [ObservableProperty] private bool _legacyDisableMicrophone;

    /// <summary>
    /// Legacy controller-speaker disable toggle at byte 16 on older firmware.
    /// </summary>
    [ObservableProperty] private bool _legacyDisableSpeaker;

    /// <summary>
    /// Selected polling cadence for current firmware, as an index into the four-option list.
    /// </summary>
    [ObservableProperty] private int _pollingRateIndex;

    /// <summary>
    /// Selected polling cadence for legacy firmware, as an index into the three-option list.
    /// Kept separate so the hidden list can never write an out-of-range index back.
    /// </summary>
    [ObservableProperty] private int _legacyPollingRateIndex;

    /// <summary>
    /// Selected controller identity as an index into the page's localized option list.
    /// </summary>
    [ObservableProperty] private int _controllerModeIndex;

    /// <summary>
    /// Selected microphone route as an index into the page's localized option list.
    /// </summary>
    [ObservableProperty] private int _microphoneIndex;

    /// <summary>
    /// Selected speaker/headset route as an index into the page's localized option list.
    /// </summary>
    [ObservableProperty] private int _speakerIndex;

    /// <summary>
    /// Selected status GPIO behaviour as an index into the page's localized option list.
    /// </summary>
    [ObservableProperty] private int _statusGpioModeIndex;

    /// <summary>
    /// Selected status GPIO pin as an index into <see cref="GpioPinValues"/>.
    /// </summary>
    [ObservableProperty] private int _statusGpioPinIndex;

    /// <summary>
    /// Incremented for every user edit. A device read-back is only loaded when the revision
    /// is unchanged, so edits made while an apply is in flight are never overwritten.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Raised when the user edits a field. Not raised while <see cref="Load"/> populates
    /// the item from a device snapshot.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Whether the device reported the older configuration layout.
    /// </summary>
    public bool IsLegacy
    {
        get
        {
            return !Template.SupportsModernSettings;
        }
    }

    /// <summary>
    /// Whether the device reported the full website configuration layout.
    /// </summary>
    public bool SupportsModernSettings
    {
        get
        {
            return Template.SupportsModernSettings;
        }
    }

    /// <summary>
    /// Whether the device reported the status GPIO settings.
    /// </summary>
    public bool SupportsGpio
    {
        get
        {
            return Template.SupportsModernSettings;
        }
    }

    /// <summary>
    /// Whether a status GPIO pin is selected.
    /// </summary>
    public bool HasGpioPin
    {
        get
        {
            return StatusGpioPinIndex > 0;
        }
    }

    /// <summary>
    /// Maximum trigger-feedback reduction accepted by the reported firmware layout.
    /// Legacy firmware accepts 0-7; current firmware accepts 0-10.
    /// </summary>
    public int TriggerReduceMax
    {
        get
        {
            return Template.SupportsModernSettings ? 10 : 7;
        }
    }

    /// <summary>
    /// Whether to suggest single-packet mode because the audio buffer is below 32.
    /// </summary>
    public bool ShowSinglePacketHint
    {
        get
        {
            return SupportsModernSettings && !SinglePacketMode && AudioBufferLength < 32;
        }
    }

    /// <summary>
    /// Creates an editor item populated from the given snapshot.
    /// </summary>
    /// <param name="config">Snapshot read from the dongle.</param>
    public DongleConfigItem(DongleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Template = config;
        Load(config);
    }

    /// <summary>
    /// Repopulates the item from a device snapshot without raising <see cref="Changed"/>.
    /// </summary>
    /// <param name="config">Snapshot read from the dongle.</param>
    public void Load(DongleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Template = config;
        _loading = true;
        HapticsGain = config.HapticsGain;
        SpeakerGain = config.SpeakerGain;
        TriggerReduce = Math.Min(config.TriggerReduce, TriggerReduceMax);
        AudioBufferLength = config.AudioBufferLength;
        InactiveTimeMinutes = config.InactiveTimeMinutes;
        LockVolumeIndex = (int)config.LockVolume;
        DisablePicoLed = config.DisablePicoLed;
        EnableWake = config.EnableWake;
        DisconnectOnDock = config.DisconnectOnDock;
        EnableUsbSerialNumber = config.EnableUsbSerialNumber;
        EnableKeyboard = config.EnableKeyboard;
        SinglePacketMode = config.PacketMode == DonglePacketMode.Single;
        LegacyPsShortcutEnabled = config.LegacyPsShortcutEnabled;
        LegacyDisableMicrophone = config.LegacyDisableMicrophone;
        LegacyDisableSpeaker = config.LegacyDisableSpeaker;
        ControllerModeIndex = (int)config.ControllerMode;
        MicrophoneIndex = (int)config.Microphone;
        SpeakerIndex = (int)config.Speaker;
        StatusGpioModeIndex = (int)config.StatusGpioMode;
        StatusGpioPinIndex = FindGpioPinIndex(config.StatusGpioPin);
        if (config.SupportsModernSettings)
        {
            PollingRateIndex = (int)config.PollingRateMode;
            LegacyPollingRateIndex = (int)DonglePollingRateMode.Hz500;
        }
        else
        {
            PollingRateIndex = (int)DonglePollingRateMode.Hz500;
            LegacyPollingRateIndex = config.PollingRateMode == DonglePollingRateMode.RealTime
                ? 2
                : (int)config.PollingRateMode;
        }

        OnPropertyChanged(nameof(Template));
        OnPropertyChanged(nameof(IsLegacy));
        OnPropertyChanged(nameof(SupportsModernSettings));
        OnPropertyChanged(nameof(SupportsGpio));
        OnPropertyChanged(nameof(HasGpioPin));
        OnPropertyChanged(nameof(TriggerReduceMax));
        OnPropertyChanged(nameof(ShowSinglePacketHint));
        _loading = false;
    }

    /// <summary>
    /// Builds a snapshot from the current editor values, preserving the template's
    /// unshown fields and trailing bytes.
    /// </summary>
    public DongleConfig ToConfig()
    {
        int pinIndex = Math.Clamp(StatusGpioPinIndex, 0, GpioPinValues.Count - 1);
        return Template with
        {
            HapticsGain = (float)Math.Clamp(HapticsGain, 1.0, 2.0),
            SpeakerGain = (byte)Math.Clamp(SpeakerGain, 0, 7),
            TriggerReduce = (byte)Math.Clamp(TriggerReduce, 0, TriggerReduceMax),
            AudioBufferLength = (byte)Math.Clamp(AudioBufferLength, 16, 128),
            InactiveTimeMinutes = (byte)Math.Clamp(InactiveTimeMinutes, 0, 60),
            LockVolume = (DongleVolumeLockMode)Math.Clamp(LockVolumeIndex, 0, (int)DongleVolumeLockMode.SystemVolume),
            DisablePicoLed = DisablePicoLed,
            EnableWake = EnableWake,
            DisconnectOnDock = DisconnectOnDock,
            EnableUsbSerialNumber = EnableUsbSerialNumber,
            EnableKeyboard = EnableKeyboard,
            PollingRateMode = Template.SupportsModernSettings
                ? FromModernPollingRateIndex(PollingRateIndex)
                : FromLegacyPollingRateIndex(LegacyPollingRateIndex),
            ControllerMode = (DongleControllerMode)Math.Clamp(ControllerModeIndex, 0, 2),
            Microphone = (DongleAudioDevice)Math.Clamp(MicrophoneIndex, 0, 3),
            Speaker = (DongleAudioDevice)Math.Clamp(SpeakerIndex, 0, 3),
            StatusGpioPin = (byte)GpioPinValues[pinIndex],
            StatusGpioMode = (DongleStatusGpioMode)Math.Clamp(StatusGpioModeIndex, 0, 1),
            PacketMode = SinglePacketMode ? DonglePacketMode.Single : DonglePacketMode.Double,
            LegacyPsShortcutEnabled = LegacyPsShortcutEnabled,
            LegacyDisableMicrophone = LegacyDisableMicrophone,
            LegacyDisableSpeaker = LegacyDisableSpeaker
        };
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(SinglePacketMode) or nameof(AudioBufferLength))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowSinglePacketHint)));
        }

        if (e.PropertyName == nameof(StatusGpioPinIndex))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasGpioPin)));
        }

        if (!_loading)
        {
            Revision++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Maps the four-option current-firmware polling list to a semantic mode.
    /// </summary>
    private static DonglePollingRateMode FromModernPollingRateIndex(int index)
    {
        return index switch
        {
            0 => DonglePollingRateMode.Hz250,
            1 => DonglePollingRateMode.Hz500,
            2 => DonglePollingRateMode.Hz1000,
            3 => DonglePollingRateMode.RealTime,
            _ => DonglePollingRateMode.Hz500
        };
    }

    /// <summary>
    /// Maps the three-option legacy polling list to a semantic mode; legacy real-time is
    /// wire value 2 rather than the current value 3.
    /// </summary>
    private static DonglePollingRateMode FromLegacyPollingRateIndex(int index)
    {
        return index switch
        {
            0 => DonglePollingRateMode.Hz250,
            1 => DonglePollingRateMode.Hz500,
            2 => DonglePollingRateMode.RealTime,
            _ => DonglePollingRateMode.Hz500
        };
    }

    /// <summary>
    /// Returns the index of a pin in <see cref="GpioPinValues"/>, or zero (disabled) when
    /// the value is not a supported Pico 2 W pin.
    /// </summary>
    private static int FindGpioPinIndex(byte pin)
    {
        for (int i = 0; i < GpioPinValues.Count; i++)
        {
            if (GpioPinValues[i] == pin)
            {
                return i;
            }
        }

        return 0;
    }
}