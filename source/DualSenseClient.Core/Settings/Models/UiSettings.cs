using System.Text.Json.Serialization;

namespace DualSenseClient.Core.Settings.Models;

public class UiSettings
{
    [JsonPropertyName("theme")]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    [JsonPropertyName("close_to_tray")]
    public bool CloseToTray { get; set; } = false;

    [JsonPropertyName("start_minimized")]
    public bool StartMinimized { get; set; } = false;

    [JsonPropertyName("tray_battery_tracking")]
    public bool TrayBatteryTracking { get; set; } = true;

    [JsonPropertyName("start_on_launch")]
    public bool StartOnLaunch { get; set; } = false;
}

public enum AppTheme
{
    Light,
    Dark,
    CosmicRed,
    PlayStation4
}