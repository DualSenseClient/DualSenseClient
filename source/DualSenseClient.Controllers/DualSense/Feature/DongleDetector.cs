using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.DualSense.Feature;

/// <summary>
/// Identifies a DS5Dongle, which presents itself over USB as a plain wired DualSense.
/// </summary>
/// <remarks>
/// A retail DualSense declares feature reports up to <c>0xF5</c>. DS5Dongle adds
/// vendor reports <c>0xF6</c>-<c>0xFB</c>; <c>0xF8</c> returns its firmware version
/// string, and <c>0xF7</c> returns its configuration body. A retail controller stalls
/// both requests, so a successful, plausible response identifies the bridge.
/// </remarks>
internal static class DongleDetector
{
    /// <summary>
    /// Feature report returning the dongle firmware version string.
    /// </summary>
    private const byte VersionReportId = 0xF8;

    /// <summary>
    /// Feature report returning the dongle configuration body.
    /// </summary>
    private const byte ConfigReportId = 0xF7;

    /// <summary>
    /// Size of the dongle's 63-byte vendor feature reports plus the report ID.
    /// </summary>
    private const int FeatureReportSize = 64;

    /// <summary>
    /// Smallest configuration version the dongle has ever written.
    /// </summary>
    private const byte MinimumConfigVersion = 1;

    /// <summary>
    /// Upper bound that still looks like a dongle configuration version rather than
    /// arbitrary data from a controller that happens to answer report <c>0xF7</c>.
    /// </summary>
    private const byte MaximumConfigVersion = 32;

    /// <summary>
    /// Returns whether the opened device answers the dongle's vendor feature reports.
    /// </summary>
    /// <param name="device">The opened controller device.</param>
    /// <returns><c>true</c> when the device is a DS5Dongle; otherwise, <c>false</c>.</returns>
    public static bool IsDongle(IHidDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.TryGetFeatureReport(VersionReportId, FeatureReportSize, out byte[] version)
            && IsVersionReport(version))
        {
            return true;
        }

        return device.TryGetFeatureReport(ConfigReportId, FeatureReportSize, out byte[] config)
               && IsConfigReport(config);
    }

    /// <summary>
    /// Validates the version report: the report ID followed by a non-empty printable
    /// ASCII string, ignoring trailing zero padding.
    /// </summary>
    /// <param name="report">Raw feature report.</param>
    private static bool IsVersionReport(byte[] report)
    {
        if (report.Length < 2 || report[0] != VersionReportId)
        {
            return false;
        }

        bool hasContent = false;
        for (int i = 1; i < report.Length; i++)
        {
            byte value = report[i];
            if (value == 0)
            {
                continue;
            }

            if (value < 0x20 || value > 0x7E)
            {
                return false;
            }

            hasContent = true;
        }

        return hasContent;
    }

    /// <summary>
    /// Validates the configuration report: the report ID followed by a plausible
    /// <c>config_version</c> byte.
    /// </summary>
    /// <param name="report">Raw feature report.</param>
    private static bool IsConfigReport(byte[] report)
    {
        return report.Length >= 2
               && report[0] == ConfigReportId
               && report[1] >= MinimumConfigVersion
               && report[1] <= MaximumConfigVersion;
    }
}