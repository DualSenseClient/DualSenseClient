using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DualSenseClient.Emulation.Core;

namespace DualSenseClient.Emulation.UsbIp;

/// <summary>
/// Configuration of the <see cref="UsbIpServer"/>: the endpoint the listener binds, the time
/// provider that schedules the interrupt IN pacers and the logging seam.
/// </summary>
public sealed class UsbIpServerOptions
{
    /// <summary>Default USB/IP port of the listener (3241).</summary>
    private const int DefaultPort = 3241;

    /// <summary>
    /// Endpoint the listener binds to; defaults to loopback 127.0.0.1:3241. Use a port of 0 to let
    /// the operating system pick a free port and read it back from
    /// <see cref="UsbIpServer.BoundEndPoint"/>.
    /// </summary>
    public IPEndPoint? ListenEndPoint { get; set; } = new IPEndPoint(IPAddress.Loopback, DefaultPort);

    /// <summary>
    /// Provider that schedules the interrupt IN pacers; <see langword="null"/> selects the system
    /// provider. Tests inject a deterministic manual provider.
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>Logging seam; <see langword="null"/> disables logging.</summary>
    public IEmulationLogger? Logger { get; set; }

    /// <summary>
    /// Interval between the ticks of one interrupt IN pacer; defaults to 1 ms for the bring-up
    /// persona. Must be positive.
    /// </summary>
    public TimeSpan InterruptInterval { get; set; } = TimeSpan.FromMilliseconds(1);
}

/// <summary>
/// USB/IP server of the Emulation engine: exports the registered <see cref="VirtualUsbDevice"/>
/// instances over a loopback TCP listener. One session serves one client connection: the
/// OP_REQ_DEVLIST / OP_REQ_IMPORT handshake followed by the URB loop (control, interrupt and bulk
/// transfers plus unlink). Interrupt IN transfers are queued per endpoint and completed by a pacer
/// with a fresh <see cref="VirtualUsbDevice.ProvideInputReport"/> payload per tick; every outbound
/// frame of a session goes through one serialized writer. Devices can be added and removed while
/// the server runs, and a failing session never stops the listener.
/// </summary>
public sealed class UsbIpServer : IDisposable
{
    /// <summary>Status of an OP_REP_IMPORT reply that rejected the import (kernel doc: 0 = OK, 1 = error).</summary>
    private const int ImportFailureStatus = 1;

    /// <summary>Status of a RET_UNLINK that cancelled a pending URB (-ECONNRESET, kernel doc).</summary>
    private const int EconnResetStatus = -104;

    /// <summary>number_of_packets field of a RET_SUBMIT answering a non-isochronous transfer (0xFFFFFFFF).</summary>
    private const uint NonIsoNumberOfPackets = uint.MaxValue;

    /// <summary>
    /// Largest transfer buffer length a client may declare. A USB/IP session has no legitimate
    /// use for a larger transfer and a garbage frame must not trigger an unbounded allocation, so
    /// a larger value closes the session.
    /// </summary>
    private const int MaxTransferBufferLength = 1024 * 1024;

    /// <summary>Direction bit of an IN endpoint address (bEndpointAddress bit 7).</summary>
    private const byte DirectionInMask = 0x80;

    /// <summary>Mask of the endpoint number bits of an endpoint address (bEndpointAddress bits 3..0).</summary>
    private const byte EndpointNumberMask = 0x0F;

    /// <summary>How long <see cref="Stop"/> waits for the accept loop and the session loops to end.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Endpoint used when <see cref="UsbIpServerOptions.ListenEndPoint"/> is not set.</summary>
    private static readonly IPEndPoint DefaultListenEndPoint = new IPEndPoint(IPAddress.Loopback, 3241);

    private readonly UsbIpServerOptions _options;

    private readonly IEmulationLogger? _logger;

    private readonly TimeProvider _timeProvider;

    private readonly TimeSpan _interruptInterval;

    private readonly object _lifecycleLock = new object();

    private readonly object _devicesLock = new object();

    private readonly object _sessionsLock = new object();

    private readonly List<DeviceRegistration> _devices = new List<DeviceRegistration>();

    private readonly List<Session> _sessions = new List<Session>();

    private readonly List<Task> _sessionTasks = new List<Task>();

    private TcpListener? _listener;

    private CancellationTokenSource? _acceptCancellation;

    private Task? _acceptTask;

    private IPEndPoint? _boundEndPoint;

    private bool _isStarted;

    private bool _isDisposed;

    /// <summary>
    /// Initializes a server that has not started listening yet.
    /// </summary>
    /// <param name="options">Server configuration; copied by reference.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="UsbIpServerOptions.InterruptInterval"/> is not positive.</exception>
    public UsbIpServer(UsbIpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.InterruptInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.InterruptInterval, "The interrupt interval must be positive.");
        }

        _options = options;
        _logger = options.Logger;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
        _interruptInterval = options.InterruptInterval;
    }

    /// <summary>Endpoint the listener is actually bound to; read <see cref="IPEndPoint.Port"/> to learn the port of a port-0 bind.</summary>
    /// <exception cref="InvalidOperationException">The server has not been started yet.</exception>
    public IPEndPoint BoundEndPoint
    {
        get
        {
            lock (_lifecycleLock)
            {
                if (_boundEndPoint is null)
                {
                    throw new InvalidOperationException("The server has not been started yet.");
                }

                return _boundEndPoint;
            }
        }
    }

    /// <summary>Bus ids of the registered devices, in registration order.</summary>
    public IReadOnlyList<string> BusIds
    {
        get
        {
            lock (_devicesLock)
            {
                string[] busIds = new string[_devices.Count];
                for (int i = 0; i < _devices.Count; i++)
                {
                    busIds[i] = _devices[i].BusId;
                }

                return busIds;
            }
        }
    }

    /// <summary>
    /// Starts accepting client connections on the configured endpoint; the listener survives every
    /// session failure until <see cref="Stop"/> is called.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is already started.</exception>
    /// <exception cref="ObjectDisposedException">The server has been disposed.</exception>
    public void Start()
    {
        IPEndPoint boundEndPoint;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isStarted)
            {
                throw new InvalidOperationException("The server is already started.");
            }

            TcpListener listener = new TcpListener(_options.ListenEndPoint ?? DefaultListenEndPoint);
            listener.Start();
            _listener = listener;
            _boundEndPoint = (IPEndPoint)listener.LocalEndpoint;
            _acceptCancellation = new CancellationTokenSource();
            _acceptTask = AcceptLoopAsync(listener, _acceptCancellation.Token);
            _isStarted = true;
            boundEndPoint = _boundEndPoint;
        }

        _logger?.Log(EmulationLogLevel.Information, $"USB/IP server listening on {boundEndPoint}.");
    }

    /// <summary>
    /// Stops accepting connections, tears down every live session (pending URBs are cancelled and
    /// pacers stop) and waits for the session loops to end. Stopping a server that is not running
    /// does nothing.
    /// </summary>
    public void Stop()
    {
        TcpListener? listener;
        CancellationTokenSource? acceptCancellation;
        Task? acceptTask;
        lock (_lifecycleLock)
        {
            if (!_isStarted)
            {
                return;
            }

            _isStarted = false;
            listener = _listener;
            _listener = null;
            acceptCancellation = _acceptCancellation;
            _acceptCancellation = null;
            acceptTask = _acceptTask;
            _acceptTask = null;
        }

        acceptCancellation?.Cancel();
        listener?.Stop();
        try
        {
            acceptTask?.Wait(ShutdownTimeout);
        }
        catch (AggregateException)
        {
            // The accept loop only ends through cancellation; a fault would already be logged.
        }

        Session[] sessions;
        Task[] sessionTasks;
        lock (_sessionsLock)
        {
            sessions = _sessions.ToArray();
            sessionTasks = _sessionTasks.ToArray();
        }

        foreach (Session session in sessions)
        {
            session.Shutdown();
        }

        try
        {
            Task.WaitAll(sessionTasks, ShutdownTimeout);
        }
        catch (AggregateException)
        {
            // Session loops contain their failures; this only guards a pathological fault.
        }

        acceptCancellation?.Dispose();
        _logger?.Log(EmulationLogLevel.Information, "USB/IP server stopped.");
    }

    /// <summary>
    /// Registers <paramref name="device"/> under <paramref name="busId"/>. The bus number of the
    /// device entry is the numeric prefix of the bus id (the facade assigns bus ids of the form
    /// "&lt;busnum&gt;-1"), and the device number is 1 for every exported device.
    /// </summary>
    /// <param name="busId">Bus id the clients use to import the device, e.g. "1-1".</param>
    /// <param name="device">Device to export.</param>
    /// <exception cref="ArgumentException"><paramref name="busId"/> is empty, does not start with its bus number, or is already registered.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public void AddDevice(string busId, VirtualUsbDevice device)
    {
        ArgumentException.ThrowIfNullOrEmpty(busId);
        ArgumentNullException.ThrowIfNull(device);
        uint busNum = ParseBusNumber(busId);
        UsbIpDeviceInfo info = CreateDeviceInfo(busId, busNum, device);
        lock (_devicesLock)
        {
            foreach (DeviceRegistration registration in _devices)
            {
                if (string.Equals(registration.BusId, busId, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"The bus id \"{busId}\" is already registered.", nameof(busId));
                }
            }

            _devices.Add(new DeviceRegistration(busId, device, info));
        }
    }

    /// <summary>
    /// Unregisters the device registered under <paramref name="busId"/>. A session that already
    /// imported the device keeps serving it until it ends.
    /// </summary>
    /// <param name="busId">Bus id to unregister.</param>
    /// <returns><see langword="true"/> when a device was unregistered; <see langword="false"/> when no device has that bus id.</returns>
    /// <exception cref="ArgumentException"><paramref name="busId"/> is empty.</exception>
    public bool RemoveDevice(string busId)
    {
        ArgumentException.ThrowIfNullOrEmpty(busId);
        lock (_devicesLock)
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                if (string.Equals(_devices[i].BusId, busId, StringComparison.Ordinal))
                {
                    _devices.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Stops the server and releases its listener; every session is torn down like in
    /// <see cref="Stop"/>. Disposing an already disposed server does nothing.
    /// </summary>
    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        Stop();
    }

    /// <summary>
    /// Parses the bus number the facade encoded into a bus id ("&lt;busnum&gt;-&lt;port&gt;").
    /// </summary>
    /// <param name="busId">Bus id to parse.</param>
    /// <returns>The bus number.</returns>
    /// <exception cref="ArgumentException">The bus id does not start with a bus number.</exception>
    private static uint ParseBusNumber(string busId)
    {
        int dashIndex = busId.IndexOf('-');
        ReadOnlySpan<char> prefix = dashIndex < 0 ? busId.AsSpan() : busId.AsSpan(0, dashIndex);
        if (!uint.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out uint busNum))
        {
            throw new ArgumentException($"The bus id \"{busId}\" must start with its bus number, e.g. \"1-1\".", nameof(busId));
        }

        return busNum;
    }

    /// <summary>
    /// Builds the handshake entry of one device: one interface record per distinct interface
    /// number using its alternate setting 0 class triple, the configuration value from the
    /// configuration descriptor and the export speed from <see cref="VirtualUsbDevice.Speed"/>.
    /// </summary>
    /// <param name="busId">Bus id of the device.</param>
    /// <param name="busNum">Bus number parsed from the bus id.</param>
    /// <param name="device">Device to build the handshake entry for.</param>
    /// <returns>The device entry written into OP_REP_DEVLIST and OP_REP_IMPORT.</returns>
    private static UsbIpDeviceInfo CreateDeviceInfo(string busId, uint busNum, VirtualUsbDevice device)
    {
        UsbDescriptorSet descriptors = device.Descriptors;
        UsbIpInterfaceInfo[] interfaces = new UsbIpInterfaceInfo[descriptors.InterfaceCount];
        for (int i = 0; i < descriptors.InterfaceNumbers.Count; i++)
        {
            byte interfaceNumber = descriptors.InterfaceNumbers[i];
            if (!descriptors.TryGetInterface(interfaceNumber, 0, out UsbInterfaceInfo interfaceInfo))
            {
                throw new InvalidDataException($"Interface {interfaceNumber} declares no alternate setting 0.");
            }

            interfaces[i] = new UsbIpInterfaceInfo
            {
                Class = interfaceInfo.Class,
                SubClass = interfaceInfo.SubClass,
                Protocol = interfaceInfo.Protocol
            };
        }

        return new UsbIpDeviceInfo
        {
            BusId = busId,
            BusNum = busNum,
            DevNum = 1, // every exported device is a single-port virtual device
            Speed = device.Speed,
            IdVendor = descriptors.IdVendor,
            IdProduct = descriptors.IdProduct,
            BcdDevice = descriptors.BcdDevice,
            DeviceClass = descriptors.DeviceClass,
            DeviceSubClass = descriptors.DeviceSubClass,
            DeviceProtocol = descriptors.DeviceProtocol,
            ConfigurationValue = descriptors.ConfigurationValue,
            Interfaces = interfaces
        };
    }

    /// <summary>Returns the registration of <paramref name="busId"/>, or <see langword="null"/> when no device has that bus id.</summary>
    /// <param name="busId">Bus id to look up.</param>
    /// <returns>The registration or <see langword="null"/>.</returns>
    private DeviceRegistration? FindRegistration(string busId)
    {
        lock (_devicesLock)
        {
            foreach (DeviceRegistration registration in _devices)
            {
                if (string.Equals(registration.BusId, busId, StringComparison.Ordinal))
                {
                    return registration;
                }
            }

            return null;
        }
    }

    /// <summary>Returns a snapshot of the handshake entries of every registered device.</summary>
    /// <returns>The device entries in registration order.</returns>
    private UsbIpDeviceInfo[] GetDeviceInfos()
    {
        lock (_devicesLock)
        {
            UsbIpDeviceInfo[] infos = new UsbIpDeviceInfo[_devices.Count];
            for (int i = 0; i < _devices.Count; i++)
            {
                infos[i] = _devices[i].Info;
            }

            return infos;
        }
    }

    /// <summary>
    /// Accepts client connections until the server stops; every accepted client runs in its own
    /// session task, and a finished session never stops the loop.
    /// </summary>
    /// <param name="listener">Bound listener to accept from.</param>
    /// <param name="cancellationToken">Token cancelled by <see cref="Stop"/>.</param>
    /// <returns>A task that ends when the listener is stopped.</returns>
    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                break; // the server is stopping
            }

            client.NoDelay = true;
            Session session = new Session(this, client, cancellationToken);
            Task sessionTask = session.RunAsync();
            lock (_sessionsLock)
            {
                _sessionTasks.RemoveAll(task => task.IsCompleted);
                _sessionTasks.Add(sessionTask);
                _sessions.Add(session);
            }
        }
    }

    /// <summary>Removes a finished session from the live-session list.</summary>
    /// <param name="session">Session that ended.</param>
    private void RemoveSession(Session session)
    {
        lock (_sessionsLock)
        {
            _sessions.Remove(session);
        }
    }

    /// <summary>One registered device with its pre-built handshake entry.</summary>
    private sealed class DeviceRegistration
    {
        /// <summary>Creates a registration.</summary>
        /// <param name="busId">Bus id of the device.</param>
        /// <param name="device">Exported device.</param>
        /// <param name="info">Handshake entry of the device.</param>
        public DeviceRegistration(string busId, VirtualUsbDevice device, UsbIpDeviceInfo info)
        {
            BusId = busId;
            Device = device;
            Info = info;
        }

        /// <summary>Bus id of the device.</summary>
        public string BusId { get; }

        /// <summary>Exported device.</summary>
        public VirtualUsbDevice Device { get; }

        /// <summary>Handshake entry of the device.</summary>
        public UsbIpDeviceInfo Info { get; }
    }

    /// <summary>
    /// One client connection: the handshake, the URB loop and the per-session state (queued
    /// interrupt IN transfers with their pacers). Every outbound frame goes through
    /// <see cref="Send"/>, which serializes the writers with a semaphore.
    /// </summary>
    private sealed class Session
    {
        private readonly UsbIpServer _server;

        private readonly TcpClient _client;

        private readonly NetworkStream _stream;

        private readonly CancellationToken _cancellationToken;

        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        private readonly byte[] _handshakeBuffer = new byte[UsbIpProtocol.OpCommonSize + UsbIpHandshake.BusIdSize];

        private readonly byte[] _urbHeaderBuffer = new byte[UsbIpProtocol.HeaderSize];

        /// <summary>One pacer and queue per interrupt IN endpoint of the imported device; written once on import, only read afterwards.</summary>
        private readonly Dictionary<byte, InterruptEndpoint> _interruptInEndpoints = new Dictionary<byte, InterruptEndpoint>();

        private readonly List<EndpointPacer> _pacers = new List<EndpointPacer>();

        private VirtualUsbDevice? _device;

        /// <summary>Creates the session of one accepted client.</summary>
        /// <param name="server">Owning server.</param>
        /// <param name="client">Accepted client connection.</param>
        /// <param name="cancellationToken">Token cancelled by <see cref="UsbIpServer.Stop"/>.</param>
        public Session(UsbIpServer server, TcpClient client, CancellationToken cancellationToken)
        {
            _server = server;
            _client = client;
            _stream = client.GetStream();
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Runs the session: the handshake, then the URB loop, then the teardown. Every failure is
        /// contained here - an unexpected one is logged at <see cref="EmulationLogLevel.Error"/> -
        /// so a broken client can never end the listener.
        /// </summary>
        /// <returns>A task that completes after the session is fully torn down.</returns>
        public async Task RunAsync()
        {
            try
            {
                _server._logger?.Log(EmulationLogLevel.Debug, $"Session accepted from {_client.Client.RemoteEndPoint}.");
                if (await HandshakeAsync().ConfigureAwait(false))
                {
                    await RunUrbLoopAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _server._logger?.Log(EmulationLogLevel.Debug, "Session stopped by the server.");
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                _server._logger?.Log(EmulationLogLevel.Debug, $"Session connection ended: {exception.Message}");
            }
            catch (Exception exception)
            {
                _server._logger?.Log(EmulationLogLevel.Error, $"Session failed: {exception}");
            }
            finally
            {
                Teardown();
            }
        }

        /// <summary>
        /// Closes the session immediately: used by <see cref="UsbIpServer.Stop"/>, which then waits
        /// for the session loop to run its teardown.
        /// </summary>
        public void Shutdown()
        {
            try
            {
                _stream.Dispose();
                _client.Dispose();
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                // The socket is already gone; teardown runs on the session loop.
            }
        }

        /// <summary>
        /// Serves the message-level handshake until the client imports a device or the connection
        /// ends: OP_REQ_DEVLIST is answered and the first valid OP_REQ_IMPORT either succeeds into
        /// the URB loop, or is rejected with a status-1 OP_REP_IMPORT after which the session ends.
        /// </summary>
        /// <returns><see langword="true"/> when a device was imported; <see langword="false"/> when the session must end.</returns>
        private async Task<bool> HandshakeAsync()
        {
            while (true)
            {
                if (!await ReadExactlyAsync(_handshakeBuffer.AsMemory(0, UsbIpProtocol.OpCommonSize)).ConfigureAwait(false))
                {
                    return false; // the client closed the connection
                }

                UsbIpHandshake.ReadOpCommon(_handshakeBuffer, out ushort version, out ushort operation, out _);
                if (version != UsbIpProtocol.Version)
                {
                    _server._logger?.Log(EmulationLogLevel.Warning, $"Session dropped: USB/IP version 0x{version:X4} is not supported.");
                    return false;
                }

                switch (operation)
                {
                    case UsbIpProtocol.OpRequestDevList:
                        Send(BuildDevListReply());
                        break;

                    case UsbIpProtocol.OpRequestImport:
                        return await ImportAsync().ConfigureAwait(false);

                    default:
                        _server._logger?.Log(EmulationLogLevel.Warning, $"Session dropped: unexpected handshake operation 0x{operation:X4}.");
                        return false;
                }
            }
        }

        /// <summary>
        /// Reads the bus id field that follows the op_common header of OP_REQ_IMPORT, so the
        /// complete 40-byte message is consumed, and either answers with the device entry or
        /// rejects the import with status 1.
        /// </summary>
        /// <returns><see langword="true"/> when a device was imported; <see langword="false"/> when the session must end.</returns>
        private async Task<bool> ImportAsync()
        {
            if (!await ReadExactlyAsync(_handshakeBuffer.AsMemory(UsbIpProtocol.OpCommonSize, UsbIpHandshake.BusIdSize)).ConfigureAwait(false))
            {
                return false; // the client closed the connection mid-message
            }

            UsbIpHandshake.ReadImportBusId(_handshakeBuffer, out string busId);
            DeviceRegistration? registration = _server.FindRegistration(busId);
            if (registration is null)
            {
                byte[] failureReply = new byte[UsbIpProtocol.OpCommonSize];
                UsbIpHandshake.WriteImportReply(failureReply, ImportFailureStatus, null);
                Send(failureReply);
                _server._logger?.Log(EmulationLogLevel.Information, $"Import of unknown bus id \"{busId}\" rejected.");
                return false;
            }

            byte[] reply = new byte[UsbIpProtocol.OpCommonSize + UsbIpDeviceInfo.EntrySize];
            UsbIpHandshake.WriteImportReply(reply, UsbIpStatus.Success, registration.Info);
            Send(reply);

            // The pacers start after the reply so a tick can never overtake the handshake reply
            // on the wire; every later frame is written behind it by the session loop's ordering.
            _device = registration.Device;
            StartPacers();
            _server._logger?.Log(EmulationLogLevel.Information, $"Device \"{busId}\" imported.");
            return true;
        }

        /// <summary>
        /// Creates one pacer per interrupt IN endpoint of the imported device's alternate setting 0.
        /// Each pacer starts exactly once and is stopped by the session teardown.
        /// </summary>
        private void StartPacers()
        {
            VirtualUsbDevice device = _device!;
            foreach (UsbInterfaceInfo interfaceInfo in device.Descriptors.Interfaces)
            {
                if (interfaceInfo.AlternateSetting != 0)
                {
                    continue;
                }

                foreach (UsbEndpointInfo endpoint in interfaceInfo.Endpoints)
                {
                    byte address = endpoint.Address;
                    if (endpoint.Type != UsbEndpointType.Interrupt || (address & DirectionInMask) == 0 || _interruptInEndpoints.ContainsKey(address))
                    {
                        continue;
                    }

                    InterruptEndpoint interruptEndpoint = new InterruptEndpoint();
                    _interruptInEndpoints.Add(address, interruptEndpoint);
                    EndpointPacer pacer = new EndpointPacer(_server._timeProvider, _server._interruptInterval);
                    pacer.Start(() => CompleteNextInterruptIn(address));
                    _pacers.Add(pacer);
                }
            }

            _server._logger?.Log(EmulationLogLevel.Debug, $"Pacing {_pacers.Count} interrupt IN endpoint(s).");
        }

        /// <summary>
        /// Runs the URB loop: USBIP_CMD_SUBMIT frames are dispatched by endpoint and
        /// USBIP_CMD_UNLINK frames cancel queued transfers; any other command closes the session.
        /// </summary>
        /// <returns>A task that completes when the connection ends or a garbage command is read.</returns>
        private async Task RunUrbLoopAsync()
        {
            while (true)
            {
                if (!await ReadExactlyAsync(_urbHeaderBuffer).ConfigureAwait(false))
                {
                    return;
                }

                uint command = BinaryPrimitives.ReadUInt32BigEndian(_urbHeaderBuffer);
                switch (command)
                {
                    case UsbIpProtocol.CommandSubmit:
                        if (!await HandleSubmitAsync(UsbIpHeader.ReadSubmit(_urbHeaderBuffer)).ConfigureAwait(false))
                        {
                            return;
                        }

                        break;

                    case UsbIpProtocol.CommandUnlink:
                        HandleUnlink(UsbIpHeader.ReadUnlink(_urbHeaderBuffer));
                        break;

                    default:
                        _server._logger?.Log(EmulationLogLevel.Warning, $"Session dropped: unexpected URB command {command}.");
                        return;
                }
            }
        }

        /// <summary>
        /// Dispatches one submitted URB: control transfers to the device's control engine,
        /// interrupt IN transfers into the endpoint queue, and every other non-control transfer
        /// (interrupt OUT, bulk) to the immediate completion path. The wire ep field carries the
        /// bare endpoint number and the direction travels in the separate direction field (kernel
        /// doc CmdIntrIN capture: direction = 1, ep = 1), so an endpoint is resolved by number plus
        /// direction; the non-ISO start_frame and number_of_packets fields are deliberately never
        /// read.
        /// </summary>
        /// <param name="header">Parsed submit header.</param>
        /// <returns><see langword="true"/> to continue the session; <see langword="false"/> to close it.</returns>
        private async Task<bool> HandleSubmitAsync(UsbIpHeader header)
        {
            if (header.TransferBufferLength > MaxTransferBufferLength)
            {
                _server._logger?.Log(
                    EmulationLogLevel.Warning,
                    $"Session dropped: URB {header.SeqNum} declares {header.TransferBufferLength} transfer bytes.");
                return false;
            }

            int transferLength = (int)header.TransferBufferLength;
            byte endpointNumber = (byte)(header.Ep & EndpointNumberMask);
            bool deviceToHost = header.Direction == UsbIpDirection.In;
            if (endpointNumber == 0)
            {
                return await HandleControlAsync(header, transferLength).ConfigureAwait(false);
            }

            if (!TryFindEndpoint(endpointNumber, deviceToHost, out UsbEndpointInfo endpoint))
            {
                _server._logger?.Log(
                    EmulationLogLevel.Information,
                    $"URB {header.SeqNum} targets no {header.Direction} endpoint with number {endpointNumber}.");
                if (!deviceToHost && transferLength > 0)
                {
                    // The declared payload still follows the header; discard it to stay in sync.
                    byte[] discardedPayload = new byte[transferLength];
                    if (!await ReadExactlyAsync(discardedPayload).ConfigureAwait(false))
                    {
                        return false;
                    }
                }

                SendRetSubmit(header.SeqNum, UsbIpStatus.NoEntry, ReadOnlyMemory<byte>.Empty);
                return true;
            }

            if (endpoint.Type == UsbEndpointType.Interrupt && deviceToHost)
            {
                QueueInterruptIn(header, transferLength, endpoint.Address);
                return true;
            }

            return await HandleImmediateTransferAsync(header, transferLength, deviceToHost).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a control transfer of endpoint 0: the OUT payload follows the header and is
        /// consumed exactly, the setup packet drives the device's request engine, and the response
        /// (IN) or the accepted payload length (OUT) is answered with a RET_SUBMIT.
        /// </summary>
        /// <param name="header">Parsed submit header carrying the setup packet.</param>
        /// <param name="transferLength">Declared transfer buffer length.</param>
        /// <returns><see langword="true"/> to continue the session; <see langword="false"/> when the connection ends mid-payload.</returns>
        private async Task<bool> HandleControlAsync(UsbIpHeader header, int transferLength)
        {
            byte[] outData = Array.Empty<byte>();
            if (header.Direction == UsbIpDirection.Out && transferLength > 0)
            {
                outData = new byte[transferLength];
                if (!await ReadExactlyAsync(outData).ConfigureAwait(false))
                {
                    return false;
                }
            }

            byte[] response = header.Direction == UsbIpDirection.In ? new byte[transferLength] : Array.Empty<byte>();
            int result = _device!.HandleControlRequest(header.Setup, outData, response);
            if (result < 0)
            {
                SendRetSubmit(header.SeqNum, UsbIpStatus.Stall, ReadOnlyMemory<byte>.Empty);
                return true;
            }

            if (header.Direction == UsbIpDirection.Out)
            {
                SendRetSubmit(header.SeqNum, UsbIpStatus.Success, ReadOnlyMemory<byte>.Empty, transferLength);
                return true;
            }

            int actualLength = Math.Min(result, response.Length);
            SendRetSubmit(header.SeqNum, UsbIpStatus.Success, response.AsMemory(0, actualLength));
            return true;
        }

        /// <summary>
        /// Handles every non-control transfer that completes without pacing: an IN transfer pulls
        /// one <see cref="VirtualUsbDevice.ProvideInputReport"/> payload (truncated to the declared
        /// length, zero for no report) and an OUT transfer consumes its exact payload and pushes it
        /// to <see cref="VirtualUsbDevice.OnOutputReport"/>; both answer immediately.
        /// </summary>
        /// <param name="header">Parsed submit header.</param>
        /// <param name="transferLength">Declared transfer buffer length.</param>
        /// <param name="deviceToHost">Whether the endpoint is an IN endpoint.</param>
        /// <returns><see langword="true"/> to continue the session; <see langword="false"/> when the connection ends mid-payload.</returns>
        private async Task<bool> HandleImmediateTransferAsync(UsbIpHeader header, int transferLength, bool deviceToHost)
        {
            VirtualUsbDevice device = _device!;
            if (deviceToHost)
            {
                byte[] payload = new byte[transferLength];
                int count = device.ProvideInputReport(payload);
                int actualLength = Math.Clamp(count, 0, payload.Length);
                SendRetSubmit(header.SeqNum, UsbIpStatus.Success, payload.AsMemory(0, actualLength));
                return true;
            }

            byte[] outData = new byte[transferLength];
            if (transferLength > 0 && !await ReadExactlyAsync(outData).ConfigureAwait(false))
            {
                return false;
            }

            device.OnOutputReport(outData);
            SendRetSubmit(header.SeqNum, UsbIpStatus.Success, ReadOnlyMemory<byte>.Empty, transferLength);
            return true;
        }

        /// <summary>
        /// Queues one interrupt IN URB for completion by the pacer of the resolved endpoint
        /// address (bEndpointAddress, the pacer dictionary's key).
        /// </summary>
        /// <param name="header">Parsed submit header.</param>
        /// <param name="transferLength">Declared transfer buffer length.</param>
        /// <param name="endpointAddress">Resolved bEndpointAddress of the URB's endpoint.</param>
        private void QueueInterruptIn(UsbIpHeader header, int transferLength, byte endpointAddress)
        {
            if (!_interruptInEndpoints.TryGetValue(endpointAddress, out InterruptEndpoint? endpoint))
            {
                SendRetSubmit(header.SeqNum, UsbIpStatus.NoEntry, ReadOnlyMemory<byte>.Empty);
                return;
            }

            endpoint.Enqueue(new UsbTransfer
            {
                SeqNum = header.SeqNum,
                Direction = UsbIpDirection.In,
                EndpointAddress = endpointAddress,
                MaxTransferLength = transferLength
            });
        }

        /// <summary>
        /// One pacer tick of an interrupt IN endpoint: completes the oldest queued URB with a fresh
        /// input report, and keeps the URB queued (at the front, preserving order) when the device
        /// reports nothing.
        /// </summary>
        /// <param name="endpointAddress">Endpoint address of the ticked pacer.</param>
        private void CompleteNextInterruptIn(byte endpointAddress)
        {
            if (!_interruptInEndpoints.TryGetValue(endpointAddress, out InterruptEndpoint? endpoint))
            {
                return;
            }

            if (!endpoint.TryTakeNext(out UsbTransfer transfer))
            {
                return;
            }

            try
            {
                byte[] payload = new byte[transfer.MaxTransferLength];
                VirtualUsbDevice device = _device!;
                int count;
                try
                {
                    count = device.ProvideInputReport(payload);
                }
                catch (Exception exception)
                {
                    // A persona bug must not be swallowed by the pacer's catch-all with the dequeued
                    // URB unanswered: the client would read forever. Answer it and keep the session.
                    _server._logger?.Log(EmulationLogLevel.Error, $"Interrupt IN report failed: {exception}");
                    SendRetSubmit(transfer.SeqNum, UsbIpStatus.Shutdown, ReadOnlyMemory<byte>.Empty);
                    return;
                }

                if (count <= 0)
                {
                    endpoint.Block(transfer); // no report yet: the URB stays pending
                    return;
                }

                int actualLength = Math.Min(count, payload.Length);
                SendRetSubmit(transfer.SeqNum, UsbIpStatus.Success, payload.AsMemory(0, actualLength));
            }
            catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
            {
                _server._logger?.Log(EmulationLogLevel.Debug, $"Interrupt IN completion dropped: {exception.Message}");
            }
        }

        /// <summary>
        /// Handles one USBIP_CMD_UNLINK: a seqnum found in an endpoint queue is cancelled and
        /// answered with status -104 (-ECONNRESET) and never completes; a seqnum that is not
        /// pending (already completed or never queued) is answered with status 0.
        /// </summary>
        /// <param name="header">Parsed unlink header carrying the seqnum to cancel.</param>
        private void HandleUnlink(UsbIpHeader header)
        {
            foreach (KeyValuePair<byte, InterruptEndpoint> entry in _interruptInEndpoints)
            {
                if (entry.Value.TryCancel(header.UnlinkSeqNum, out _))
                {
                    SendRetUnlink(header.SeqNum, EconnResetStatus);
                    return;
                }
            }

            SendRetUnlink(header.SeqNum, UsbIpStatus.Success);
        }

        /// <summary>
        /// Answers a submitted URB with a 48-byte RET_SUBMIT: the header pins number_of_packets to
        /// 0xFFFFFFFF for non-ISO transfers and leaves devid, direction, ep, start_frame,
        /// error_count and the padding zeroed; the payload of an IN transfer is appended.
        /// </summary>
        /// <param name="seqNum">Sequence number of the answered URB.</param>
        /// <param name="status">Completion status.</param>
        /// <param name="payload">Device-to-host payload appended to the header; empty for OUT transfers.</param>
        private void SendRetSubmit(uint seqNum, int status, ReadOnlyMemory<byte> payload) => SendRetSubmit(seqNum, status, payload, payload.Length);

        /// <summary>
        /// Answers a submitted URB with a 48-byte RET_SUBMIT and an explicit actual length; used by
        /// OUT transfers, whose transferred byte count is the consumed payload length even though
        /// the reply carries no payload.
        /// </summary>
        /// <param name="seqNum">Sequence number of the answered URB.</param>
        /// <param name="status">Completion status.</param>
        /// <param name="payload">Device-to-host payload appended to the header; empty for OUT transfers.</param>
        /// <param name="actualLength">Number of bytes actually transferred.</param>
        private void SendRetSubmit(uint seqNum, int status, ReadOnlyMemory<byte> payload, int actualLength)
        {
            byte[] frame = new byte[UsbIpProtocol.HeaderSize + payload.Length];
            UsbIpHeader ret = UsbIpHeader.CreateRetSubmit(seqNum, status, (uint)actualLength) with
            {
                NumberOfPackets = NonIsoNumberOfPackets
            };
            ret.WriteRetSubmit(frame);
            payload.CopyTo(frame.AsMemory(UsbIpProtocol.HeaderSize));
            Send(frame);
        }

        /// <summary>
        /// Answers a USBIP_CMD_UNLINK with a 48-byte RET_UNLINK whose devid, direction, ep and
        /// padding are zeroed.
        /// </summary>
        /// <param name="seqNum">Sequence number of the answered unlink request.</param>
        /// <param name="status">Completion status: 0 for a non-pending seqnum, -104 for a cancelled one.</param>
        private void SendRetUnlink(uint seqNum, int status)
        {
            byte[] frame = new byte[UsbIpProtocol.HeaderSize];
            UsbIpHeader ret = UsbIpHeader.CreateRetUnlink(seqNum, status);
            ret.WriteRetUnlink(frame);
            Send(frame);
        }

        /// <summary>
        /// Serializes <paramref name="frame"/> into the output stream through the session's write
        /// lock, so handshake replies and paced completions can never interleave.
        /// </summary>
        /// <param name="frame">Complete frame to write: header followed by any payload.</param>
        private void Send(byte[] frame)
        {
            _writeLock.Wait();
            try
            {
                _stream.Write(frame, 0, frame.Length);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>Builds the OP_REP_DEVLIST reply for every registered device.</summary>
        /// <returns>The complete reply bytes.</returns>
        private byte[] BuildDevListReply()
        {
            UsbIpDeviceInfo[] devices = _server.GetDeviceInfos();
            int replySize = UsbIpProtocol.OpCommonSize + 4;
            foreach (UsbIpDeviceInfo device in devices)
            {
                replySize += UsbIpDeviceInfo.DevListEntrySize(device.Interfaces.Count);
            }

            byte[] reply = new byte[replySize];
            int writtenLength = UsbIpHandshake.WriteDevListReply(reply, devices);
            if (writtenLength != replySize)
            {
                throw new InvalidOperationException($"The device list reply size changed while it was written ({writtenLength} of {replySize}).");
            }

            return reply;
        }

        /// <summary>
        /// Reads exactly <paramref name="buffer"/>.Length bytes into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">Buffer to fill.</param>
        /// <returns><see langword="true"/> on success; <see langword="false"/> when the client closed the connection first.</returns>
        private async Task<bool> ReadExactlyAsync(Memory<byte> buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await _stream.ReadAsync(buffer.Slice(offset), _cancellationToken).ConfigureAwait(false);
                if (count <= 0)
                {
                    return false;
                }

                offset += count;
            }

            return true;
        }

        /// <summary>
        /// Stops the pacers, cancels every pending queued transfer and releases the socket; runs
        /// once, after the session loop returns, whatever ended the session.
        /// </summary>
        private void Teardown()
        {
            foreach (EndpointPacer pacer in _pacers)
            {
                pacer.Stop();
            }

            _pacers.Clear();
            foreach (InterruptEndpoint endpoint in _interruptInEndpoints.Values)
            {
                endpoint.Clear();
            }

            // _writeLock is deliberately not disposed: a blocked Send waiter must keep its
            // documented SemaphoreSlim behaviour during a concurrent teardown.
            _stream.Dispose();
            _client.Dispose();
            _server._logger?.Log(EmulationLogLevel.Debug, "Session ended.");
            _server.RemoveSession(this);
        }

        /// <summary>
        /// Looks up the endpoint descriptor matching the wire pair of endpoint number and
        /// direction in the device's active alternate settings: the ep field carries the number and
        /// the direction field the direction, so the full endpoint address is rebuilt and resolved
        /// through <see cref="VirtualUsbDevice.TryResolveEndpoint"/>.
        /// </summary>
        /// <param name="endpointNumber">Bare endpoint number of the URB (ep field).</param>
        /// <param name="deviceToHost">Whether the URB's direction is IN.</param>
        /// <param name="endpoint">Receives the endpoint descriptor when found.</param>
        /// <returns><see langword="true"/> when the device declares the endpoint in an active alternate setting; otherwise <see langword="false"/>.</returns>
        private bool TryFindEndpoint(byte endpointNumber, bool deviceToHost, out UsbEndpointInfo endpoint)
        {
            byte endpointAddress = (byte)(endpointNumber | (deviceToHost ? DirectionInMask : 0));
            return _device!.TryResolveEndpoint(endpointAddress, deviceToHost, out endpoint);
        }
    }

    /// <summary>
    /// The pending interrupt IN transfers of one endpoint: a thread-safe FIFO queue plus the head
    /// that a tick kept queued because the device had no report. The blocked head stays logically
    /// in front of the queue, so completions keep URB order across empty ticks, and unlinking
    /// finds it like any other pending transfer.
    /// </summary>
    private sealed class InterruptEndpoint
    {
        private readonly object _gate = new object();

        private readonly UsbTransferQueue _queue = new UsbTransferQueue();

        private UsbTransfer? _blockedHead;

        /// <summary>Appends a transfer to the back of the queue.</summary>
        /// <param name="transfer">Transfer to enqueue.</param>
        public void Enqueue(UsbTransfer transfer)
        {
            lock (_gate)
            {
                _queue.Enqueue(transfer);
            }
        }

        /// <summary>
        /// Returns the oldest pending transfer and marks it in flight: the blocked head while one
        /// exists, otherwise the front of the queue.
        /// </summary>
        /// <param name="transfer">Receives the transfer when one is pending.</param>
        /// <returns><see langword="true"/> when a transfer was taken; otherwise <see langword="false"/>.</returns>
        public bool TryTakeNext(out UsbTransfer transfer)
        {
            lock (_gate)
            {
                if (_blockedHead is not null)
                {
                    transfer = _blockedHead;
                    _blockedHead = null;
                    return true;
                }

                return _queue.TryDequeue(out transfer);
            }
        }

        /// <summary>Keeps <paramref name="transfer"/> queued as the blocked head after a reportless tick.</summary>
        /// <param name="transfer">In-flight transfer that found no report.</param>
        public void Block(UsbTransfer transfer)
        {
            lock (_gate)
            {
                _blockedHead = transfer;
            }
        }

        /// <summary>
        /// Removes the pending transfer with <paramref name="seqNum"/> - the blocked head or
        /// anywhere in the queue - preserving the order of the remaining transfers.
        /// </summary>
        /// <param name="seqNum">Sequence number of the transfer to cancel.</param>
        /// <param name="transfer">Receives the cancelled transfer when the method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> when a pending transfer was removed; <see langword="false"/> when the seqnum is already completing or unknown.</returns>
        public bool TryCancel(uint seqNum, out UsbTransfer transfer)
        {
            lock (_gate)
            {
                if (_blockedHead is not null && _blockedHead.SeqNum == seqNum)
                {
                    transfer = _blockedHead;
                    _blockedHead = null;
                    return true;
                }

                return _queue.TryCancel(seqNum, out transfer);
            }
        }

        /// <summary>Drops every pending transfer.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _blockedHead = null;
                _queue.Clear();
            }
        }
    }
}