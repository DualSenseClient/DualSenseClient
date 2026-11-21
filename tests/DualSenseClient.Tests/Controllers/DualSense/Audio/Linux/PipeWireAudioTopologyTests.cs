using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Audio.Linux;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Covers the pure topology lookups used by the Linux capture sources. The process-backed
/// <c>pw-dump</c>/<c>pw-link</c> calls are exercised by the manual PipeWire smoke test.
/// </summary>
public class PipeWireAudioTopologyTests
{
    private const string GraphJson = """
                                     [
                                       { "id": 31, "type": "PipeWire:Interface:Metadata",
                                         "metadata": [ { "key": "default.audio.sink", "value": { "name": "alsa_output.test" } } ] },
                                       { "id": 56, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Audio/Sink", "node.name": "alsa_output.test" } } }
                                     ]
                                     """;

    [Test]
    public void TryGetDefaultSink_KnownMetadata_ReturnsTheSinkNameAndNodeId()
    {
        PipeWireGraph graph = ParseGraph();

        bool found = PipeWireAudioTopology.TryGetDefaultSink(graph, out string sinkName, out int sinkNodeId);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(sinkName, Is.EqualTo("alsa_output.test"));
            Assert.That(sinkNodeId, Is.EqualTo(56));
        });
    }

    [Test]
    public void TryGetDefaultSink_MissingMetadata_ReturnsFalse()
    {
        PipeWireGraph? graph = PipeWireDump.TryParse("[]");

        bool found = PipeWireAudioTopology.TryGetDefaultSink(graph, out string sinkName, out int sinkNodeId);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(sinkName, Is.Empty);
            Assert.That(sinkNodeId, Is.Zero);
        });
    }

    private static PipeWireGraph ParseGraph()
    {
        PipeWireGraph? graph = PipeWireDump.TryParse(GraphJson);
        Assert.That(graph, Is.Not.Null, "the fixture must parse");
        return graph!;
    }
}