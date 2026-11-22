using System.Net.Sockets;
using System.Text;

namespace DualSenseClient.Tests.HidHide.TestSupport;

/// <summary>
/// Minimal Unix-socket helper used to exercise the hiding client protocol.
/// </summary>
internal sealed class FakeHidingServer : IDisposable
{
    /// <summary>
    /// Listener socket.
    /// </summary>
    private readonly Socket _listener;

    /// <summary>
    /// Produces a response for each request line.
    /// </summary>
    private readonly Func<string, string> _responder;

    /// <summary>
    /// Cancels the accept loop.
    /// </summary>
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    /// <summary>
    /// Accept-loop task.
    /// </summary>
    private readonly Task _acceptTask;

    /// <summary>
    /// Guards <see cref="_requests"/>, <see cref="_current"/>, and <see cref="_connectionCount"/>.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// Requests received so far.
    /// </summary>
    private readonly List<string> _requests = new List<string>();

    /// <summary>
    /// Currently accepted connection, if any.
    /// </summary>
    private Socket? _current;

    /// <summary>
    /// Number of accepted connections.
    /// </summary>
    private int _connectionCount;

    /// <summary>
    /// Socket path, removed on dispose.
    /// </summary>
    private readonly string _socketPath;

    /// <summary>
    /// Starts a server on the given Unix socket path.
    /// </summary>
    /// <param name="socketPath">Socket path to listen on.</param>
    /// <param name="responder">Response producer.</param>
    /// <returns>The running server.</returns>
    public static FakeHidingServer Start(string socketPath, Func<string, string> responder) => new FakeHidingServer(socketPath, responder);

    /// <summary>
    /// Binds the socket and starts accepting connections.
    /// </summary>
    /// <param name="socketPath">Socket path.</param>
    /// <param name="responder">Response producer.</param>
    private FakeHidingServer(string socketPath, Func<string, string> responder)
    {
        _socketPath = socketPath;
        _responder = responder;
        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        _listener.Listen(8);
        _acceptTask = Task.Run(AcceptLoop);
    }

    /// <summary>
    /// Requests received so far.
    /// </summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_sync)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>
    /// Number of accepted connections.
    /// </summary>
    public int ConnectionCount
    {
        get
        {
            lock (_sync)
            {
                return _connectionCount;
            }
        }
    }

    /// <summary>
    /// Drops the currently accepted connection; the accept loop continues.
    /// </summary>
    public void CloseConnection()
    {
        lock (_sync)
        {
            _current?.Close();
            _current = null;
        }
    }

    /// <summary>
    /// Stops the server and removes the socket file.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
        CloseConnection();
        try
        {
            _acceptTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        _cts.Dispose();
    }

    /// <summary>
    /// Accepts connections until cancelled, serving one request per line.
    /// </summary>
    private void AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                Socket client = _listener.Accept();
                lock (_sync)
                {
                    _current = client;
                    _connectionCount++;
                }

                using (client)
                using (NetworkStream stream = new NetworkStream(client, false))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))
                       {
                           AutoFlush = true,
                           NewLine = "\n"
                       })
                {
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        lock (_sync)
                        {
                            _requests.Add(line);
                        }

                        writer.WriteLine(_responder(line));
                    }
                }
            }
            catch (SocketException)
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (IOException)
            {
            }
            finally
            {
                lock (_sync)
                {
                    _current = null;
                }
            }
        }
    }
}