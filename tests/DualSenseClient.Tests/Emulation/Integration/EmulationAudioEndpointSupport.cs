using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// Shared plumbing of the gated Emulation audio endpoint tests: the persona wire formats, the
/// endpoint-pair and removal assertions built on <see cref="WasapiAudioEndpoint"/>, the 1 kHz tone
/// generators for the speaker and microphone lanes, the bounded event recorder that copies every
/// <c>AudioOutReceived</c> packet inside the callback (the event memory is only valid during the
/// callback, ruling R2), and the paced microphone producer that keeps the persona's 16-frame ring
/// fed instead of overwriting it in one burst. The helper asserts only on facts every persona shares;
/// each fixture pins its own ids, packet sizes and haptics expectations.
/// </summary>
public static class EmulationAudioEndpointSupport
{
    /// <summary>Frequency of the rendered and microphone test tone.</summary>
    public const double ToneFrequencyHz = 1000.0;

    /// <summary>Peak amplitude of the rendered and microphone test tone (quarter scale).</summary>
    public const double ToneAmplitude = 0.25;

    /// <summary>How long the microphone tone is enqueued, one frame per millisecond.</summary>
    public const int MicrophoneToneMilliseconds = 200;

    /// <summary>Speaker format of the DualSense persona: 4 ch S16LE 48 kHz.</summary>
    public static readonly WasapiAudioFormat DualSenseRenderFormat = new WasapiAudioFormat(4, 48000, 16, false);

    /// <summary>Microphone format of the DualSense persona: 2 ch S16LE 48 kHz.</summary>
    public static readonly WasapiAudioFormat DualSenseCaptureFormat = new WasapiAudioFormat(2, 48000, 16, false);

    /// <summary>Speaker format of the DualShock 4 persona: 2 ch S16LE 32 kHz.</summary>
    public static readonly WasapiAudioFormat DualShock4RenderFormat = new WasapiAudioFormat(2, 32000, 16, false);

    /// <summary>Microphone format of the DualShock 4 persona: 1 ch S16LE 16 kHz.</summary>
    public static readonly WasapiAudioFormat DualShock4CaptureFormat = new WasapiAudioFormat(1, 16000, 16, false);

    /// <summary>HRESULT <c>AUDCLNT_E_RESOURCES_INVALIDATED</c>: the audio service invalidated the stream.</summary>
    public const int AudioClientResourcesInvalidatedHResult = unchecked((int)0x88890026);

    /// <summary>How long the helpers poll for the endpoints an attach created (the task's 5 s budget).</summary>
    public static readonly TimeSpan EndpointAppearanceTimeout = WasapiAudioEndpoint.EndpointAppearanceTimeout;

    /// <summary>How long an audio endpoint may take to disappear after a detach.</summary>
    public static readonly TimeSpan EndpointRemovalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a newly observed endpoint must stay present before the tests open it. Windows lists
    /// the MMDevice endpoint before the audio service has finished registering it; opening an
    /// exclusive stream in that window can race the endpoint's final initialization.
    /// </summary>
    public static readonly TimeSpan EndpointSettleTime = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long to let the audio service finish the endpoint teardown after the endpoint ids left the
    /// active set. Reattaching immediately reuses the same ids, and an unfinished teardown can
    /// invalidate the new stream with <c>AUDCLNT_E_RESOURCES_INVALIDATED</c>.
    /// </summary>
    public static readonly TimeSpan EndpointTeardownSettleTime = TimeSpan.FromMilliseconds(500);

    /// <summary>How much audio the render tone test writes.</summary>
    public static readonly TimeSpan ToneDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>How long to wait for the AudioOutReceived packets after rendering the tone.</summary>
    public static readonly TimeSpan AudioOutDrainTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Creates an Emulation server on a loopback port selected by the OS.</summary>
    /// <returns>A started server is not returned; the caller calls <see cref="EmulationServer.Start"/>.</returns>
    public static EmulationServer CreateServer()
    {
        return new EmulationServer(new EmulationServerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0)
        });
    }

    /// <summary>Attaches a persona or fails the test with the strategy's diagnostics.</summary>
    /// <param name="server">Server the persona is registered with.</param>
    /// <param name="device">Persona to attach.</param>
    /// <param name="personaName">Display name of the persona, for the failure message.</param>
    /// <returns>The attach diagnostics (<see langword="null"/> on success), for later failure messages.</returns>
    public static string? AttachOrFail(EmulationServer server, VirtualUsbDevice device, string personaName)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(device);
        if (!server.TryAttach(device, out string? attachError))
        {
            Assert.Fail($"The {personaName} could not be attached to this machine: {attachError}");
        }

        return attachError;
    }

    /// <summary>Detaches a persona and asserts that the strategy accepted the detach.</summary>
    /// <param name="server">Server the persona was attached through.</param>
    /// <param name="device">Persona to detach.</param>
    /// <param name="personaName">Display name of the persona, for the failure message.</param>
    /// <returns><see langword="true"/> when the detach succeeded; the assertion throws otherwise.</returns>
    public static bool DetachOrFail(EmulationServer server, VirtualUsbDevice device, string personaName)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(device);
        bool detached = server.TryDetach(device, out string? detachError);
        Assert.That(detached, Is.True, $"Detach of the {personaName} failed: {detachError}");
        return detached;
    }

    /// <summary>
    /// Retries the detach of a failed test and folds a cleanup failure into the original failure, so a
    /// vhci port is never left held silently and the original assertion is not masked.
    /// </summary>
    /// <param name="server">Server the persona was attached through.</param>
    /// <param name="device">Persona to detach.</param>
    /// <param name="detached">Whether the test already detached the persona.</param>
    /// <param name="busId">Bus id of the persona, for the failure message.</param>
    /// <param name="failure">The failure that is already propagating.</param>
    /// <param name="baseline">
    /// Active audio endpoints captured before the attach; when supplied, the cleanup also waits for
    /// the endpoint set to return to the baseline so a failed test cannot poison the next one.
    /// </param>
    public static void CleanupAfterFailure(
        EmulationServer server,
        VirtualUsbDevice device,
        bool detached,
        string busId,
        Exception failure,
        WasapiEndpointSnapshot? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(failure);
        if (!detached && !server.TryDetach(device, out string? cleanupError))
        {
            Assert.Fail($"{failure.Message} Additionally, the cleanup detach failed, so a vhci port may still be held "
                        + $"by bus id \"{busId}\": {cleanupError}");
        }

        if (baseline is null)
        {
            return;
        }

        bool settled;
        try
        {
            settled = WaitForNoExtraEndpoints(baseline, EndpointRemovalTimeout);
        }
        catch (Exception cleanupException)
        {
            Assert.Fail($"{failure.Message} Additionally, the cleanup could not confirm that the audio endpoints returned "
                        + $"to the pre-attach set: {cleanupException.Message}");
            return;
        }

        if (!settled)
        {
            Assert.Fail($"{failure.Message} Additionally, audio endpoints created by the failed test were still active "
                        + $"{EndpointRemovalTimeout.TotalSeconds:0} s after the cleanup detach.");
        }

        Thread.Sleep(EndpointTeardownSettleTime);
    }

    /// <summary>
    /// Waits up to <see cref="EndpointAppearanceTimeout"/> for exactly one new render and one new
    /// capture endpoint, identified by ids absent from <paramref name="baseline"/>, and fails with the
    /// baseline, the observed diff and the attach diagnostics when the pair is not exactly one each.
    /// </summary>
    /// <param name="baseline">Active endpoints captured before the attach.</param>
    /// <param name="personaName">Display name of the persona, for the failure message.</param>
    /// <param name="attachError">Diagnostics of the attach call.</param>
    /// <returns>The render and capture endpoints the attach created.</returns>
    public static WasapiEndpointPair WaitForEndpointPair(
        WasapiEndpointSnapshot baseline,
        string personaName,
        string? attachError)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        WasapiEndpointAppearance appearance = WasapiAudioEndpoint.WaitForNewEndpoints(baseline, EndpointAppearanceTimeout);
        if (appearance.NewRenderEndpoints.Count != 1 || appearance.NewCaptureEndpoints.Count != 1)
        {
            Assert.Fail($"Expected exactly one new {personaName} render endpoint and one new capture endpoint within "
                        + $"{EndpointAppearanceTimeout.TotalSeconds:0} s; baseline: {DescribeSnapshot(baseline)}; "
                        + $"new render: {DescribeEndpoints(appearance.NewRenderEndpoints)}; "
                        + $"new capture: {DescribeEndpoints(appearance.NewCaptureEndpoints)}; "
                        + $"attach diagnostics: {attachError}");
        }

        WasapiEndpointInfo renderEndpoint = appearance.NewRenderEndpoints[0];
        WasapiEndpointInfo captureEndpoint = appearance.NewCaptureEndpoints[0];
        TestContext.Out.WriteLine($"New {personaName} render endpoint: \"{renderEndpoint.FriendlyName}\" [{renderEndpoint.Id}].");
        TestContext.Out.WriteLine($"New {personaName} capture endpoint: \"{captureEndpoint.FriendlyName}\" [{captureEndpoint.Id}].");

        Thread.Sleep(EndpointSettleTime);
        WasapiEndpointSnapshot confirmed = WasapiAudioEndpoint.CaptureActiveEndpoints();
        if (!confirmed.ContainsEndpoint(renderEndpoint.Id) || !confirmed.ContainsEndpoint(captureEndpoint.Id))
        {
            Assert.Fail($"The new {personaName} audio endpoints disappeared during the "
                        + $"{EndpointSettleTime.TotalMilliseconds:0} ms settle window; current endpoint set: "
                        + $"{DescribeSnapshot(confirmed)}; attach diagnostics: {attachError}");
        }

        TestContext.Out.WriteLine($"Both endpoints stayed active through the {EndpointSettleTime.TotalMilliseconds:0} ms settle window.");
        return new WasapiEndpointPair(renderEndpoint, captureEndpoint);
    }

    /// <summary>Waits for both endpoints of <paramref name="pair"/> to disappear and asserts that they did.</summary>
    /// <param name="pair">Endpoints the attach created.</param>
    /// <param name="personaName">Display name of the persona, for the failure message.</param>
    public static void AssertEndpointsRemoved(WasapiEndpointPair pair, string personaName)
    {
        ArgumentNullException.ThrowIfNull(pair);
        string[] endpointIds = [pair.RenderEndpoint.Id, pair.CaptureEndpoint.Id];
        bool removed = WasapiAudioEndpoint.WaitForEndpointRemoval(endpointIds, EndpointRemovalTimeout);
        Assert.That(removed, Is.True,
            $"The {personaName} audio endpoints were still active {EndpointRemovalTimeout.TotalSeconds:0} s after detach; "
            + $"render: \"{pair.RenderEndpoint.FriendlyName}\" [{pair.RenderEndpoint.Id}], "
            + $"capture: \"{pair.CaptureEndpoint.FriendlyName}\" [{pair.CaptureEndpoint.Id}].");
        TestContext.Out.WriteLine($"Both {personaName} audio endpoints disappeared from the active endpoint set.");
        Thread.Sleep(EndpointTeardownSettleTime);
    }

    /// <summary>Describes the render and capture endpoint lists of one snapshot for test output.</summary>
    /// <param name="snapshot">Snapshot to describe.</param>
    /// <returns>One line with the counts and endpoint names/ids.</returns>
    public static string DescribeSnapshot(WasapiEndpointSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return $"{snapshot.RenderEndpoints.Count} render [{DescribeEndpoints(snapshot.RenderEndpoints)}], "
               + $"{snapshot.CaptureEndpoints.Count} capture [{DescribeEndpoints(snapshot.CaptureEndpoints)}]";
    }

    /// <summary>Describes an opened render stream's negotiation outcome.</summary>
    /// <param name="stream">Stream to describe.</param>
    /// <returns>Share mode, negotiated format, buffer size and negotiation note.</returns>
    public static string DescribeStream(WasapiRenderEndpoint stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return $"{stream.ShareMode} mode, {stream.Format}, buffer {stream.BufferFrames} frame(s); {stream.NegotiationNote}";
    }

    /// <summary>Describes an opened capture stream's negotiation outcome.</summary>
    /// <param name="stream">Stream to describe.</param>
    /// <returns>Share mode, negotiated format and negotiation note.</returns>
    public static string DescribeStream(WasapiCaptureEndpoint stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return $"{stream.ShareMode} mode, {stream.Format}; {stream.NegotiationNote}";
    }

    /// <summary>Describes the start of an AudioOutReceived packet for test output.</summary>
    /// <param name="packet">Packet copy to describe; empty when none was received.</param>
    /// <returns>The packet length and its first bytes in hex.</returns>
    public static string DescribePacket(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return "(no packet)";
        }

        int shown = Math.Min(packet.Length, 24);
        return $"{packet.Length} byte(s), first {shown}: {Convert.ToHexString(packet[..shown])}";
    }

    /// <summary>Polls the recorder until at least <paramref name="minimumPackets"/> AudioOutReceived packets arrived.</summary>
    /// <param name="recorder">Recorder subscribed to the persona's audio event.</param>
    /// <param name="minimumPackets">Packet count that satisfies the wait.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns><see langword="true"/> when the count was reached.</returns>
    public static bool WaitForAudioPackets(AudioOutRecorder recorder, long minimumPackets, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            if (recorder.PacketCount >= minimumPackets)
            {
                return true;
            }

            Thread.Sleep(10);
        } while (stopwatch.Elapsed < timeout);

        return recorder.PacketCount >= minimumPackets;
    }

    /// <summary>
    /// Creates one microphone frame of a continuous 1 kHz tone: one millisecond of interleaved S16LE
    /// samples at the persona's microphone rate and channel count. The caller calls this once per
    /// millisecond so the 16-frame persona ring is fed instead of overwritten in one burst.
    /// </summary>
    /// <param name="channels">Microphone channel count (2 for DualSense, 1 for DualShock 4).</param>
    /// <param name="sampleRateHz">Microphone sample rate in hertz.</param>
    /// <param name="sampleIndex">Running sample index, advanced by one frame per call.</param>
    /// <returns>Exactly one microphone frame of the persona's wire format.</returns>
    public static byte[] CreateMicrophoneToneFrame(int channels, int sampleRateHz, ref int sampleIndex)
    {
        if (sampleRateHz % 1000 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz), sampleRateHz, "The microphone rate must be a multiple of 1000 Hz.");
        }

        int samplesPerFrame = sampleRateHz / 1000;
        byte[] frame = new byte[samplesPerFrame * channels * 2];
        int offset = 0;
        for (int sample = 0; sample < samplesPerFrame; sample++)
        {
            double phase = 2.0 * Math.PI * ToneFrequencyHz * sampleIndex / sampleRateHz;
            short value = (short)Math.Round(Math.Sin(phase) * ToneAmplitude * short.MaxValue);
            for (int channel = 0; channel < channels; channel++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset, 2), value);
                offset += 2;
            }

            sampleIndex++;
        }

        return frame;
    }

    /// <summary>
    /// Starts the capture stream, enqueues a 1 kHz tone for <paramref name="duration"/> at one frame per
    /// millisecond while draining the capture client, then drains the buffered tail for at most another
    /// 100 ms. The pacing keeps the persona's bounded microphone ring fed rather than overwriting it.
    /// </summary>
    /// <param name="capture">Opened capture stream.</param>
    /// <param name="enqueueFrame">Enqueues one frame into the persona (e.g. <c>device.SetMicrophonePcm</c>).</param>
    /// <param name="microphoneChannels">Persona microphone channel count.</param>
    /// <param name="microphoneSampleRateHz">Persona microphone sample rate in hertz.</param>
    /// <param name="duration">How long to enqueue tone frames.</param>
    /// <returns>The captured bytes in stream order.</returns>
    public static byte[] CaptureMicrophoneTone(
        WasapiCaptureEndpoint capture,
        Action<byte[]> enqueueFrame,
        int microphoneChannels,
        int microphoneSampleRateHz,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(enqueueFrame);
        int frameCount = (int)Math.Round(duration.TotalMilliseconds);
        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The microphone tone duration must be at least one millisecond.");
        }

        using MemoryStream captured = new MemoryStream();
        int sampleIndex = 0;
        capture.Start();
        try
        {
            Thread.Sleep(20);
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int frame = 0; frame < frameCount; frame++)
            {
                WaitUntilMilliseconds(stopwatch, frame);
                enqueueFrame(CreateMicrophoneToneFrame(microphoneChannels, microphoneSampleRateHz, ref sampleIndex));
                byte[] available = capture.DrainAvailable();
                captured.Write(available, 0, available.Length);
            }

            Stopwatch drain = Stopwatch.StartNew();
            while (drain.Elapsed < TimeSpan.FromMilliseconds(100))
            {
                byte[] available = capture.DrainAvailable();
                captured.Write(available, 0, available.Length);
                Thread.Sleep(1);
            }
        }
        finally
        {
            capture.Stop();
        }

        return captured.ToArray();
    }

    /// <summary>
    /// Opens the capture endpoint, enqueues the paced microphone tone and returns the captured PCM.
    /// Windows occasionally invalidates a freshly opened exclusive capture stream with
    /// <c>AUDCLNT_E_RESOURCES_INVALIDATED</c> while the audio service is still retiring the previous
    /// endpoint instance (the tests reuse the same MMDevice ids across attach cycles); the capture
    /// endpoint open and the whole paced capture are each retried exactly once, and the capture retry
    /// is disclosed through <see cref="WasapiMicrophoneCapture.Attempt"/>. Every other failure
    /// propagates.
    /// </summary>
    /// <param name="endpointId">Device id of the capture endpoint.</param>
    /// <param name="exactFormat">Exact persona microphone format to try exclusively first.</param>
    /// <param name="enqueueFrame">Enqueues one frame into the persona.</param>
    /// <param name="microphoneChannels">Persona microphone channel count.</param>
    /// <param name="microphoneSampleRateHz">Persona microphone sample rate in hertz.</param>
    /// <param name="duration">How long to enqueue tone frames.</param>
    /// <returns>The capture attempt, bytes, analysis and negotiated stream details.</returns>
    public static WasapiMicrophoneCapture CaptureMicrophoneToneWithRetry(
        string endpointId,
        WasapiAudioFormat exactFormat,
        Action<byte[]> enqueueFrame,
        int microphoneChannels,
        int microphoneSampleRateHz,
        TimeSpan duration)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using (WasapiCaptureEndpoint capture = WasapiAudioEndpoint.OpenCaptureEndpointWithRetry(endpointId, exactFormat))
            {
                TestContext.Out.WriteLine($"Capture endpoint opened (attempt {attempt}): {DescribeStream(capture)}.");
                try
                {
                    byte[] captured = CaptureMicrophoneTone(
                        capture,
                        enqueueFrame,
                        microphoneChannels,
                        microphoneSampleRateHz,
                        duration);
                    WasapiCaptureSummary summary = AnalyzeCapture(captured, capture.Format);
                    return new WasapiMicrophoneCapture(
                        attempt,
                        captured,
                        summary,
                        capture.ShareMode,
                        capture.Format,
                        capture.NegotiationNote);
                }
                catch (WasapiException exception) when (attempt == 1 && exception.HResult == AudioClientResourcesInvalidatedHResult)
                {
                    TestContext.Out.WriteLine(
                        $"The capture stream was invalidated by the audio service (HRESULT 0x{exception.HResult:X8}); "
                        + "reopening the endpoint and retrying the paced microphone capture once.");
                    Thread.Sleep(EndpointTeardownSettleTime);
                }
            }
        }

        throw new InvalidOperationException("The microphone capture retry loop ended without a result.");
    }

    /// <summary>
    /// Counts the samples of captured PCM and its peak magnitude in the negotiated format. A nonzero
    /// count is the capture-lane assertion: the persona's queued microphone tone reached the host.
    /// </summary>
    /// <param name="data">Captured bytes in the stream's negotiated format.</param>
    /// <param name="format">Negotiated capture format.</param>
    /// <returns>Total samples, nonzero samples and peak magnitude (1.0 is full scale).</returns>
    public static WasapiCaptureSummary AnalyzeCapture(ReadOnlySpan<byte> data, WasapiAudioFormat format)
    {
        int bytesPerSample = format.BitsPerSample / 8;
        int sampleCount = data.Length / bytesPerSample;
        int nonZeroSamples = 0;
        double peak = 0;
        for (int index = 0; index < sampleCount; index++)
        {
            double value = ReadSample(data.Slice(index * bytesPerSample, bytesPerSample), format);
            if (value != 0.0)
            {
                nonZeroSamples++;
            }

            double magnitude = Math.Abs(value);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return new WasapiCaptureSummary(sampleCount, nonZeroSamples, peak);
    }

    /// <summary>
    /// Estimates the frequency and continuity of a captured tone from the positive-going zero
    /// crossings of its first channel. Intervals between crossings of a continuous tone are one
    /// period; a silence gap adds whole periods to one interval, so the median period stays robust
    /// while the maximum interval exposes dropped frames. Only the active tone region produces
    /// crossings: leading and trailing silence contributes none. This is the live continuity
    /// assertion of the microphone lane (a nonzero/peak check alone accepts a fragmented tone).
    /// </summary>
    /// <param name="data">Captured bytes in the stream's negotiated format.</param>
    /// <param name="format">Negotiated capture format.</param>
    /// <returns>Estimated frequency, median/maximum crossing intervals and the crossing count.</returns>
    public static WasapiToneContinuity AnalyzeToneContinuity(ReadOnlySpan<byte> data, WasapiAudioFormat format)
    {
        if (format.Channels <= 0 || format.BitsPerSample <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "The capture format must declare channels and a sample size.");
        }

        int bytesPerSample = format.BitsPerSample / 8;
        int frameBytes = bytesPerSample * format.Channels;
        int sampleCount = data.Length / frameBytes;
        List<int> crossings = [];
        int lastNonZeroSign = 0;
        for (int frame = 0; frame < sampleCount; frame++)
        {
            double value = ReadSample(data.Slice(frame * frameBytes, bytesPerSample), format);
            if (value > 0.0)
            {
                if (lastNonZeroSign < 0)
                {
                    crossings.Add(frame);
                }

                lastNonZeroSign = 1;
            }
            else if (value < 0.0)
            {
                lastNonZeroSign = -1;
            }
        }

        if (crossings.Count < 2)
        {
            return new WasapiToneContinuity(0, 0, 0, crossings.Count);
        }

        List<int> intervals = [];
        for (int index = 1; index < crossings.Count; index++)
        {
            intervals.Add(crossings[index] - crossings[index - 1]);
        }

        intervals.Sort();
        int medianInterval = intervals[intervals.Count / 2];
        int maximumInterval = intervals[^1];
        double estimatedFrequency = medianInterval > 0 ? (double)format.SampleRateHz / medianInterval : 0;
        return new WasapiToneContinuity(estimatedFrequency, medianInterval, maximumInterval, crossings.Count);
    }

    /// <summary>Reads one sample in the negotiated format as a normalized double (-1.0 to 1.0).</summary>
    /// <param name="bytes">Sample bytes.</param>
    /// <param name="format">Negotiated stream format.</param>
    /// <returns>The sample value.</returns>
    private static double ReadSample(ReadOnlySpan<byte> bytes, WasapiAudioFormat format)
    {
        if (format.IsFloat)
        {
            return BinaryPrimitives.ReadSingleLittleEndian(bytes);
        }

        return format.BitsPerSample == 16
            ? BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768.0
            : BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648.0;
    }

    /// <summary>
    /// Asserts that a captured microphone tone is a continuous 1 kHz signal, not merely nonzero:
    /// enough positive-going zero crossings, a median crossing interval that converts to 1 kHz
    /// within 5%, and no interval longer than 2.5 periods (a dropped frame or silence gap shows up
    /// as a longer interval). The exact tolerance is printed for the live report.
    /// </summary>
    /// <param name="result">Paced microphone capture to analyze.</param>
    /// <param name="personaName">Display name of the persona, for failure messages.</param>
    public static void AssertContinuousTone(WasapiMicrophoneCapture result, string personaName)
    {
        ArgumentNullException.ThrowIfNull(result);
        WasapiToneContinuity continuity = AnalyzeToneContinuity(result.Data, result.Format);
        TestContext.Out.WriteLine(
            $"Microphone tone continuity ({personaName}, {result.Format}): {continuity.CrossingCount} positive zero crossing(s), "
            + $"median {continuity.MedianPeriodSamples} sample(s), maximum {continuity.MaximumPeriodSamples} sample(s), "
            + $"estimated {continuity.EstimatedFrequencyHz:0.0} Hz.");
        Assert.That(continuity.CrossingCount, Is.GreaterThanOrEqualTo(15),
            $"The captured {personaName} microphone tone has only {continuity.CrossingCount} positive zero crossing(s); "
            + "it is too short or not a continuous tone.");
        Assert.That(continuity.EstimatedFrequencyHz, Is.EqualTo(ToneFrequencyHz).Within(ToneFrequencyHz * 0.05),
            $"The captured {personaName} microphone tone measures {continuity.EstimatedFrequencyHz:0.0} Hz from its median "
            + $"zero-crossing interval ({continuity.MedianPeriodSamples} samples); expected 1000 Hz +/- 5%.");
        Assert.That((double)continuity.MaximumPeriodSamples, Is.LessThanOrEqualTo(continuity.MedianPeriodSamples * 2.5),
            $"The captured {personaName} microphone tone has a {continuity.MaximumPeriodSamples}-sample gap between crossings "
            + $"against a {continuity.MedianPeriodSamples}-sample median period; the tone is not continuous.");
    }

    /// <summary>Polls until every active endpoint is part of <paramref name="baseline"/> again.</summary>
    /// <param name="baseline">Endpoint set captured before the failed attach.</param>
    /// <param name="timeout">How long to poll.</param>
    /// <returns><see langword="true"/> when no endpoint outside the baseline remains active.</returns>
    private static bool WaitForNoExtraEndpoints(WasapiEndpointSnapshot baseline, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            WasapiEndpointSnapshot current = WasapiAudioEndpoint.CaptureActiveEndpoints();
            bool extraEndpointPresent = false;
            foreach (WasapiEndpointInfo endpoint in current.RenderEndpoints)
            {
                if (!baseline.ContainsEndpoint(endpoint.Id))
                {
                    extraEndpointPresent = true;
                    break;
                }
            }

            if (!extraEndpointPresent)
            {
                foreach (WasapiEndpointInfo endpoint in current.CaptureEndpoints)
                {
                    if (!baseline.ContainsEndpoint(endpoint.Id))
                    {
                        extraEndpointPresent = true;
                        break;
                    }
                }
            }

            if (!extraEndpointPresent)
            {
                return true;
            }

            Thread.Sleep(100);
        } while (stopwatch.Elapsed < timeout);

        return false;
    }

    /// <summary>Describes one endpoint list for a failure message.</summary>
    private static string DescribeEndpoints(IReadOnlyList<WasapiEndpointInfo> endpoints)
    {
        if (endpoints.Count == 0)
        {
            return "(none)";
        }

        List<string> descriptions = [];
        foreach (WasapiEndpointInfo endpoint in endpoints)
        {
            descriptions.Add($"\"{endpoint.FriendlyName}\" [{endpoint.Id}]");
        }

        return string.Join("; ", descriptions);
    }

    /// <summary>Busy-waits until the stopwatch reaches the frame's one-millisecond deadline.</summary>
    private static void WaitUntilMilliseconds(Stopwatch stopwatch, double milliseconds)
    {
        while (stopwatch.Elapsed.TotalMilliseconds < milliseconds)
        {
            Thread.SpinWait(100);
        }
    }
}

/// <summary>The render and capture endpoints one attach created, identified by their new device ids.</summary>
/// <param name="RenderEndpoint">New render endpoint (persona speaker).</param>
/// <param name="CaptureEndpoint">New capture endpoint (persona microphone).</param>
public sealed record WasapiEndpointPair(WasapiEndpointInfo RenderEndpoint, WasapiEndpointInfo CaptureEndpoint);

/// <summary>
/// Records the packets of a persona's <c>AudioOutReceived</c> event. The handler copies the event
/// memory before analyzing or retaining it (ruling R2), so the assertions read a stable buffer after
/// the callback returned. The recorder parses the persona's wire format (S16LE), not the negotiated
/// WASAPI mix format: the audio engine converts to the endpoint's wire format before USB delivery.
/// </summary>
public sealed class AudioOutRecorder
{
    private readonly object _gate = new object();
    private readonly int _channels;
    private readonly bool _hasHaptics;
    private readonly int _frameBytes;
    private long _packetCount;
    private long _byteCount;
    private int _minimumPacketBytes = int.MaxValue;
    private int _maximumPacketBytes;
    private int _frontPeak;
    private int _hapticsPeak;
    private byte[] _lastPacket = Array.Empty<byte>();

    /// <summary>Creates a recorder for a persona's wire format.</summary>
    /// <param name="channels">Speaker channel count of the wire format (4 for DualSense, 2 for DualShock 4).</param>
    /// <param name="hasHaptics"><see langword="true"/> when channels 2/3 carry the DualSense haptics lane.</param>
    public AudioOutRecorder(int channels, bool hasHaptics)
    {
        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "The recorder needs at least one channel.");
        }

        _channels = channels;
        _hasHaptics = hasHaptics && channels >= 4;
        _frameBytes = channels * 2;
    }

    /// <summary>Number of AudioOutReceived packets observed so far.</summary>
    public long PacketCount
    {
        get
        {
            lock (_gate)
            {
                return _packetCount;
            }
        }
    }

    /// <summary>
    /// Copies and analyzes one packet inside the event callback, then updates the aggregate counters.
    /// The callback owns no view over the persona's scratch buffer after it returns.
    /// </summary>
    /// <param name="packet">Packet view the persona raised for the ISO OUT endpoint.</param>
    public void OnAudioOut(ReadOnlyMemory<byte> packet)
    {
        byte[] copy = packet.ToArray();
        int frontPeak = 0;
        int hapticsPeak = 0;
        int wholeFrames = copy.Length / _frameBytes;
        for (int frame = 0; frame < wholeFrames; frame++)
        {
            int offset = frame * _frameBytes;
            frontPeak = Math.Max(frontPeak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(copy.AsSpan(offset, 2))));
            frontPeak = Math.Max(frontPeak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(copy.AsSpan(offset + 2, 2))));
            if (_hasHaptics)
            {
                hapticsPeak = Math.Max(hapticsPeak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(copy.AsSpan(offset + 4, 2))));
                hapticsPeak = Math.Max(hapticsPeak, Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(copy.AsSpan(offset + 6, 2))));
            }
        }

        lock (_gate)
        {
            _packetCount++;
            _byteCount += copy.Length;
            _minimumPacketBytes = Math.Min(_minimumPacketBytes, copy.Length);
            _maximumPacketBytes = Math.Max(_maximumPacketBytes, copy.Length);
            _frontPeak = Math.Max(_frontPeak, frontPeak);
            _hapticsPeak = Math.Max(_hapticsPeak, hapticsPeak);
            _lastPacket = copy;
        }
    }

    /// <summary>Takes a consistent snapshot of the aggregate counters and the last packet copy.</summary>
    /// <returns>The observed packet statistics.</returns>
    public AudioOutSummary Snapshot()
    {
        lock (_gate)
        {
            int minimum = _packetCount == 0 ? 0 : _minimumPacketBytes;
            return new AudioOutSummary(
                _packetCount,
                _byteCount,
                minimum,
                _maximumPacketBytes,
                _frontPeak,
                _hapticsPeak,
                _lastPacket,
                _channels);
        }
    }
}

/// <summary>Aggregate statistics of the packets one render test observed.</summary>
/// <param name="PacketCount">Number of AudioOutReceived packets.</param>
/// <param name="ByteCount">Total packet bytes.</param>
/// <param name="MinimumPacketBytes">Smallest packet size, or zero when no packet arrived.</param>
/// <param name="MaximumPacketBytes">Largest packet size, or zero when no packet arrived.</param>
/// <param name="FrontPeak">Peak absolute sample of the front channels (0/1).</param>
/// <param name="HapticsPeak">Peak absolute sample of channels 2/3, or zero for a persona without haptics.</param>
/// <param name="LastPacket">Copy of the last packet, for diagnostics.</param>
/// <param name="Channels">Speaker channel count of the wire format.</param>
public readonly record struct AudioOutSummary(
    long PacketCount,
    long ByteCount,
    int MinimumPacketBytes,
    int MaximumPacketBytes,
    int FrontPeak,
    int HapticsPeak,
    byte[] LastPacket,
    int Channels);

/// <summary>Result of the paced microphone capture, including the retry disclosure.</summary>
/// <param name="Attempt">Attempt number: 1 when the first stream stayed valid, 2 after a retry.</param>
/// <param name="Data">Captured bytes in <paramref name="Format"/>.</param>
/// <param name="Summary">Sample statistics of <paramref name="Data"/>.</param>
/// <param name="ShareMode">Share mode of the capture stream that produced <paramref name="Data"/>.</param>
/// <param name="Format">Negotiated capture format.</param>
/// <param name="NegotiationNote">Exclusive/shared negotiation detail for the report.</param>
public sealed record WasapiMicrophoneCapture(
    int Attempt,
    byte[] Data,
    WasapiCaptureSummary Summary,
    WasapiShareMode ShareMode,
    WasapiAudioFormat Format,
    string NegotiationNote);

/// <summary>Statistics of one captured PCM buffer.</summary>
/// <param name="TotalSamples">Number of whole samples in the buffer.</param>
/// <param name="NonZeroSamples">Number of samples whose value is not zero.</param>
/// <param name="Peak">Peak absolute sample magnitude (1.0 is full scale).</param>
public readonly record struct WasapiCaptureSummary(int TotalSamples, int NonZeroSamples, double Peak);

/// <summary>
/// Tone continuity and frequency estimated from the positive-going zero crossings of a captured
/// stream's first channel.
/// </summary>
/// <param name="EstimatedFrequencyHz">Sample rate divided by the median crossing interval; zero when there are fewer than two crossings.</param>
/// <param name="MedianPeriodSamples">Median interval between positive-going zero crossings, in samples.</param>
/// <param name="MaximumPeriodSamples">Largest interval between positive-going zero crossings, in samples.</param>
/// <param name="CrossingCount">Number of positive-going zero crossings observed.</param>
public readonly record struct WasapiToneContinuity(
    double EstimatedFrequencyHz,
    int MedianPeriodSamples,
    int MaximumPeriodSamples,
    int CrossingCount);