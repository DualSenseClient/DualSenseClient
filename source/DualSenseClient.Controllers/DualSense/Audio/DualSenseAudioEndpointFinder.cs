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
/// <para>
/// TODO(audio-endpoints): The name-based match is ambiguous once virtual DualSense
/// devices exist (libVIIPER's virtual controller also presents "DualSense Wireless
/// Controller"), so this can pick the app's own virtual render endpoint — the
/// forwarded audio then loops back into the capture and howls. With several USB pads
/// connected it can also target the wrong pad's speaker. The correct fix is OS-level
/// correlation between the pad's HID path and its UAC endpoint: on Windows walk the
/// SetupAPI parent chain from the HID device to the USB device and match endpoints
/// whose device-instance parent is that USB device (virtual endpoints belong to the
/// usbip bus, so they are excluded naturally); on Linux match the pad's USB serial
/// against the ALSA/Pulse/PipeWire device names and the /proc/asound/cards sysfs
/// parent. SoundFlow's <see cref="DeviceInfo.Id"/> is an opaque miniaudio device id
/// (friendly name on WASAPI, device name on ALSA, sink/node name on Pulse/PipeWire)
/// and cannot disambiguate on its own.
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