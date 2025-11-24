namespace DualSenseClient.Services;

public class NullHidHideService : IHidHideService
{
    public bool IsInstalled => false;

    public bool IsRunningAsAdmin()
    {
        return false;
    }

    public bool IsReady => false;

    public bool IsAppRegistered()
    {
        return false;
    }

    public bool RegisterApp()
    {
        return false;
    }

    public bool UnregisterApp()
    {
        return false;
    }

    public bool IsDeviceHidden(string deviceInstanceId)
    {
        return false;
    }

    public bool HideDevice(string deviceInstanceId)
    {
        return false;
    }

    public bool UnhideDevice(string deviceInstanceId)
    {
        return false;
    }

    public bool SetCloakingState(bool active)
    {
        return false;
    }

    public bool IsCloakingActive()
    {
        return false;
    }

    public string? FindDeviceInstanceIdByMacAddress(string macAddress)
    {
        return null;
    }
}