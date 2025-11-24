using System.Runtime.Versioning;

namespace DualSenseClient.Services;

[SupportedOSPlatform("windows")]
public interface IHidHideService
{
    bool IsInstalled { get; }
    bool IsRunningAsAdmin();
    bool IsReady { get; }
    bool IsAppRegistered();
    bool RegisterApp();
    bool UnregisterApp();
    bool IsDeviceHidden(string deviceInstanceId);
    bool HideDevice(string deviceInstanceId);
    bool UnhideDevice(string deviceInstanceId);
    bool SetCloakingState(bool active);
    bool IsCloakingActive();
    string? FindDeviceInstanceIdByMacAddress(string macAddress);
}