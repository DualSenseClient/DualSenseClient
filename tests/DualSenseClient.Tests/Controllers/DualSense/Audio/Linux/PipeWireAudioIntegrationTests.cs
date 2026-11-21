using System.Diagnostics;
using System.Text;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Audio.Linux;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Opt-in PipeWire smoke tests. Set <c>PIPEWIRE_INTEGRATION=1</c> on a Linux desktop with
/// <c>pw-record</c>, <c>pw-dump</c>, <c>pw-link</c>, and <c>pw-play</c> installed to run them.
/// The default suite marks them inconclusive.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class PipeWireAudioIntegrationTests
{
    private Process? _playback;

    [SetUp]
    public void RequirePipeWire()
    {
        Assume.That(OperatingSystem.IsLinux(), "PipeWire integration tests require Linux");
        Assume.That(Environment.GetEnvironmentVariable("PIPEWIRE_INTEGRATION") == "1",
            "set PIPEWIRE_INTEGRATION=1 to run the PipeWire integration tests");
        Assume.That(PipeWireAudioTopology.IsAvailable, "the PipeWire command-line tools are not installed");
        Assume.That(PipeWireTool.ExistsOnPath("pw-play"), "pw-play is not installed");
    }

    [TearDown]
    public void StopPlayback()
    {
        if (_playback is not null)
        {
            try
            {
                _playback.Kill(true);
                _playback.WaitForExit(2000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The test playback already exited.
            }
            finally
            {
                _playback.Dispose();
                _playback = null;
            }
        }
    }

    [Test]
    public void SystemOutputCapture_StartsAgainstTheDefaultSinkAndStops()
    {
        using DualSenseAudioForwarder forwarder = new DualSenseAudioForwarder(null, null);
        using PipeWireSystemOutputAudioCapture capture = new PipeWireSystemOutputAudioCapture(forwarder);

        Assert.That(capture.Start(), Is.True, "the default sink should be linkable");
        Assert.That(capture.IsActive, Is.True);
        AssertCaptureLinked(capture.CaptureNodeName);
        capture.Stop();
        Assert.That(capture.IsActive, Is.False);
    }

    [Test]
    public void SystemOutputCapture_RecoversAfterItsChildExits()
    {
        using DualSenseAudioForwarder forwarder = new DualSenseAudioForwarder(null, null);
        using PipeWireSystemOutputAudioCapture capture = new PipeWireSystemOutputAudioCapture(forwarder);

        Assert.That(capture.Start(), Is.True, "the default sink should be linkable");
        int childProcessId = capture.ChildProcessId;
        Assert.That(childProcessId, Is.GreaterThan(0), "the capture must own a pw-record child");

        using (Process child = Process.GetProcessById(childProcessId))
        {
            child.Kill(true);
        }

        Assert.That(SpinWait.SpinUntil(() => !capture.IsActive, 3000), Is.True,
            "the capture must report inactive after its child exits");
        Assert.That(capture.Start(), Is.True, "a dead pw-record must not poison a later start");
        Assert.That(capture.IsActive, Is.True);
        AssertCaptureLinked(capture.CaptureNodeName);
        capture.Stop();
    }

    [Test]
    public void ApplicationCapture_LinksAPlayingNativeStream()
    {
        string tonePath = CreateToneFile();
        try
        {
            _playback = Process.Start(new ProcessStartInfo
            {
                FileName = "pw-play",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "--target",
                    "0",
                    tonePath
                }
            });
            Assert.That(_playback, Is.Not.Null);

            AudioApplicationService service = new AudioApplicationService();
            IReadOnlyList<AudioApplicationInfo> applications = WaitForPlayingApplication(service, "pw-play");
            AudioApplicationInfo playback = applications.Single(app => app.DisplayName == "pw-play");
            Assert.That(playback.ExecutablePath, Is.Not.Empty);

            using DualSenseAudioForwarder forwarder = new DualSenseAudioForwarder(null, null);
            using PipeWireApplicationAudioCapture capture = new PipeWireApplicationAudioCapture("pw-play", forwarder);
            Assert.That(capture.Start(), Is.True, "the playing stream should be linkable");
            Assert.That(capture.IsActive, Is.True);
            AssertCaptureLinked(capture.CaptureNodeName);

            int childProcessId = capture.ChildProcessId;
            Assert.That(childProcessId, Is.GreaterThan(0), "the capture must own a pw-record child");
            using (Process child = Process.GetProcessById(childProcessId))
            {
                child.Kill(true);
            }

            Assert.That(SpinWait.SpinUntil(() => !capture.IsActive, 3000), Is.True,
                "the capture must report inactive after its child exits");
            Assert.That(capture.Start(), Is.True, "a dead pw-record must not poison a later start");
            Assert.That(capture.IsActive, Is.True);
            AssertCaptureLinked(capture.CaptureNodeName);

            capture.Stop();
            Assert.That(capture.IsActive, Is.False);
        }
        finally
        {
            File.Delete(tonePath);
        }
    }

    private static void AssertCaptureLinked(string nodeName)
    {
        Assert.That(nodeName, Is.Not.Empty, "the capture must expose its node name");
        Assert.That(
            PipeWireTool.TryRun("pw-link", ["-l", "-I"], 2000, out string listing, out string error),
            Is.True,
            error);
        Assert.That(
            PipeWireAudioTopology.ContainsLink(listing, nodeName),
            Is.True,
            $"the capture node '{nodeName}' must have at least one link");
    }

    private static IReadOnlyList<AudioApplicationInfo> WaitForPlayingApplication(
        AudioApplicationService service, string displayName)
    {
        IReadOnlyList<AudioApplicationInfo> applications = [];
        for (int attempt = 0; attempt < 30; attempt++)
        {
            applications = service.GetPlayingApplications();
            if (applications.Any(app => app.DisplayName == displayName))
            {
                break;
            }

            Thread.Sleep(100);
        }

        Assert.That(applications.Any(app => app.DisplayName == displayName), Is.True,
            $"'{displayName}' did not appear in the playing-application list");
        return applications;
    }

    private static string CreateToneFile()
    {
        const int sampleRate = 48000;
        const int seconds = 8;
        const int sampleCount = sampleRate * seconds;
        string path = Path.Combine(Path.GetTempPath(), $"dualsense-pipewire-{Guid.NewGuid():N}.wav");
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * 2);
        for (int sample = 0; sample < sampleCount; sample++)
        {
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * sample / sampleRate) * 8000));
        }

        return path;
    }
}