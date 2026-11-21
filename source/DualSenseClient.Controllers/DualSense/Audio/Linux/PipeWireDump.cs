using System.Text.Json;

namespace DualSenseClient.Controllers.DualSense.Audio.Linux;

/// <summary>
/// Parses a <c>pw-dump</c> JSON snapshot into the small graph view the Linux forwarded-audio
/// sources need: output streams, sink nodes, and the session manager's default sink.
/// </summary>
/// <remarks>
/// The parser is deliberately tolerant. PipeWire versions differ in whether process ids are
/// numbers or strings, and properties can be missing, so malformed or partial input yields
/// <c>null</c> or empty fields instead of throwing.
/// </remarks>
internal static class PipeWireDump
{
    /// <summary>
    /// Parses a <c>pw-dump</c> document. Returns <c>null</c> when the text is empty, not a
    /// JSON array, or not valid JSON.
    /// </summary>
    /// <param name="json">The raw <c>pw-dump</c> output.</param>
    public static PipeWireGraph? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            Dictionary<int, PipeWireClient> clients = new Dictionary<int, PipeWireClient>();
            string? defaultSinkName = null;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (!TryGetType(element, out string type))
                {
                    continue;
                }

                if (type == "PipeWire:Interface:Client")
                {
                    PipeWireClient? client = ParseClient(element);
                    if (client is not null)
                    {
                        clients[client.Id] = client;
                    }
                }
                else if (type == "PipeWire:Interface:Metadata")
                {
                    defaultSinkName ??= ParseDefaultSink(element);
                }
            }

            List<PipeWireNode> nodes = new List<PipeWireNode>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (!TryGetType(element, out string type) || type != "PipeWire:Interface:Node")
                {
                    continue;
                }

                PipeWireNode? node = ParseNode(element, clients);
                if (node is not null)
                {
                    nodes.Add(node);
                }
            }

            return new PipeWireGraph(defaultSinkName, nodes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the element is an object with a string <c>type</c> property.
    /// </summary>
    private static bool TryGetType(JsonElement element, out string type)
    {
        type = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("type", out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        type = value.GetString() ?? string.Empty;
        return type.Length > 0;
    }

    /// <summary>
    /// Reads an object's <c>info.props</c> object, returning <c>false</c> when any level is
    /// missing or has the wrong shape.
    /// </summary>
    private static bool TryGetProperties(JsonElement element, out JsonElement properties)
    {
        properties = default;
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty("info", out JsonElement info)
               && info.ValueKind == JsonValueKind.Object
               && info.TryGetProperty("props", out properties)
               && properties.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Reads a client object's id and application properties.
    /// </summary>
    private static PipeWireClient? ParseClient(JsonElement element)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement) || !idElement.TryGetInt32(out int id)
                                                                     || !TryGetProperties(element, out JsonElement properties))
        {
            return null;
        }

        return new PipeWireClient(
            id,
            ReadUInt32(properties, "application.process.id"),
            ReadString(properties, "application.name"),
            ReadString(properties, "application.process.binary"));
    }

    /// <summary>
    /// Reads the session manager's <c>default.audio.sink</c> metadata value.
    /// </summary>
    private static string? ParseDefaultSink(JsonElement element)
    {
        if (!element.TryGetProperty("metadata", out JsonElement metadata)
            || metadata.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement entry in metadata.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("key", out JsonElement key)
                || key.ValueKind != JsonValueKind.String
                || key.GetString() != "default.audio.sink"
                || !entry.TryGetProperty("value", out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("name", out JsonElement name)
                && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a node object. Process properties come from the node itself when present, then
    /// from its client, because native PipeWire clients often expose only the client-side id.
    /// </summary>
    private static PipeWireNode? ParseNode(JsonElement element, IReadOnlyDictionary<int, PipeWireClient> clients)
    {
        if (!element.TryGetProperty("id", out JsonElement idElement) || !idElement.TryGetInt32(out int id)
                                                                     || !TryGetProperties(element, out JsonElement properties))
        {
            return null;
        }

        PipeWireClient? client = null;
        int clientId = ReadInt32(properties, "client.id");
        if (clientId != 0)
        {
            clients.TryGetValue(clientId, out client);
        }

        string applicationName = ReadString(properties, "application.name");
        if (applicationName.Length == 0)
        {
            applicationName = client?.ApplicationName ?? string.Empty;
        }

        string binaryName = ReadString(properties, "application.process.binary");
        if (binaryName.Length == 0)
        {
            binaryName = client?.BinaryName ?? string.Empty;
        }

        uint processId = ReadUInt32(properties, "application.process.id");
        if (processId == 0)
        {
            processId = client?.ProcessId ?? 0;
        }

        return new PipeWireNode(
            id,
            ReadString(properties, "node.name"),
            ReadString(properties, "media.class"),
            applicationName,
            binaryName,
            ReadString(properties, "media.name"),
            processId);
    }

    /// <summary>
    /// Reads a string property, accepting numeric values as their raw text.
    /// </summary>
    private static string ReadString(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(name, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Reads an unsigned integer property, accepting a JSON string as well.
    /// </summary>
    private static uint ReadUInt32(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && uint.TryParse(value.GetString(), out uint parsed)
            ? parsed
            : 0;
    }

    /// <summary>
    /// Reads a signed integer property, accepting a JSON string as well.
    /// </summary>
    private static int ReadInt32(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(name, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed)
            ? parsed
            : 0;
    }
}