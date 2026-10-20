using System.Diagnostics;
using DualSenseClient.Core.Utilities;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;

namespace DualSenseClient.Tests.Emulation.Integration;

/// <summary>
/// Gated regression of the virtual speaker's OUT cadence. It renders one second of audio to the
/// Emulation DualSense render endpoint in exclusive and shared (Windows default-playback) mode and
/// records when the persona receives the corresponding ISO OUT packets. Real-time operation is one
/// packet per millisecond; a host that is driven by packet completion rather than the USB frame
/// consumes the same packets in a fraction of a second, which is the audible speed-up this test
/// pins.
/// </summary>
[TestFixture]
public sealed class VirtualAudioClockTests
{
    private bool _timerResolutionRaised;

    [OneTimeSetUp]
    public void RequireIntegration()
    {
        if (Environment.GetEnvironmentVariable("EMULATION_INTEGRATION") != "1")
        {
            Assert.Ignore("Set EMULATION_INTEGRATION=1 to run USB/IP integration tests");
        }

        TimerResolution.AddRef();
        _timerResolutionRaised = true;
    }

    [OneTimeTearDown]
    public void RestoreTimerResolution()
    {
        if (_timerResolutionRaised)
        {
            TimerResolution.Release();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DualSenseVirtualSpeaker_RenderedSecond_ArrivesAtWireRate(bool preferShared)
    {
        const string personaName = "DualSense";
        WasapiEndpointSnapshot baseline = WasapiAudioEndpoint.CaptureActiveEndpoints();
        using EmulationServer server = EmulationAudioEndpointSupport.CreateServer();
        server.Start();
        DualSenseDevice device = server.AddDualSense(false);
        string? attachError = EmulationAudioEndpointSupport.AttachOrFail(server, device, personaName);
        WasapiEndpointPair? endpoints = null;
        try
        {
            endpoints = EmulationAudioEndpointSupport.WaitForEndpointPair(baseline, personaName, attachError);
            using WasapiRenderEndpoint render = WasapiAudioEndpoint.OpenRenderEndpoint(
                endpoints!.RenderEndpoint.Id,
                EmulationAudioEndpointSupport.DualSenseRenderFormat,
                preferShared);
            TestContext.Out.WriteLine($"Render endpoint opened: {EmulationAudioEndpointSupport.DescribeStream(render)}.");

            List<(double Seconds, int Bytes)> arrivals = [];
            Stopwatch clock = Stopwatch.StartNew();

            void Record(ReadOnlyMemory<byte> packet)
            {
                lock (arrivals)
                {
                    arrivals.Add((clock.Elapsed.TotalSeconds, packet.Length));
                }
            }

            device.AudioOutReceived += Record;
            try
            {
                render.PlayTone(
                    EmulationAudioEndpointSupport.ToneFrequencyHz,
                    TimeSpan.FromSeconds(1),
                    EmulationAudioEndpointSupport.ToneAmplitude);
                Thread.Sleep(1500);
            }
            finally
            {
                device.AudioOutReceived -= Record;
            }

            double[] times;
            lock (arrivals)
            {
                times = arrivals.Select(item => item.Seconds).ToArray();
            }

            Assert.That(times, Is.Not.Empty, "No ISO OUT packet arrived for the rendered second.");
            double first = times[0];
            double last = times[^1];
            double span = last - first;
            double rate = span > 0 ? (times.Length - 1) / span : double.PositiveInfinity;
            TestContext.Out.WriteLine(
                $"Probe: {times.Length} packet(s), first {first:F3} s, last {last:F3} s, "
                + $"span {span:F3} s, cadence {rate:F1} packet/s.");
            Assert.That(rate, Is.InRange(900, 1100),
                $"The virtual speaker consumed {rate:F1} packet/s instead of the 1000 packet/s wire rate.");
        }
        finally
        {
            EmulationAudioEndpointSupport.DetachOrFail(server, device, personaName);

            if (endpoints is not null)
            {
                bool removed = WasapiAudioEndpoint.WaitForEndpointRemoval(
                    [endpoints.RenderEndpoint.Id, endpoints.CaptureEndpoint.Id],
                    EmulationAudioEndpointSupport.EndpointRemovalTimeout);
                Assert.That(removed, Is.True, "The virtual audio endpoints did not disappear after detach.");
                Thread.Sleep(EmulationAudioEndpointSupport.EndpointTeardownSettleTime);
            }
        }
    }
}