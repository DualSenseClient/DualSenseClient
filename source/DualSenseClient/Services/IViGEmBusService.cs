using System.Runtime.Versioning;

namespace DualSenseClient.Services;

[SupportedOSPlatform("windows")]
public interface IViGEmBusService
{
    bool IsViGEMBusInstalled { get; }
}