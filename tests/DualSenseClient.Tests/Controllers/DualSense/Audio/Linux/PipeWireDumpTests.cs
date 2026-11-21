using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Audio.Linux;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Covers the pure <c>pw-dump</c> snapshot parser: default sink metadata, output streams,
/// client-side process ids, and tolerant handling of malformed input.
/// </summary>
public class PipeWireDumpTests
{
    private const string GraphJson = """
                                     [
                                       {
                                         "id": 31,
                                         "type": "PipeWire:Interface:Metadata",
                                         "metadata": [
                                           {
                                             "subject": 0,
                                             "key": "default.audio.sink",
                                             "type": "Spa:String:JSON",
                                             "value": { "name": "alsa_output.test" }
                                           }
                                         ]
                                       },
                                       {
                                         "id": 56,
                                         "type": "PipeWire:Interface:Node",
                                         "info": {
                                           "props": {
                                             "media.class": "Audio/Sink",
                                             "node.name": "alsa_output.test"
                                           }
                                         }
                                       },
                                       {
                                         "id": 74,
                                         "type": "PipeWire:Interface:Node",
                                         "info": {
                                           "props": {
                                             "media.class": "Stream/Output/Audio",
                                             "node.name": "game",
                                             "application.name": "Game",
                                             "application.process.id": 1234,
                                             "application.process.binary": "game",
                                             "media.name": "Game audio"
                                           }
                                         }
                                       },
                                       {
                                         "id": 96,
                                         "type": "PipeWire:Interface:Client",
                                         "info": {
                                           "props": {
                                             "application.name": "pw-cat",
                                             "application.process.id": 4321,
                                             "application.process.binary": "pw-cat"
                                           }
                                         }
                                       },
                                       {
                                         "id": 101,
                                         "type": "PipeWire:Interface:Node",
                                         "info": {
                                           "props": {
                                             "media.class": "Stream/Output/Audio",
                                             "node.name": "pw-play",
                                             "application.name": "pw-play",
                                             "client.id": 96
                                           }
                                         }
                                       }
                                     ]
                                     """;

    [Test]
    public void TryParse_DefaultSinkAndOutputStreams_AreExposed()
    {
        PipeWireGraph? graph = PipeWireDump.TryParse(GraphJson);

        Assert.That(graph, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(graph!.DefaultSinkName, Is.EqualTo("alsa_output.test"));
            Assert.That(graph.FindSinkId("alsa_output.test"), Is.EqualTo(56));
            Assert.That(graph.OutputStreams, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void TryParse_OutputStream_ExposesApplicationAndProcessFields()
    {
        PipeWireGraph graph = ParseGraph();

        PipeWireNode game = graph.OutputStreams.Single(node => node.Id == 74);

        Assert.Multiple(() =>
        {
            Assert.That(game.Name, Is.EqualTo("game"));
            Assert.That(game.ApplicationName, Is.EqualTo("Game"));
            Assert.That(game.BinaryName, Is.EqualTo("game"));
            Assert.That(game.MediaName, Is.EqualTo("Game audio"));
            Assert.That(game.ProcessId, Is.EqualTo(1234u));
        });
    }

    [Test]
    public void TryParse_NodeWithoutOwnProcessId_UsesTheClientProcessId()
    {
        PipeWireGraph graph = ParseGraph();

        PipeWireNode native = graph.OutputStreams.Single(node => node.Id == 101);

        Assert.Multiple(() =>
        {
            Assert.That(native.ProcessId, Is.EqualTo(4321u));
            Assert.That(native.ApplicationName, Is.EqualTo("pw-play"), "the node name wins over the client name");
        });
    }

    [Test]
    public void TryParse_StringProcessId_IsParsed()
    {
        const string json = """
                            [
                              {
                                "id": 74,
                                "type": "PipeWire:Interface:Node",
                                "info": {
                                  "props": {
                                    "media.class": "Stream/Output/Audio",
                                    "node.name": "game",
                                    "application.process.id": "9876"
                                  }
                                }
                              }
                            ]
                            """;

        PipeWireGraph? graph = PipeWireDump.TryParse(json);

        Assert.That(graph, Is.Not.Null);
        Assert.That(graph!.OutputStreams.Single().ProcessId, Is.EqualTo(9876u));
    }

    [Test]
    public void TryParse_MalformedJson_ReturnsNull() => Assert.That(PipeWireDump.TryParse("{ not json"), Is.Null);

    [Test]
    public void TryParse_EmptyJson_ReturnsNull() => Assert.That(PipeWireDump.TryParse("   "), Is.Null);

    [Test]
    public void TryParse_NodeWithNullInfo_IsSkippedAndOtherNodesStillParse()
    {
        const string json = """
                            [
                              { "id": 1, "type": "PipeWire:Interface:Node", "info": null },
                              { "id": 2, "type": "PipeWire:Interface:Node" },
                              { "id": 3, "type": "PipeWire:Interface:Node",
                                "info": { "props": { "media.class": "Stream/Output/Audio", "node.name": "valid" } } }
                            ]
                            """;

        PipeWireGraph? graph = PipeWireDump.TryParse(json);

        Assert.That(graph, Is.Not.Null);
        Assert.That(graph!.OutputStreams, Has.Count.EqualTo(1));
        Assert.That(graph.OutputStreams[0].Name, Is.EqualTo("valid"));
    }

    [Test]
    public void FindNodeId_ReturnsTheNamedCaptureNode()
    {
        const string json = """
                            [
                              {
                                "id": 91,
                                "type": "PipeWire:Interface:Node",
                                "info": {
                                  "props": {
                                    "media.class": "Stream/Input/Audio",
                                    "node.name": "dualsense-client-capture-abc"
                                  }
                                }
                              }
                            ]
                            """;

        PipeWireGraph? graph = PipeWireDump.TryParse(json);

        Assert.Multiple(() =>
        {
            Assert.That(graph, Is.Not.Null);
            Assert.That(graph!.FindNodeId("dualsense-client-capture-abc"), Is.EqualTo(91));
            Assert.That(graph.FindNodeId("missing"), Is.Null);
            Assert.That(graph.FindSinkId("missing"), Is.Null);
        });
    }

    private static PipeWireGraph ParseGraph()
    {
        PipeWireGraph? graph = PipeWireDump.TryParse(GraphJson);
        Assert.That(graph, Is.Not.Null, "the fixture must parse");
        return graph!;
    }
}