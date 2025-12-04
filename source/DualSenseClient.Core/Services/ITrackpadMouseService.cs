using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.Core.Services;

public interface ITrackpadMouseService : IDisposable
{
    void Initialize(DualSenseController controller, VirtualControllerSettings settings);
}