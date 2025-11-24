using System;
using System.Runtime.Versioning;
using DualSenseClient.Core.Logging;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace DualSenseClient.Services;

[SupportedOSPlatform("windows")]
public class ViGEmBusService : IViGEmBusService
{
    public bool IsViGEMBusInstalled { get; private set; } = false;

    public ViGEmBusService()
    {
        InitializeViGEmClient();
    }

    private void InitializeViGEmClient()
    {
        try
        {
            using ViGEmClient client = new ViGEmClient();
            IsViGEMBusInstalled = true;
            Logger.Info<ViGEmBusService>("ViGEm client initialized successfully, ViGEmBus driver is installed");
        }
        catch (VigemBusNotFoundException)
        {
            IsViGEMBusInstalled = false;
            Logger.Warning<ViGEmBusService>("ViGEmBus driver not found");
        }
        catch (Exception ex)
        {
            IsViGEMBusInstalled = false;
            Logger.Error<ViGEmBusService>($"Error initializing ViGEm client: {ex.Message}");
        }
    }
}