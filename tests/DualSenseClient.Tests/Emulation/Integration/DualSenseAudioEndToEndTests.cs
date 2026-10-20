using DualSenseClient.Hid;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// End-to-end integration tests of the DualSense persona's UAC1 audio function over the whole
/// Emulation path, parameterized for the base DualSense (Sony 054C:0CE6, 321-byte HID report
/// descriptor) and the DualSense Edge (054C:0DF2, 437-byte report descriptor). The facade serves
/// the composite device (audio control interface 0, speaker
/// streaming interface 1 with isochronous OUT endpoint 0x01, microphone streaming interface 2 with
/// isochronous IN endpoint 0x82, HID interface 3) over USB/IP; the production attach strategy attaches
/// it to this machine; Windows creates a render and a capture endpoint for it. The tests identify the
/// new endpoints by device id (never by friendly name, because a physical DualSense may be connected),
/// open them through <see cref="WasapiAudioEndpoint"/> (exclusive with the exact 4 ch/2 ch S16LE
/// 48 kHz formats, or shared with the disclosed mix format), render a 1 kHz tone and assert the
/// persona's <see cref="DualSenseDevice.AudioOutReceived"/> lane carries nonzero front channels and
/// (when four channels were rendered) nonzero haptics channels, enqueue a paced 1 kHz microphone tone
/// and assert the host capture endpoint receives nonzero samples, detach and assert the endpoints
/// disappear, and finally assert that HID interface 3 still enumerates and returns input report 0x01
/// after the composite switch. Gated behind EMULATION_INTEGRATION=1: without the environment variable
/// every test is ignored (CI and normal runs never attach devices to the machine). An attach failure -
/// e.g. no usbip-win2 installation - is a failure signal of these tests, not a skip. The shared
/// plumbing lives in <see cref="EmulationAudioEndpointSupport"/> and
/// <see cref="EmulationIntegrationSupport"/>.
/// </summary>
[TestFixture]
public sealed class DualSenseAudioEndToEndTests
{
    /// <summary>Vendor id of the DualSense persona (Sony).</summary>
    private const ushort SonyVendorId = 0x054C;

    /// <summary>Product id of the base DualSense persona (the reference's base mode).</summary>
    private const ushort DualSenseProductId = 0x0CE6;

    /// <summary>Product id of the DualSense Edge persona (the reference's Edge mode).</summary>
    private const ushort DualSenseEdgeProductId = 0x0DF2;

    /// <summary>Product string the base persona serves.</summary>
    private const string DualSenseProductName = "DualSense Wireless Controller";

    /// <summary>Product string the Edge persona serves.</summary>
    private const string DualSenseEdgeProductName = "DualSense Edge Wireless Controller";

    /// <summary>Size of the DualSense input report including its report id.</summary>
    private const int InputReportSize = 64;

    /// <summary>Report id of the main DualSense input report.</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Speaker wire format of the persona: four interleaved 16-bit channels at 48 kHz.</summary>
    private const int SpeakerChannels = 4;

    /// <summary>Nominal speaker packet length: 48 one-millisecond frames of four 16-bit channels.</summary>
    private const int RenderNominalPacketBytes = 384;

    /// <summary>wMaxPacketSize of the speaker endpoint (392 bytes).</summary>
    private const int RenderMaximumPacketBytes = 392;

    /// <summary>
    /// Minimum packet count a 250 ms tone must produce before the tone assertions run. The tone fills
    /// roughly 250 one-millisecond packets; 200 allows service jitter without accepting a mostly dropped tone.
    /// </summary>
    private const long MinimumTonePackets = 200;

    /// <summary>Microphone channel count of the persona's wire format.</summary>
    private const int MicrophoneChannels = 2;

    /// <summary>Microphone sample rate of the persona's wire format.</summary>
    private const int MicrophoneSampleRateHz = 48000;

    /// <summary>Display name of the persona under test.</summary>
    /// <param name="edge">Whether the Edge persona is under test.</param>
    /// <returns>The persona display name used in output and failure messages.</returns>
    private static string PersonaName(bool edge) => edge ? "DualSense Edge" : "DualSense";

    /// <summary>Product id the persona under test serves.</summary>
    /// <param name="edge">Whether the Edge persona is under test.</param>
    /// <returns>The persona's product id.</returns>
    private static ushort PersonaProductId(bool edge) => edge ? DualSenseEdgeProductId : DualSenseProductId;

    /// <summary>Product string the persona under test serves.</summary>
    /// <param name="edge">Whether the Edge persona is under test.</param>
    /// <returns>The persona's product string.</returns>
    private static string PersonaProductName(bool edge) => edge ? DualSenseEdgeProductName : DualSenseProductName;

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DualSenseAudio_Attach_ExposesRenderAndCaptureEndpoints(bool edge)
    {
        string personaName = PersonaName(edge);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualSenseDevice device = server.AddDualSense(edge);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);

            using (WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                       endpoints.RenderEndpoint.Id,
                       EmulationAudioEndpointSupport.DualSenseRenderFormat))
            {
                TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");
            }

            using (WasapiCaptureEndpoint capture = WasapiAudioEndpoint.OpenCaptureEndpoint(
                       endpoints.CaptureEndpoint.Id,
                       EmulationAudioEndpointSupport.DualSenseCaptureFormat))
            {
                TestContext.Out.WriteLine($"Capture endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(capture)}.");
            }

            detached = EmulationAudioEndpointSupport.DetachOrFail(server, device, personaName);
            TestContext.Out.WriteLine($"Detached bus id \"{busId}\".");
            EmulationAudioEndpointSupport.AssertEndpointsRemoved(endpoints, personaName);
        }
        catch (Exception failure)
        {
            EmulationAudioEndpointSupport.CleanupAfterFailure(server, device, detached, busId, failure, baseline);
            throw;
        }

        Assert.That(detached, Is.True, "The test ended without detaching the controller; a vhci port may still be held "
                                       + $"by bus id \"{busId}\".");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DualSenseAudio_RenderedTone_ReachesAudioOutReceived(bool edge)
    {
        string personaName = PersonaName(edge);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualSenseDevice device = server.AddDualSense(edge);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);
            using (WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                       endpoints.RenderEndpoint.Id,
                       EmulationAudioEndpointSupport.DualSenseRenderFormat))
            {
                TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");
                AudioOutRecorder recorder = new AudioOutRecorder(SpeakerChannels, true);
                device.AudioOutReceived += recorder.OnAudioOut;
                try
                {
                    render.PlayTone(
                        EmulationAudioEndpointSupport.ToneFrequencyHz,
                        EmulationAudioEndpointSupport.ToneDuration,
                        EmulationAudioEndpointSupport.ToneAmplitude);
                    bool received = EmulationAudioEndpointSupport.WaitForAudioPackets(
                        recorder,
                        MinimumTonePackets,
                        EmulationAudioEndpointSupport.AudioOutDrainTimeout);
                    AudioOutSummary summary = recorder.Snapshot();
                    TestContext.Out.WriteLine(
                        $"Audio OUT: {summary.PacketCount} packet(s), {summary.ByteCount} byte(s), "
                        + $"sizes {summary.MinimumPacketBytes}..{summary.MaximumPacketBytes} byte(s), "
                        + $"front peak {summary.FrontPeak}, haptics peak {summary.HapticsPeak}; "
                        + $"last packet {EmulationAudioEndpointSupport.DescribePacket(summary.LastPacket)}.");

                    Assert.That(received, Is.True, $"No AudioOutReceived packets arrived within "
                                                   + $"{EmulationAudioEndpointSupport.AudioOutDrainTimeout.TotalSeconds:0} s after rendering the tone.");
                    Assert.That(summary.PacketCount, Is.GreaterThanOrEqualTo(MinimumTonePackets),
                        $"The rendered tone produced only {summary.PacketCount} speaker packet(s).");
                    Assert.That(summary.MinimumPacketBytes, Is.GreaterThan(0), "An empty speaker packet was delivered.");
                    Assert.That(summary.MaximumPacketBytes, Is.LessThanOrEqualTo(RenderMaximumPacketBytes),
                        $"A speaker packet exceeded the endpoint's {RenderMaximumPacketBytes}-byte maximum.");
                    Assert.That(summary.ByteCount, Is.GreaterThanOrEqualTo(summary.PacketCount * RenderNominalPacketBytes / 2),
                        $"The speaker packets carried {summary.ByteCount} byte(s), fewer than half the nominal "
                        + $"{RenderNominalPacketBytes} bytes per packet.");
                    Assert.That(summary.ByteCount, Is.LessThanOrEqualTo(summary.PacketCount * RenderMaximumPacketBytes),
                        $"The speaker packets carried {summary.ByteCount} byte(s), more than {summary.PacketCount} maximum-size packets.");
                    Assert.That(summary.FrontPeak, Is.GreaterThan(0),
                        "The front channels of the AudioOutReceived packets are silent; the rendered tone did not reach the persona's speaker lane.");

                    if (render.Format.Channels >= 4)
                    {
                        Assert.That(summary.HapticsPeak, Is.GreaterThan(0),
                            "The haptics channels (2/3) of the 4-channel speaker packets are silent even though a 4-channel format was rendered.");
                    }
                    else
                    {
                        TestContext.Out.WriteLine(
                            $"The endpoint negotiated {render.Format.Channels} channel(s), so the 4-channel haptics assertion is not applicable; "
                            + "the manual owner checklist keeps the 4-channel haptics verification.");
                    }
                }
                finally
                {
                    device.AudioOutReceived -= recorder.OnAudioOut;
                }
            }

            detached = EmulationAudioEndpointSupport.DetachOrFail(server, device, personaName);
            TestContext.Out.WriteLine($"Detached bus id \"{busId}\".");
            EmulationAudioEndpointSupport.AssertEndpointsRemoved(endpoints, personaName);
        }
        catch (Exception failure)
        {
            EmulationAudioEndpointSupport.CleanupAfterFailure(server, device, detached, busId, failure, baseline);
            throw;
        }

        Assert.That(detached, Is.True, "The test ended without detaching the controller; a vhci port may still be held "
                                       + $"by bus id \"{busId}\".");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DualSenseAudio_MicrophonePcm_ReachesCaptureEndpoint(bool edge)
    {
        string personaName = PersonaName(edge);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualSenseDevice device = server.AddDualSense(edge);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);
            TestContext.Out.WriteLine($"Enqueuing {EmulationAudioEndpointSupport.MicrophoneToneMilliseconds} microphone frame(s) at 1 ms pacing "
                                      + "so the persona's 16-frame ring is not overwritten in one burst.");
            WasapiMicrophoneCapture result = EmulationAudioEndpointSupport.CaptureMicrophoneToneWithRetry(
                endpoints.CaptureEndpoint.Id,
                EmulationAudioEndpointSupport.DualSenseCaptureFormat,
                frame => device.SetMicrophonePcm(frame),
                MicrophoneChannels,
                MicrophoneSampleRateHz,
                TimeSpan.FromMilliseconds(EmulationAudioEndpointSupport.MicrophoneToneMilliseconds));
            TestContext.Out.WriteLine($"Captured {result.Data.Length} byte(s) on attempt {result.Attempt} "
                                      + $"({result.ShareMode} mode, {result.Format}): {result.Summary.TotalSamples} sample(s), "
                                      + $"{result.Summary.NonZeroSamples} non-zero sample(s), peak {result.Summary.Peak:0.000}.");

            Assert.That(result.Data.Length, Is.GreaterThan(0),
                "The capture endpoint delivered no packet while the microphone tone was queued.");
            Assert.That(result.Summary.TotalSamples, Is.GreaterThan(0), "The capture endpoint delivered no whole sample.");
            Assert.That(result.Summary.NonZeroSamples, Is.GreaterThan(0),
                "Every captured sample is zero; the queued microphone tone did not reach the host capture endpoint.");
            EmulationAudioEndpointSupport.AssertContinuousTone(result, personaName);

            detached = EmulationAudioEndpointSupport.DetachOrFail(server, device, personaName);
            TestContext.Out.WriteLine($"Detached bus id \"{busId}\".");
            EmulationAudioEndpointSupport.AssertEndpointsRemoved(endpoints, personaName);
        }
        catch (Exception failure)
        {
            EmulationAudioEndpointSupport.CleanupAfterFailure(server, device, detached, busId, failure, baseline);
            throw;
        }

        Assert.That(detached, Is.True, "The test ended without detaching the controller; a vhci port may still be held "
                                       + $"by bus id \"{busId}\".");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DualSenseAudio_Detach_RemovesEndpoints(bool edge)
    {
        string personaName = PersonaName(edge);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualSenseDevice device = server.AddDualSense(edge);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);
            using (WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                       endpoints.RenderEndpoint.Id,
                       EmulationAudioEndpointSupport.DualSenseRenderFormat))
            {
                TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");
                render.PlayTone(
                    EmulationAudioEndpointSupport.ToneFrequencyHz,
                    TimeSpan.FromMilliseconds(100),
                    EmulationAudioEndpointSupport.ToneAmplitude);
            }

            using (WasapiCaptureEndpoint capture = WasapiAudioEndpoint.OpenCaptureEndpoint(
                       endpoints.CaptureEndpoint.Id,
                       EmulationAudioEndpointSupport.DualSenseCaptureFormat))
            {
                TestContext.Out.WriteLine($"Capture endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(capture)}.");
                capture.Start();
                Thread.Sleep(50);
                capture.Stop();
            }

            detached = EmulationAudioEndpointSupport.DetachOrFail(server, device, personaName);
            TestContext.Out.WriteLine($"Detached bus id \"{busId}\" after streaming from both endpoints.");
            EmulationAudioEndpointSupport.AssertEndpointsRemoved(endpoints, personaName);
        }
        catch (Exception failure)
        {
            EmulationAudioEndpointSupport.CleanupAfterFailure(server, device, detached, busId, failure, baseline);
            throw;
        }

        Assert.That(detached, Is.True, "The test ended without detaching the controller; a vhci port may still be held "
                                       + $"by bus id \"{busId}\".");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DualSenseHid_StillEnumeratesAndReportsAfterCompositeSwitch(bool edge)
    {
        string personaName = PersonaName(edge);
        ushort productId = PersonaProductId(edge);
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        HashSet<string> baselinePaths = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, productId);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"HID enumeration before attach: {baselinePaths.Count} {personaName} device(s).");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualSenseDevice device = server.AddDualSense(edge);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            IHidDeviceInfo controller = EmulationIntegrationSupport.WaitForNewDevice(
                                            enumerator,
                                            baselinePaths,
                                            SonyVendorId,
                                            productId,
                                            EmulationIntegrationSupport.DeviceEnumerationTimeout)
                                        ?? throw new AssertionException(
                                            $"No new {personaName} HID device enumerated within "
                                            + $"{EmulationIntegrationSupport.DeviceEnumerationTimeout.TotalSeconds:0} s; "
                                            + $"attach diagnostics: {attachError}");
            TestContext.Out.WriteLine($"HID interface {controller.InterfaceNumber} enumerated at {controller.Path} as \"{controller.ProductName}\".");
            Assert.That(controller.InterfaceNumber, Is.EqualTo(3),
                "The composite persona must expose its HID interface as interface 3.");
            Assert.That(controller.VendorId, Is.EqualTo(SonyVendorId));
            Assert.That(controller.ProductId, Is.EqualTo(productId));
            Assert.That(controller.ProductName, Is.EqualTo(PersonaProductName(edge)),
                "The product string served by the persona did not reach the host.");

            string devicePath = controller.Path;
            string lastReportHex = "(no report received)";
            using (IHidDevice hid = enumerator.OpenDevice(devicePath))
            {
                byte[] report = new byte[InputReportSize];
                bool received = EmulationIntegrationSupport.WaitForInputReport(
                    hid,
                    report,
                    (data, length) => length == InputReportSize && data[0] == InputReportId,
                    ref lastReportHex,
                    EmulationIntegrationSupport.InputReportTimeout);
                Assert.That(received, Is.True, $"No 64-byte input report 0x01 arrived from HID interface 3 within "
                                               + $"{EmulationIntegrationSupport.InputReportTimeout.TotalSeconds:0} s; last report: {lastReportHex}");
                TestContext.Out.WriteLine($"HID input report 0x01 after the composite switch: {lastReportHex}.");
            }

            detached = EmulationAudioEndpointSupport.DetachOrFail(server, device, personaName);
            TestContext.Out.WriteLine($"Detached bus id \"{busId}\".");
            bool disappeared = EmulationIntegrationSupport.WaitForDeviceDisappearance(
                enumerator,
                SonyVendorId,
                productId,
                devicePath,
                EmulationIntegrationSupport.DeviceDisappearanceTimeout);
            Assert.That(disappeared, Is.True, $"The {personaName} HID device at {devicePath} was still enumerated "
                                              + $"{EmulationIntegrationSupport.DeviceDisappearanceTimeout.TotalSeconds:0} s after detach.");
            TestContext.Out.WriteLine($"The {personaName} HID device disappeared after detach.");
        }
        catch (Exception failure)
        {
            EmulationAudioEndpointSupport.CleanupAfterFailure(server, device, detached, busId, failure, baseline);
            throw;
        }

        Assert.That(detached, Is.True, "The test ended without detaching the controller; a vhci port may still be held "
                                       + $"by bus id \"{busId}\".");
    }
}