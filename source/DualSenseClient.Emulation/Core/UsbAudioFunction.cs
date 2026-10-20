using System.Buffers.Binary;

namespace DualSenseClient.Emulation.Core;

/// <summary>
/// Fixed UAC1 audio parameters of one persona: playback and capture geometry, the feature-unit IDs
/// of the two audio paths, the microphone queue depth and the volume default/range table. Volume
/// values are in 1/256 dB (Q8) and are the values the reference implementations expose over the
/// same feature units, observed as request behavior only.
/// </summary>
public readonly struct AudioFunctionFormat
{
    /// <summary>Feature-unit ID of the speaker path in the audio control interface.</summary>
    public const byte SpeakerFeatureUnitId = 2;

    /// <summary>Feature-unit ID of the microphone path in the audio control interface.</summary>
    public const byte MicrophoneFeatureUnitId = 5;

    /// <summary>Number of one-millisecond microphone frames the queue holds before dropping the oldest queued audio bytes.</summary>
    public const int MicrophoneQueueCapacityFrames = 16;

    /// <summary>Bytes of one speaker sample frame: every channel of one sample (8 or 4).</summary>
    public int SpeakerFrameBytes { get; init; }

    /// <summary>Number of interleaved speaker channels.</summary>
    public int SpeakerChannels { get; init; }

    /// <summary>Speaker sample rate in hertz.</summary>
    public int SpeakerSampleRateHz { get; init; }

    /// <summary>
    /// Bytes of one microphone PCM frame as queued by the persona: one nominal one-millisecond
    /// packet (192 bytes for two channels at 48 kHz, 32 bytes for one channel at 16 kHz).
    /// </summary>
    public int MicrophoneFrameBytes { get; init; }

    /// <summary>Number of interleaved microphone channels.</summary>
    public int MicrophoneChannels { get; init; }

    /// <summary>Microphone sample rate in hertz.</summary>
    public int MicrophoneSampleRateHz { get; init; }

    /// <summary>Speaker volume reported before the host's first SET_CUR, in 1/256 dB.</summary>
    public short SpeakerVolumeDefaultQ8 { get; init; }

    /// <summary>Lowest speaker volume the feature unit advertises, in 1/256 dB.</summary>
    public short SpeakerVolumeMinQ8 { get; init; }

    /// <summary>Highest speaker volume the feature unit advertises, in 1/256 dB.</summary>
    public short SpeakerVolumeMaxQ8 { get; init; }

    /// <summary>Speaker volume step the feature unit advertises, in 1/256 dB.</summary>
    public short SpeakerVolumeResolutionQ8 { get; init; }

    /// <summary>Microphone volume reported before the host's first SET_CUR, in 1/256 dB.</summary>
    public short MicrophoneVolumeDefaultQ8 { get; init; }

    /// <summary>Lowest microphone volume the feature unit advertises, in 1/256 dB.</summary>
    public short MicrophoneVolumeMinQ8 { get; init; }

    /// <summary>Highest microphone volume the feature unit advertises, in 1/256 dB.</summary>
    public short MicrophoneVolumeMaxQ8 { get; init; }

    /// <summary>Microphone volume step the feature unit advertises, in 1/256 dB.</summary>
    public short MicrophoneVolumeResolutionQ8 { get; init; }

    /// <summary>
    /// Audio parameters of the DualSense persona: four-channel 48 kHz speaker, two-channel 48 kHz
    /// microphone. The speaker range is -100..0 dB in 1 dB steps with a 0 dB default; the
    /// microphone range is 0..+48 dB in 122/256 dB steps with a +48 dB default.
    /// </summary>
    public static AudioFunctionFormat DualSense { get; } = new AudioFunctionFormat
    {
        SpeakerFrameBytes = 8,
        SpeakerChannels = 4,
        SpeakerSampleRateHz = 48000,
        MicrophoneFrameBytes = 192,
        MicrophoneChannels = 2,
        MicrophoneSampleRateHz = 48000,
        SpeakerVolumeDefaultQ8 = 0,
        SpeakerVolumeMinQ8 = -25600,
        SpeakerVolumeMaxQ8 = 0,
        SpeakerVolumeResolutionQ8 = 256,
        MicrophoneVolumeDefaultQ8 = 12288,
        MicrophoneVolumeMinQ8 = 0,
        MicrophoneVolumeMaxQ8 = 12288,
        MicrophoneVolumeResolutionQ8 = 122
    };

    /// <summary>
    /// Audio parameters of the DualShock 4 persona: two-channel 32 kHz speaker, one-channel
    /// 16 kHz microphone. The speaker range is -73..-1 dB in 1 dB steps with a -1 dB default (the
    /// reference seeds full volume at the top of the advertised range); the microphone range is
    /// -23.25..+24 dB in 0.75 dB steps with a 0 dB default.
    /// </summary>
    public static AudioFunctionFormat DualShock4 { get; } = new AudioFunctionFormat
    {
        SpeakerFrameBytes = 4,
        SpeakerChannels = 2,
        SpeakerSampleRateHz = 32000,
        MicrophoneFrameBytes = 32,
        MicrophoneChannels = 1,
        MicrophoneSampleRateHz = 16000,
        SpeakerVolumeDefaultQ8 = -256,
        SpeakerVolumeMinQ8 = -18688,
        SpeakerVolumeMaxQ8 = -256,
        SpeakerVolumeResolutionQ8 = 256,
        MicrophoneVolumeDefaultQ8 = 0,
        MicrophoneVolumeMinQ8 = -5952,
        MicrophoneVolumeMaxQ8 = 6144,
        MicrophoneVolumeResolutionQ8 = 192
    };
}

/// <summary>
/// UAC1 audio function of one persona: the feature-unit mute and volume state machine of the
/// speaker and microphone paths, and the bounded microphone PCM byte ring read by the ISO IN
/// endpoint. The control values are state only: the reference forwards volume to a controller
/// amplifier, but this device has none, so the values are stored and reported back without
/// scaling the outgoing PCM. The microphone ring holds whole one-millisecond frames; the producer
/// (client thread) and the reader (ISO scheduler timer thread) run concurrently, so one lock
/// guards the state and the ring, and the overflow warning is logged outside it.
/// </summary>
/// <remarks>
/// The ring holds exactly <see cref="AudioFunctionFormat.MicrophoneQueueCapacityFrames"/> whole
/// frames: an overflow drops only the oldest frame(s) needed so the newest audio survives, and it
/// logs at most one warning per overflow burst, the burst ending when the ring is drained or
/// cleared. Reads never pad stream bytes: an empty ring zero-fills the destination and returns at
/// most one nominal frame (192 bytes for the DualSense, 32 for the DualShock 4, or the destination
/// length when that is shorter), while a non-empty ring returns at most the
/// bytes available, so a short host packet leaves the remaining bytes queued and the byte stream
/// stays continuous.
/// </remarks>
public sealed class UsbAudioFunction
{
    /// <summary>Class-specific SET_CUR request code (UAC1).</summary>
    private const byte SetCurRequest = 0x01;

    /// <summary>Class-specific GET_CUR request code (UAC1).</summary>
    private const byte GetCurRequest = 0x81;

    /// <summary>Class-specific GET_MIN request code (UAC1).</summary>
    private const byte GetMinRequest = 0x82;

    /// <summary>Class-specific GET_MAX request code (UAC1).</summary>
    private const byte GetMaxRequest = 0x83;

    /// <summary>Class-specific GET_RES request code (UAC1).</summary>
    private const byte GetResRequest = 0x84;

    /// <summary>Mute control selector (UAC1 feature unit).</summary>
    private const byte MuteSelector = 0x01;

    /// <summary>Volume control selector (UAC1 feature unit).</summary>
    private const byte VolumeSelector = 0x02;

    /// <summary>bmRequestType type field of a class request.</summary>
    private const byte ClassRequestType = 0x20;

    /// <summary>bmRequestType recipient field of an interface request.</summary>
    private const byte InterfaceRecipient = 0x01;

    /// <summary>bmRequestType direction bit: device-to-host.</summary>
    private const byte DirectionIn = 0x80;

    private readonly object _gate = new object();

    private readonly IEmulationLogger? _logger;

    private readonly byte[] _microphoneRing;

    private int _microphoneReadOffset;

    private int _microphoneAvailableBytes;

    private bool _microphoneOverflowWarned;

    private byte _speakerMute;

    private byte _microphoneMute;

    private short _speakerVolumeQ8;

    private short _microphoneVolumeQ8;

    /// <summary>
    /// Initializes the audio function of one device with the format's startup volume defaults, an
    /// empty microphone ring and unmuted paths.
    /// </summary>
    /// <param name="format">Fixed audio parameters of the persona.</param>
    /// <param name="logger">
    /// Optional logging sink: a microphone queue overflow logs at most one warning per burst.
    /// Without a logger nothing is logged.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> has no microphone frame size.</exception>
    public UsbAudioFunction(AudioFunctionFormat format, IEmulationLogger? logger = null)
    {
        if (format.MicrophoneFrameBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format.MicrophoneFrameBytes, "The microphone frame size must be positive.");
        }

        Format = format;
        _logger = logger;
        _speakerVolumeQ8 = format.SpeakerVolumeDefaultQ8;
        _microphoneVolumeQ8 = format.MicrophoneVolumeDefaultQ8;
        _microphoneRing = new byte[format.MicrophoneFrameBytes * AudioFunctionFormat.MicrophoneQueueCapacityFrames];
    }

    /// <summary>Fixed audio parameters this function was created with.</summary>
    public AudioFunctionFormat Format { get; }

    /// <summary>Whether the speaker path is muted by the last successful SET_CUR mute.</summary>
    public bool SpeakerMuted
    {
        get
        {
            lock (_gate)
            {
                return _speakerMute != 0;
            }
        }
    }

    /// <summary>Speaker volume in 1/256 dB, as last stored by SET_CUR (0 dB by default on the DualSense).</summary>
    public short SpeakerVolumeQ8
    {
        get
        {
            lock (_gate)
            {
                return _speakerVolumeQ8;
            }
        }
    }

    /// <summary>Whether the microphone path is muted by the last successful SET_CUR mute.</summary>
    public bool MicrophoneMuted
    {
        get
        {
            lock (_gate)
            {
                return _microphoneMute != 0;
            }
        }
    }

    /// <summary>Microphone volume in 1/256 dB, as last stored by SET_CUR (+48 dB by default on the DualSense).</summary>
    public short MicrophoneVolumeQ8
    {
        get
        {
            lock (_gate)
            {
                return _microphoneVolumeQ8;
            }
        }
    }

    /// <summary>
    /// Handles one class-specific control request addressed to the audio control interface: only
    /// feature units 2 (speaker) and 5 (microphone) are served. SET_CUR stores the mute (selector
    /// 0x01, one byte) or volume (selector 0x02, two little-endian bytes); GET_CUR returns mute or
    /// volume; GET_MIN, GET_MAX and GET_RES return the volume range of the addressed feature unit.
    /// Every other combination - unknown entity, selector, request or data-stage length, mute
    /// range requests, a request whose code contradicts the direction bit, and non-class or
    /// non-interface requests - is rejected without touching the state.
    /// </summary>
    /// <param name="setup">Control transfer setup packet (wValue is selector/channel, wIndex is entity/interface).</param>
    /// <param name="outData">Host-to-device data stage; read only by SET_CUR requests.</param>
    /// <param name="response">Buffer that receives the device-to-host data stage.</param>
    /// <returns>The number of response bytes written, zero for a successful SET_CUR, or -1 when the caller must stall the request.</returns>
    public int HandleClassRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, Span<byte> response)
    {
        if ((setup.RequestType & 0x60) != ClassRequestType || (setup.RequestType & 0x1F) != InterfaceRecipient)
        {
            return -1;
        }

        int entityId = setup.Index >> 8;
        bool isSpeaker = entityId == AudioFunctionFormat.SpeakerFeatureUnitId;
        if (!isSpeaker && entityId != AudioFunctionFormat.MicrophoneFeatureUnitId)
        {
            return -1;
        }

        int selector = setup.Value >> 8;
        if (selector != MuteSelector && selector != VolumeSelector)
        {
            return -1;
        }

        if ((setup.RequestType & DirectionIn) == 0)
        {
            return HandleSetRequest(setup, outData, isSpeaker, selector);
        }

        return HandleGetRequest(setup, response, isSpeaker, selector);
    }

    /// <summary>
    /// Appends one whole microphone PCM frame to the bounded ring. When the ring is full the
    /// oldest <see cref="AudioFunctionFormat.MicrophoneFrameBytes"/> bytes are dropped so the
    /// newest audio survives; a burst of drops logs one warning, and the burst ends when the
    /// ring has been drained or cleared. The producer runs on the client thread while the ISO
    /// scheduler reads on its timer thread, so the copy is guarded by the lock and the logger
    /// call happens after it is released.
    /// </summary>
    /// <param name="frame">Exactly <see cref="AudioFunctionFormat.MicrophoneFrameBytes"/> bytes of interleaved 16-bit PCM.</param>
    /// <exception cref="ArgumentException"><paramref name="frame"/> is not exactly one frame.</exception>
    public void EnqueueMicrophonePcm(ReadOnlySpan<byte> frame)
    {
        int frameBytes = Format.MicrophoneFrameBytes;
        if (frame.Length != frameBytes)
        {
            throw new ArgumentException($"A microphone frame requires exactly {frameBytes} bytes; got {frame.Length}.", nameof(frame));
        }

        bool warnOverflow = false;
        lock (_gate)
        {
            int capacityBytes = _microphoneRing.Length;
            while (_microphoneAvailableBytes + frameBytes > capacityBytes)
            {
                _microphoneReadOffset = (_microphoneReadOffset + frameBytes) % capacityBytes;
                _microphoneAvailableBytes -= frameBytes;
                if (!_microphoneOverflowWarned)
                {
                    _microphoneOverflowWarned = true;
                    warnOverflow = true;
                }
            }

            int writeOffset = (_microphoneReadOffset + _microphoneAvailableBytes) % capacityBytes;
            int firstPart = Math.Min(frameBytes, capacityBytes - writeOffset);
            frame[..firstPart].CopyTo(_microphoneRing.AsSpan(writeOffset));
            frame[firstPart..].CopyTo(_microphoneRing);
            _microphoneAvailableBytes += frameBytes;
        }

        if (warnOverflow)
        {
            _logger?.Log(EmulationLogLevel.Warning, "The microphone PCM queue overflowed; dropped the oldest queued audio bytes.");
        }
    }

    /// <summary>
    /// Copies queued microphone PCM into <paramref name="destination"/> without blocking. When
    /// bytes are queued, at most <paramref name="destination"/>.Length stream bytes are copied and
    /// that count is returned: a short packet is produced instead of padding audio bytes, and a
    /// read may split a frame while the remaining bytes stay queued. When the ring is empty the
    /// whole destination is zero-filled and the nominal length of one frame is returned (at most
    /// the destination length), which is the only zero-padded packet.
    /// </summary>
    /// <param name="destination">Buffer the ISO scheduler offers for one packet.</param>
    /// <returns>The number of bytes written, or the nominal zero-packet length when the ring is empty.</returns>
    public int ReadMicrophonePcm(Span<byte> destination)
    {
        lock (_gate)
        {
            if (_microphoneAvailableBytes == 0)
            {
                destination.Clear();
                return Math.Min(destination.Length, Format.MicrophoneFrameBytes);
            }

            int count = Math.Min(destination.Length, _microphoneAvailableBytes);
            int ringLength = _microphoneRing.Length;
            int firstPart = Math.Min(count, ringLength - _microphoneReadOffset);
            _microphoneRing.AsSpan(_microphoneReadOffset, firstPart).CopyTo(destination);
            _microphoneRing.AsSpan(0, count - firstPart).CopyTo(destination[firstPart..]);

            _microphoneReadOffset = (_microphoneReadOffset + count) % ringLength;
            _microphoneAvailableBytes -= count;
            if (_microphoneAvailableBytes == 0)
            {
                _microphoneOverflowWarned = false;
            }

            return count;
        }
    }

    /// <summary>Drops every queued microphone audio byte; the next overflow after this starts a new burst.</summary>
    public void ClearMicrophonePcm()
    {
        lock (_gate)
        {
            _microphoneReadOffset = 0;
            _microphoneAvailableBytes = 0;
            _microphoneOverflowWarned = false;
        }
    }

    /// <summary>Stores the mute byte or Q8 volume of one path after the request shape has been validated.</summary>
    /// <param name="setup">Validated setup packet.</param>
    /// <param name="outData">Host-to-device data stage.</param>
    /// <param name="isSpeaker">Whether the request addresses feature unit 2 rather than 5.</param>
    /// <param name="selector">Validated control selector.</param>
    /// <returns>Zero when stored, -1 when the request shape is unsupported.</returns>
    private int HandleSetRequest(in UsbSetupPacket setup, ReadOnlySpan<byte> outData, bool isSpeaker, int selector)
    {
        if (setup.Request != SetCurRequest)
        {
            return -1;
        }

        if (selector == MuteSelector)
        {
            if (setup.Length != 1 || outData.Length < 1)
            {
                return -1;
            }

            lock (_gate)
            {
                if (isSpeaker)
                {
                    _speakerMute = outData[0];
                }
                else
                {
                    _microphoneMute = outData[0];
                }
            }

            return 0;
        }

        if (setup.Length != 2 || outData.Length < 2)
        {
            return -1;
        }

        short volumeQ8 = BinaryPrimitives.ReadInt16LittleEndian(outData);
        lock (_gate)
        {
            if (isSpeaker)
            {
                _speakerVolumeQ8 = volumeQ8;
            }
            else
            {
                _microphoneVolumeQ8 = volumeQ8;
            }
        }

        return 0;
    }

    /// <summary>Writes the requested mute or volume parameter block after the request shape has been validated.</summary>
    /// <param name="setup">Validated setup packet.</param>
    /// <param name="response">Buffer receiving the parameter block.</param>
    /// <param name="isSpeaker">Whether the request addresses feature unit 2 rather than 5.</param>
    /// <param name="selector">Validated control selector.</param>
    /// <returns>The number of bytes written, or -1 when the request shape is unsupported.</returns>
    private int HandleGetRequest(in UsbSetupPacket setup, Span<byte> response, bool isSpeaker, int selector)
    {
        if (selector == MuteSelector)
        {
            if (setup.Request != GetCurRequest || setup.Length != 1 || response.Length < 1)
            {
                return -1;
            }

            lock (_gate)
            {
                response[0] = isSpeaker ? _speakerMute : _microphoneMute;
            }

            return 1;
        }

        short valueQ8;
        if (setup.Request == GetCurRequest)
        {
            lock (_gate)
            {
                valueQ8 = isSpeaker ? _speakerVolumeQ8 : _microphoneVolumeQ8;
            }
        }
        else if (setup.Request == GetMinRequest)
        {
            valueQ8 = isSpeaker ? Format.SpeakerVolumeMinQ8 : Format.MicrophoneVolumeMinQ8;
        }
        else if (setup.Request == GetMaxRequest)
        {
            valueQ8 = isSpeaker ? Format.SpeakerVolumeMaxQ8 : Format.MicrophoneVolumeMaxQ8;
        }
        else if (setup.Request == GetResRequest)
        {
            valueQ8 = isSpeaker ? Format.SpeakerVolumeResolutionQ8 : Format.MicrophoneVolumeResolutionQ8;
        }
        else
        {
            return -1;
        }

        if (setup.Length != 2 || response.Length < 2)
        {
            return -1;
        }

        BinaryPrimitives.WriteInt16LittleEndian(response, valueQ8);
        return 2;
    }
}