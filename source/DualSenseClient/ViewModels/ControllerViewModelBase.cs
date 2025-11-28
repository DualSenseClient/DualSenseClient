using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Models;
using DualSenseClient.Core.DualSense.Reports;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.ViewModels;

/// <summary>
/// Base ViewModel for controller
/// </summary>
public partial class ControllerViewModelBase : ObservableObject, IDisposable
{
    // Properties
    protected readonly DualSenseController _controller;
    protected ControllerInfo? _controllerInfo;
    private CancellationTokenSource? _animationCts;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _devicePath;
    [ObservableProperty] private string _connectionType;
    [ObservableProperty] private string _connectionIcon;
    [ObservableProperty] private string _macAddress;
    [ObservableProperty] private double _batteryLevel;
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private bool _isFullyCharged;
    [ObservableProperty] private string _batteryIcon = "Battery";
    [ObservableProperty] private string _chargingIcon = "BatteryCharge";
    [ObservableProperty] private string _batteryText = string.Empty;
    [ObservableProperty] private string _firmwareVersion = "Unknown";
    [ObservableProperty] private string _hardwareVersion = "Unknown";

    public DualSenseController Controller => _controller;
    public ControllerInfo? ControllerInfo => _controllerInfo;
    public string ControllerId => _controllerInfo?.Id ?? string.Empty;

    // Constructor
    public ControllerViewModelBase(DualSenseController controller, ControllerInfo? controllerInfo)
    {
        Logger.Trace<ControllerViewModelBase>($"Creating ControllerViewModelBase for: {controllerInfo?.Name ?? "Unknown"}");

        _controller = controller;
        _controllerInfo = controllerInfo;

        Name = controllerInfo?.Name ?? "DualSense Controller";
        DevicePath = controller.Device.DevicePath;
        ConnectionType = controller.ConnectionType.ToString();
        ConnectionIcon = controller.ConnectionType == Core.DualSense.Enums.ConnectionType.Bluetooth ? "BluetoothConnected" : "UsbPlug";
        MacAddress = controller.MacAddress ?? "N/A";

        // Set firmware and hardware versions
        if (controller.ParsedFirmwareVersion != null)
        {
            FirmwareVersion = controller.ParsedFirmwareVersion.ToString();
            HardwareVersion = controller.ParsedFirmwareVersion.GetHardwareVersionString();
        }
        else if (controller.RawFirmwareVersion.HasValue)
        {
            // Fallback to raw format if parsing failed
            FirmwareVersion = $"v{controller.RawFirmwareVersion.Value:X8}";
            HardwareVersion = controller.RawHardwareVersion?.ToString() ?? "Unknown";
        }
        else
        {
            FirmwareVersion = "Unknown";
            HardwareVersion = "Unknown";
        }

        UpdateBatteryState(controller.Battery);

        Logger.Trace<ControllerViewModelBase>("ControllerViewModelBase created successfully");
    }

    // Functions
    public void UpdateControllerInfo(ControllerInfo? controllerInfo)
    {
        Logger.Debug<ControllerViewModelBase>($"Updating controller info: {controllerInfo?.Name ?? "null"}");
        _controllerInfo = controllerInfo;
        if (controllerInfo != null)
        {
            Name = controllerInfo.Name;
        }
    }

    public void UpdateName(string newName)
    {
        Logger.Debug<ControllerViewModelBase>($"Updating controller name from '{Name}' to '{newName}'");
        Name = newName;
        if (_controllerInfo != null)
        {
            _controllerInfo.Name = newName;
        }
    }

    public void UpdateBatteryState(BatteryState battery)
    {
        Logger.Trace<ControllerViewModelBase>($"Updating battery state: Level={battery.BatteryLevel:F0}%, Charging={battery.IsCharging}, Full={battery.IsFullyCharged}");

        BatteryLevel = battery.BatteryLevel;

        bool wasCharging = IsCharging;
        IsCharging = battery.IsCharging;
        IsFullyCharged = battery.IsFullyCharged;

        BatteryIcon = GetBatteryIcon(battery);
        BatteryText = GetBatteryText(battery);

        if (IsCharging && !wasCharging)
        {
            Logger.Trace<ControllerViewModelBase>("Battery started charging, starting animation");
            StartChargingAnimation();
        }
        else if (!IsCharging && wasCharging)
        {
            Logger.Trace<ControllerViewModelBase>("Battery stopped charging, stopping animation");
            StopChargingAnimation();
        }
    }

    protected void StartChargingAnimation()
    {
        StopChargingAnimation();

        _animationCts = new CancellationTokenSource();
        CancellationToken token = _animationCts.Token;

        Task.Run(async () =>
        {
            int frame = 0;
            while (!token.IsCancellationRequested)
            {
                string icon = $"BatteryCharge{frame}";

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ChargingIcon = icon;
                });

                frame = (frame + 1) % 11;
                await Task.Delay(150, token);
            }
        }, token);
    }

    protected void StopChargingAnimation()
    {
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        _animationCts = null;
        ChargingIcon = "BatteryCharge";
    }

    private string GetBatteryIcon(BatteryState battery)
    {
        if (battery.IsCharging)
        {
            return "BatteryCharge";
        }

        return battery.BatteryLevel switch
        {
            >= 90 => "Battery10",
            >= 80 => "Battery9",
            >= 70 => "Battery8",
            >= 60 => "Battery7",
            >= 50 => "Battery6",
            >= 40 => "Battery5",
            >= 30 => "Battery4",
            >= 20 => "Battery3",
            >= 10 => "Battery2",
            >= 5 => "Battery1",
            _ => "Battery0"
        };
    }

    private string GetBatteryText(BatteryState battery)
    {
        if (battery.IsFullyCharged)
        {
            return "Fully Charged";
        }

        if (battery.IsCharging)
        {
            return $"Charging - {battery.BatteryLevel:F0}%";
        }

        return $"{battery.BatteryLevel:F0}%";
    }

    public virtual void Dispose()
    {
        Logger.Trace<ControllerViewModelBase>($"Disposing ControllerViewModelBase for: {Name}");
        StopChargingAnimation();
    }
}