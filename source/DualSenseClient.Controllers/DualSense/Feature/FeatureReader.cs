using DualSenseClient.Controllers.DualSense.Utilities;
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
    /// Reads the motion-sensor calibration feature report (0x05).
    /// The controller requires this read before it starts sending full Bluetooth
    /// <c>0x31</c> input reports, so it is issued during connection setup.
    /// </summary>
    /// <param name="device">The controller device to read from.</param>
    /// <returns>The parsed calibration, or <c>null</c> if the read failed or the
    /// response was not a valid 0x05 report.</returns>
    public static CalibrationInfo? ReadCalibration(IControllerDevice device)
    {
        try
        {
            byte[] raw = device.GetFeatureReport(0x05, 41);
            if (!HasValidBluetoothCrc(device, raw, 37, DualSenseCRC32.FeatureGetSeed))
            {
                // Still serve the structurally valid report: this GET is what switches a
                // Bluetooth controller from the minimal 0x01 report to the full 0x31
                // report, and the app uses the raw motion words without rescaling.
                _log.Warning("Calibration report failed the Bluetooth CRC check; using its structural content");
            }

            CalibrationInfo info = new CalibrationInfo(raw);
            if (!info.IsValid)
            {
                byte reportId = raw.Length > 0 ? raw[0] : (byte)0;
                _log.Warning($"Calibration report was not valid (report ID 0x{reportId:X2}, {raw.Length} byte(s))");
                return null;
            }

            _log.Debug(
                $"Calibration: gyro plus/minus ({info.GyroPitchPlus}/{info.GyroPitchMinus}), accel plus/minus ({info.AccelXPlus}/{info.AccelXMinus})");
            return info;
        }
        catch (HidException ex)
        {
            _log.Error($"GetFeatureReport(0x05) failed: {ex.Message}");
            return null;
        }
    }

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
            if (!HasValidBluetoothCrc(device, raw, 16, DualSenseCRC32.FeatureGetSeed))
            {
                _log.Warning("Pairing info report failed the Bluetooth CRC check");
                return null;
            }

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
            if (!HasValidBluetoothCrc(device, raw, 60, DualSenseCRC32.FeatureGetSeed))
            {
                _log.Warning("Firmware info report failed the Bluetooth CRC check");
                return null;
            }

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

    /// <summary>
    /// Validates the trailing CRC of a Bluetooth feature GET reply. USB reports carry
    /// no CRC, so the check is skipped on that transport.
    /// </summary>
    /// <param name="device">Controller whose transport selects the check.</param>
    /// <param name="raw">Raw feature report.</param>
    /// <param name="crcOffset">Wire offset of the little-endian CRC.</param>
    /// <param name="seed">HIDP transaction seed.</param>
    private static bool HasValidBluetoothCrc(IControllerDevice device, byte[] raw, int crcOffset, uint seed)
    {
        if (device.ConnectionType != ConnectionType.Bluetooth)
        {
            return true;
        }

        if (raw.Length < crcOffset + 4)
        {
            return false;
        }

        uint expected = (uint)(raw[crcOffset]
                               | (raw[crcOffset + 1] << 8)
                               | (raw[crcOffset + 2] << 16)
                               | (raw[crcOffset + 3] << 24));
        uint actual = DualSenseCRC32.Compute(raw, 0, crcOffset, seed);
        return expected == actual;
    }
}