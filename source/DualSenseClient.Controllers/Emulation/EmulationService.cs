using System.Collections.Concurrent;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Events;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.SpecialActions;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings;
using DualSenseClient.Settings.Sections;
using DualSenseClient.Emulation;
using DualSenseClient.Emulation.Core;
using SoundFlow.Abstracts;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Creates a virtual controller for every tracked DualSense whose controller-level
/// emulation settings enable it. Owns the shared Emulation server, the
/// per-controller virtual device lifecycles, the HID exclusion that keeps the virtual
/// devices out of the app's own enumeration, and the host-audio forwarders that play
/// host audio through the physical controllers — either from the virtual controller's
/// endpoint or, for a direct source (the system output or a single application), from a
/// loopback capture without any virtual device.
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
    /// Reloads the given controller's button remapping rules from its stored emulation
    /// settings and applies them to its running virtual controller without recreating the
    /// device. No-op when the controller has no active virtual controller.
    /// </summary>
    void ApplyButtonMappings(DualSenseDevice device);

    /// <summary>
    /// Temporarily overrides the given controller's emulation mode, hardware variants and
    /// forwarded-audio source (e.g. for foreground-app auto profiles) without touching its
    /// stored settings. A <c>null</c> override clears it. Variants are only used with their
    /// matching mode (DualSense/Edge with <see cref="EmulationMode.DualSense"/>, V1/V2 with
    /// <see cref="EmulationMode.DualShock4"/>). Call <see cref="Refresh"/> afterwards to
    /// recreate the virtual controller when the effective settings changed.
    /// </summary>
    void SetTemporaryOverride(DualSenseDevice device, EmulationOverride? overrides);
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
    public static bool IsSupported
    {
        get
        {
            return !OperatingSystem.IsLinux();
        }
    }

    private readonly IControllerTracker _tracker;
    private readonly IHidDeviceEnumerator _enumerator;
    private readonly ControllerInfoService _controllerInfo;

    /// <summary>
    /// Creates the Emulation personas and adapters.
    /// </summary>
    private readonly IVirtualControllerFactory _virtualFactory;

    /// <summary>
    /// Creates a fresh Emulation server whenever the first Emulation entry needs one, so a server
    /// torn down when the last entry left is never reused.
    /// </summary>
    private readonly Func<EmulationServer> _emulationServerFactory;

    private readonly SpecialActionEngineRegistry _specialActions;
    private readonly AudioEngine _audioEngine;

    /// <summary>
    /// Resolves the executable selected as the forwarded-audio application to a live
    /// process id.
    /// </summary>
    private readonly AudioApplicationService _audioApplications;

    /// <summary>
    /// Guards the per-controller entries and the server handle.
    /// </summary>
    private readonly Lock _sync = new Lock();

    /// <summary>
    /// The virtual controller state per tracked controller.
    /// </summary>
    private readonly Dictionary<DualSenseDevice, VirtualControllerSlot> _entries = new Dictionary<DualSenseDevice, VirtualControllerSlot>();

    /// <summary>
    /// Temporary per-controller emulation overrides (foreground-app auto profiles).
    /// Consulted by <see cref="GetEmulationSettings"/> before the stored settings; a
    /// <c>null</c> entry leaves that setting at its stored value.
    /// Lock-free so <see cref="GetEmulationSettings"/> stays callable while
    /// <see cref="_sync"/> is held (it is invoked from <see cref="Refresh"/> and
    /// <see cref="ApplyButtonMappings"/> under that lock; <see cref="Lock"/> is not
    /// re-entrant and must never be acquired twice on one thread).
    /// </summary>
    private readonly ConcurrentDictionary<DualSenseDevice, EmulationOverride> _temporaryModes =
        new ConcurrentDictionary<DualSenseDevice, EmulationOverride>();

    /// <summary>
    /// Set when a controller-info save needs a refresh; consumed by the background worker.
    /// </summary>
    private int _refreshPending;

    /// <summary>
    /// Whether the background refresh worker is running.
    /// </summary>
    private int _refreshRunning;

    /// <summary>
    /// The emulation server shared by all Emulation entries, or <c>null</c> while no entry runs on
    /// the Emulation engine. Created lazily by <see cref="EnsureEmulationServerLocked"/> and torn
    /// down by <see cref="MaybeTeardownEmulationServerLocked"/> once the last entry leaves.
    /// </summary>
    private EmulationServer? _emulationServer;

    /// <summary>
    /// Personas attached by a creation that has not committed them to an entry yet, or whose
    /// entry was removed while the creation was still running. The shared server must not stop
    /// while any of these is host-attached; the owning creation detaches and removes its persona
    /// on every exit path. Guarded by <see cref="_sync"/>.
    /// </summary>
    private readonly HashSet<VirtualUsbDevice> _inFlightEmulationPersonas = new HashSet<VirtualUsbDevice>();

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

    /// <inheritdoc/>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Creates a new <see cref="EmulationService"/>.
    /// </summary>
    public EmulationService(IControllerTracker tracker, IHidDeviceEnumerator enumerator,
        ControllerInfoService controllerInfo,
        AudioEngine audioEngine, SpecialActionEngineRegistry specialActions, AudioApplicationService audioApplications,
        Func<EmulationServer> emulationServerFactory, IVirtualControllerFactory virtualFactory)
    {
        _tracker = tracker;
        _enumerator = enumerator;
        _controllerInfo = controllerInfo;
        _audioEngine = audioEngine;
        _specialActions = specialActions;
        _audioApplications = audioApplications;
        _emulationServerFactory = emulationServerFactory;
        _virtualFactory = virtualFactory;
    }

    /// <inheritdoc/>
    public void Start()
    {
        _tracker.ControllersChanged += OnControllersChanged;
        _controllerInfo.ControllersChanged += OnControllerInfoChanged;
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
        _controllerInfo.ControllersChanged -= OnControllerInfoChanged;

        // Wait for the in-flight creation (if any) to finish before stopping the
        // server, so no task is still using it. Queued creations observe _disposed
        // once they acquire the gate and return without touching the server. Blocks
        // at most one create+discover cycle (~6 s).
        _creationGate.Wait();
        try
        {
            lock (_sync)
            {
                foreach (KeyValuePair<DualSenseDevice, VirtualControllerSlot> pair in _entries.ToList())
                {
                    pair.Value.StatusChanged -= OnSlotStatusChanged;
                    pair.Value.MarkStopped();
                    DeviceDisposeUnsubscribe(pair.Key);
                    DisposeEntryLocked(pair.Value);
                }

                _entries.Clear();
                _temporaryModes.Clear();
                // Every creation has finished by now (the gate was drained above); never leave
                // the in-flight persona spotlight behind.
                _inFlightEmulationPersonas.Clear();
                if (_emulationServer is { } emulationServer)
                {
                    emulationServer.Stop();
                    emulationServer.Dispose();
                    _emulationServer = null;
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
        List<(VirtualControllerSlot Entry, bool Settle)> toRecreate = new List<(VirtualControllerSlot Entry, bool Settle)>();
        lock (_sync)
        {
            foreach (VirtualControllerSlot entry in _entries.Values)
            {
                // Only rebuild entries whose recorded creation inputs changed: with several
                // controllers, recreating every virtual device on every change would briefly
                // detach the other controllers' virtual devices for no reason.
                EmulationSettings emulation = GetEmulationSettings(entry.Device);
                EmulationMode mode = emulation?.Mode ?? EmulationMode.Off;
                DualSenseVariant? variant = mode == EmulationMode.DualSense
                    ? emulation?.Variant.DualSense ?? DualSenseVariant.Standard
                    : null;
                DualShock4Variant? ds4Variant = mode == EmulationMode.DualShock4
                    ? emulation?.Variant.DualShock4 ?? DualShock4Variant.V2
                    : null;
                EmulationAudioSource audioSource = emulation?.Forward.AudioSource ?? EmulationAudioSource.ControllerEndpoint;
                string audioApp = emulation?.Forward.AudioApp ?? string.Empty;
                bool audioEnabled = emulation?.Forward.AudioEnabled ?? true;
                ApplyForwardingLocked(entry, emulation);
                if (entry.Status.State == EmulationState.Creating && entry.Status.Mode == mode)
                {
                    // A create+discover cycle for the same mode is already running. Leave it
                    // alone so the settings-save notification and an explicit Refresh cannot
                    // start a second creation for the same slot.
                    SyncAudioLocked(entry, audioSource, audioApp, audioEnabled);
                    continue;
                }

                if (!ShouldRecreateEntry(entry.Virtual is not null, entry.Status.Mode, entry.Status.Variant, entry.Status.Ds4Variant,
                        mode, variant, ds4Variant))
                {
                    SyncAudioLocked(entry, audioSource, audioApp, audioEnabled);
                    continue;
                }

                entry.Generation++;
                // Only the virtual side is rebuilt: direct system audio keeps playing
                // across the virtual recreate instead of restarting its loopback.
                bool hadVirtual = DisposeVirtualLocked(entry);
                if (StartCreationLocked(entry))
                {
                    toRecreate.Add((entry, hadVirtual));
                }
                else
                {
                    SyncAudioLocked(entry, audioSource, audioApp, audioEnabled);
                }
            }
        }

        foreach ((VirtualControllerSlot entry, bool settle) in toRecreate)
        {
            _ = CreateVirtualControllerAsync(entry, settle);
        }
    }

    /// <summary>
    /// Whether <see cref="Refresh"/> must rebuild a tracked entry: an entry without a virtual
    /// controller (failed or in-flight creations) is always rebuilt so a settings change also
    /// retries it, and a running entry is rebuilt when any recorded creation input (mode,
    /// variant) differs from the effective settings. A running entry that still matches
    /// is left untouched. Audio source changes are applied in place by the audio sync and are
    /// deliberately not part of this decision.
    /// </summary>
    /// <param name="hasVirtualController">Whether the entry currently holds a virtual controller.</param>
    /// <param name="createdMode">Mode the running virtual controller was created with.</param>
    /// <param name="createdVariant">DualSense variant the running virtual controller was created with.</param>
    /// <param name="createdDs4Variant">DualShock 4 variant the running virtual controller was created with.</param>
    /// <param name="mode">Effective mode from the current settings.</param>
    /// <param name="variant">Effective DualSense variant from the current settings.</param>
    /// <param name="ds4Variant">Effective DualShock 4 variant from the current settings.</param>
    /// <returns><c>true</c> when the entry must be rebuilt; <c>false</c> when it still matches.</returns>
    internal static bool ShouldRecreateEntry(bool hasVirtualController,
        EmulationMode createdMode, DualSenseVariant? createdVariant, DualShock4Variant? createdDs4Variant,
        EmulationMode mode, DualSenseVariant? variant, DualShock4Variant? ds4Variant) =>
        !hasVirtualController
        || createdMode != mode
        || createdVariant != variant
        || createdDs4Variant != ds4Variant;

    /// <inheritdoc/>
    public EmulationStatus GetStatus(DualSenseDevice device)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(device, out VirtualControllerSlot? entry)
                ? entry.Status
                : new EmulationStatus(EmulationMode.Off, false, null, null);
        }
    }

    /// <inheritdoc/>
    public void ApplyButtonMappings(DualSenseDevice device)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(device, out VirtualControllerSlot? entry) && entry.Virtual is { } virtualController)
            {
                ApplyButtonMappingsLocked(virtualController, device);
            }
        }
    }

    /// <inheritdoc/>
    public void SetTemporaryOverride(DualSenseDevice device, EmulationOverride? overrides)
    {
        if (overrides is null || overrides.IsEmpty)
        {
            _temporaryModes.TryRemove(device, out _);
            return;
        }

        // Rules are edited on the UI thread while the watcher reads them: ignore
        // undefined values (e.g. torn reads) rather than recreating devices for them.
        if (overrides.Mode is not null && !Enum.IsDefined(overrides.Mode.Value))
        {
            return;
        }

        _temporaryModes[device] = overrides;
    }

    /// <summary>
    /// Resolves the controller's stored button mapping rules for the virtual controller's
    /// emulation mode and assigns them to it. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void ApplyButtonMappingsLocked(IVirtualController virtualController, DualSenseDevice device)
    {
        EmulationSettings emulation = GetEmulationSettings(device);
        if (virtualController.Mode == EmulationMode.DualSense
            && (emulation.Mappings.DualSense is null || emulation.Mappings.DualSense.Count == 0))
        {
            // Built-in one-to-one mapping: share the singleton so the virtual
            // controller can take the verbatim raw path.
            virtualController.ButtonMappings = VirtualInputMapper.DualSenseDefaultTable;
            return;
        }

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
    /// (Re)builds the per-controller entries whenever the tracked controller set changes:
    /// disposes the entries of untracked controllers and creates entries (and virtual
    /// controllers) for newly tracked ones.
    /// </summary>
    private void OnControllersChanged(object? sender, EventArgs e) => Reconcile();

    /// <summary>
    /// Reconciles the running virtual controllers after stored emulation settings change.
    /// Runs off the saving thread: starting a forwarded-audio capture can wait on WASAPI
    /// activation, and the save may be raised while the settings service holds its lock.
    /// </summary>
    private void OnControllerInfoChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _refreshPending, 1);
        if (Interlocked.Exchange(ref _refreshRunning, 1) == 1)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                while (!_disposed && Interlocked.Exchange(ref _refreshPending, 0) == 1)
                {
                    Refresh();
                }
            }
            catch (Exception ex)
            {
                _log.Error("Emulation refresh failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshRunning, 0);
                if (!_disposed && Volatile.Read(ref _refreshPending) == 1)
                {
                    OnControllerInfoChanged(this, EventArgs.Empty);
                }
            }
        });
    }

    /// <summary>
    /// Re-raises a slot's state transition on the service-wide event.
    /// </summary>
    private void OnSlotStatusChanged(object? sender, EventArgs e)
    {
        if (sender is VirtualControllerSlot slot)
        {
            EmulationStatus status = slot.Status;
            string detail = status.IsCreating ? "creating" : status.Detail ?? status.VirtualDevicePath ?? status.State.ToString();
            if (status.State == EmulationState.Failed)
            {
                _log.Warning($"Emulation failed for {slot.Device.Info.ProductName} ({status.Mode}): {detail}");
            }
            else if (status.State == EmulationState.Running)
            {
                _log.Info($"Emulation running for {slot.Device.Info.ProductName} ({status.Mode})");
            }
            else
            {
                _log.Debug($"Emulation status: {slot.Device.Info.ProductName} {status.Mode} state={status.State} ({detail})");
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Diffs the tracked controllers against the current entries, adding and removing
    /// virtual controller state as needed.
    /// </summary>
    private void Reconcile()
    {
        List<VirtualControllerSlot> toCreate = new List<VirtualControllerSlot>();
        lock (_sync)
        {
            HashSet<DualSenseDevice> current = _tracker.Controllers.OfType<DualSenseDevice>().ToHashSet();

            foreach (KeyValuePair<DualSenseDevice, VirtualControllerSlot> pair in _entries.ToList())
            {
                if (current.Contains(pair.Key))
                {
                    continue;
                }

                _entries.Remove(pair.Key);
                _temporaryModes.TryRemove(pair.Key, out _);
                pair.Value.StatusChanged -= OnSlotStatusChanged;
                pair.Value.MarkStopped();
                DeviceDisposeUnsubscribe(pair.Key);
                DisposeEntryLocked(pair.Value);
            }

            foreach (DualSenseDevice device in current)
            {
                if (_entries.ContainsKey(device))
                {
                    continue;
                }

                VirtualControllerSlot entry = new VirtualControllerSlot(device);
                entry.StatusChanged += OnSlotStatusChanged;
                _entries.Add(device, entry);
                DeviceSubscribe(device);
                EmulationSettings settings = GetEmulationSettings(device);
                entry.Device.AudioLane.SoftwareGain = (float)Math.Clamp(settings.Forward.SoftwareVolumeScale, 1.0, 8.0);
                if (StartCreationLocked(entry))
                {
                    toCreate.Add(entry);
                }
                else
                {
                    // No virtual device (emulation off or unsupported platform), but a
                    // direct (system-output or application) source forwards without one.
                    SyncAudioLocked(entry, settings.Forward.AudioSource, settings.Forward.AudioApp, settings.Forward.AudioEnabled);
                }
            }
        }

        foreach (VirtualControllerSlot entry in toCreate)
        {
            _ = CreateVirtualControllerAsync(entry, false);
        }
    }

    /// <summary>
    /// Prepares an entry for a (re)creation: resolves the controller's emulation mode,
    /// surfaces the creating status, and reports whether a virtual controller should be
    /// created. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private bool StartCreationLocked(VirtualControllerSlot entry)
    {
        if (!IsSupported)
        {
            entry.MarkUnavailable("Emulation is not available on this platform");
            return false;
        }

        EmulationMode mode = GetEmulationSettings(entry.Device).Mode;
        if (mode == EmulationMode.Off)
        {
            entry.MarkDisabled("Emulation is disabled for this controller");
            return false;
        }

        entry.BeginCreation(mode);
        return true;
    }

    /// <summary>
    /// Runs the device creation + path discovery on a background thread so the tracker
    /// callback does not block on enumeration polling. The whole cycle is serialized
    /// through <see cref="_creationGate"/> (see there for why). A fresh persona is registered
    /// for the virtual controller and attached to the local host. When
    /// <paramref name="settleAfterRemoval"/> is set, a short delay is applied first so the USBIP
    /// client finishes detaching the previous device. Creation is retried once after
    /// <see cref="CreateRetryDelay"/> on failure.
    /// </summary>
    private async Task CreateVirtualControllerAsync(VirtualControllerSlot entry, bool settleAfterRemoval)
    {
        int generation = entry.Generation;
        if (!IsSupported)
        {
            entry.MarkUnavailable("Emulation is not available on this platform");
            return;
        }

        EmulationMode mode = GetEmulationSettings(entry.Device).Mode;
        if (mode == EmulationMode.Off)
        {
            entry.MarkDisabled("Emulation is disabled for this controller");
            return;
        }

        await _creationGate.WaitAsync();
        try
        {
            await CreateEmulationVirtualControllerAsync(entry, generation, mode, settleAfterRemoval);
        }
        catch (Exception ex)
        {
            _log.Error(
                $"Failed to create the emulated {mode} controller for {entry.Device.Info.ProductName}; the slot is marked failed and retries on the next settings change",
                ex);
            entry.MarkFailed(mode, ex.Message);
        }
        finally
        {
            _creationGate.Release();
        }
    }

    /// <summary>
    /// Runs the Emulation device registration, attachment and path discovery. The whole cycle is
    /// serialized through <see cref="_creationGate"/>: a fresh persona is registered on the shared
    /// Emulation server and attached to the local host, and the device then appears in HID
    /// enumeration, so the same discovery/cleanup flow applies. Creation is retried once after
    /// <see cref="CreateRetryDelay"/> on failure. Caller must hold <see cref="_creationGate"/>.
    /// </summary>
    private async Task CreateEmulationVirtualControllerAsync(VirtualControllerSlot entry, int generation, EmulationMode mode,
        bool settleAfterRemoval)
    {
        EmulationServer server;
        lock (_sync)
        {
            if (_disposed || IsStale(entry, generation))
            {
                // The service was disposed or the entry was rebuilt while this creation
                // was queued: a server started for a creation that is already stale must
                // not be left behind.
                MaybeTeardownEmulationServerLocked();
                return;
            }

            if (!EnsureEmulationServerLocked())
            {
                entry.MarkFailed(mode, "Could not start the Emulation server");
                return;
            }

            server = _emulationServer!;
        }

        if (settleAfterRemoval)
        {
            await Task.Delay(RecreationSettleDelay);
        }

        EmulationSettings emulation = GetEmulationSettings(entry.Device);
        bool edge = emulation?.Variant.DualSense == DualSenseVariant.Edge;
        DualShock4Variant ds4Variant = emulation?.Variant.DualShock4 ?? DualShock4Variant.V2;
        EmulationAudioSource audioSource = emulation?.Forward.AudioSource ?? EmulationAudioSource.ControllerEndpoint;
        string audioApp = emulation?.Forward.AudioApp ?? string.Empty;
        bool audioEnabled = emulation?.Forward.AudioEnabled ?? true;
        ForwardSettings forwarding = emulation?.Forward ?? new ForwardSettings();
        (ushort vid, ushort pid) = GetDeviceIds(mode, edge, ds4Variant);
        DualSenseDeviceOutputs outputs;
        lock (_sync)
        {
            if (IsStale(entry, generation))
            {
                MaybeTeardownEmulationServerLocked();
                return;
            }

            // Ensure the controller's special-action engine is attached; its light
            // overrides now go through the device's lighting authority.
            _specialActions.GetOrCreate(entry.Device);
            // One shared outputs lane per entry: the virtual controller and the
            // audio forwarder must serialize writes to the physical device.
            entry.Outputs ??= new DualSenseDeviceOutputs(entry.Device);
            outputs = entry.Outputs;
        }

        // The Emulation Xbox 360 persona is a vendor-class device with no HID interface:
        // HID enumeration can never find it.
        bool canDiscover = mode != EmulationMode.Xbox360;
        HashSet<string> before = canDiscover
            ? _enumerator.EnumerateIncludingExcluded(vid, pid).Select(info => info.Path).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        VirtualControllerCreation? creation = TryCreateEmulationDevice(entry, server, mode, outputs, entry.Device.UsesVibrationV2, edge, ds4Variant,
            forwarding);
        if (creation is null)
        {
            _log.Debug(
                $"Failed to create the virtual {mode} device for {entry.Device.Info.ProductName}; retrying in {(int)CreateRetryDelay.TotalMilliseconds} ms");
            await Task.Delay(CreateRetryDelay);

            if (IsStale(entry, generation))
            {
                lock (_sync)
                {
                    MaybeTeardownEmulationServerLocked();
                }

                return;
            }

            creation = TryCreateEmulationDevice(entry, server, mode, outputs, entry.Device.UsesVibrationV2, edge, ds4Variant, forwarding);
        }

        if (creation is null)
        {
            entry.MarkFailed(mode,
                "The emulation server could not create the virtual device. Check the emulation logs; USB/IP must be installed for auto-attachment");
            lock (_sync)
            {
                MaybeTeardownEmulationServerLocked();
            }

            return;
        }

        IVirtualController virtualController = creation.Controller;
        VirtualUsbDevice persona = creation.Persona;

        // The persona is spotlighted on the entry (and the in-flight set) from the moment it
        // attaches until this creation commits or cleans it up, so the shared server can never
        // stop while it is host-attached. personaHandled marks either outcome; the catch below
        // only cleans up a persona this creation did not hand over to the entry.
        bool personaHandled = false;

        void CleanupAttachedPersona()
        {
            virtualController.Dispose();
            server.TryDetach(persona, out _);
            server.RemoveDevice(persona);
            lock (_sync)
            {
                entry.Persona = null;
                entry.Device.LightbarPassthroughEnabled = false;
                entry.Device.PlayerLedPassthroughEnabled = false;
                _inFlightEmulationPersonas.Remove(persona);
                MaybeTeardownEmulationServerLocked();
            }

            personaHandled = true;
        }

        try
        {
            // Candidates discovered for this creation; empty when the mode never appears in HID.
            HashSet<string> candidates = canDiscover
                ? await DiscoverVirtualPathCandidatesAsync(vid, pid, entry.Device.Info.Path, before)
                : [];
            if (canDiscover && candidates.Count != 1)
            {
                // The persona is detached and unregistered again, so no half-attached
                // virtual device is left behind. All candidates were already excluded on
                // first sighting during discovery; afterwards, any that survived removal
                // was real hardware and is unexcluded again.
                _log.Warning(entry.Device.Info.ProductName + ": " + (candidates.Count == 0
                    ? $"could not discover the virtual {mode} device in HID enumeration; emulation stays off for this controller"
                    : $"found {candidates.Count} new matching devices instead of one; emulation stays off for this controller"));
                CleanupAttachedPersona();
                await UnexcludeSurvivingCandidatesAsync(vid, pid, candidates);
                entry.MarkFailed(mode,
                    "The virtual device did not appear in HID enumeration. Check the emulation logs; USB/IP must be installed for auto-attachment");
                return;
            }

            if (candidates.Count == 1)
            {
                virtualController.VirtualDevicePath = candidates.Single();
            }

            lock (_sync)
            {
                if (IsStale(entry, generation))
                {
                    // The stale virtual device's path stays excluded: unexcluding before
                    // the detach completes would let the watcher see it as a phantom.
                    // Re-created devices reusing the path are still discoverable via
                    // EnumerateIncludingExcluded during discovery.
                    // The forwarder stays: it may be feeding direct system audio, which
                    // lives independently of the virtual device.
                    CleanupAttachedPersona();
                    return;
                }

                personaHandled = true;
                entry.Persona = persona;
                entry.Virtual = virtualController;
                ApplyPassthroughLocked(entry, forwarding);
                _inFlightEmulationPersonas.Remove(persona);

                // The controller-audio capture taps the Emulation persona's speaker endpoint
                // and feeds the forwarder; a direct source feeds the forwarder itself (see
                // SyncAudioLocked) and needs no controller capture.
                IDisposable? capture = null;
                if (audioSource == EmulationAudioSource.ControllerEndpoint
                    && audioEnabled
                    && IsSpeakerBearingAdapter(virtualController))
                {
                    EnsureForwarderLocked(entry, emulation);
                    capture = TryCreateControllerCapture(virtualController, entry.Forwarder);
                }

                if (entry.Forwarder?.IsDisposed == true)
                {
                    // The audio source was switched away and back mid-creation, tearing
                    // the forwarder down: drop the dead capture and rebuild from the
                    // current settings instead of publishing it.
                    capture?.Dispose();
                    capture = null;
                    EmulationSettings current = GetEmulationSettings(entry.Device);
                    audioSource = current.Forward.AudioSource;
                    audioApp = current.Forward.AudioApp;
                    audioEnabled = current.Forward.AudioEnabled;
                    if (audioSource == EmulationAudioSource.ControllerEndpoint
                        && audioEnabled
                        && IsSpeakerBearingAdapter(virtualController))
                    {
                        EnsureForwarderLocked(entry, current);
                        capture = TryCreateControllerCapture(virtualController, entry.Forwarder);
                    }
                }

                entry.Capture = capture;
                WireGameCallbacksLocked(entry);
                ApplyButtonMappingsLocked(virtualController, entry.Device);
                SyncAudioLocked(entry, audioSource, audioApp, audioEnabled);
            }

            entry.MarkRunning(mode, virtualController.VirtualDevicePath,
                mode == EmulationMode.DualSense ? edge ? DualSenseVariant.Edge : DualSenseVariant.Standard : null,
                mode == EmulationMode.DualShock4 ? ds4Variant : null);
        }
        catch
        {
            if (!personaHandled)
            {
                CleanupAttachedPersona();
            }

            throw;
        }
    }

    /// <summary>
    /// Registers a fresh Emulation persona on the shared server (see
    /// <see cref="IVirtualControllerFactory"/>) and attaches it to the local host. On failure
    /// the persona is unregistered again (detached first when it was attached) and the adapter is
    /// disposed, so nothing is left behind for the retry. On success the persona is spotlighted on
    /// the entry and the in-flight set until the calling creation commits or cleans it up.
    /// </summary>
    /// <param name="entry">Entry the attached persona is spotlighted on until the owning
    /// creation commits or cleans it up.</param>
    /// <param name="server">The running Emulation server hosting the persona.</param>
    /// <param name="mode">The requested emulation mode.</param>
    /// <param name="outputs">The physical controller receiving host feedback.</param>
    /// <param name="vibrationV2">Whether the physical controller uses the V2 report format.</param>
    /// <param name="edge">Whether the virtual DualSense should be an Edge variant (DualSense mode only).</param>
    /// <param name="ds4Variant">The DualShock 4 hardware generation to present (DualShock 4 mode only).</param>
    /// <param name="forwarding">The host-feedback forwarding options to apply before the host can send output.</param>
    /// <returns>The created adapter and persona, or <c>null</c> when creation or attachment failed.</returns>
    private VirtualControllerCreation? TryCreateEmulationDevice(VirtualControllerSlot entry, EmulationServer server, EmulationMode mode,
        DualSenseDeviceOutputs outputs, bool vibrationV2, bool edge, DualShock4Variant ds4Variant, ForwardSettings forwarding)
    {
        VirtualControllerCreation? creation = null;
        try
        {
            creation = _virtualFactory.Create(server, mode, outputs, vibrationV2, edge, ds4Variant);
            if (creation is null)
            {
                return null;
            }

            // Apply before attaching so the host cannot send output in the window
            // between the device appearing and the settings being known.
            creation.Controller.ApplyForwardingSettings(ForwardingSnapshot.From(forwarding));
            if (server.TryAttach(creation.Persona, out string? error))
            {
                // Spotlight the attached persona immediately: until the owning creation
                // commits or cleans it up, a Refresh or a tracker removal may dispose the
                // entry, and the shared server must not stop while the persona is
                // host-attached.
                lock (_sync)
                {
                    entry.Persona = creation.Persona;
                    _inFlightEmulationPersonas.Add(creation.Persona);
                }

                return creation;
            }

            _log.Warning($"Failed to attach the virtual {mode} device for {entry.Device.Info.ProductName}: {error}");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to create or attach the emulated {mode} controller for {entry.Device.Info.ProductName}", ex);
        }

        if (creation is not null)
        {
            creation.Controller.Dispose();
            server.TryDetach(creation.Persona, out _);
            server.RemoveDevice(creation.Persona);
        }

        return null;
    }

    /// <summary>
    /// Whether the given creation is still wanted: the service is not disposed, the
    /// entry is still tracked, and its generation was not bumped by a newer rebuild.
    /// </summary>
    private bool IsStale(VirtualControllerSlot entry, int generation)
    {
        lock (_sync)
        {
            return _disposed || !_entries.TryGetValue(entry.Device, out VirtualControllerSlot? current)
                             || !ReferenceEquals(current, entry) || entry.Generation != generation;
        }
    }

    /// <summary>
    /// Gets the effective emulation settings of a controller: its stored settings with
    /// a temporary mode and variant override applied when one is active. Returns a copy when
    /// overridden so the stored object is never mutated.
    /// </summary>
    private EmulationSettings GetEmulationSettings(DualSenseDevice device)
    {
        EmulationSettings stored = _controllerInfo.GetEmulationSettings(device.Key);
        if (_temporaryModes.TryGetValue(device, out EmulationOverride? temp))
        {
            EmulationMode mode = temp.Mode ?? stored.Mode;
            DualSenseVariant dsVariant = temp.DualSenseVariant ?? stored.Variant.DualSense;
            DualShock4Variant ds4Variant = temp.DualShock4Variant ?? stored.Variant.DualShock4;
            EmulationAudioSource audioSource = temp.AudioSource ?? stored.Forward.AudioSource;
            string audioApp = temp.AudioApp ?? stored.Forward.AudioApp;
            bool audioChanged = stored.Forward.AudioSource != audioSource
                                || !string.Equals(stored.Forward.AudioApp, audioApp, StringComparison.Ordinal);
            if (stored.Mode != mode || stored.Variant.DualSense != dsVariant || stored.Variant.DualShock4 != ds4Variant
                || audioChanged)
            {
                return new EmulationSettings
                {
                    Mode = mode,
                    Variant = new VariantSettings
                    {
                        DualSense = dsVariant,
                        DualShock4 = ds4Variant
                    },
                    Forward = audioChanged ? CloneForward(stored.Forward, audioSource, audioApp) : stored.Forward,
                    Mappings = stored.Mappings
                };
            }
        }

        return stored;
    }

    /// <summary>
    /// Copies the forwarding settings with the audio source overridden, so a temporary
    /// override never mutates the stored (or profile-bound) object. Caller must hold
    /// no lock for the copy itself.
    /// </summary>
    private static ForwardSettings CloneForward(ForwardSettings source, EmulationAudioSource audioSource, string audioApp)
    {
        return new ForwardSettings
        {
            AudioOutput = source.AudioOutput,
            Volume = source.Volume,
            AudioBufferLength = source.AudioBufferLength,
            PacketMode = source.PacketMode,
            Haptics = source.Haptics,
            ForceAudioHaptics = source.ForceAudioHaptics,
            Rumble = source.Rumble,
            Triggers = source.Triggers,
            Lightbar = source.Lightbar,
            PlayerLeds = source.PlayerLeds,
            HapticsEnabled = source.HapticsEnabled,
            AudioEnabled = source.AudioEnabled,
            AudioSource = audioSource,
            AudioApp = audioApp
        };
    }

    /// <summary>
    /// Creates the host-audio forwarder: Bluetooth audio reports to the physical
    /// controller over the same outputs lane, and the USB UAC render endpoint for
    /// wired playback (when the pad exposes one). The controller's forwarding
    /// volume, haptic strength, haptics/audio toggles and speaker/headset route are
    /// applied on creation. Without an audio engine (the unit-test fixture) no USB
    /// render endpoint is constructed; the forwarder simply has no transport.
    /// </summary>
    private DualSenseAudioForwarder CreateAudioForwarder(DualSenseDeviceOutputs outputs, EmulationSettings? emulation)
    {
        DualSenseUsbAudioTarget? usbTarget = _audioEngine is not null
                                             && DualSenseAudioForwarder.ShouldUseUsbTarget(outputs.ConnectionType)
            ? new DualSenseUsbAudioTarget(_audioEngine, new DualSenseAudioEndpointFinder(_audioEngine))
            : null;
        DualSenseAudioForwarder forwarder = new DualSenseAudioForwarder(outputs, usbTarget);
        if (emulation is not null)
        {
            // One snapshot path for creation and live updates, so a new forwarding option
            // cannot be applied on only one of them.
            forwarder.ApplyForwardingSettings(ForwardingSnapshot.From(emulation.Forward));
        }

        return forwarder;
    }

    /// <summary>
    /// Applies the host-feedback forwarding options to the entry's live adapter and audio
    /// forwarder without recreating either. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private static void ApplyForwardingLocked(VirtualControllerSlot entry, EmulationSettings? emulation)
    {
        ForwardSettings forwarding = emulation?.Forward ?? new ForwardSettings();
        ForwardingSnapshot snapshot = ForwardingSnapshot.From(forwarding);
        entry.Virtual?.ApplyForwardingSettings(snapshot);
        entry.Forwarder?.ApplyForwardingSettings(snapshot);
        // The software volume applies to every audio owner on this device, including the
        // local player and special-action sounds that never touch the forwarder.
        entry.Device.AudioLane.SoftwareGain = (float)snapshot.SoftwareVolumeScale;
        ApplyPassthroughLocked(entry, forwarding);
    }

    /// <summary>
    /// Lets the game own the lightbar and player LEDs only while a live DualSense virtual
    /// device mirrors the pad and the matching forwarding option is on; otherwise the
    /// profile effects must keep driving the physical controller.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private static void ApplyPassthroughLocked(VirtualControllerSlot entry, ForwardSettings forwarding)
    {
        bool dualSense = entry.Virtual is VirtualDualSenseController;
        entry.Device.LightbarPassthroughEnabled = dualSense && forwarding.Lightbar;
        entry.Device.PlayerLedPassthroughEnabled = dualSense && forwarding.PlayerLeds;
    }

    /// <summary>
    /// Brings the entry's audio side to the requested source. The direct sources (the
    /// system output and a single application) are routed straight to the physical
    /// controller and need no virtual device; steady states are left untouched so
    /// spurious syncs never drop buffered audio, while a source or application change
    /// restarts the direct capture. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void SyncAudioLocked(VirtualControllerSlot entry, EmulationAudioSource audioSource, string audioApp, bool audioEnabled)
    {
        bool direct = audioSource is EmulationAudioSource.SystemOutput or EmulationAudioSource.Application;
        if (!direct)
        {
            if (!audioEnabled)
            {
                // The controller-endpoint passthrough is off: stop the capture. The
                // forwarder stays alive when game haptics are enabled because it is the
                // only Bluetooth lane that carries them; its pump idles until the game
                // first sends a nonzero haptics payload. Direct captures are independent
                // and never reach this branch, but one may still be running from a
                // previous source.
                StopControllerCaptureLocked(entry);
                EmulationSettings settings = GetEmulationSettings(entry.Device);
                if (settings.Forward.HapticsEnabled
                    && entry.Virtual is VirtualDualSenseController
                    && entry.Outputs?.ConnectionType == ConnectionType.Bluetooth)
                {
                    // Game haptics only travel through the forwarder's Bluetooth audio
                    // lane; a wired pad has no use for it with the audio passthrough off.
                    StopDirectCaptureLocked(entry);
                    EnsureForwarderLocked(entry, settings);
                    entry.Forwarder?.Flush();
                }
                else
                {
                    TeardownAudioLocked(entry);
                }

                return;
            }

            if (entry.DirectAudioActive)
            {
                DeactivateDirectAudioLocked(entry);
            }
            else
            {
                ResumeControllerCaptureLocked(entry);
            }

            return;
        }

        if (entry.DirectAudioActive && entry.DirectSource == audioSource
                                    && string.Equals(entry.DirectApp, audioApp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (entry.DirectAudioActive)
        {
            // The source or the chosen application changed: stop the old capture without
            // touching the forwarder, then start the new one into the same ring.
            StopDirectCaptureLocked(entry);
        }

        ActivateDirectAudioLocked(entry, audioSource, audioApp);
    }

    /// <summary>
    /// Starts feeding the forwarder from the requested direct source (the system-output
    /// loopback or an application's process loopback), suspending the virtual
    /// controller's capture while it owns the ring so two unrelated streams can never
    /// interleave. Buffered PCM from the previous source is dropped for the same reason.
    /// When the chosen application is not running, the virtual controller's audio keeps
    /// driving the controller instead. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void ActivateDirectAudioLocked(VirtualControllerSlot entry, EmulationAudioSource audioSource, string audioApp)
    {
        if (entry.Forwarder is null)
        {
            EnsureForwarderLocked(entry, GetEmulationSettings(entry.Device));
        }

        if (entry.Forwarder is not { } forwarder)
        {
            return;
        }

        IForwardedAudioCapture capture;
        if (audioSource == EmulationAudioSource.Application)
        {
            if (!_audioApplications.TryResolveProcessId(audioApp, out uint processId))
            {
                // Nothing to capture yet: leave the controller's own audio running rather
                // than streaming silence. Re-selecting the application retries.
                _log.Info($"The application '{Path.GetFileName(audioApp)}' is not running; the virtual controller's audio keeps driving the controller");
                forwarder.Flush();
                if (entry.Capture is IVirtualAudioCapture resumeCapture)
                {
                    resumeCapture.IsSuspended = false;
                }
                else
                {
                    TeardownAudioLocked(entry);
                }

                return;
            }

            capture = new ApplicationLoopbackAudioCapture(processId, audioApp, forwarder);
        }
        else
        {
            capture = new SystemOutputAudioCapture(_audioEngine, forwarder);
        }

        // Suspend the controller capture before the direct capture starts feeding: the
        // switch is a hard cutover, not a mix.
        if (entry.Capture is IVirtualAudioCapture controllerCapture)
        {
            controllerCapture.IsSuspended = true;
        }

        forwarder.Flush();

        if (!capture.Start())
        {
            _log.Warning(entry.Device.Info.ProductName + ": " + (entry.Capture is IVirtualAudioCapture
                ? "the direct audio source could not be captured; the virtual controller's audio keeps driving the controller"
                : "the direct audio source could not be captured; no audio will be forwarded"));
            capture.Stop();
            if (entry.Capture is IVirtualAudioCapture fallbackCapture)
            {
                fallbackCapture.IsSuspended = false;
            }

            return;
        }

        entry.DirectCapture = capture;
        entry.DirectSource = audioSource;
        entry.DirectApp = audioApp;
        entry.DirectAudioActive = true;
        _log.Info(audioSource == EmulationAudioSource.Application
            ? $"Host audio forwarding now follows the application '{Path.GetFileName(audioApp)}' for {entry.Device.Info.ProductName}"
            : $"Host audio forwarding now follows the system's default output for {entry.Device.Info.ProductName}");
    }

    /// <summary>
    /// Stops the direct capture. The forwarder is kept when the virtual controller's capture
    /// can take over feeding it: an existing controller capture is resumed, and a missing one
    /// (a controller endpoint selected after a direct source owned the forwarder) is rebuilt.
    /// The forwarder is disposed only when no speaker-bearing capture can feed it. Caller must
    /// hold <see cref="_sync"/>.
    /// </summary>
    private void DeactivateDirectAudioLocked(VirtualControllerSlot entry)
    {
        StopDirectCaptureLocked(entry);

        if (ResumeControllerCaptureLocked(entry))
        {
            return;
        }

        TeardownAudioLocked(entry);
    }

    /// <summary>
    /// Whether the virtual controller presents a speaker endpoint that can feed the forwarder.
    /// The Xbox 360 persona has no speaker, so it never gets a controller capture.
    /// </summary>
    private static bool IsSpeakerBearingAdapter(IVirtualController? virtualController) =>
        virtualController is VirtualDualSenseController or VirtualDualShock4Controller;

    /// <summary>
    /// Resumes the entry's controller capture, or rebuilds it when the controller endpoint was
    /// selected while a direct source owned the forwarder and no capture existed. Rebuilding
    /// keeps the live forwarder, so leaving a direct source does not silence the controller
    /// until it is re-created. Returns <c>false</c> when nothing speaker-bearing can feed the
    /// forwarder. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private bool ResumeControllerCaptureLocked(VirtualControllerSlot entry)
    {
        if (entry.Capture is IVirtualAudioCapture controllerCapture)
        {
            controllerCapture.IsSuspended = false;
            return true;
        }

        if (!IsSpeakerBearingAdapter(entry.Virtual))
        {
            return false;
        }

        EnsureForwarderLocked(entry, GetEmulationSettings(entry.Device));
        if (entry.Forwarder is not { } forwarder)
        {
            return false;
        }

        IDisposable? capture = TryCreateControllerCapture(entry.Virtual!, forwarder);
        if (capture is not IVirtualAudioCapture rebuiltCapture)
        {
            capture?.Dispose();
            return false;
        }

        entry.Capture = capture;
        rebuiltCapture.IsSuspended = false;
        _log.Info($"Host audio forwarding now follows the virtual controller's speaker endpoint for {entry.Device.Info.ProductName}");
        return true;
    }

    /// <summary>
    /// Stops and drops the entry's direct capture, clearing the remembered source and
    /// application. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private static void StopDirectCaptureLocked(VirtualControllerSlot entry)
    {
        entry.DirectAudioActive = false;
        entry.DirectCapture?.Stop();
        entry.DirectCapture = null;
        entry.DirectSource = EmulationAudioSource.ControllerEndpoint;
        entry.DirectApp = string.Empty;
    }

    /// <summary>
    /// Stops and drops the entry's controller-endpoint capture, leaving the forwarder
    /// alone. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private static void StopControllerCaptureLocked(VirtualControllerSlot entry)
    {
        entry.Capture?.Dispose();
        entry.Capture = null;
    }

    /// <summary>
    /// Builds the capture tapping the virtual controller's audio endpoint, or <c>null</c>
    /// when following a direct source (no sink), when the sink is a disposed forwarder, or
    /// when the virtual controller exposes no audio endpoint.
    /// </summary>
    internal static IDisposable? TryCreateControllerCapture(IVirtualController virtualController, IAudioPcmSink? sink)
    {
        if (sink is null || sink is DualSenseAudioForwarder { IsDisposed: true })
        {
            return null;
        }

        if (virtualController is VirtualDualSenseController virtualDualSense)
        {
            return new EmulationDualSenseAudioCapture(virtualDualSense.Persona, sink);
        }

        if (virtualController is VirtualDualShock4Controller virtualDualShock4)
        {
            return new EmulationDualShock4AudioCapture(virtualDualShock4.Persona, sink);
        }

        return null;
    }

    /// <summary>
    /// Creates and starts the entry's forwarder when missing, wiring the virtual
    /// controller's game-output callbacks when one is present. A disposed forwarder
    /// is replaced. No-op when a live one already exists. Caller must hold
    /// <see cref="_sync"/>.
    /// </summary>
    private void EnsureForwarderLocked(VirtualControllerSlot entry, EmulationSettings? emulation)
    {
        if (entry.Forwarder is { } existing)
        {
            if (!existing.IsDisposed)
            {
                if (emulation is not null)
                {
                    existing.ApplyForwardingSettings(ForwardingSnapshot.From(emulation.Forward));
                }

                entry.Device.AudioLevelProvider = () => existing.AudioLevel;
                WireGameCallbacksLocked(entry);
                return;
            }

            entry.Forwarder = null;
        }

        entry.Outputs ??= new DualSenseDeviceOutputs(entry.Device);
        DualSenseAudioForwarder forwarder = CreateAudioForwarder(entry.Outputs, emulation);
        entry.Forwarder = forwarder;
        entry.Device.AudioLevelProvider = () => forwarder.AudioLevel;
        WireGameCallbacksLocked(entry);
        bool started = forwarder.Start();
        if (started)
        {
            _log.Debug($"Host audio forwarding started for {entry.Device.Info.ProductName}");
        }
        else
        {
            _log.Warning($"Host audio forwarding is unavailable for {entry.Device.Info.ProductName}; no forwarded audio will reach the controller");
        }
    }

    /// <summary>
    /// Subscribes the forwarder to the virtual controller's game-output callbacks so
    /// lightbar, triggers and game haptics ride the Bluetooth audio reports. No-op
    /// without both sides, or when already wired. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void WireGameCallbacksLocked(VirtualControllerSlot entry)
    {
        if (entry.AudioCallbacksWired || entry.Forwarder is not { } forwarder)
        {
            return;
        }

        if (entry.Virtual is VirtualDualSenseController virtualDualSense)
        {
            virtualDualSense.OutputStateReceived += forwarder.UpdateGameOutputState;
            virtualDualSense.RealtimeHapticsReceived += forwarder.UpdateGameHaptics;
            entry.AudioCallbacksWired = true;
        }
        else if (entry.Virtual is VirtualDualShock4Controller virtualDualShock4)
        {
            virtualDualShock4.OutputStateReceived += forwarder.UpdateGameOutputState;
            entry.AudioCallbacksWired = true;
        }
    }

    /// <summary>
    /// Unsubscribes the forwarder from the virtual controller's game-output
    /// callbacks. Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void UnwireGameCallbacksLocked(VirtualControllerSlot entry)
    {
        if (!entry.AudioCallbacksWired || entry.Forwarder is not { } forwarder)
        {
            entry.AudioCallbacksWired = false;
            return;
        }

        if (entry.Virtual is VirtualDualSenseController virtualDualSense)
        {
            virtualDualSense.OutputStateReceived -= forwarder.UpdateGameOutputState;
            virtualDualSense.RealtimeHapticsReceived -= forwarder.UpdateGameHaptics;
        }
        else if (entry.Virtual is VirtualDualShock4Controller virtualDualShock4)
        {
            virtualDualShock4.OutputStateReceived -= forwarder.UpdateGameOutputState;
        }

        entry.AudioCallbacksWired = false;
    }

    /// <summary>
    /// Disposes the entry's forwarder and loopback capture. Caller must hold
    /// <see cref="_sync"/>.
    /// </summary>
    private void TeardownAudioLocked(VirtualControllerSlot entry)
    {
        UnwireGameCallbacksLocked(entry);

        StopDirectCaptureLocked(entry);

        entry.Forwarder?.Dispose();
        entry.Forwarder = null;
        entry.Device.AudioLevelProvider = null;
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
                    _log.Debug($"Discovered virtual device candidate: {candidate}");
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
            _log.Debug($"Restoring non-virtual discovery candidate: {candidate}");
            _enumerator.RemoveExcludedDevice(candidate);
        }
    }

    /// <summary>
    /// Ensures the shared Emulation server exists and is listening, creating and starting it on
    /// first use (buses are per persona and torn down with it, see
    /// <see cref="CreateEmulationVirtualControllerAsync"/>).
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private bool EnsureEmulationServerLocked()
    {
        if (_emulationServer is not null)
        {
            return true;
        }

        EmulationServer? server = null;
        try
        {
            server = _emulationServerFactory();
            server.Start();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to start the Emulation server; virtual controllers cannot start. Verify that usbip-win2 is installed", ex);
            server?.Dispose();
            return false;
        }

        _emulationServer = server;
        _log.Debug("Emulation server ready");
        return true;
    }

    /// <summary>
    /// Stops and drops the shared Emulation server once no tracked entry still needs it: no
    /// persona is spotlighted by an in-flight creation, no entry holds an attached or live
    /// Emulation virtual controller (its persona must be detached first, which requires the
    /// server) and no entry's effective settings still ask for emulation (emulation off,
    /// controller removed). A fresh instance is created if emulation is needed again.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    private void MaybeTeardownEmulationServerLocked()
    {
        if (_emulationServer is null)
        {
            return;
        }

        // A persona that attached but has not been committed to an entry yet (or whose entry
        // was removed meanwhile) must be detached by its creation first; that cleanup runs even
        // when its entry is already gone from the tracked set.
        if (_inFlightEmulationPersonas.Count > 0)
        {
            return;
        }

        foreach (VirtualControllerSlot entry in _entries.Values)
        {
            if (entry.Persona is not null || entry.Virtual is not null)
            {
                return;
            }

            EmulationSettings settings = GetEmulationSettings(entry.Device);
            if (settings.Mode != EmulationMode.Off)
            {
                return;
            }
        }

        _log.Debug("Stopping the Emulation server");
        _emulationServer.Stop();
        _emulationServer.Dispose();
        _emulationServer = null;
    }

    /// <summary>
    /// Detaches and unregisters the entry's persona, keeping direct system audio running: its
    /// forwarder outlives the virtual device. A forwarder that only ever served the virtual
    /// controller's capture is disposed with it.
    /// Caller must hold <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when a virtual device was removed.</returns>
    private bool DisposeVirtualLocked(VirtualControllerSlot entry)
    {
        if (entry.Virtual is null)
        {
            // The entry may still be the reason the shared server is running (a failed or
            // disabled entry whose settings changed): let the teardown checks run.
            MaybeTeardownEmulationServerLocked();
            return false;
        }

        IVirtualController virtualController = entry.Virtual;
        UnwireGameCallbacksLocked(entry);
        entry.Virtual = null;
        entry.Device.LightbarPassthroughEnabled = false;
        entry.Device.PlayerLedPassthroughEnabled = false;

        entry.Capture?.Dispose();
        entry.Capture = null;

        if (!entry.DirectAudioActive)
        {
            entry.Forwarder?.Dispose();
            entry.Forwarder = null;
            entry.Device.AudioLevelProvider = null;
        }

        // The virtual device's path stays excluded: unexcluding before the detach
        // completes would let the watcher see the still-attached device as a phantom.
        // Re-created devices reusing the path are still discoverable via
        // EnumerateIncludingExcluded during discovery.
        virtualController.Dispose();
        if (entry.Persona is { } persona && _emulationServer is { } emulationServer)
        {
            if (!emulationServer.TryDetach(persona, out string? error))
            {
                _log.Warning(
                    $"Failed to detach the Emulation virtual device for {entry.Device.Info.ProductName}: {error}; it may stay attached until the server stops");
            }

            emulationServer.RemoveDevice(persona);
        }

        entry.Persona = null;

        // Reclaim the shared server once its last user is gone (the persona is detached and
        // unregistered).
        MaybeTeardownEmulationServerLocked();
        return true;
    }

    /// <summary>
    /// Removes the entry's virtual device and its audio forwarding. Caller must hold
    /// <see cref="_sync"/>.
    /// </summary>
    /// <returns><c>true</c> when a virtual device was removed.</returns>
    private bool DisposeEntryLocked(VirtualControllerSlot entry)
    {
        bool hadVirtual = DisposeVirtualLocked(entry);
        TeardownAudioLocked(entry);
        return hadVirtual;
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
            if (_entries.TryGetValue(device, out VirtualControllerSlot? entry))
            {
                entry.PushInput(report);
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
            if (_entries.TryGetValue(device, out VirtualControllerSlot? entry))
            {
                entry.PushBattery(e.CurrentState, device.InputReport);
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
            if (_entries.TryGetValue(device, out VirtualControllerSlot? entry))
            {
                entry.PushConnectionStatus(e.CurrentStatus);
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
}