namespace DualSenseClient.HidHide;

/// <summary>
/// Line protocol shared by the Linux hiding service and the root helper.
/// </summary>
internal static class HidingProtocol
{
    /// <summary>
    /// Protocol version supported by this client.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Builds the handshake request.
    /// </summary>
    /// <returns>The request line.</returns>
    public static string HelloRequest() => $"HELLO {Version}";

    /// <summary>
    /// Builds a hide request for the given controller ID.
    /// </summary>
    /// <param name="id">Controller ID.</param>
    /// <returns>The request line.</returns>
    public static string HideRequest(string id) => $"HIDE {id}";

    /// <summary>
    /// Builds an unhide request for the given controller ID.
    /// </summary>
    /// <param name="id">Controller ID.</param>
    /// <returns>The request line.</returns>
    public static string UnhideRequest(string id) => $"UNHIDE {id}";

    /// <summary>
    /// Builds a status request for the given controller ID.
    /// </summary>
    /// <param name="id">Controller ID.</param>
    /// <returns>The request line.</returns>
    public static string StatusRequest(string id) => $"STATUS {id}";

    /// <summary>
    /// Parses a handshake response of the form <c>OK &lt;protocol&gt; &lt;helper-version&gt; ...</c>.
    /// </summary>
    /// <param name="line">Response line.</param>
    /// <param name="protocolVersion">Parsed protocol version.</param>
    /// <param name="helperVersion">Parsed helper version.</param>
    /// <returns><c>true</c> when the line is a valid success response; otherwise <c>false</c>.</returns>
    public static bool TryParseHelloResponse(string line, out int protocolVersion, out string helperVersion)
    {
        protocolVersion = 0;
        helperVersion = string.Empty;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "OK", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out int parsed))
        {
            return false;
        }

        protocolVersion = parsed;
        helperVersion = parts[2];
        return true;
    }

    /// <summary>
    /// Parses a <c>HIDDEN</c>/<c>VISIBLE</c> status response.
    /// </summary>
    /// <param name="line">Response line.</param>
    /// <param name="hidden">Whether the controller is hidden.</param>
    /// <returns><c>true</c> when the line is a status response; otherwise <c>false</c>.</returns>
    public static bool TryParseStatusResponse(string line, out bool hidden)
    {
        hidden = false;

        if (string.Equals(line, "HIDDEN", StringComparison.Ordinal))
        {
            hidden = true;
            return true;
        }

        return string.Equals(line, "VISIBLE", StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses an <c>OK</c>/<c>ERR &lt;message&gt;</c> command response.
    /// </summary>
    /// <param name="line">Response line.</param>
    /// <param name="error">Error text for an <c>ERR</c> response, otherwise empty.</param>
    /// <returns><c>true</c> for <c>OK</c>; otherwise <c>false</c>.</returns>
    public static bool TryParseCommandResponse(string line, out string error)
    {
        error = string.Empty;

        if (string.Equals(line, "OK", StringComparison.Ordinal))
        {
            return true;
        }

        if (line.StartsWith("ERR ", StringComparison.Ordinal))
        {
            error = line[4..];
        }
        else if (string.Equals(line, "ERR", StringComparison.Ordinal))
        {
            error = "ERR";
        }

        return false;
    }

    /// <summary>
    /// Whether an ID is safe to use as a marker file name: 1–64 characters from
    /// <c>A-Z a-z 0-9 : . _ -</c>, and not <c>.</c> or <c>..</c>.
    /// </summary>
    /// <param name="id">Candidate ID.</param>
    /// <returns><c>true</c> when the ID is valid; otherwise <c>false</c>.</returns>
    public static bool IsValidId(string id)
    {
        if (id is null || id.Length is 0 or > 64 || id == "." || id == "..")
        {
            return false;
        }

        foreach (char c in id)
        {
            bool valid = (c >= 'A' && c <= 'Z')
                         || (c >= 'a' && c <= 'z')
                         || (c >= '0' && c <= '9')
                         || c == ':'
                         || c == '.'
                         || c == '_'
                         || c == '-';
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}