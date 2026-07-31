using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Controllers.DualSense.Enum;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Feature;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.GUI.Services;
using DualSenseClient.Hid;
using FluentIcons.Common;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display model for the device info page. Wraps a <see cref="ControllerItem"/>
/// and exposes human-readable firmware, hardware, battery, and connection properties
/// bound by the UI. Missing or unreadable values render as "-".
/// </summary>
/// <remarks>
/// <para>
/// Firmware/hardware data is static and read once at connect time. Battery and
/// connection status are live: the item subscribes to the controller's
/// <see cref="DualSenseDevice.BatteryStateChanged"/> and
/// <see cref="DualSenseDevice.ConnectionStatusChanged"/> events and raises
/// <see cref="ObservableObject.PropertyChanged"/> so bound UI values update as reports arrive.
/// </para>
/// <para>
/// Event handlers fire on the device read-loop thread, so property-change notifications are
/// marshaled to the UI thread via <see cref="Dispatcher.UIThread"/>.
/// </para>
/// </remarks>
public sealed partial class DeviceInfoItem : ObservableObject, IDisposable
{
    /// <summary>
    /// Placeholder rendered when a value is missing or unreadable.
    /// </summary>
    private const string Unavailable = "-";

    /// <summary>
    /// Battery level icons indexed by level (0..10) for the non-charging case.
    /// </summary>
    private static readonly Icon[] RegularBatteryIcons =
    [
        Icon.Battery0, Icon.Battery1, Icon.Battery2, Icon.Battery3,
        Icon.Battery4, Icon.Battery5, Icon.Battery6, Icon.Battery7,
        Icon.Battery8, Icon.Battery9, Icon.Battery10
    ];

    /// <summary>
    /// Battery level icons indexed by level (0..10) for the charging case.
    /// </summary>
    private static readonly Icon[] ChargingBatteryIcons =
    [
        Icon.BatteryCharge0, Icon.BatteryCharge1, Icon.BatteryCharge2,
        Icon.BatteryCharge3, Icon.BatteryCharge4, Icon.BatteryCharge5,
        Icon.BatteryCharge6, Icon.BatteryCharge7, Icon.BatteryCharge8,
        Icon.BatteryCharge9, Icon.BatteryCharge10
    ];

    /// <summary>
    /// The concrete controller the live status is read from, or <c>null</c> for
    /// non-DualSense devices or when the device is not reachable.
    /// </summary>
    private readonly DualSenseDevice? _device;

    /// <summary>
    /// Tracks whether the event subscriptions have been released.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// The controller item being displayed.
    /// </summary>
    public ControllerItem Controller { get; }

    /// <summary>
    /// Human-readable product name.
    /// </summary>
    public string DisplayName => Controller.DisplayName;

    /// <summary>
    /// Physical transport (USB / Bluetooth).
    /// </summary>
    public ConnectionType ConnectionType => Controller.ConnectionType;

    /// <summary>
    /// Device serial number.
    /// </summary>
    public string SerialNumber
    {
        get
        {
            string serial = Controller.Device.GetSerialNumber();
            return string.IsNullOrEmpty(serial) ? Unavailable : serial;
        }
    }

    // ── Firmware / Hardware ────────────────────────────────────

    /// <summary>
    /// Whether a valid firmware info report was read from the controller.
    /// </summary>
    public bool HasFirmwareInfo => Controller.FirmwareInfo?.IsValid == true;

    /// <summary>
    /// Main firmware version (major.minor.patch).
    /// </summary>
    public string MainFirmwareVersion => Controller.FirmwareInfo?.MainFirmwareVersion ?? Unavailable;

    /// <summary>
    /// SBL firmware version (major.minor.patch).
    /// </summary>
    public string SblFirmwareVersion => Controller.FirmwareInfo?.SblFirmwareVersion ?? Unavailable;

    /// <summary>
    /// DSP firmware version (hex_hex).
    /// </summary>
    public string DspFirmwareVersion => Controller.FirmwareInfo?.DspFirmwareVersion ?? Unavailable;

    /// <summary>
    /// MCU/Spider DSP firmware version (major.minor.patch).
    /// </summary>
    public string McuSpiderDspFirmwareVersion => Controller.FirmwareInfo?.McuSpiderDspFirmwareVersion ?? Unavailable;

    /// <summary>
    /// Model revision number, or "-" when unavailable.
    /// </summary>
    public string ModelRevision => HasFirmwareInfo ? Controller.FirmwareInfo!.Value.ModelRevision.ToString() : Unavailable;

    /// <summary>
    /// Firmware build date.
    /// </summary>
    public string BuildDate => Controller.FirmwareInfo?.BuildDate ?? Unavailable;

    /// <summary>
    /// Firmware build time.
    /// </summary>
    public string BuildTime => Controller.FirmwareInfo?.BuildTime ?? Unavailable;

    // ── Pairing ─────────────────────────────────────────────────

    /// <summary>
    /// Whether pairing information (MAC addresses) was read from the controller.
    /// </summary>
    public bool HasPairingInfo => Controller.PairingInfo?.IsValid == true;

    /// <summary>
    /// Controller (client) Bluetooth MAC address, or "-" when unavailable.
    /// </summary>
    public string ClientMac => Controller.PairingInfo?.ClientMac ?? Unavailable;

    /// <summary>
    /// Host Bluetooth MAC address, or "-" when unavailable.
    /// </summary>
    public string HostMac => Controller.PairingInfo?.HostMac ?? Unavailable;

    // ── Battery ────────────────────────────────────────────────

    /// <summary>
    /// Whether an input report has been received, so live status is available.
    /// </summary>
    public bool HasStatus => _device?.InputReport is not null;

    /// <summary>
    /// Battery level as a percentage (e.g. "85%"), or "-" when unknown.
    /// </summary>
    public string BatteryPercentage
    {
        get
        {
            if (_device?.InputReport is not { } report)
            {
                return Unavailable;
            }

            int percentage = report.Battery.DisplayPercentage;
            return percentage < 0 ? Unavailable : $"{percentage}%";
        }
    }

    /// <summary>
    /// Battery level icon, using <c>Battery0</c>..<c>Battery10</c> or, while charging,
    /// <c>BatteryCharge0</c>..<c>BatteryCharge10</c>. Renders an empty battery until the
    /// first input report arrives.
    /// </summary>
    public Icon BatteryIcon
    {
        get
        {
            if (_device?.InputReport is not { } report)
            {
                return Icon.Battery0;
            }

            int level = Math.Clamp((int)report.Battery.RawLevel, 0, 10);
            bool charging = report.Battery.PowerState == BatteryPowerState.Charging;
            return (charging ? ChargingBatteryIcons : RegularBatteryIcons)[level];
        }
    }

    /// <summary>
    /// Battery power/charging state (e.g. "Charging"), or "-" when unknown.
    /// </summary>
    public string PowerState
    {
        get
        {
            if (_device?.InputReport is not { } report)
            {
                return Unavailable;
            }

            BatteryPowerState state = report.Battery.PowerState;
            return state == BatteryPowerState.Unknown ? Unavailable : state.ToString();
        }
    }

    // ── Connection Status ──────────────────────────────────────

    /// <summary>
    /// Whether headphones are connected to the controller, or "-" when unknown.
    /// </summary>
    public string Headphones => ConnectionText(status => status.Headphone, IsConnected);

    /// <summary>
    /// Whether a microphone is connected to the controller, or "-" when unknown.
    /// </summary>
    public string Microphone => ConnectionText(status => status.Mic, IsConnected);

    /// <summary>
    /// Whether the microphone is muted, or "-" when unknown.
    /// </summary>
    public string MicrophoneMuted => ConnectionText(status => status.MicMuted, IsYesNo);

    /// <summary>
    /// Whether the USB data connection is active, or "-" when unknown.
    /// </summary>
    public string UsbData => ConnectionText(status => status.UsbData, IsActive);

    /// <summary>
    /// Whether USB power is connected, or "-" when unknown.
    /// </summary>
    public string UsbPower => ConnectionText(status => status.UsbPower, IsConnected);

    /// <summary>
    /// Creates a new device info item for the given controller and subscribes to its
    /// live battery/connection status events.
    /// </summary>
    /// <param name="controller">The controller item to display.</param>
    public DeviceInfoItem(ControllerItem controller)
    {
        Controller = controller;
        _device = controller.Device as DualSenseDevice;
        if (_device is not null)
        {
            _device.BatteryStateChanged += OnBatteryStateChanged;
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;
        }
    }

    /// <summary>
    /// Unsubscribes from the controller's live status events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_device is not null)
        {
            _device.BatteryStateChanged -= OnBatteryStateChanged;
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
    }

    /// <summary>
    /// Resolves a connection status field to a localized display string, falling back
    /// to "-" when no report has been received yet.
    /// </summary>
    /// <param name="selector">Selects the bool field from the current connection status.</param>
    /// <param name="formatter">Formats the selected bool as a localized string.</param>
    private string ConnectionText(Func<ConnectionStatus, bool> selector, Func<bool, string> formatter)
    {
        if (_device?.InputReport is not { } report)
        {
            return Unavailable;
        }

        return formatter(selector(report.Connection));
    }

    /// <summary>
    /// Localized "Connected"/"Not connected" text.
    /// </summary>
    private string IsConnected(bool value) => value
        ? LocalizationService.GetText("DeviceInfoPage.Common.Connected")
        : LocalizationService.GetText("DeviceInfoPage.Common.NotConnected");

    /// <summary>
    /// Localized "Active"/"Inactive" text.
    /// </summary>
    private string IsActive(bool value) => value
        ? LocalizationService.GetText("DeviceInfoPage.Common.Active")
        : LocalizationService.GetText("DeviceInfoPage.Common.Inactive");

    /// <summary>
    /// Localized "Yes"/"No" text.
    /// </summary>
    private string IsYesNo(bool value) => value
        ? LocalizationService.GetText("DeviceInfoPage.Common.Yes")
        : LocalizationService.GetText("DeviceInfoPage.Common.No");

    /// <summary>
    /// Re-raises battery-related property changes on the UI thread when the battery changes.
    /// </summary>
    private void OnBatteryStateChanged(object? sender, BatteryStateEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(BatteryIcon));
            OnPropertyChanged(nameof(BatteryPercentage));
            OnPropertyChanged(nameof(PowerState));
        });
    }

    /// <summary>
    /// Re-raises connection-related property changes on the UI thread when the
    /// connection status changes.
    /// </summary>
    private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(Headphones));
            OnPropertyChanged(nameof(Microphone));
            OnPropertyChanged(nameof(MicrophoneMuted));
            OnPropertyChanged(nameof(UsbData));
            OnPropertyChanged(nameof(UsbPower));
        });
    }
}