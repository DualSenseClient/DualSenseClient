namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// A parsed <c>pw-dump</c> graph view: the nodes relevant to forwarded audio, the session
/// manager's default sink, and lookup helpers used by the Linux capture sources.
/// </summary>
internal sealed class PipeWireGraph
{
    /// <summary>
    /// Creates a graph view and materializes its filtered node lists.
    /// </summary>
    /// <param name="defaultSinkName">The session manager's default sink node name, if known.</param>
    /// <param name="nodes">Every node in the snapshot.</param>
    public PipeWireGraph(string? defaultSinkName, IReadOnlyList<PipeWireNode> nodes)
    {
        DefaultSinkName = defaultSinkName;
        Nodes = nodes;
        OutputStreams = nodes.Where(node => node.MediaClass == "Stream/Output/Audio").ToList();
    }

    /// <summary>
    /// The session manager's default sink node name, or <c>null</c> when no metadata entry
    /// exists.
    /// </summary>
    public string? DefaultSinkName { get; }

    /// <summary>
    /// Every node in the snapshot.
    /// </summary>
    public IReadOnlyList<PipeWireNode> Nodes { get; }

    /// <summary>
    /// The application output streams, in snapshot order.
    /// </summary>
    public IReadOnlyList<PipeWireNode> OutputStreams { get; }

    /// <summary>
    /// Finds a node by its <c>node.name</c>.
    /// </summary>
    /// <param name="nodeName">The node name to look for.</param>
    /// <returns>The node id, or <c>null</c> when no node has that name.</returns>
    public int? FindNodeId(string nodeName)
    {
        foreach (PipeWireNode node in Nodes)
        {
            if (string.Equals(node.Name, nodeName, StringComparison.Ordinal))
            {
                return node.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an audio sink node by its <c>node.name</c>.
    /// </summary>
    /// <param name="sinkName">The sink name to look for.</param>
    /// <returns>The sink node id, or <c>null</c> when no sink has that name.</returns>
    public int? FindSinkId(string sinkName)
    {
        foreach (PipeWireNode node in Nodes)
        {
            if (node.MediaClass == "Audio/Sink"
                && string.Equals(node.Name, sinkName, StringComparison.Ordinal))
            {
                return node.Id;
            }
        }

        return null;
    }
}

/// <summary>
/// One node from a <c>pw-dump</c> snapshot, with the application properties needed by the
/// forwarded-audio picker and capture.
/// </summary>
internal sealed class PipeWireNode
{
    /// <summary>
    /// Creates a node descriptor.
    /// </summary>
    /// <param name="id">The PipeWire node id.</param>
    /// <param name="name">The <c>node.name</c> property.</param>
    /// <param name="mediaClass">The <c>media.class</c> property.</param>
    /// <param name="applicationName">The <c>application.name</c> property.</param>
    /// <param name="binaryName">The <c>application.process.binary</c> property.</param>
    /// <param name="mediaName">The <c>media.name</c> property.</param>
    /// <param name="processId">The owning process id, or zero when unknown.</param>
    public PipeWireNode(
        int id,
        string name,
        string mediaClass,
        string applicationName,
        string binaryName,
        string mediaName,
        uint processId)
    {
        Id = id;
        Name = name;
        MediaClass = mediaClass;
        ApplicationName = applicationName;
        BinaryName = binaryName;
        MediaName = mediaName;
        ProcessId = processId;
    }

    /// <summary>
    /// The PipeWire node id, stable while the node exists.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The <c>node.name</c> property.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The <c>media.class</c> property.
    /// </summary>
    public string MediaClass { get; }

    /// <summary>
    /// The application name shown in the picker.
    /// </summary>
    public string ApplicationName { get; }

    /// <summary>
    /// The executable name reported by the application, if any.
    /// </summary>
    public string BinaryName { get; }

    /// <summary>
    /// The stream's media name, used only for diagnostics.
    /// </summary>
    public string MediaName { get; }

    /// <summary>
    /// The owning process id, or zero when the node and its client expose none.
    /// </summary>
    public uint ProcessId { get; }
}

/// <summary>
/// Application properties reported by a <c>pw-dump</c> client object. Native PipeWire
/// clients often expose the process id only here, not on the stream node.
/// </summary>
internal sealed class PipeWireClient
{
    /// <summary>
    /// Creates a client descriptor.
    /// </summary>
    /// <param name="id">The PipeWire client id.</param>
    /// <param name="processId">The owning process id, or zero when unknown.</param>
    /// <param name="applicationName">The <c>application.name</c> property.</param>
    /// <param name="binaryName">The <c>application.process.binary</c> property.</param>
    public PipeWireClient(int id, uint processId, string applicationName, string binaryName)
    {
        Id = id;
        ProcessId = processId;
        ApplicationName = applicationName;
        BinaryName = binaryName;
    }

    /// <summary>
    /// The PipeWire client id.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The owning process id, or zero when unknown.
    /// </summary>
    public uint ProcessId { get; }

    /// <summary>
    /// The <c>application.name</c> property.
    /// </summary>
    public string ApplicationName { get; }

    /// <summary>
    /// The <c>application.process.binary</c> property.
    /// </summary>
    public string BinaryName { get; }
}