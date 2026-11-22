using DualSenseClient.HidHide;

namespace DualSenseClient.Tests.HidHide.TestSupport;

/// <summary>
/// Test double for the Linux hiding socket client.
/// </summary>
internal sealed class FakeLinuxHidingClient : ILinuxHidingClient
{
    /// <summary>
    /// Number of connect attempts.
    /// </summary>
    public int ConnectCalls { get; private set; }

    /// <summary>
    /// Number of dispose calls.
    /// </summary>
    public int DisposeCalls { get; private set; }

    /// <summary>
    /// IDs passed to hide requests.
    /// </summary>
    public List<string> HideCalls { get; } = new List<string>();

    /// <summary>
    /// IDs passed to unhide requests.
    /// </summary>
    public List<string> UnhideCalls { get; } = new List<string>();

    /// <summary>
    /// IDs passed to status requests.
    /// </summary>
    public List<string> StatusCalls { get; } = new List<string>();

    /// <summary>
    /// Result returned by <see cref="TryConnect"/>.
    /// </summary>
    public bool ConnectResult { get; set; } = true;

    /// <summary>
    /// Result returned by <see cref="TrySetHidden"/> after a successful connect.
    /// </summary>
    public bool SetHiddenResult { get; set; } = true;

    /// <summary>
    /// Result returned by <see cref="TryGetStatus"/> after a successful connect.
    /// </summary>
    public bool StatusResult { get; set; } = true;

    /// <summary>
    /// Status value returned by <see cref="TryGetStatus"/>.
    /// </summary>
    public bool StatusHidden { get; set; }

    /// <summary>
    /// Whether the fake currently considers itself connected.
    /// </summary>
    public bool Connected { get; private set; }

    /// <summary>
    /// Increments after each successful connect.
    /// </summary>
    public long ConnectionGeneration { get; private set; }

    /// <summary>
    /// Error text returned by a failed connect.
    /// </summary>
    public string ConnectError { get; set; } = string.Empty;

    /// <inheritdoc/>
    public bool TryConnect(out string error)
    {
        ConnectCalls++;
        error = ConnectError;
        Connected = ConnectResult;
        if (ConnectResult)
        {
            ConnectionGeneration++;
        }

        return ConnectResult;
    }

    /// <inheritdoc/>
    public bool TryGetStatus(string id, out bool hidden, out string error)
    {
        StatusCalls.Add(id);
        hidden = StatusHidden;
        error = string.Empty;
        return StatusResult && Connected;
    }

    /// <inheritdoc/>
    public bool TrySetHidden(string id, bool hidden, out string error)
    {
        if (!Connected)
        {
            error = "not connected";
            return false;
        }

        if (!SetHiddenResult)
        {
            error = "failed";
            return false;
        }

        (hidden ? HideCalls : UnhideCalls).Add(id);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Simulates a dropped connection.
    /// </summary>
    public void Disconnect() => Connected = false;

    /// <summary>
    /// Simulates a helper restart: connected again, new generation, state lost.
    /// </summary>
    public void Restart()
    {
        Connected = true;
        ConnectionGeneration++;
        StatusHidden = false;
    }

    /// <inheritdoc/>
    public void Dispose() => DisposeCalls++;
}