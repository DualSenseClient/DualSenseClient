using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Audio.Linux;

namespace DualSenseClient.Tests.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Covers Linux application selection: grouping playing streams for the picker, resolving
/// a stored selector to processes, following child processes, and the name fallback for
/// streams that expose no process id.
/// </summary>
public class LinuxAudioApplicationsTests
{
    private const string GraphJson = """
                                     [
                                       { "id": 31, "type": "PipeWire:Interface:Metadata",
                                         "metadata": [ { "key": "default.audio.sink", "value": { "name": "alsa_output.test" } } ] },
                                       { "id": 56, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Audio/Sink", "node.name": "alsa_output.test" } } },
                                       { "id": 74, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Stream/Output/Audio", "node.name": "game",
                                           "application.name": "Game", "application.process.id": 100 } } },
                                       { "id": 75, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Stream/Output/Audio", "node.name": "game-helper",
                                           "application.name": "Game", "application.process.id": 101 } } },
                                       { "id": 76, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Stream/Output/Audio", "node.name": "other",
                                           "application.name": "Other", "application.process.id": 200 } } },
                                       { "id": 77, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Stream/Output/Audio", "node.name": "native",
                                           "application.name": "Native" } } },
                                       { "id": 78, "type": "PipeWire:Interface:Node",
                                         "info": { "props": { "media.class": "Stream/Output/Audio", "node.name": "orphan",
                                           "application.name": "Orphan", "application.process.id": 2147483647 } } }
                                     ]
                                     """;

    [Test]
    public void GetPlayingApplications_DedupesBySelectorAndSortsByDisplayName()
    {
        PipeWireGraph graph = ParseGraph();
        IReadOnlyDictionary<int, LinuxProcessInfo> processes = CreateProcesses();

        IReadOnlyList<AudioApplicationInfo> applications =
            LinuxAudioApplications.GetPlayingApplications(graph, processes);

        List<string> selectors = applications.Select(app => app.ExecutablePath).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(selectors, Is.Unique);
            Assert.That(selectors, Is.EquivalentTo(new[]
            {
                "/usr/bin/game", "/usr/lib/game/helper", "/usr/bin/other", "Native", "Orphan"
            }));
            Assert.That(applications.Select(app => app.DisplayName), Is.Ordered);
        });
    }

    [Test]
    public void GetMatchingProcessIds_IncludesChildProcesses()
    {
        IReadOnlySet<int> ids = LinuxAudioApplications.GetMatchingProcessIds("/usr/bin/game", CreateProcesses());

        Assert.That(ids, Is.EquivalentTo(new[]
        {
            100, 101
        }));
    }

    [Test]
    public void GetMatchingProcessIds_FullPathWithoutExecutableMetadata_MatchesByProcessName()
    {
        Dictionary<int, LinuxProcessInfo> processes = new Dictionary<int, LinuxProcessInfo>
        {
            [int.MaxValue] = new LinuxProcessInfo(int.MaxValue, 1, string.Empty, "Metrolist.bin")
        };

        IReadOnlySet<int> ids = LinuxAudioApplications.GetMatchingProcessIds(
            "/tmp/.mount_example/bin/Metrolist.bin", processes);

        Assert.That(ids, Is.EquivalentTo(new[]
        {
            int.MaxValue
        }));
    }

    [Test]
    public void GetMatchingProcessIds_TruncatedProcessName_MatchesFullPathSelector()
    {
        Dictionary<int, LinuxProcessInfo> processes = new Dictionary<int, LinuxProcessInfo>
        {
            [int.MaxValue] = new LinuxProcessInfo(int.MaxValue, 1, string.Empty, "telegram-deskto")
        };

        IReadOnlySet<int> ids = LinuxAudioApplications.GetMatchingProcessIds(
            "/usr/bin/telegram-desktop", processes);

        Assert.That(ids, Is.EquivalentTo(new[]
        {
            int.MaxValue
        }));
    }

    [Test]
    public void GetMatchingProcessIds_TruncatedProcessNameWithDifferentExecutable_DoesNotMatch()
    {
        Dictionary<int, LinuxProcessInfo> processes = new Dictionary<int, LinuxProcessInfo>
        {
            [int.MaxValue] = new LinuxProcessInfo(int.MaxValue, 1, "/usr/bin/other", "telegram-deskto")
        };

        IReadOnlySet<int> ids = LinuxAudioApplications.GetMatchingProcessIds(
            "/usr/bin/telegram-desktop", processes);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void GetMatchingStreamNodeIds_TruncatedProcessName_MatchesTheStream()
    {
        PipeWireGraph graph = new PipeWireGraph(null,
        [
            new PipeWireNode(80, "telegram", "Stream/Output/Audio", "Telegram", "telegram-desktop", "audio", int.MaxValue)
        ]);
        Dictionary<int, LinuxProcessInfo> processes = new Dictionary<int, LinuxProcessInfo>
        {
            [int.MaxValue] = new LinuxProcessInfo(int.MaxValue, 1, string.Empty, "telegram-deskto")
        };

        IReadOnlyList<int> nodes =
            LinuxAudioApplications.GetMatchingStreamNodeIds("/usr/bin/telegram-desktop", graph, processes);

        Assert.That(nodes, Is.EquivalentTo(new[]
        {
            80
        }));
    }

    [Test]
    public void GetMatchingProcessIds_BareNameMatchesTheProcessName()
    {
        IReadOnlySet<int> ids = LinuxAudioApplications.GetMatchingProcessIds("game", CreateProcesses());

        Assert.That(ids, Is.EquivalentTo(new[]
        {
            100, 101
        }));
    }

    [Test]
    public void GetMatchingStreamNodeIds_FollowsTheProcessTreeAndExcludesOtherApps()
    {
        IReadOnlyList<int> nodes =
            LinuxAudioApplications.GetMatchingStreamNodeIds("/usr/bin/game", ParseGraph(), CreateProcesses());

        Assert.That(nodes, Is.EquivalentTo(new[]
        {
            74, 75
        }));
    }

    [Test]
    public void GetMatchingStreamNodeIds_NoProcessId_FallsBackToTheApplicationName()
    {
        IReadOnlyList<int> nodes =
            LinuxAudioApplications.GetMatchingStreamNodeIds("Native", ParseGraph(), CreateProcesses());

        Assert.That(nodes, Is.EquivalentTo(new[]
        {
            77
        }));
    }

    [Test]
    public void GetMatchingStreamNodeIds_UnknownProcessId_FallsBackToTheApplicationName()
    {
        IReadOnlyList<int> nodes =
            LinuxAudioApplications.GetMatchingStreamNodeIds("Orphan", ParseGraph(), CreateProcesses());

        Assert.That(nodes, Is.EquivalentTo(new[]
        {
            78
        }));
    }

    [Test]
    public void TryResolveProcessId_FullPath_ReturnsAMatchingProcess()
    {
        PipeWireGraph graph = ParseGraph();

        bool resolved = LinuxAudioApplications.TryResolveProcessId(
            "/usr/bin/game", CreateProcesses(), graph, out uint processId);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(processId, Is.EqualTo(100u));
        });
    }

    [Test]
    public void TryResolveProcessId_Unknown_ReturnsFalse()
    {
        bool resolved = LinuxAudioApplications.TryResolveProcessId(
            "/usr/bin/missing", CreateProcesses(), ParseGraph(), out uint processId);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(processId, Is.Zero);
        });
    }

    [Test]
    public void TryResolveProcessId_NameOnlyStream_ReturnsTrueWithoutAProcessId()
    {
        bool resolved = LinuxAudioApplications.TryResolveProcessId(
            "Native", CreateProcesses(), ParseGraph(), out uint processId);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(processId, Is.Zero);
        });
    }

    [Test]
    [Platform("Linux")]
    public void GetMatchingProcessIds_SymlinkedSelector_ResolvesToTheTargetExecutable()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dsc-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "real-app");
        string link = Path.Combine(directory, "launcher");
        File.WriteAllText(target, string.Empty);
        File.CreateSymbolicLink(link, target);
        try
        {
            Dictionary<int, LinuxProcessInfo> processes = new Dictionary<int, LinuxProcessInfo>
            {
                [400] = new LinuxProcessInfo(400, 1, target, "real-app")
            };

            IReadOnlySet<int> ids = LinuxAudioApplications.GetMatchingProcessIds(link, processes);

            Assert.That(ids, Is.EquivalentTo(new[]
            {
                400
            }));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private static IReadOnlyDictionary<int, LinuxProcessInfo> CreateProcesses()
    {
        return new Dictionary<int, LinuxProcessInfo>
        {
            [100] = new LinuxProcessInfo(100, 1, "/usr/bin/game", "game"),
            [101] = new LinuxProcessInfo(101, 100, "/usr/lib/game/helper", "game-helper"),
            [200] = new LinuxProcessInfo(200, 1, "/usr/bin/other", "other")
        };
    }

    private static PipeWireGraph ParseGraph()
    {
        PipeWireGraph? graph = PipeWireDump.TryParse(GraphJson);
        Assert.That(graph, Is.Not.Null, "the fixture must parse");
        return graph!;
    }
}