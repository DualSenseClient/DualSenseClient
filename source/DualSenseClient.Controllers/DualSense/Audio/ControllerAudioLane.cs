using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;

namespace DualSenseClient.Controllers.DualSense.Audio;

/// <summary>
/// The priority a source claims when it wants the physical controller's audio lane.
/// </summary>
public enum AudioPriority
{
    /// <summary>
    /// Host-audio forwarding from the virtual controller or a direct capture.
    /// </summary>
    Forwarder = 0,

    /// <summary>
    /// The user-initiated audio player.
    /// </summary>
    Player = 1,

    /// <summary>
    /// A special action's sound, which preempts other playback.
    /// </summary>
    SpecialAction = 2,

    /// <summary>
    /// Manual motor testing. The pad ignores classic rumble motor bytes while an
    /// audio stream is open, so manual vibration must preempt every audio owner.
    /// </summary>
    ManualRumble = 3
}

/// <summary>
/// The narrow report port the audio lane needs. Implemented by the physical controller.
/// </summary>
public interface IControllerAudioSink
{
    /// <summary>
    /// The physical controller's connection type.
    /// </summary>
    ConnectionType ConnectionType { get; }

    /// <summary>
    /// Whether the physical controller currently has headphones connected.
    /// </summary>
    bool HeadphonesConnected { get; }

    /// <summary>
    /// Routes audio output and applies speaker/headphone volume.
    /// </summary>
    void SetAudioOutput(AudioControl outputControl, byte speakerVolume, byte headphoneVolume);

    /// <summary>
    /// Restarts the Bluetooth audio/haptics report sequence and packet counters.
    /// </summary>
    void ResetBluetoothAudioStream();

    /// <summary>
    /// Sends the Bluetooth audio init-prime report.
    /// </summary>
    void SendBluetoothAudioPrime(SetStateData state);

    /// <summary>
    /// Sends one combined audio and haptics report.
    /// </summary>
    void SendBluetoothAudioAndHaptics(SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm,
        BluetoothAudioRoute route);

    /// <summary>
    /// Sends one double combined audio and haptics report.
    /// </summary>
    void SendBluetoothAudioAndHapticsDouble(ReadOnlySpan<byte> opusFrames, ReadOnlySpan<byte> hapticsPcm,
        BluetoothAudioRoute route);

    /// <summary>
    /// Sends one audio-only report.
    /// </summary>
    void SendBluetoothAudio(ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route);

    /// <summary>
    /// Sends one haptics-only report.
    /// </summary>
    void SendBluetoothHaptics(ReadOnlySpan<byte> hapticsPcm);
}

/// <summary>
/// Owns the physical controller's audio lane: which source is allowed to drive it and
/// the report entry points every source shares.
/// </summary>
public sealed class ControllerAudioLane
{
    private readonly IControllerAudioSink _sink;
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<object, (AudioPriority Priority, long Sequence)> _owners = new Dictionary<object, (AudioPriority, long)>();
    private long _sequence;
    private long _activationVersion;
    private object? _active;
    private float _audioLevel;

    /// <summary>
    /// Creates the lane around the given report sink.
    /// </summary>
    /// <param name="sink">The physical controller report port.</param>
    public ControllerAudioLane(IControllerAudioSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>
    /// The priority currently allowed to drive the lane, or <c>null</c> when no source
    /// has claimed it.
    /// </summary>
    public AudioPriority? ActivePriority
    {
        get
        {
            lock (_lock)
            {
                return _active is null ? null : _owners[_active].Priority;
            }
        }
    }

    /// <summary>
    /// Increments whenever the active source changes. Writers use it to detect that the
    /// lane was handed to another source and later returned to them.
    /// </summary>
    public long ActivationVersion
    {
        get
        {
            lock (_lock)
            {
                return _activationVersion;
            }
        }
    }

    /// <summary>
    /// The active source's latest audio level (0-1), used by audio-reactive lighting.
    /// </summary>
    public float AudioLevel
    {
        get
        {
            lock (_lock)
            {
                return _audioLevel;
            }
        }
    }

    /// <summary>
    /// Claims the lane for the given owner at the given priority. The owner remains
    /// registered when a higher-priority source preempts it, so it resumes automatically
    /// when that source leaves.
    /// </summary>
    /// <param name="owner">The source claiming the lane.</param>
    /// <param name="priority">The source's priority.</param>
    /// <returns><c>true</c> when the owner is active immediately.</returns>
    public bool TryEnter(object owner, AudioPriority priority)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_lock)
        {
            _owners[owner] = (priority, ++_sequence);
            RecomputeActiveLocked();
            return ReferenceEquals(_active, owner);
        }
    }

    /// <summary>
    /// Releases the lane for the given owner. A lower-priority registered source becomes
    /// active again.
    /// </summary>
    /// <param name="owner">The source releasing the lane.</param>
    public void Leave(object owner)
    {
        lock (_lock)
        {
            if (_owners.Remove(owner))
            {
                RecomputeActiveLocked();
            }
        }
    }

    /// <summary>
    /// Whether the given owner currently owns the lane.
    /// </summary>
    /// <param name="owner">The source to check.</param>
    /// <returns><c>true</c> when the owner is active.</returns>
    public bool IsActive(object owner)
    {
        lock (_lock)
        {
            return ReferenceEquals(_active, owner);
        }
    }

    /// <summary>
    /// Publishes an audio level for the active owner. Inactive owners are ignored.
    /// </summary>
    /// <param name="owner">The source publishing the level.</param>
    /// <param name="level">The level in the 0-1 range.</param>
    public void SetAudioLevel(object owner, float level)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_active, owner))
            {
                _audioLevel = Math.Clamp(level, 0f, 1f);
            }
        }
    }

    /// <summary>
    /// Applies the output route for the active owner.
    /// </summary>
    /// <param name="owner">The source applying the route.</param>
    /// <param name="outputControl">The output path.</param>
    /// <param name="speakerVolume">Speaker volume.</param>
    /// <param name="headphoneVolume">Headphone volume.</param>
    /// <returns><c>true</c> when the active owner applied the route.</returns>
    public bool SetAudioOutput(object owner, AudioControl outputControl, byte speakerVolume, byte headphoneVolume)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.SetAudioOutput(outputControl, speakerVolume, headphoneVolume);
        return true;
    }

    /// <summary>
    /// Resets the Bluetooth stream for the active owner.
    /// </summary>
    /// <param name="owner">The source resetting the stream.</param>
    /// <returns><c>true</c> when the active owner reset the stream.</returns>
    public bool ResetBluetoothStream(object owner)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.ResetBluetoothAudioStream();
        return true;
    }

    /// <summary>
    /// Sends the Bluetooth init-prime for the active owner.
    /// </summary>
    /// <param name="owner">The source priming the stream.</param>
    /// <param name="state">The output state embedded in the prime.</param>
    /// <returns><c>true</c> when the active owner primed the stream.</returns>
    public bool Prime(object owner, SetStateData state)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.SendBluetoothAudioPrime(state);
        return true;
    }

    /// <summary>
    /// Sends one audio-only report for the active owner.
    /// </summary>
    /// <param name="owner">The source sending the report.</param>
    /// <param name="opusFrame">The encoded audio frame.</param>
    /// <param name="route">The target output.</param>
    /// <returns><c>true</c> when the report was sent.</returns>
    public bool SendAudio(object owner, ReadOnlySpan<byte> opusFrame, BluetoothAudioRoute route)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.SendBluetoothAudio(opusFrame, route);
        return true;
    }

    /// <summary>
    /// Sends one haptics-only report for the active owner.
    /// </summary>
    /// <param name="owner">The source sending the report.</param>
    /// <param name="hapticsPcm">The haptics frame.</param>
    /// <returns><c>true</c> when the report was sent.</returns>
    public bool SendHaptics(object owner, ReadOnlySpan<byte> hapticsPcm)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.SendBluetoothHaptics(hapticsPcm);
        return true;
    }

    /// <summary>
    /// Sends one combined audio and haptics report for the active owner.
    /// </summary>
    /// <param name="owner">The source sending the report.</param>
    /// <param name="state">The output state embedded in the report.</param>
    /// <param name="opusFrame">The encoded audio frame.</param>
    /// <param name="hapticsPcm">The haptics frame.</param>
    /// <param name="route">The target output.</param>
    /// <returns><c>true</c> when the report was sent.</returns>
    public bool SendAudioAndHaptics(object owner, SetStateData state, ReadOnlySpan<byte> opusFrame, ReadOnlySpan<byte> hapticsPcm,
        BluetoothAudioRoute route)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.SendBluetoothAudioAndHaptics(state, opusFrame, hapticsPcm, route);
        return true;
    }

    /// <summary>
    /// Sends one double combined report for the active owner.
    /// </summary>
    /// <param name="owner">The source sending the report.</param>
    /// <param name="opusFrames">Two encoded audio frames.</param>
    /// <param name="hapticsPcm">Two haptics frames.</param>
    /// <param name="route">The target output.</param>
    /// <returns><c>true</c> when the report was sent.</returns>
    public bool SendDouble(object owner, ReadOnlySpan<byte> opusFrames, ReadOnlySpan<byte> hapticsPcm, BluetoothAudioRoute route)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_active, owner))
            {
                return false;
            }
        }

        _sink.SendBluetoothAudioAndHapticsDouble(opusFrames, hapticsPcm, route);
        return true;
    }

    private void RecomputeActiveLocked()
    {
        object? best = null;
        AudioPriority bestPriority = AudioPriority.Forwarder;
        long bestSequence = -1;
        foreach ((object owner, (AudioPriority priority, long sequence)) in _owners)
        {
            if (best is null || priority > bestPriority || (priority == bestPriority && sequence > bestSequence))
            {
                best = owner;
                bestPriority = priority;
                bestSequence = sequence;
            }
        }

        if (!ReferenceEquals(_active, best))
        {
            _active = best;
            _activationVersion++;
        }

        if (_active is null)
        {
            _audioLevel = 0f;
        }
    }
}