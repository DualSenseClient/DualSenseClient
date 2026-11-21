using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Captures the audio the host plays to its default PipeWire sink and forwards it to
/// <see cref="DualSenseAudioForwarder"/>, so the physical controller's speaker and haptics
/// follow whatever the system is playing without the game selecting the controller as its
/// output.
/// </summary>
/// <remarks>
/// The default sink is resolved once at start, matching the Windows default-endpoint
/// loopback. A sink change does not migrate the running capture; reselect the source to
/// refresh it.
/// </remarks>
internal sealed class PipeWireSystemOutputAudioCapture : IForwardedAudioCapture, IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("PipeWireSystemOutputAudioCapture");

    /// <summary>
    /// The forwarder receiving the captured audio.
    /// </summary>
    private readonly DualSenseAudioForwarder _forwarder;

    /// <summary>
    /// Serializes start/stop and guards the active capture.
    /// </summary>
    private readonly Lock _sync = new Lock();

    private PipeWireCaptureProcess? _capture;

    /// <summary>
    /// Creates the capture feeding the given forwarder.
    /// </summary>
    /// <param name="forwarder">The forwarder receiving the captured audio.</param>
    public PipeWireSystemOutputAudioCapture(DualSenseAudioForwarder forwarder)
    {
        _forwarder = forwarder;
    }

    /// <inheritdoc/>
    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _capture?.IsActive == true;
            }
        }
    }

    /// <inheritdoc/>
    public bool Start()
    {
        if (!OperatingSystem.IsLinux())
        {
            _log.Debug("PipeWire system output capture is only available on Linux; capture not started");
            return false;
        }

        lock (_sync)
        {
            if (_capture is not null)
            {
                return _capture.IsActive;
            }

            if (!PipeWireAudioTopology.TryGetDefaultSink(out string sinkName, out int sinkNodeId))
            {
                _log.Warning("No default PipeWire sink is available; forwarded system audio is unavailable");
                return false;
            }

            PipeWireCaptureProcess capture = new PipeWireCaptureProcess(_forwarder);
            if (!capture.Start())
            {
                capture.Dispose();
                return false;
            }

            if (!PipeWireAudioTopology.TryLinkNodes(sinkNodeId, capture.NodeId))
            {
                _log.Warning($"Could not link the default sink '{sinkName}' to the capture stream; forwarded system audio is unavailable");
                capture.Dispose();
                return false;
            }

            _capture = capture;
            _log.Info($"System output capture follows the default sink '{sinkName}' (node {sinkNodeId})");
            return true;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        PipeWireCaptureProcess? capture;
        lock (_sync)
        {
            capture = _capture;
            _capture = null;
        }

        capture?.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();
}