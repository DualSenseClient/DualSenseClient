namespace DualSenseClient.HidHide;

/// <summary>
/// Transport used by <see cref="LinuxControllerHidingService"/> to talk to the root helper.
/// </summary>
internal interface ILinuxHidingClient : IDisposable
{
    /// <summary>
    /// Increments after each successful connection so the service can detect a helper restart.
    /// </summary>
    long ConnectionGeneration { get; }

    /// <summary>
    /// Connects and performs the protocol handshake.
    /// </summary>
    /// <param name="error">Error text when the connection fails.</param>
    /// <returns><c>true</c> when connected and the protocol is supported; otherwise <c>false</c>.</returns>
    bool TryConnect(out string error);

    /// <summary>
    /// Queries whether a controller is hidden.
    /// </summary>
    /// <param name="id">Controller ID.</param>
    /// <param name="hidden">Whether the controller is hidden.</param>
    /// <param name="error">Error text when the request fails.</param>
    /// <returns><c>true</c> when the helper answered; otherwise <c>false</c>.</returns>
    bool TryGetStatus(string id, out bool hidden, out string error);

    /// <summary>
    /// Requests a hide or unhide operation.
    /// </summary>
    /// <param name="id">Controller ID.</param>
    /// <param name="hidden">Whether to hide the controller.</param>
    /// <param name="error">Error text when the request fails.</param>
    /// <returns><c>true</c> when the helper applied the operation; otherwise <c>false</c>.</returns>
    bool TrySetHidden(string id, bool hidden, out string error);
}