using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Reads and parses DualSense feature reports.
/// Returns null instead of throwing when a report cannot be read, so callers
/// can degrade gracefully (e.g. an unsupported transport or disconnected device).
/// </summary>
public static class FeatureReader
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("FeatureReader");

    /// <summary>
    /// Reads the pairing information feature report (0x09).
    /// </summary>
    /// <param name="device">The controller device to read from.</param>
    /// <returns>The parsed pairing info, or <c>null</c> if the read failed or the
    /// response was not a valid 0x09 report.</returns>
    public static PairingInfo? ReadPairingInfo(IControllerDevice device)
    {
        try
        {
            byte[] raw = device.GetFeatureReport(0x09, 20);
            PairingInfo info = new PairingInfo(raw);
            if (!info.IsValid)
            {
                byte reportId = raw.Length > 0 ? raw[0] : (byte)0;
                _log.Warning($"Pairing info report was not valid (report ID 0x{reportId:X2})");
                return null;
            }

            _log.Debug($"Pairing info: client {info.ClientMac}, host {info.HostMac}");
            return info;
        }
        catch (HidException ex)
        {
            _log.Error($"GetFeatureReport(0x09) failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the firmware and hardware info feature report (0x20).
    /// </summary>
    /// <param name="device">The controller device to read from.</param>
    /// <returns>The parsed firmware info, or <c>null</c> if the read failed or the
    /// response was not a valid 0x20 report.</returns>
    public static FirmwareInfo? ReadFirmwareInfo(IControllerDevice device)
    {
        try
        {
            byte[] raw = device.GetFeatureReport(0x20, 64);
            FirmwareInfo info = new FirmwareInfo(raw);
            if (!info.IsValid)
            {
                byte reportId = raw.Length > 0 ? raw[0] : (byte)0;
                _log.Warning($"Firmware info report was not valid (report ID 0x{reportId:X2})");
                return null;
            }

            _log.Debug($"Firmware info: {info.MainFirmwareVersion} (model {info.ModelRevision}, built {info.BuildDate} {info.BuildTime})");
            return info;
        }
        catch (HidException ex)
        {
            _log.Error($"GetFeatureReport(0x20) failed: {ex.Message}");
            return null;
        }
    }
}