using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Receives feedback commands from a virtual controller and applies them to the
/// physical DualSense. Abstracted so the virtual controllers can be unit-tested
/// against a fake instead of real hardware.
/// </summary>
public interface IDualSenseOutputs
{
    /// <summary>
    /// Sets the classic rumble motors on the physical controller.
    /// </summary>
    /// <param name="left">Left (low-frequency) motor strength (0-255).</param>
    /// <param name="right">Right (high-frequency) motor strength (0-255).</param>
    void SetVibration(byte left, byte right);

    /// <summary>
    /// Sends an arbitrary output state (lightbar, player LEDs, trigger effects,
    /// mic LED, brightness) to the physical controller.
    /// </summary>
    /// <param name="payload">The output state to send.</param>
    void SendOutputState(SetStateData payload);

    /// <summary>
    /// Whether the next Bluetooth audio report must carry the output-state block
    /// (<c>0x36</c>) because a lightbar/player-LED override is driving the physical pad.
    /// </summary>
    bool NeedsOutputStateBlock { get; }
}

/// <summary>
/// Extends <see cref="IDualSenseOutputs"/> with the Bluetooth audio/haptics lane the
/// DualSense emulation uses to forward host audio to the physical controller.
/// </summary>
public interface IDualSenseAudioOutputs : IDualSenseOutputs
{
    /// <summary>
    /// The physical controller's connection type (only Bluetooth carries the
    /// <c>0x35</c>/<c>0x32</c>/<c>0x36</c> audio reports).
    /// </summary>
    ConnectionType ConnectionType { get; }

    /// <summary>
    /// Restarts the Bluetooth audio/haptics report sequence and packet counters.
    /// </summary>
    void ResetBluetoothAudioStream();

    /// <summary>
    /// Sends the report <c>0x32</c> init-prime that opens the Bluetooth audio/haptics
    /// stream. The stream must be primed before sending audio or haptics reports.
    /// </summary>
    void SendBluetoothAudioPrime(SetStateData state);

    /// <summary>
    /// Sends a combined report <c>0x36</c> carrying the output state, one 200-byte Opus
    /// frame and the 64-byte voice-coil haptics frame in a single report.
    /// </summary>
    void SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route);

    /// <summary>
    /// Sends an audio-only report <c>0x35</c> carrying one 200-byte Opus frame.
    /// </summary>
    void SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route);

    /// <summary>
    /// Routes audio output and applies speaker/headphone volume.
    /// </summary>
    void SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume);
}

/// <summary>
/// Default <see cref="IDualSenseOutputs"/> implementation forwarding to a physical
/// <see cref="DualSenseDevice"/>. Writes are serialized because the physical
/// controller keeps a rolling Bluetooth sequence counter that must not be written
/// from native callback threads and the UI thread at the same time.
/// </summary>
public sealed class DualSenseDeviceOutputs : IDualSenseAudioOutputs
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("DualSenseDeviceOutputs");

    /// <summary>
    /// Guards feedback writes to the physical device.
    /// </summary>
    private readonly Lock _writeLock = new Lock();

    /// <summary>
    /// The physical controller receiving the feedback.
    /// </summary>
    private readonly DualSenseDevice _device;

    /// <summary>
    /// The special-actions engine whose active light actions override the forwarded
    /// lightbar color.
    /// </summary>
    private readonly SpecialActionEngine _specialActions;

    /// <summary>
    /// Creates a new adapter around the given physical controller.
    /// </summary>
    public DualSenseDeviceOutputs(DualSenseDevice device, SpecialActionEngine specialActions)
    {
        _device = device;
        _specialActions = specialActions;
    }

    /// <inheritdoc/>
    public ConnectionType ConnectionType
    {
        get
        {
            return _device.ConnectionType;
        }
    }

    /// <inheritdoc/>
    public bool NeedsOutputStateBlock
    {
        get
        {
            OutputStateOverride overrideState = _specialActions.OutputOverride;
            bool profileLights = _device.TryGetProfileLights(out _, out _, out _, out _)
                                 && (!_device.LightbarPassthroughEnabled
                                     || !_device.PlayerLedPassthroughEnabled);
            return overrideState.LightbarColor is not null
                   || overrideState.PlayerLeds is not null
                   || profileLights;
        }
    }

    /// <inheritdoc/>
    public void SetVibration(byte left, byte right)
    {
        lock (_writeLock)
        {
            _device.SetVibration(left, right);
        }
    }

    /// <inheritdoc/>
    public void SendOutputState(SetStateData payload)
    {
        lock (_writeLock)
        {
            try
            {
                _device.SendOutputState(ApplyOutputOverride(payload));
            }
            catch (HidException ex)
            {
                // A dropped link (e.g. Bluetooth disconnect) makes the write fail;
                // log instead of letting the exception escape the engine's callback
                // thread and crash the process.
                _log.Error($"Failed to send output state to the physical controller: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Merges the applied profile's static lights and the fields held by the active
    /// special actions (lightbar color, player LEDs) over the payload, so neither the
    /// game's output nor a plain passthrough can overwrite them. The profile's lightbar
    /// and player LEDs are only restored while the game does not own that channel.
    /// Actions are applied afterwards, so an action always wins. Fields nothing holds
    /// pass through unchanged. Returns the payload unchanged when nothing is driving the
    /// lights.
    /// </summary>
    private SetStateData ApplyOutputOverride(SetStateData payload)
    {
        OutputStateOverride overrideState = _specialActions.OutputOverride;
        (byte Red, byte Green, byte Blue)? color = overrideState.LightbarColor;
        byte? leds = overrideState.PlayerLeds;

        bool hasProfile = _device.TryGetProfileLights(
            out byte profileRed,
            out byte profileGreen,
            out byte profileBlue,
            out PlayerLedMask profileLeds);
        bool profileLightbar = hasProfile && !_device.LightbarPassthroughEnabled;
        bool profilePlayerLeds = hasProfile && !_device.PlayerLedPassthroughEnabled;
        if (color is null && leds is null && !profileLightbar && !profilePlayerLeds)
        {
            return payload;
        }

        // Clone first: SetStateData shares its backing buffer through 'with',
        // so writing via the copy would mutate the caller's payload in place.
        byte[] raw = new byte[SetStateData.PayloadSize];
        payload.CopyTo(raw, 0);
        SetStateData copy = new SetStateData(raw, 0);

        if (profileLightbar)
        {
            copy = copy with
            {
                ValidFlag1 = copy.ValidFlag1 | ValidFlags.AllowLedColor,
                ValidFlag2 = copy.ValidFlag2 | ValidFlags.AllowColorFadeAnim,
                LightFadeAnimation = 0x02,
                LedRed = profileRed,
                LedGreen = profileGreen,
                LedBlue = profileBlue
            };
        }

        if (profilePlayerLeds)
        {
            copy = copy with
            {
                ValidFlag1 = copy.ValidFlag1 | ValidFlags.AllowPlayerIndicators,
                PlayerLeds = profileLeds
            };
        }

        if (color is not null || leds is not null)
        {
            copy = copy with
            {
                LedRed = color?.Red ?? copy.LedRed,
                LedGreen = color?.Green ?? copy.LedGreen,
                LedBlue = color?.Blue ?? copy.LedBlue,
                PlayerLeds = leds is { } mask ? (PlayerLedMask)mask : copy.PlayerLeds
            };
        }

        return copy;
    }

    /// <inheritdoc/>
    public void ResetBluetoothAudioStream()
    {
        lock (_writeLock)
        {
            _device.ResetBluetoothAudioStream();
        }
    }

    /// <inheritdoc/>
    public void SendBluetoothAudioPrime(SetStateData state)
    {
        lock (_writeLock)
        {
            _device.SendBluetoothAudioPrime(ApplyOutputOverride(state));
        }
    }

    /// <inheritdoc/>
    public void SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route)
    {
        lock (_writeLock)
        {
            _device.SendBluetoothAudioAndHaptics(ApplyOutputOverride(state), opusFrame, hapticsPcm, route);
        }
    }

    /// <inheritdoc/>
    public void SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route)
    {
        lock (_writeLock)
        {
            _device.SendBluetoothAudio(opusFrame, route);
        }
    }

    /// <inheritdoc/>
    public void SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume)
    {
        lock (_writeLock)
        {
            _device.SetAudioOutput(outputControl, speakerVolume, headphoneVolume);
        }
    }
}