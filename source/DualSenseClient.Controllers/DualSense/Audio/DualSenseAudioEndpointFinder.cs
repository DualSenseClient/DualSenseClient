using DualSenseClient.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// Locates the Windows audio render endpoint exposed by a connected DualSense
/// (the UAC1 streaming interface). Used to play controller audio over USB.
/// </summary>
/// <remarks>
/// <para>
/// Windows names the endpoint after the HID product string, so a match is made by
/// looking for "DualSense" or "Wireless Controller" in the name. This is host/driver
/// dependent: if the pad does not expose a render endpoint, USB playback is simply
/// unavailable and the caller falls back to other outputs.
/// </para>
/// <para>
/// Not the transport used over Bluetooth — a BT-paired pad is driven directly with
/// HID reports (<c>0x35</c>), see <see cref="DualSenseClient.Controllers.Devices.DualSenseDevice"/>.
/// </para>
/// </remarks>
public sealed class DualSenseAudioEndpointFinder
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseAudioEndpointFinder");

    /// <summary>
    /// The shared audio engine whose device list is refreshed before each lookup.
    /// </summary>
    private readonly AudioEngine _engine;

    /// <summary>
    /// Creates the finder backed by the shared audio engine, whose device list is
    /// refreshed on every lookup.
    /// </summary>
    public DualSenseAudioEndpointFinder(AudioEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Finds the first active render endpoint that looks like a DualSense. Returns
    /// <c>null</c> when no such endpoint is present.
    /// </summary>
    public DeviceInfo? FindRenderDevice()
    {
        _engine.UpdateAudioDevicesInfo();
        foreach (DeviceInfo device in _engine.PlaybackDevices)
        {
            if (device.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase))
            {
                _log.Debug($"Found DualSense render endpoint: '{device.Name}'");
                return device;
            }
        }

        return null;
    }
}