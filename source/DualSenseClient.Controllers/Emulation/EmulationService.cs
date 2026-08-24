using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using DualSenseClient.VIIPER;
using DualSenseClient.VIIPER.Callbacks;
using SoundFlow.Abstracts;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Creates a virtual controller for every tracked DualSense whose controller-level
/// emulation settings enable it. Owns the libVIIPER USB server, the per-controller
/// virtual device lifecycles, the HID exclusion that keeps the virtual devices out of
/// the app's own enumeration, and — for DualSense and DualShock 4 emulation — the
/// host-audio forwarders that play host audio through the physical controllers.
/// </summary>
public interface IEmulationService : IDisposable
{
    /// <summary>
    /// The current emulation state of the given controller, surfaced for the UI.
    /// </summary>
    EmulationStatus GetStatus(DualSenseDevice device);

    /// <summary>
    /// Raised whenever any controller's <see cref="EmulationStatus"/> changes. May be
    /// raised on a background thread, so UI subscribers should dispatch to the UI thread.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Starts watching the tracked controllers and creating virtual controllers
    /// according to each one's stored emulation settings.
    /// </summary>
    void Start();

    /// <summary>
    /// Re-evaluates every tracked controller against its stored emulation settings,
    /// recreating the virtual controllers whose emulation mode changed. Used when the
    /// user edits the emulation settings of a controller (device info page or tray).
    /// </summary>
    void Refresh();

    /// <summary>
    /// Applies the forwarding volume and haptic strength to the given controller's
    /// active host-audio forwarder without recreating the virtual controller. No-op
    /// when audio forwarding is not active for that controller.
    /// </summary>
    void SetForwardingAudioOptions(DualSenseDevice device, byte speakerVolume, float hapticStrength);

    /// <summary>
    /// Routes the given controller's forwarded host audio to its headset jack instead
    /// of its internal speaker without recreating the virtual controller. No-op when
    /// audio forwarding is not active for that controller.
    /// </summary>
    void SetForwardingAudioOutput(DualSenseDevice device, bool headset);

    /// <summary>
    /// Reloads the given controller's button remapping rules from its stored emulation
    /// settings and applies them to its running virtual controller without recreating the
    /// device. No-op when the controller has no active virtual controller.
    /// </summary>
    void ApplyButtonMappings(DualSenseDevice device);
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
    /// How long to watch abandoned discovery candidates after removing the virtual
    /// device, before deciding which of them were real hardware caught mid-connection.
    /// </summary>
    private static readonly TimeSpan DetachConfirmTimeout = TimeSpan.FromSeconds(2);

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
    /// Whether virtual controller emulation is available on this platform. Disabled on
    /// Linux for now.
    /// </summary>
    public static bool IsSupported => !OperatingSystem.IsLinux();

    /// <summary>
    /// Logger receiving the libVIIPER USB server's own log messages.
    /// </summary>
    private static readonly DualSenseClientLogger _nativeLog = DualSenseClientLogger.For("VIIPER");

    private readonly IControllerTracker _tracker;
    private readonly IHidDeviceEnumerator _enumerator;
    private readonly ControllerInfoService _controllerInfo;
    private readonly IVirtualControllerFactory _factory;
    private readonly SpecialActionEngineRegistry _specialActions;
    private readonly AudioEngine _audioEngine;

    /// <summary>
    /// Guards the per-controller entries and the server handle.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The virtual controller state per tracked controller.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, VirtualControllerEntry> _entries = new();

    /// <summary>
    /// The USB bus owned by <see cref="_serverHandle"/>, shared by all virtual controllers.
    /// </summary>
    private nuint? _serverHandle;

    /// <summary>
    /// Serializes virtual device creation: only one create+discover cycle runs at a time.
    /// Concurrent creations (two controllers tracked at the same scan, or one connecting
    /// while another is still being created) would each see the other's new device during
    /// path discovery and abandon it, leaving a virtual controller visible to the app's own
    /// scanner — which then tracks it as a phantom controller. With serialization, every
    /// discovery sees exactly one new device.
    /// </summary>
    private readonly SemaphoreSlim _creationGate = new SemaphoreSlim(1, 1);

    private bool _disposed;

    /// <summary>
    /// Keeps the native log callback delegate alive for the lifetime of the process.
    /// </summary>
    private readonly VIIPERLogCallback _nativeLogCallback = OnNativeLog;

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Creates a new <see cref="EmulationService"/>.
    /// </summary>
    public EmulationService(IControllerTracker tracker, IHidDeviceEnumerator enumerator,
        ControllerInfoService controllerInfo, IVirtualControllerFactory factory,
        AudioEngine audioEngine, SpecialActionEngineRegistry specialActions)
    {
        _tracker = tracker;
        _enumerator = enumerator;
        _controllerInfo = controllerInfo;
        _factory = factory;
        _audioEngine = audioEngine;
        _specialActions = specialActions;
    }

    /// <inheritdoc/>
    public void Start()
    {
        _tracker.ControllersChanged += OnControllersChanged;
        Reconcile();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _tracker.ControllersChanged -= OnControllersChanged;

        // Wait for the in-flight creation (if any) to finish before closing the
        // server, so no task is still using the native handle. Queued creations
        // observe _disposed once they acquire the gate and return without touching
        // the server. Blocks at most one create+discover cycle (~6 s).
        _creationGate.Wait();
        try
        {
            lock (_sync)
            {
                foreach (KeyValuePair<DualSenseDevice, VirtualControllerEntry> pair in _entries.ToList())
                {
                    DeviceDisposeUnsubscribe(pair.Key);
                    DisposeEntryLocked(pair.Value);
                }

                _entries.Clear();
                if (_serverHandle is { } serverHandle)
                {
                    LibVIIPER.CloseUSBServer(serverHandle);
                    _serverHandle = null;
                }
            }
        }
        finally
        {
            _creationGate.Release();
        }
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        List<(VirtualControllerEntry Entry, bool Settle)> toRecreate = new();
        lock (_sync)
        {
            foreach (VirtualControllerEntry entry in _entries.Values)
            {
                // Only rebuild entries whose controller settings actually changed: with
                // several controllers, recreating every virtual device on every change
                // would briefly detach the other controllers' virtual devices for no
                // reason. A running entry that still matches its settings is left
                // untouched; entries without a virtual device (failed or in-flight
                // creations) are always rebuilt so a change also retries them.
                EmulationSettings emulation = GetEmulationSettings(entry.Device);
                EmulationMode mode = emulation?.Mode ?? EmulationMode.Off;
                DualSenseVariant? variant = mode == EmulationMode.DualSense
                    ? emulation?.Variant.DualSense ?? DualSenseVariant.Standard
                    : null;
                DualShock4Variant? ds4Variant = mode == EmulationMode.DualShock4
                    ? emulation?.Variant.DualShock4 ?? DualShock4Variant.V2
                    : null;
                if (entry.Virtual is not null && entry.Status.Mode == mode
                                              && entry.Status.Variant == variant && entry.Status.Ds4Variant == ds4Variant)
                {
                    continue;
                }

                entry.Generation++;
                bool hadVirtual = DisposeEntryLocked(entry);
                if (StartCreationLocked(entry))
                {
                    toRecreate.Add((entry, hadVirtual));
                }
            }
        }

        foreach ((VirtualControllerEntry entry, bool settle) in toRecreate)
        {
            _ = CreateVirtualControllerAsync(entry, settle);
        }
    }

    /// <inheritdoc/>
    public EmulationStatus GetStatus(DualSenseDevice device)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(device, out VirtualControllerEntry? entry)
                ? entry.Status
                : new EmulationStatus(EmulationMode.Off, false, null, null);
        }
    }

    /// <inheritdoc/>
    public void SetForwardingAudioOptions(DualSenseDevice device, byte speakerVolume, float hapticStrength)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerEntry? entry) && entry.Forwarder is { } forwarder)
            {
                forwarder.SpeakerVolume = speakerVolume;
                forwarder.HapticStrength = Math.Clamp(hapticStrength, 0f, 2f);
            }
        }
    }

    /// <inheritdoc/>
    public void SetForwardingAudioOutput(DualSenseDevice device, bool headset)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerEntry? entry) && entry.Forwarder is { } forwarder)
            {
                forwarder.PlayToHeadset = headset;
            }
        }
    }

    /// <inheritdoc/>
    public void ApplyButtonMappings(DualSenseDevice device)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerEntry? entry) && entry.Virtual is { } virtualController)
            {
                ApplyButtonMappingsLocked(virtualController, device);
            }
        }
    }

    /// <summary>
    /// Resolves the controller's stored button mapping rules for the virtual controller's
    /// emulation mode and assigns them to it. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void ApplyButtonMappingsLocked(IVirtualController virtualController, DualSenseDevice device)
    {
        EmulationSettings emulation = GetEmulationSettings(device);
        virtualController.ButtonMappings = virtualController.Mode switch
        {
            EmulationMode.Xbox360 => VirtualInputMapper.Xbox360Table(emulation.Mappings.Xbox360, WarnInvalidMapping),
            EmulationMode.DualShock4 => VirtualInputMapper.DualShock4Table(emulation.Mappings.DualShock4, WarnInvalidMapping),
            EmulationMode.DualSense => VirtualInputMapper.DualSenseTable(emulation.Mappings.DualSense, WarnInvalidMapping),
            _ => null
        };
    }

    /// <summary>
    /// Logs an invalid button mapping entry name without interrupting the remaining rules.
    /// </summary>
    private void WarnInvalidMapping(string message) => _log.Warning(message);

    /// <summary>
    /// Per-controller virtual device lifecycle: the physical controller, the virtual
    /// device, its USB bus, and the host-audio forwarder/capture pair (DualSense mode).
    /// </summary>
    private sealed class VirtualControllerEntry
    {
        /// <summary>
        /// The physical controller being mirrored.
        /// </summary>
        public required DualSenseDevice Device { get; init; }

        /// <summary>
        /// The current emulation state, surfaced for the UI.
        /// </summary>
        public EmulationStatus Status { get; set; } = new EmulationStatus(EmulationMode.Off, false, null, null);

        /// <summary>
        /// Incremented on every rebuild so stale background creations are discarded
        /// when the controller or its profile changes while one is in flight.
        /// </summary>
        public int Generation;

        /// <summary>
        /// The virtual controller, or <c>null</c> while not emulating.
        /// </summary>
        public IVirtualController? Virtual;

        /// <summary>
        /// Forwards host audio to the physical controller while DualSense or
        /// DualShock 4 emulation is active, or <c>null</c> in other modes. Lives and
        /// dies with <see cref="Virtual"/>.
        /// </summary>
        public ViiperDualSenseAudioForwarder? Forwarder;

        /// <summary>
        /// Captures the audio the host renders to the virtual controller (DualSense or
        /// DualShock 4) and feeds it to <see cref="Forwarder"/>, or <c>null</c> when
        /// not forwarding. Lives and dies with <see cref="Virtual"/>; must be disposed
        /// before the virtual device is removed.
        /// </summary>
        public IDisposable? Capture;

        /// <summary>
        /// The USB bus owned by <see cref="Virtual"/>, removed together with it.
        /// </summary>
        public uint BusId;
    }

    /// <summary>
    /// (Re)builds the per-controller entries whenever the tracked controller set changes:
    /// disposes the entries of untracked controllers and creates entries (and virtual
    /// controllers) for newly tracked ones.
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e) => Reconcile();

    /// <summary>
    /// Diffs the tracked controllers against the current entries, adding and removing
    /// virtual controller state as needed.
    /// </summary>
    private void Reconcile()
    {
        List<VirtualControllerEntry> toCreate = new();
        lock (_sync)
        {
            HashSet<DualSenseDevice> current = _tracker.Controllers.OfType<DualSenseDevice>().ToHashSet();

            foreach (KeyValuePair<DualSenseDevice, VirtualControllerEntry> pair in _entries.ToList())
            {
                if (current.Contains(pair.Key))
                {
                    continue;
                }

                _entries.Remove(pair.Key);
                DeviceDisposeUnsubscribe(pair.Key);
                DisposeEntryLocked(pair.Value);
            }

            foreach (DualSenseDevice device in current)
            {
                if (_entries.ContainsKey(device))
                {
                    continue;
                }

                VirtualControllerEntry entry = new VirtualControllerEntry
                {
                    Device = device
                };
                _entries.Add(device, entry);
                DeviceSubscribe(device);
                if (StartCreationLocked(entry))
                {
                    toCreate.Add(entry);
                }
            }
        }

        foreach (VirtualControllerEntry entry in toCreate)
        {
            _ = CreateVirtualControllerAsync(entry, settleAfterRemoval: false);
        }
    }

    /// <summary>
    /// Prepares an entry for a (re)creation: resolves the controller's emulation mode,
    /// surfaces the creating status, and reports whether a virtual controller should be
    /// created. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private bool StartCreationLocked(VirtualControllerEntry entry)
    {
        if (!IsSupported)
        {
            SetStatus(entry, new EmulationStatus(EmulationMode.Off, false, "Emulation is not available on this platform", null));
            return false;
        }

        EmulationMode mode = GetEmulationSettings(entry.Device).Mode;
        if (mode == EmulationMode.Off)
        {
            SetStatus(entry, new EmulationStatus(EmulationMode.Off, false, "Emulation is disabled for this controller", null));
            return false;
        }

        SetStatus(entry, new EmulationStatus(mode, false, null, null, IsCreating: true));
        return true;
    }

    /// <summary>
    /// Runs the device creation + path discovery on a background thread so the tracker
    /// callback does not block on enumeration polling. The whole cycle is serialized
    /// through <see cref="_creationGate"/> (see there for why). A fresh USB bus is created
    /// for the virtual controller (and removed again when it is disposed), because libVIIPER
    /// auto-removes empty buses after the server's cleanup timeout, which would make a
    /// reused bus id invalid. When <paramref name="settleAfterRemoval"/> is set, a short
    /// delay is applied first so the USBIP client finishes detaching the previous device.
    /// Creation is retried once after <see cref="CreateRetryDelay"/> on failure.
    /// </summary>
    private async Task CreateVirtualControllerAsync(VirtualControllerEntry entry, bool settleAfterRemoval)
    {
        int generation = entry.Generation;
        if (!IsSupported)
        {
            SetStatus(entry, new EmulationStatus(EmulationMode.Off, false, "Emulation is not available on this platform", null));
            return;
        }

        EmulationMode mode = GetEmulationSettings(entry.Device).Mode;
        if (mode == EmulationMode.Off)
        {
            SetStatus(entry, new EmulationStatus(EmulationMode.Off, false, "Emulation is disabled for this controller", null));
            return;
        }

        await _creationGate.WaitAsync();
        try
        {
            nuint serverHandle;
            lock (_sync)
            {
                if (_disposed)
                {
                    // The service was disposed while this creation was queued: the
                    // server is gone, so do not recreate it.
                    return;
                }

                if (!EnsureServerLocked())
                {
                    SetStatus(entry, new EmulationStatus(mode, false, "Could not start the libVIIPER USB server", null));
                    return;
                }

                serverHandle = _serverHandle!.Value;
            }

            if (settleAfterRemoval)
            {
                await Task.Delay(RecreationSettleDelay);
            }

            EmulationSettings emulation = GetEmulationSettings(entry.Device);
            bool edge = emulation?.Variant.DualSense == DualSenseVariant.Edge;
            DualShock4Variant ds4Variant = emulation?.Variant.DualShock4 ?? DualShock4Variant.V2;
            (ushort vid, ushort pid) = GetDeviceIds(mode, edge, ds4Variant);
            SpecialActionEngine specialActions;
            lock (_sync)
            {
                if (IsStale(entry, generation))
                {
                    return;
                }

                specialActions = _specialActions.GetOrCreate(entry.Device);
            }

            DualSenseDeviceOutputs outputs = new DualSenseDeviceOutputs(entry.Device, specialActions);
            // The virtual Xbox 360 exposes no HID interface (only vendor-specific
            // 0xff/5d interfaces), so Windows never creates a HID node for it and
            // HID enumeration can never find it; there is nothing to discover.
            bool canDiscover = mode != EmulationMode.Xbox360;
            HashSet<string> before = canDiscover
                ? _enumerator.EnumerateIncludingExcluded(vid, pid).Select(info => info.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];

            uint busId = 0;
            IVirtualController? virtualController = TryCreateOnFreshBus(serverHandle, mode, outputs, entry.Device.UsesVibrationV2, edge, ds4Variant, ref busId);
            if (virtualController is null)
            {
                _log.Warning(
                    $"Failed to create the virtual {mode} device for {entry.Device.Info.ProductName}; retrying in {(int)CreateRetryDelay.TotalMilliseconds} ms");
                await Task.Delay(CreateRetryDelay);

                if (IsStale(entry, generation))
                {
                    return;
                }

                virtualController = TryCreateOnFreshBus(serverHandle, mode, outputs, entry.Device.UsesVibrationV2, edge, ds4Variant, ref busId);
            }

            if (virtualController is null)
            {
                SetStatus(entry,
                    new EmulationStatus(mode, false,
                        "The native library could not create the virtual device. Check the VIIPER logs; USB/IP must be installed for auto-attachment", null));
                return;
            }

            HashSet<string> candidates = canDiscover
                ? await DiscoverVirtualPathCandidatesAsync(vid, pid, entry.Device.Info.Path, before)
                : [];
            if (canDiscover && candidates.Count != 1)
            {
                // The virtual device never appeared in HID enumeration (slow USBIP
                // attach) or appeared alongside other new devices, e.g. another
                // controller connecting mid-creation or a previous virtual still
                // detaching during a mode switch. Leaving it attached and unexcluded
                // would make the watcher report it as a phantom controller, so the
                // device and its bus are removed instead. All candidates were already
                // excluded on first sighting during discovery; afterwards, any that
                // survived removal was real hardware and is unexcluded again.
                _log.Warning(candidates.Count == 0
                    ? "Could not discover the virtual device in HID enumeration; removing it again"
                    : $"Found {candidates.Count} new matching devices; removing them again");
                virtualController.Dispose();
                LibVIIPER.RemoveUSBBus(serverHandle, busId);
                await UnexcludeSurvivingCandidatesAsync(vid, pid, candidates);
                SetStatus(entry,
                    new EmulationStatus(mode, false,
                        "The virtual device did not appear in HID enumeration. Check the VIIPER logs; USB/IP must be installed for auto-attachment", null));
                return;
            }

            if (candidates.Count == 1)
            {
                virtualController.VirtualDevicePath = candidates.Single();
            }

            bool forwardFeatures = mode is EmulationMode.DualSense or EmulationMode.DualShock4;
            ViiperDualSenseAudioForwarder? forwarder = forwardFeatures
                ? CreateAudioForwarder(outputs, emulation)
                : null;
            IDisposable? capture = null;
            if (forwarder is not null && virtualController is VirtualDualSenseController dualSense)
            {
                dualSense.OutputStateReceived += forwarder.UpdateGameOutputState;
                dualSense.RealtimeHapticsReceived += forwarder.UpdateGameHaptics;
                if (dualSense.DeviceHandle is { } deviceHandle)
                {
                    capture = new ViiperDualSenseAudioCapture(deviceHandle, forwarder);
                }
            }
            else if (forwarder is not null && virtualController is VirtualDualShock4Controller dualShock4)
            {
                dualShock4.OutputStateReceived += forwarder.UpdateGameOutputState;
                if (dualShock4.DeviceHandle is { } deviceHandle)
                {
                    capture = new ViiperDualShock4AudioCapture(deviceHandle, forwarder);
                }
            }

            if (forwarder is not null)
            {
                _log.Info($"Host audio forwarding {(forwarder.Start() ? "started" : "unavailable")} for the virtual {mode}");
            }

            lock (_sync)
            {
                if (IsStale(entry, generation))
                {
                    // The stale virtual device's path stays excluded: unexcluding before
                    // the detach completes would let the watcher see it as a phantom.
                    // Re-created devices reusing the path are still discoverable via
                    // EnumerateIncludingExcluded during discovery.
                    capture?.Dispose();
                    forwarder?.Dispose();
                    virtualController.Dispose();
                    LibVIIPER.RemoveUSBBus(serverHandle, busId);
                    return;
                }

                entry.BusId = busId;
                entry.Virtual = virtualController;
                entry.Forwarder = forwarder;
                entry.Capture = capture;
                ApplyButtonMappingsLocked(virtualController, entry.Device);
            }

            SetStatus(entry, new EmulationStatus(mode, true, null, virtualController.VirtualDevicePath,
                Variant: mode == EmulationMode.DualSense ? (edge ? DualSenseVariant.Edge : DualSenseVariant.Standard) : null,
                Ds4Variant: mode == EmulationMode.DualShock4 ? ds4Variant : null));
        }
        catch (Exception ex)
        {
            _log.LogExceptionDetails(ex);
            SetStatus(entry, new EmulationStatus(mode, false, ex.Message, null));
        }
        finally
        {
            _creationGate.Release();
        }
    }

    /// <summary>
    /// Whether the given creation is still wanted: the service is not disposed, the
    /// entry is still tracked, and its generation was not bumped by a newer rebuild.
    /// </summary>
    private bool IsStale(VirtualControllerEntry entry, int generation)
    {
        lock (_sync)
        {
            return _disposed || !_entries.TryGetValue(entry.Device, out VirtualControllerEntry? current)
                             || !ReferenceEquals(current, entry) || entry.Generation != generation;
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
    /// <param name="ds4Variant">The DualShock 4 hardware generation to present (DualShock 4 mode only).</param>
    /// <param name="busId">Receives the id of the created bus, valid only on success.</param>
    /// <returns>The created virtual controller, or <c>null</c> when creation failed.</returns>
    private IVirtualController? TryCreateOnFreshBus(nuint serverHandle, EmulationMode mode, DualSenseDeviceOutputs outputs, bool vibrationV2, bool edge,
        DualShock4Variant ds4Variant, ref uint busId)
    {
        if (!LibVIIPER.CreateUSBBus(serverHandle, ref busId))
        {
            return null;
        }

        IVirtualController? virtualController = _factory.Create(mode, serverHandle, busId, outputs, vibrationV2, edge, ds4Variant);
        if (virtualController?.DeviceHandle is null)
        {
            virtualController?.Dispose();
            virtualController = null;
            LibVIIPER.RemoveUSBBus(serverHandle, busId);
        }

        return virtualController;
    }

    /// <summary>
    /// Gets the emulation settings stored for a controller (the emulation section of
    /// the device info page), defaulting to emulation off.
    /// </summary>
    private EmulationSettings GetEmulationSettings(DualSenseDevice device)
        => _controllerInfo.GetEmulationSettings(device.PairingInfo?.ClientMac, device.Info.Path);

    /// <summary>
    /// Creates the host-audio forwarder: Bluetooth audio reports to the physical
    /// controller over the same outputs lane, and the USB UAC render endpoint for
    /// wired playback (when the pad exposes one). The controller's forwarding
    /// volume, haptic strength, haptics/audio toggles and speaker/headset route are
    /// applied on creation.
    /// </summary>
    private ViiperDualSenseAudioForwarder CreateAudioForwarder(DualSenseDeviceOutputs outputs, EmulationSettings? emulation)
    {
        DualSenseAudioEndpointFinder endpointFinder = new DualSenseAudioEndpointFinder(_audioEngine);
        DualSenseUsbAudioTarget usbTarget = new DualSenseUsbAudioTarget(_audioEngine, endpointFinder);
        ViiperDualSenseAudioForwarder forwarder = new ViiperDualSenseAudioForwarder(outputs, usbTarget);
        if (emulation is not null)
        {
            forwarder.SpeakerVolume = (byte)Math.Clamp(emulation.Forward.Volume, 0, 255);
            forwarder.HapticStrength = Math.Clamp(emulation.Forward.Haptics, 0, 200) / 100f;
            forwarder.PlayToHeadset = emulation.Forward.AudioOutput == EmulationAudioOutput.Headset;
        }

        return forwarder;
    }

    /// <summary>
    /// Polls HID enumeration until new device(s) with the given VID/PID (other than the
    /// physical controller) appear, and returns their paths. Enumeration includes paths
    /// excluded by earlier teardowns: USB/IP reattaches at the same bus/port, so a
    /// re-created virtual device reuses its predecessor's devnode path, which is still
    /// excluded and would otherwise be invisible forever. Every candidate is excluded
    /// from enumeration on first sighting — exclusion takes effect immediately, well
    /// before the watcher's two-poll confirmation could surface the device as a phantom
    /// connection. Returns the set of candidate paths: exactly one entry means success;
    /// more than one means an ambiguous attach (the caller removes the virtual device
    /// again); empty means the virtual device never appeared within
    /// <see cref="DiscoveryTimeout"/>. Excluded sightings are accumulated across
    /// iterations.
    /// </summary>
    private async Task<HashSet<string>> DiscoverVirtualPathCandidatesAsync(ushort vid, ushort pid, string? physicalPath,
        HashSet<string> before)
    {
        HashSet<string> discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < DiscoveryTimeout.TotalMilliseconds / DiscoveryPollInterval.TotalMilliseconds; i++)
        {
            await Task.Delay(DiscoveryPollInterval);
            List<string> candidates = _enumerator.EnumerateIncludingExcluded(vid, pid)
                .Where(info => !before.Contains(info.Path)
                               && !string.Equals(info.Path, physicalPath, StringComparison.OrdinalIgnoreCase))
                .Select(info => info.Path)
                .ToList();

            foreach (string candidate in candidates)
            {
                if (discovered.Add(candidate))
                {
                    _enumerator.ExcludeDevice(candidate);
                    _log.Info($"Discovered virtual device candidate: {candidate}");
                }
            }

            if (discovered.Count > 0)
            {
                return discovered;
            }
        }

        return discovered;
    }

    /// <summary>
    /// Restores enumeration visibility of abandoned discovery candidates that survived
    /// removal of the virtual device: those were real controllers connecting at the same
    /// time, not the virtual device. Candidates that vanished were (part of) the removed
    /// virtual attach and stay excluded — their devnode instance IDs never recur.
    /// Enumeration must include excluded devices here, because the candidates were
    /// excluded during discovery and are invisible to the filtered view.
    /// </summary>
    private async Task UnexcludeSurvivingCandidatesAsync(ushort vid, ushort pid, HashSet<string> candidates)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        for (int i = 0; i < DetachConfirmTimeout.TotalMilliseconds / DiscoveryPollInterval.TotalMilliseconds; i++)
        {
            HashSet<string> present = _enumerator.EnumerateIncludingExcluded(vid, pid)
                .Select(info => info.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!candidates.Overlaps(present))
            {
                break;
            }

            await Task.Delay(DiscoveryPollInterval);
        }

        HashSet<string> remaining = _enumerator.EnumerateIncludingExcluded(vid, pid)
            .Select(info => info.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in candidates.Where(remaining.Contains))
        {
            _log.Info($"Restoring non-virtual discovery candidate: {candidate}");
            _enumerator.RemoveExcludedDevice(candidate);
        }
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
            _log.Error("Failed to create the libVIIPER USB server. USB/IP must be installed for server/device attachment to work");
            return false;
        }

        _serverHandle = serverHandle;
        _log.Info($"libVIIPER server ready (native '{LibVIIPER.NativeLibraryVersion}')");
        return true;
    }

    /// <summary>
    /// Removes the entry's virtual device and its USB bus. Caller must hold
    /// <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when a virtual device was removed.</returns>
    private bool DisposeEntryLocked(VirtualControllerEntry entry)
    {
        if (entry.Virtual is null)
        {
            return false;
        }

        IVirtualController virtualController = entry.Virtual;
        entry.Virtual = null;

        if (entry.Forwarder is { } forwarder)
        {
            if (virtualController is VirtualDualSenseController dualSense)
            {
                dualSense.OutputStateReceived -= forwarder.UpdateGameOutputState;
                dualSense.RealtimeHapticsReceived -= forwarder.UpdateGameHaptics;
            }
            else if (virtualController is VirtualDualShock4Controller dualShock4)
            {
                dualShock4.OutputStateReceived -= forwarder.UpdateGameOutputState;
            }
        }

        entry.Capture?.Dispose();
        entry.Capture = null;

        entry.Forwarder?.Dispose();
        entry.Forwarder = null;

        // The virtual device's path stays excluded: unexcluding before the detach
        // completes would let the watcher see the still-attached device as a phantom.
        // Re-created devices reusing the path are still discoverable via
        // EnumerateIncludingExcluded during discovery.
        virtualController.Dispose();
        if (_serverHandle is { } serverHandle && entry.BusId != 0)
        {
            LibVIIPER.RemoveUSBBus(serverHandle, entry.BusId);
            entry.BusId = 0;
        }

        return true;
    }

    /// <summary>
    /// Subscribes to the physical device events that drive its virtual controller.
    /// </summary>
    private void DeviceSubscribe(DualSenseDevice device)
    {
        device.InputReportReceived += OnInputReportReceived;
        device.BatteryStateChanged += OnBatteryChanged;
        device.ConnectionStatusChanged += OnConnectionStatusChanged;
    }

    /// <summary>
    /// Unsubscribes from the physical device events.
    /// </summary>
    private void DeviceDisposeUnsubscribe(DualSenseDevice device)
    {
        device.InputReportReceived -= OnInputReportReceived;
        device.BatteryStateChanged -= OnBatteryChanged;
        device.ConnectionStatusChanged -= OnConnectionStatusChanged;
    }

    /// <summary>
    /// Forwards the latest input report to the device's virtual controller.
    /// </summary>
    private void OnInputReportReceived(object? sender, InputReport report)
    {
        if (sender is not DualSenseDevice device)
        {
            return;
        }

        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerEntry? entry) && entry.Virtual is not null)
            {
                entry.Virtual.PushInput(report);
            }
        }
    }

    /// <summary>
    /// Forwards battery changes to the device's virtual controller.
    /// </summary>
    private void OnBatteryChanged(object? sender, BatteryStateEventArgs e)
    {
        if (sender is not DualSenseDevice device)
        {
            return;
        }

        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerEntry? entry)
                && entry.Virtual is not null
                && device.InputReport is { } report)
            {
                entry.Virtual.PushInput(report);
                entry.Virtual.PushBattery(e.CurrentState);
            }
        }
    }

    /// <summary>
    /// Forwards connection status changes to the device's virtual controller.
    /// </summary>
    private void OnConnectionStatusChanged(object? sender, ConnectionStatusEventArgs e)
    {
        if (sender is not DualSenseDevice device)
        {
            return;
        }

        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerEntry? entry) && entry.Virtual is not null)
            {
                entry.Virtual.PushConnectionStatus(e.CurrentStatus);
            }
        }
    }

    /// <summary>
    /// The host VID/PID pair of the virtual device, used for exclusion discovery. Must
    /// match the IDs the virtual device actually presents.
    /// </summary>
    private static (ushort, ushort) GetDeviceIds(EmulationMode mode, bool edge, DualShock4Variant ds4Variant) => mode switch
    {
        EmulationMode.Xbox360 => (0x045E, 0x028E),
        EmulationMode.DualShock4 => (VirtualDualShock4Controller.VendorId,
            ds4Variant == DualShock4Variant.V1
                ? VirtualDualShock4Controller.ProductIdV1
                : VirtualDualShock4Controller.ProductIdV2),
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
    /// Updates an entry's <see cref="EmulationStatus"/> and raises <see cref="StateChanged"/>.
    /// </summary>
    private void SetStatus(VirtualControllerEntry entry, EmulationStatus status)
    {
        entry.Status = status;
        string detail = status.IsCreating ? "creating" : status.Detail ?? status.VirtualDevicePath ?? "ok";
        _log.Info($"Emulation status: {entry.Device.Info.ProductName} {status.Mode} running={status.Running} ({detail})");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}