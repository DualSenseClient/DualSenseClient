using System.Net.Sockets;
using System.Text;

namespace DualSenseClient.HidHide;

/// <summary>
/// Unix-socket transport for the Linux hiding helper.
/// </summary>
internal sealed class UnixSocketHidingClient : ILinuxHidingClient
{
    /// <summary>
    /// Handshake timeout in milliseconds.
    /// </summary>
    private const int HandshakeTimeoutMs = 3000;

    /// <summary>
    /// Status request timeout in milliseconds.
    /// </summary>
    private const int StatusTimeoutMs = 2000;

    /// <summary>
    /// Mutating request timeout in milliseconds; rebinds can take several seconds.
    /// </summary>
    private const int MutationTimeoutMs = 15000;

    /// <summary>
    /// Path of the helper socket.
    /// </summary>
    private readonly string _socketPath;

    /// <summary>
    /// Guards the connection state.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Connected socket, or <c>null</c> when disconnected.
    /// </summary>
    private Socket? _socket;

    /// <summary>
    /// Network stream over <see cref="_socket"/>.
    /// </summary>
    private NetworkStream? _stream;

    /// <summary>
    /// Line reader over <see cref="_stream"/>.
    /// </summary>
    private StreamReader? _reader;

    /// <summary>
    /// Line writer over <see cref="_stream"/>.
    /// </summary>
    private StreamWriter? _writer;

    /// <summary>
    /// Whether the client has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Increments after each successful connection.
    /// </summary>
    private long _connectionGeneration;

    /// <inheritdoc/>
    public long ConnectionGeneration
    {
        get
        {
            return Interlocked.Read(ref _connectionGeneration);
        }
    }

    /// <summary>
    /// Creates a client for the given helper socket.
    /// </summary>
    /// <param name="socketPath">Unix socket path.</param>
    public UnixSocketHidingClient(string socketPath)
    {
        _socketPath = socketPath;
    }

    /// <inheritdoc/>
    public bool TryConnect(out string error)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                error = "client disposed";
                return false;
            }

            if (_socket is not null)
            {
                error = string.Empty;
                return true;
            }

            return ConnectLocked(out error);
        }
    }

    /// <inheritdoc/>
    public bool TryGetStatus(string id, out bool hidden, out string error)
    {
        lock (_sync)
        {
            hidden = false;
            if (!TryExchangeLocked(HidingProtocol.StatusRequest(id), StatusTimeoutMs,
                    out string response, out error))
            {
                return false;
            }

            if (HidingProtocol.TryParseStatusResponse(response, out hidden))
            {
                error = string.Empty;
                return true;
            }

            error = $"unexpected status response: {response}";
            return false;
        }
    }

    /// <inheritdoc/>
    public bool TrySetHidden(string id, bool hidden, out string error)
    {
        lock (_sync)
        {
            string request = hidden ? HidingProtocol.HideRequest(id) : HidingProtocol.UnhideRequest(id);
            if (!TryExchangeLocked(request, MutationTimeoutMs, out string response, out error))
            {
                return false;
            }

            bool ok = HidingProtocol.TryParseCommandResponse(response, out string commandError);
            error = ok ? string.Empty : commandError;
            return ok;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CloseLocked();
        }
    }

    /// <summary>
    /// Connects and performs the handshake while the caller holds <see cref="_sync"/>.
    /// </summary>
    /// <param name="error">Error text when the connection fails.</param>
    /// <returns><c>true</c> when connected and the protocol matches.</returns>
    private bool ConnectLocked(out string error)
    {
        error = string.Empty;
        try
        {
            Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            {
                SendTimeout = HandshakeTimeoutMs,
                ReceiveTimeout = HandshakeTimeoutMs
            };
            _socket = socket;
            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));

            NetworkStream stream = new NetworkStream(socket, true);
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            _stream = stream;
            _reader = reader;
            _writer = writer;

            writer.WriteLine(HidingProtocol.HelloRequest());
            string? line = reader.ReadLine();
            if (line is null)
            {
                error = "helper closed the connection during handshake";
                CloseLocked();
                return false;
            }

            if (!HidingProtocol.TryParseHelloResponse(line, out int protocol, out _))
            {
                error = $"unexpected handshake response: {line}";
                CloseLocked();
                return false;
            }

            if (protocol != HidingProtocol.Version)
            {
                error = $"unsupported helper protocol {protocol}";
                CloseLocked();
                return false;
            }

            Interlocked.Increment(ref _connectionGeneration);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            error = ex.Message;
            CloseLocked();
            return false;
        }
    }

    /// <summary>
    /// Sends one request and reads one response, retrying once after a broken connection.
    /// </summary>
    /// <param name="request">Request line.</param>
    /// <param name="timeoutMs">Timeout for this request in milliseconds.</param>
    /// <param name="response">Response line.</param>
    /// <param name="error">Error text when both attempts fail.</param>
    /// <returns><c>true</c> when a response was read.</returns>
    private bool TryExchangeLocked(string request, int timeoutMs, out string response,
        out string error)
    {
        response = string.Empty;
        error = string.Empty;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (!EnsureConnectedLocked(out error))
            {
                return false;
            }

            try
            {
                _socket!.SendTimeout = timeoutMs;
                _socket.ReceiveTimeout = timeoutMs;
                _writer!.WriteLine(request);
                string? line = _reader!.ReadLine();
                if (line is null)
                {
                    throw new IOException("helper closed the connection");
                }

                response = line;
                return true;
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
            {
                error = ex.Message;
                CloseLocked();
            }
        }

        return false;
    }

    /// <summary>
    /// Connects if needed while the caller holds <see cref="_sync"/>.
    /// </summary>
    /// <param name="error">Error text when the connection fails.</param>
    /// <returns><c>true</c> when connected.</returns>
    private bool EnsureConnectedLocked(out string error)
    {
        if (_socket is not null)
        {
            error = string.Empty;
            return true;
        }

        return ConnectLocked(out error);
    }

    /// <summary>
    /// Closes the current connection while the caller holds <see cref="_sync"/>.
    /// </summary>
    private void CloseLocked()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _socket?.Dispose();
        _writer = null;
        _reader = null;
        _stream = null;
        _socket = null;
    }
}