
using DualSenseClient.Core.Logging;

namespace DualSenseClient.Core.Services;

public static class TrackpadMouseServiceFactory
{
    public static ITrackpadMouseService CreateService()
    {
        // Determine the platform and return the appropriate implementation
        if (OperatingSystem.IsWindows())
        {
            return new TrackpadMouseService();
        }
        else if (OperatingSystem.IsLinux())
        {
            return new LinuxTrackpadMouseService();
        }
        else
        {
            Logger.Warning<TrackpadMouseService>("Unsupported platform");
            return new TrackpadMouseService();
        }
    }
}