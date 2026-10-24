using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.Emulation.Devices.DualSense;
using DualSenseClient.Emulation.Devices.DualShock4;
using DualSenseClient.Emulation.Devices.Xbox360;

// The physical controller and an Emulation persona are both named DualSenseDevice in their
// namespaces; the aliases keep the two roles explicit in the fixture.
using PersonaDualSense = DualSenseClient.Emulation.Devices.DualSense.DualSenseDevice;
using PersonaDualShock4 = DualSenseClient.Emulation.Devices.DualShock4.DualShock4Device;
using PersonaXbox360 = DualSenseClient.Emulation.Devices.Xbox360.Xbox360Device;

namespace DualSenseClient.Tests.Controllers.Emulation;

/// <summary>
/// Pins the controller-endpoint capture selection of <see cref="EmulationService"/>: an Emulation
/// DualSense or DualShock 4 adapter yields its matching speaker capture, while an Xbox 360 adapter
/// (no speaker endpoint) and a missing sink yield nothing.
/// </summary>
[TestFixture]
public sealed class EmulationControllerCaptureTests
{
    [Test]
    public void TryCreateControllerCapture_VirtualDualSense_ReturnsDualSenseCapture()
    {
        using VirtualDualSenseController controller =
            new VirtualDualSenseController(new PersonaDualSense(new DualSenseIdentity()), new RecordingDualSenseOutputs(), false);
        RecordingSink sink = new RecordingSink();

        IDisposable? capture = EmulationService.TryCreateControllerCapture(controller, sink);

        Assert.That(capture, Is.InstanceOf<EmulationDualSenseAudioCapture>());
        Assert.DoesNotThrow(() => capture!.Dispose());
    }

    [Test]
    public void TryCreateControllerCapture_VirtualDualShock4_ReturnsDualShock4Capture()
    {
        using VirtualDualShock4Controller controller =
            new VirtualDualShock4Controller(new PersonaDualShock4(new DualShock4Identity()), new RecordingDualSenseOutputs());
        RecordingSink sink = new RecordingSink();

        IDisposable? capture = EmulationService.TryCreateControllerCapture(controller, sink);

        Assert.That(capture, Is.InstanceOf<EmulationDualShock4AudioCapture>());
        Assert.DoesNotThrow(() => capture!.Dispose());
    }

    [Test]
    public void TryCreateControllerCapture_VirtualXbox360_ReturnsNull()
    {
        using VirtualXbox360Controller controller =
            new VirtualXbox360Controller(new PersonaXbox360(new Xbox360Identity()), new RecordingDualSenseOutputs());
        RecordingSink sink = new RecordingSink();

        IDisposable? capture = EmulationService.TryCreateControllerCapture(controller, sink);

        Assert.That(capture, Is.Null);
    }

    [Test]
    public void TryCreateControllerCapture_NullSink_ReturnsNull()
    {
        using VirtualDualSenseController controller =
            new VirtualDualSenseController(new PersonaDualSense(new DualSenseIdentity()), new RecordingDualSenseOutputs(), false);

        IDisposable? capture = EmulationService.TryCreateControllerCapture(controller, null);

        Assert.That(capture, Is.Null);
    }

    /// <summary>
    /// Records the blocks it is fed (copied, so later packet reuse cannot reach them) and the
    /// number of flushes. This selection suite never emits audio; the sink only supplies the
    /// required non-null consumer.
    /// </summary>
    private sealed class RecordingSink : IAudioPcmSink
    {
        public List<float[]> Blocks { get; } = [];

        public int FlushCount { get; private set; }

        public void FeedPcm(ReadOnlySpan<float> stereo) => Blocks.Add(stereo.ToArray());

        public void Flush() => FlushCount++;
    }
}