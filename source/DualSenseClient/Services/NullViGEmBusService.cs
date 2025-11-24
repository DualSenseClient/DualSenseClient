using System.Runtime.Versioning;

namespace DualSenseClient.Services;

[SupportedOSPlatform("windows")]
public class NullViGEmBusService : IViGEmBusService
{
    public bool IsViGEMBusInstalled => false;
}