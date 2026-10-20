using DualSenseClient.Hid;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualShock4;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// End-to-end integration tests of the DualShock 4 persona's UAC1 audio function over the whole
/// Emulation path, parameterized for v1 (Sony 054C:05C4, 467-byte HID report descriptor) and v2
/// (054C:09CC, 507-byte report descriptor). Both variants serve the same derived 184-byte UAC1
/// audio block (2 ch 32 kHz render, 1 ch 16 kHz capture, no haptics lane). The facade serves the
/// composite device (audio control interface 0, speaker
/// streaming interface 1 with isochronous OUT endpoint 0x01, microphone streaming interface 2 with
/// isochronous IN endpoint 0x82, HID interface 3) over USB/IP; the production attach strategy attaches
/// it to this machine; Windows creates a render and a capture endpoint for it. The tests identify the
/// new endpoints by device id (never by friendly name, because a physical DualShock 4 may be
/// connected), open them through <see cref="WasapiAudioEndpoint"/> (exclusive with the exact 2 ch
/// S16LE 32 kHz render and 1 ch S16LE 16 kHz capture formats, or shared with the disclosed mix
/// format), render a 1 kHz stereo tone and assert the persona's
/// <see cref="DualShock4Device.AudioOutReceived"/> lane carries nonzero front channels (the DualShock 4
/// has no haptics lane), enqueue a paced 1 kHz microphone tone and assert the host capture endpoint
/// receives nonzero samples, detach and assert the endpoints disappear, and finally assert that HID
/// interface 3 still enumerates and returns input report 0x01 after the composite switch. Gated behind
/// EMULATION_INTEGRATION=1: without the environment variable every test is ignored (CI and normal runs
/// never attach devices to the machine). An attach failure - e.g. no usbip-win2 installation - is a
/// failure signal of these tests, not a skip. The shared plumbing lives in
/// <see cref="EmulationAudioEndpointSupport"/> and <see cref="EmulationIntegrationSupport"/>.
/// </summary>
[TestFixture]
public sealed class DualShock4AudioEndToEndTests
{
    /// <summary>Vendor id of the DualShock 4 persona (Sony).</summary>
    private const ushort SonyVendorId = 0x054C;

    /// <summary>Product id of the DualShock 4 v1 persona (CUH-ZCT1).</summary>
    private const ushort DualShock4V1ProductId = 0x05C4;

    /// <summary>Product id of the DualShock 4 v2 persona (CUH-ZCT2).</summary>
    private const ushort DualShock4V2ProductId = 0x09CC;

    /// <summary>Product string both variants serve.</summary>
    private const string DualShock4ProductName = "Wireless Controller";

    /// <summary>Size of the DualShock 4 input report including its report id.</summary>
    private const int InputReportSize = 64;

    /// <summary>Report id of the main DualShock 4 input report.</summary>
    private const byte InputReportId = 0x01;

    /// <summary>Speaker wire format of the persona: two interleaved 16-bit channels at 32 kHz.</summary>
    private const int SpeakerChannels = 2;

    /// <summary>Nominal speaker packet length: 32 one-millisecond frames of two 16-bit channels.</summary>
    private const int RenderNominalPacketBytes = 128;

    /// <summary>wMaxPacketSize of the speaker endpoint (132 bytes).</summary>
    private const int RenderMaximumPacketBytes = 132;

    /// <summary>
    /// Minimum packet count a 250 ms tone must produce before the tone assertions run. The tone fills
    /// roughly 250 one-millisecond packets; 200 allows service jitter without accepting a mostly dropped tone.
    /// </summary>
    private const long MinimumTonePackets = 200;

    /// <summary>Microphone channel count of the persona's wire format.</summary>
    private const int MicrophoneChannels = 1;

    /// <summary>Microphone sample rate of the persona's wire format.</summary>
    private const int MicrophoneSampleRateHz = 16000;

    /// <summary>Display name of the persona under test.</summary>
    /// <param name="variant">Descriptor variant of the persona.</param>
    /// <returns>The persona display name used in output and failure messages.</returns>
    private static string PersonaName(DualShock4Variant variant) =>
        variant == DualShock4Variant.V1 ? "DualShock 4 v1" : "DualShock 4 v2";

    /// <summary>Product id the persona under test serves.</summary>
    /// <param name="variant">Descriptor variant of the persona.</param>
    /// <returns>The persona's product id.</returns>
    private static ushort PersonaProductId(DualShock4Variant variant) =>
        variant == DualShock4Variant.V1 ? DualShock4V1ProductId : DualShock4V2ProductId;

    /// <summary>Manufacturer string the persona under test serves (a variant-distinguishing fact).</summary>
    /// <param name="variant">Descriptor variant of the persona.</param>
    /// <returns>The persona's manufacturer string.</returns>
    private static string PersonaManufacturer(DualShock4Variant variant) =>
        variant == DualShock4Variant.V1 ? "Sony Computer Entertainment" : "Sony Interactive Entertainment";

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }
    }

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void DualShock4Audio_Attach_ExposesRenderAndCaptureEndpoints(DualShock4Variant variant)
    {
        string personaName = PersonaName(variant);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device device = server.AddDualShock4(variant);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);

            using (WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                       endpoints.RenderEndpoint.Id,
                       EmulationAudioEndpointSupport.DualShock4RenderFormat))
            {
                TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");
            }

            using (WasapiCaptureEndpoint capture = WasapiAudioEndpoint.OpenCaptureEndpointWithRetry(
                       endpoints.CaptureEndpoint.Id,
                       EmulationAudioEndpointSupport.DualShock4CaptureFormat))
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

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void DualShock4Audio_RenderedTone_ReachesAudioOutReceived(DualShock4Variant variant)
    {
        string personaName = PersonaName(variant);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device device = server.AddDualShock4(variant);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);
            using (WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                       endpoints.RenderEndpoint.Id,
                       EmulationAudioEndpointSupport.DualShock4RenderFormat))
            {
                TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");
                AudioOutRecorder recorder = new AudioOutRecorder(SpeakerChannels, false);
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
                        + $"front peak {summary.FrontPeak} (2 ch, no haptics lane); "
                        + $"last packet {EmulationAudioEndpointSupport.DescribePacket(summary.LastPacket)}.");

                    Assert.That(received, Is.True, $"No AudioOutReceived packets arrived within "
                                                   + $"{EmulationAudioEndpointSupport.AudioOutDrainTimeout.TotalSeconds:0} s after rendering the tone.");
                    Assert.That(summary.PacketCount, Is.GreaterThanOrEqualTo(MinimumTonePackets),
                        $"The rendered tone produced only {summary.PacketCount} speaker packet(s).");
                    Assert.That(summary.MinimumPacketBytes, Is.EqualTo(RenderNominalPacketBytes),
                        $"The DualShock 4's 2 ch, no-haptics-lane wire format must deliver {RenderNominalPacketBytes}-byte "
                        + $"packets; the smallest observed packet was {summary.MinimumPacketBytes} byte(s).");
                    Assert.That(summary.MaximumPacketBytes, Is.EqualTo(RenderNominalPacketBytes),
                        $"The DualShock 4's 2 ch, no-haptics-lane wire format must deliver {RenderNominalPacketBytes}-byte "
                        + $"packets; the largest observed packet was {summary.MaximumPacketBytes} byte(s).");
                    Assert.That(summary.ByteCount, Is.GreaterThanOrEqualTo(summary.PacketCount * RenderNominalPacketBytes / 2),
                        $"The speaker packets carried {summary.ByteCount} byte(s), fewer than half the nominal "
                        + $"{RenderNominalPacketBytes} bytes per packet.");
                    Assert.That(summary.ByteCount, Is.LessThanOrEqualTo(summary.PacketCount * RenderMaximumPacketBytes),
                        $"The speaker packets carried {summary.ByteCount} byte(s), more than {summary.PacketCount} maximum-size packets.");
                    Assert.That(summary.FrontPeak, Is.GreaterThan(0),
                        "The front channels of the AudioOutReceived packets are silent; the rendered tone did not reach the persona's speaker lane.");
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

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void DualShock4Audio_MicrophonePcm_ReachesCaptureEndpoint(DualShock4Variant variant)
    {
        string personaName = PersonaName(variant);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device device = server.AddDualShock4(variant);
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
                EmulationAudioEndpointSupport.DualShock4CaptureFormat,
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

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void DualShock4Audio_Detach_RemovesEndpoints(DualShock4Variant variant)
    {
        string personaName = PersonaName(variant);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"Active audio endpoints before attach: {EmulationAudioEndpointSupport.DescribeSnapshot(baseline)}.");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device device = server.AddDualShock4(variant);
        string busId = server.BusIds[^1];
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        TestContext.Out.WriteLine($"Attached bus id \"{busId}\".");

        bool detached = false;
        try
        {
            WasapiEndpointPair endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);
            using (WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                       endpoints.RenderEndpoint.Id,
                       EmulationAudioEndpointSupport.DualShock4RenderFormat))
            {
                TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");
                render.PlayTone(
                    EmulationAudioEndpointSupport.ToneFrequencyHz,
                    TimeSpan.FromMilliseconds(100),
                    EmulationAudioEndpointSupport.ToneAmplitude);
            }

            using (WasapiCaptureEndpoint capture = WasapiAudioEndpoint.OpenCaptureEndpointWithRetry(
                       endpoints.CaptureEndpoint.Id,
                       EmulationAudioEndpointSupport.DualShock4CaptureFormat))
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

    [TestCase(DualShock4Variant.V1)]
    [TestCase(DualShock4Variant.V2)]
    public void DualShock4Hid_StillEnumeratesAndReportsAfterCompositeSwitch(DualShock4Variant variant)
    {
        string personaName = PersonaName(variant);
        ushort productId = PersonaProductId(variant);
        using HidDeviceEnumerator enumerator = new HidDeviceEnumerator();
        HashSet<string> baselinePaths = EmulationIntegrationSupport.CollectDevicePaths(enumerator, SonyVendorId, productId);
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        TestContext.Out.WriteLine($"HID enumeration before attach: {baselinePaths.Count} DualShock 4 device(s).");

        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        TestContext.Out.WriteLine($"USB/IP server listening on {server.BoundEndPoint}.");
        DualShock4Device device = server.AddDualShock4(variant);
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
            Assert.That(controller.Manufacturer, Is.EqualTo(PersonaManufacturer(variant)),
                "The variant's manufacturer string did not reach the host.");
            Assert.That(controller.ProductName, Is.EqualTo(DualShock4ProductName),
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