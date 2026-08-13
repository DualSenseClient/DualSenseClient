using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;
using SoundFlow.Abstracts;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Creates a virtual controller for the active DualSense when its profile enables
/// emulation. Owns the libVIIPER USB server, the virtual device lifecycle, the
/// HID exclusion that keeps the virtual device out of the app's own enumeration,
/// and — for DualSense emulation — the host-audio forwarder that plays host audio
/// through the physical controller.
/// </summary>
public interface IEmulationService : IDisposable
{
    /// <summary>
    /// The current emulation state, surfaced for the UI.
    /// </summary>
    EmulationStatus Status { get; }

    /// <summary>
    /// Raised whenever <see cref="Status"/> changes. May be raised on a background
    /// thread, so UI subscribers should dispatch to the UI thread.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Starts watching the active controller and creating virtual controllers
    /// according to the bound profile.
    /// </summary>
    void Start();

    /// <summary>
    /// Re-evaluates the active controller against the profile it currently uses,
    /// recreating the virtual controller when the emulation mode changed. Used when
    /// the user edits the emulation mode of the applied profile.
    /// </summary>
    void Refresh();

    /// <summary>
    /// Applies the forwarding volume and haptic strength to the active host-audio
    /// forwarder without recreating the virtual controller. No-op when DualSense
    /// emulation is not active.
    /// </summary>
    void SetForwardingAudioOptions(byte speakerVolume, float hapticStrength);

    /// <summary>
    /// Routes forwarded host audio to the physical controller's headset jack instead
    /// of its internal speaker without recreating the virtual controller. No-op when
    /// DualSense emulation is not active.
    /// </summary>
    void SetForwardingAudioOutput(bool headset);
}

/// <summary>
/// Default <see cref="IEmulationService"/> implementation.
/// </summary>
public sealed class EmulationService : IEmulationService
{
    /// <summary>
    /// How long to wait for the virtual device to appear in HID enumeration.
    /// </summary>
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Polling interval while waiting for the virtual device to appear.
    /// </summary>
    private static readonly TimeSpan DiscoveryPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Delay before (re)creating a virtual device after the previous one was removed,
    /// letting the USBIP client driver finish detaching the old device so the new
    /// device does not collide with it during attachment.
    /// </summary>
    private static readonly TimeSpan RecreationSettleDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Delay before retrying device creation after a transient failure (for example a
    /// detach/attach race when switching the emulation mode).
    /// </summary>
    private static readonly TimeSpan CreateRetryDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("EmulationService");

    /// <summary>
    /// Logger receiving the libVIIPER USB server's own log messages.
    /// </summary>
    private static readonly DualSenseClientLogger _nativeLog = DualSenseClientLogger.For("VIIPER");

    private readonly IControllerTracker _tracker;
    private readonly IHidDeviceEnumerator _enumerator;
    private readonly ProfileService _profiles;
    private readonly ControllerInfoService _controllerInfo;
    private readonly IVirtualControllerFactory _factory;
    private readonly AudioEngine _audioEngine;

    /// <summary>
    /// Guards the active controller/virtual controller pair and the server handles.
    /// </summary>
    private readonly Lock _sync = new Lock();

    private DualSenseDevice? _device;
    private IVirtualController? _virtual;

    /// <summary>
    /// Forwards host audio to the physical controller while DualSense emulation is
    /// active, or <c>null</c> in other modes. Lives and dies with <see cref="_virtual"/>.
    /// </summary>
    private ViiperDualSenseAudioForwarder? _forwarder;

    /// <summary>
    /// Captures the audio the host renders to the virtual DualSense and feeds it to
    /// <see cref="_forwarder"/>, or <c>null</c> when not forwarding. Lives and dies
    /// with <see cref="_virtual"/>; must be disposed before the virtual device is
    /// removed.
    /// </summary>
    private ViiperDualSenseAudioCapture? _capture;

    private nuint? _serverHandle;

    /// <summary>
    /// The USB bus owned by <see cref="_virtual"/>, removed together with it.
    /// </summary>
    private uint _busId;

    private bool _disposed;

    /// <summary>
    /// Keeps the native log callback delegate alive for the lifetime of the process.
    /// </summary>
    private readonly VIIPERLogCallback _nativeLogCallback = OnNativeLog;

    /// <summary>
    /// Incremented on every rebuild so stale background creations are discarded
    /// when the active controller or profile changes while one is in flight.
    /// </summary>
    private int _generation;

    /// <inheritdoc/>
    public EmulationStatus Status { get; private set; } = new EmulationStatus(EmulationMode.Off, false, null, null);

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Creates a new <see cref="EmulationService"/>.
    /// </summary>
    public EmulationService(IControllerTracker tracker, IHidDeviceEnumerator enumerator,
        ProfileService profiles, ControllerInfoService controllerInfo, IVirtualControllerFactory factory,
        AudioEngine audioEngine)
    {
        _tracker = tracker;
        _enumerator = enumerator;
        _profiles = profiles;
        _controllerInfo = controllerInfo;
        _factory = factory;
        _audioEngine = audioEngine;
    }

    /// <inheritdoc/>
    public void Start()
    {
        _tracker.ActiveControllerChanged += OnActiveControllerChanged;
        OnActiveControllerChanged(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _tracker.ActiveControllerChanged -= OnActiveControllerChanged;
        lock (_sync)
        {
            DeviceDisposeUnsubscribe();
            DisposeVirtualLocked();
            if (_serverHandle is { } serverHandle)
            {
                LibVIIPER.CloseUSBServer(serverHandle);
                _serverHandle = null;
            }
        }
    }

    /// <inheritdoc/>
    public void Refresh() => RebuildVirtualController();

    /// <inheritdoc/>
    public void SetForwardingAudioOptions(byte speakerVolume, float hapticStrength)
    {
        lock (_sync)
        {
            if (_forwarder is null)
            {
                return;
            }
            _forwarder.SpeakerVolume = speakerVolume;
            _forwarder.HapticStrength = Math.Clamp(hapticStrength, 0f, 2f);
        }
    }

    /// <inheritdoc/>
    public void SetForwardingAudioOutput(bool headset)
    {
        lock (_sync)
        {
            if (_forwarder is null)
            {
                return;
            }
            _forwarder.PlayToHeadset = headset;
        }
    }

    /// <summary>
    /// (Re)builds the virtual controller whenever the active controller changes.
    /// </summary>
    private void OnActiveControllerChanged(object? sender, EventArgs e) => RebuildVirtualController();

    /// <summary>
    /// Re-evaluates the active controller against the profile it currently uses:
    /// disposes the current virtual controller and (re)creates one for the new
    /// profile's emulation mode, unless the mode is off or no controller is active.
    /// </summary>
    private void RebuildVirtualController()
    {
        DualSenseDevice? device;
        bool removedDevice;
        int generation;
        lock (_sync)
        {
            generation = ++_generation;
            removedDevice = DisposeVirtualLocked();
            DeviceDisposeUnsubscribe();
            device = _device = _tracker.ActiveController as DualSenseDevice;
        }

        if (device is null)
        {
            SetStatus(new EmulationStatus(EmulationMode.Off, false, "No active controller", null));
            return;
        }

        Profile? profile = ResolveProfile(device);
        EmulationMode mode = profile?.Emulation?.Mode ?? EmulationMode.Off;
        if (mode == EmulationMode.Off)
        {
            SetStatus(new EmulationStatus(EmulationMode.Off, false, "Emulation is disabled for this profile", null));
            return;
        }

        SetStatus(new EmulationStatus(mode, false, "Creating virtual controller…", null, IsCreating: true));
        _ = CreateVirtualControllerAsync(device, mode, generation, removedDevice);
    }

    /// <summary>
    /// Runs the device creation + path discovery on a background thread so the
    /// tracker callback does not block on enumeration polling. A fresh USB bus is
    /// created for the virtual controller (and removed again when it is disposed),
    /// because libVIIPER auto-removes empty buses after the server's cleanup timeout,
    /// which would make a reused bus id invalid. When
    /// <paramref name="settleAfterRemoval"/> is set, a short delay is applied first so
    /// the USBIP client finishes detaching the previous device. Creation is retried
    /// once after <see cref="CreateRetryDelay"/> on failure.
    /// </summary>
    private async Task CreateVirtualControllerAsync(DualSenseDevice device, EmulationMode mode, int generation, bool settleAfterRemoval)
    {
        try
        {
            nuint serverHandle;
            lock (_sync)
            {
                if (!EnsureServerLocked())
                {
                    SetStatus(new EmulationStatus(mode, false, "Could not start the libVIIPER USB server", null));
                    return;
                }
                serverHandle = _serverHandle!.Value;
            }

            if (settleAfterRemoval)
            {
                await Task.Delay(RecreationSettleDelay);
            }

            EmulationSettings? emulation = ResolveProfile(device)?.Emulation;
            bool edge = emulation?.DeviceType == DualSenseVariant.Edge;
            (ushort vid, ushort pid) = GetDeviceIds(mode, edge);
            DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(device);
            HashSet<string> before = _enumerator.Enumerate(vid, pid)
                .Select(info => info.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            uint busId = 0;
            IVirtualController? virtualController = TryCreateOnFreshBus(serverHandle, mode, outputs, device.UsesVibrationV2, edge, ref busId);
            if (virtualController is null)
            {
                _log.Warning($"Failed to create the virtual {mode} device; retrying in {(int)CreateRetryDelay.TotalMilliseconds} ms");
                await Task.Delay(CreateRetryDelay);

                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(_device, device) || generation != _generation)
                    {
                        return;
                    }
                }
                virtualController = TryCreateOnFreshBus(serverHandle, mode, outputs, device.UsesVibrationV2, edge, ref busId);
            }

            if (virtualController is null)
            {
                SetStatus(new EmulationStatus(mode, false, "The native library could not create the virtual device. Check the VIIPER logs; the usbip-win2 driver must be installed (with Test Signing enabled) for auto-attachment", null));
                return;
            }

            string? path = await DiscoverVirtualPathAsync(vid, pid, device.Info.Path, before);
            if (path is null)
            {
                _log.Warning("Could not find the virtual device in HID enumeration; it will not be excluded");
            }
            else
            {
                virtualController.VirtualDevicePath = path;
                _enumerator.ExcludeDevice(path);
            }

            ViiperDualSenseAudioForwarder? forwarder = mode == EmulationMode.DualSense
                ? CreateAudioForwarder(outputs, emulation)
                : null;
            ViiperDualSenseAudioCapture? capture = null;
            if (forwarder is not null && virtualController is VirtualDualSenseController dualSense)
            {
                dualSense.OutputStateReceived += forwarder.UpdateGameOutputState;
                dualSense.RealtimeHapticsReceived += forwarder.UpdateGameHaptics;
                if (dualSense.DeviceHandle is { } deviceHandle)
                {
                    capture = new ViiperDualSenseAudioCapture(deviceHandle, forwarder);
                }
            }
            if (forwarder is not null)
            {
                _log.Info($"Host audio forwarding {(forwarder.Start() ? "started" : "unavailable")} for the virtual DualSense");
            }

            lock (_sync)
            {
                if (_disposed || !ReferenceEquals(_device, device) || generation != _generation)
                {
                    if (virtualController.VirtualDevicePath is { } stalePath)
                    {
                        _enumerator.RemoveExcludedDevice(stalePath);
                    }
                    capture?.Dispose();
                    forwarder?.Dispose();
                    virtualController.Dispose();
                    LibVIIPER.RemoveUSBBus(serverHandle, busId);
                    return;
                }
                _busId = busId;
                _virtual = virtualController;
                _forwarder = forwarder;
                _capture = capture;
                DeviceSubscribe();
            }

            SetStatus(new EmulationStatus(mode, true, null, virtualController.VirtualDevicePath));
        }
        catch (Exception ex)
        {
            _log.LogExceptionDetails(ex);
            SetStatus(new EmulationStatus(mode, false, ex.Message, null));
        }
    }

    /// <summary>
    /// Creates a fresh USB bus and attempts to create the virtual controller on it.
    /// On failure the partial bus is removed again so no empty or half-populated bus
    /// is left behind for the server to clean up asynchronously.
    /// </summary>
    /// <param name="serverHandle">The USB server hosting the bus.</param>
    /// <param name="mode">The requested emulation mode.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">Whether the physical controller uses the V2 report format.</param>
    /// <param name="edge">Whether the virtual DualSense should be an Edge variant (DualSense mode only).</param>
    /// <param name="busId">Receives the id of the created bus, valid only on success.</param>
    /// <returns>The created virtual controller, or <c>null</c> when creation failed.</returns>
    private IVirtualController? TryCreateOnFreshBus(nuint serverHandle, EmulationMode mode, DualSenseDeviceOutputs outputs, bool vibrationV2, bool edge, ref uint busId)
    {
        if (!LibVIIPER.CreateUSBBus(serverHandle, ref busId))
        {
            return null;
        }

        IVirtualController? virtualController = _factory.Create(mode, serverHandle, busId, outputs, vibrationV2, edge);
        if (virtualController?.DeviceHandle is null)
        {
            virtualController?.Dispose();
            virtualController = null;
            LibVIIPER.RemoveUSBBus(serverHandle, busId);
        }
        return virtualController;
    }

    /// <summary>
    /// Resolves the profile bound to the given controller, falling back to the
    /// default profile.
    /// </summary>
    private Profile? ResolveProfile(DualSenseDevice device)
    {
        string? bound = _controllerInfo.GetBoundProfileName(device.PairingInfo?.ClientMac, device.Info.Path);
        if (!string.IsNullOrEmpty(bound))
        {
            Profile? profile = _profiles.GetProfile(bound);
            if (profile is not null)
            {
                return profile;
            }
        }
        return _profiles.GetProfile(ProfileService.DefaultProfileName)
               ?? _profiles.Settings.Profiles.FirstOrDefault();
    }

    /// <summary>
    /// Creates the host-audio forwarder for the virtual DualSense: Bluetooth audio
    /// reports to the physical controller over the same outputs lane, and the USB
    /// UAC render endpoint for wired playback (when the pad exposes one). The
    /// profile's forwarding volume, haptic strength and speaker/headset route are
    /// applied on creation.
    /// </summary>
    private ViiperDualSenseAudioForwarder CreateAudioForwarder(DualSenseDeviceOutputs outputs, EmulationSettings? emulation)
    {
        DualSenseAudioEndpointFinder endpointFinder = new DualSenseAudioEndpointFinder(_audioEngine);
        DualSenseUsbAudioTarget usbTarget = new DualSenseUsbAudioTarget(_audioEngine, endpointFinder);
        ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(outputs, usbTarget);
        if (emulation is not null)
        {
            forwarder.SpeakerVolume = (byte)Math.Clamp(emulation.ForwardVolume, 0, 255);
            forwarder.HapticStrength = Math.Clamp(emulation.ForwardHapticStrength, 0, 200) / 100f;
            forwarder.PlayToHeadset = emulation.ForwardAudioOutput == EmulationAudioOutput.Headset;
        }
        return forwarder;
    }

    /// <summary>
    /// Polls HID enumeration until exactly one new device with the given VID/PID
    /// (other than the physical controller) appears, and returns its path.
    /// </summary>
    private async Task<string?> DiscoverVirtualPathAsync(ushort vid, ushort pid, string? physicalPath, HashSet<string> before)
    {
        for (int i = 0; i < DiscoveryTimeout.TotalMilliseconds / DiscoveryPollInterval.TotalMilliseconds; i++)
        {
            await Task.Delay(DiscoveryPollInterval);
            List<string> candidates = _enumerator.Enumerate(vid, pid)
                .Where(info => !before.Contains(info.Path)
                               && !string.Equals(info.Path, physicalPath, StringComparison.OrdinalIgnoreCase))
                .Select(info => info.Path)
                .ToList();

            if (candidates.Count == 1)
            {
                _log.Info($"Discovered virtual device path: {candidates[0]}");
                return candidates[0];
            }
            if (candidates.Count > 1)
            {
                _log.Warning($"Found {candidates.Count} new matching devices; abandoning discovery");
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Ensures the USB server exists, creating it on first use. Buses are created
    /// per virtual controller (see <see cref="CreateVirtualControllerAsync"/>) and
    /// removed with it.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private bool EnsureServerLocked()
    {
        if (_serverHandle is not null)
        {
            return true;
        }

        USBServerConfig config = new USBServerConfig
        {
            addr = "",
            connection_timeout_ms = 30000,
            device_handler_connect_timeout_ms = 5000,
            write_batch_flush_interval_ms = 1
        };
        if (!LibVIIPER.NewUSBServer(ref config, out nuint serverHandle, _nativeLogCallback))
        {
            _log.Error("Failed to create the libVIIPER USB server. The USBIP driver (usbip-win2) must be installed for server/device attachment to work");
            return false;
        }

        _serverHandle = serverHandle;
        _log.Info($"libVIIPER server ready (native '{LibVIIPER.NativeLibraryVersion}')");
        return true;
    }

    /// <summary>
    /// Removes the current virtual device and its USB bus. Caller must hold
    /// <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when a virtual device was removed.</returns>
    private bool DisposeVirtualLocked()
    {
        if (_virtual is null)
        {
            return false;
        }
        IVirtualController virtualController = _virtual;
        _virtual = null;

        if (virtualController is VirtualDualSenseController dualSense && _forwarder is { } forwarder)
        {
            dualSense.OutputStateReceived -= forwarder.UpdateGameOutputState;
            dualSense.RealtimeHapticsReceived -= forwarder.UpdateGameHaptics;
        }

        _capture?.Dispose();
        _capture = null;

        _forwarder?.Dispose();
        _forwarder = null;

        if (virtualController.VirtualDevicePath is { } path)
        {
            _enumerator.RemoveExcludedDevice(path);
        }
        virtualController.Dispose();
        if (_serverHandle is { } serverHandle && _busId != 0)
        {
            LibVIIPER.RemoveUSBBus(serverHandle, _busId);
            _busId = 0;
        }
        return true;
    }

    /// <summary>
    /// Subscribes to the physical device events that drive the virtual controller.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void DeviceSubscribe()
    {
        if (_device is null)
        {
            return;
        }
        _device.InputStateChanged += OnInputReportChanged;
        _device.MotionChanged += OnInputReportChanged;
        _device.TouchpadChanged += OnInputReportChanged;
        _device.BatteryStateChanged += OnBatteryChanged;
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    /// <summary>
    /// Unsubscribes from the physical device events.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void DeviceDisposeUnsubscribe()
    {
        if (_device is null)
        {
            return;
        }
        _device.InputStateChanged -= OnInputReportChanged;
        _device.MotionChanged -= OnInputReportChanged;
        _device.TouchpadChanged -= OnInputReportChanged;
        _device.BatteryStateChanged -= OnBatteryChanged;
        _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _device = null;
    }

    /// <summary>
    /// Forwards the latest input report to the virtual controller.
    /// </summary>
    private void OnInputReportChanged(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (_device is null || _virtual is null)
            {
                return;
            }
            _virtual.PushInput(_device.InputReport);
        }
    }

    /// <summary>
    /// Forwards battery changes to the virtual controller.
    /// </summary>
    private void OnBatteryChanged(object? sender, BatteryStateEventArgs e)
    {
        lock (_sync)
        {
            if (_device is null || _virtual is null)
            {
                return;
            }
            _virtual.PushInput(_device.InputReport);
            _virtual.PushBattery(e.CurrentState);
        }
    }

    /// <summary>
    /// Forwards connection status changes to the virtual controller.
    /// </summary>
    private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
    {
        lock (_sync)
        {
            if (_device is null || _virtual is null)
            {
                return;
            }
            _virtual.PushConnectionStatus(e.CurrentStatus);
        }
    }

    /// <summary>
    /// The host VID/PID pair of the virtual device, used for exclusion discovery.
    /// </summary>
    private static (ushort, ushort) GetDeviceIds(EmulationMode mode, bool edge) => mode switch
    {
        EmulationMode.Xbox360 => (0x045E, 0x028E),
        EmulationMode.DualShock4 => (0x054C, 0x05C4),
        EmulationMode.DualSense when edge => (0x054C, 0x0DF2),
        EmulationMode.DualSense => (0x054C, 0x0CE6),
        _ => (0, 0)
    };

    /// <summary>
    /// Forwards native libVIIPER log messages to the application logger. Called from
    /// background threads of the native library.
    /// </summary>
    private static void OnNativeLog(VIIPERLogLevel level, string message)
    {
        switch (level)
        {
            case VIIPERLogLevel.Debug:
                _nativeLog.Debug(message);
                break;
            case VIIPERLogLevel.Info:
                _nativeLog.Info(message);
                break;
            case VIIPERLogLevel.Warn:
                _nativeLog.Warning(message);
                break;
            default:
                _nativeLog.Error(message);
                break;
        }
    }

    /// <summary>
    /// Updates <see cref="Status"/> and raises <see cref="StateChanged"/>.
    /// </summary>
    private void SetStatus(EmulationStatus status)
    {
        Status = status;
        _log.Info($"Emulation status: {status.Mode} running={status.Running} ({status.Detail ?? status.VirtualDevicePath ?? "ok"})");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}