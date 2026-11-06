using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Audio;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Emulation.Core;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// The per-controller owner of one Virtual Controller's lifecycle: desired settings,
/// explicit state transitions, the virtual device and persona, and its audio attachment.
/// </summary>
public sealed class VirtualControllerSlot
{
    /// <summary>
    /// Creates a slot for the given physical controller.
    /// </summary>
    /// <param name="device">The physical controller being mirrored.</param>
    public VirtualControllerSlot(DualSenseDevice device)
    {
        Device = device;
    }

    /// <summary>
    /// The physical controller being mirrored.
    /// </summary>
    public DualSenseDevice Device { get; }

    /// <summary>
    /// The current lifecycle state, surfaced for the UI.
    /// </summary>
    public EmulationStatus Status { get; private set; } = new EmulationStatus(EmulationMode.Off, false, null, null);

    /// <summary>
    /// Raised whenever <see cref="Status"/> changes.
    /// </summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Incremented on every rebuild so stale background creations are discarded.
    /// </summary>
    public int Generation;

    /// <summary>
    /// The virtual controller, or <c>null</c> while not emulating.
    /// </summary>
    public IVirtualController? Virtual;

    /// <summary>
    /// The physical controller's shared feedback lane.
    /// </summary>
    public DualSenseDeviceOutputs? Outputs;

    /// <summary>
    /// The host-audio forwarder, when one is active.
    /// </summary>
    public DualSenseAudioForwarder? Forwarder;

    /// <summary>
    /// Whether the forwarder is fed by the direct host capture.
    /// </summary>
    public bool DirectAudioActive;

    /// <summary>
    /// Whether the virtual controller's output callbacks are wired to the forwarder.
    /// </summary>
    public bool AudioCallbacksWired;

    /// <summary>
    /// The controller-endpoint capture feeding the forwarder, when active.
    /// </summary>
    public IDisposable? Capture;

    /// <summary>
    /// The direct host capture feeding the forwarder, when active.
    /// </summary>
    public IForwardedAudioCapture? DirectCapture;

    /// <summary>
    /// The source the direct capture was created for.
    /// </summary>
    public EmulationAudioSource DirectSource;

    /// <summary>
    /// The application captured by the direct capture.
    /// </summary>
    public string DirectApp = string.Empty;

    /// <summary>
    /// The Emulation persona while a creation is in flight or committed.
    /// </summary>
    public VirtualUsbDevice? Persona;

    /// <summary>
    /// Whether the slot currently holds a running virtual controller.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            return Status.State == EmulationState.Running;
        }
    }

    /// <summary>
    /// Marks the slot as unable to emulate, clearing any prior running state.
    /// </summary>
    /// <param name="detail">Why emulation is unavailable.</param>
    public void MarkUnavailable(string detail) => SetStatus(new EmulationStatus(EmulationMode.Off, false, detail, null, State: EmulationState.Off));

    /// <summary>
    /// Marks the slot as disabled by its stored settings.
    /// </summary>
    /// <param name="detail">Why emulation is disabled.</param>
    public void MarkDisabled(string detail) => SetStatus(new EmulationStatus(EmulationMode.Off, false, detail, null, State: EmulationState.Off));

    /// <summary>
    /// Starts a creation attempt.
    /// </summary>
    /// <param name="mode">The mode being created.</param>
    public void BeginCreation(EmulationMode mode) => SetStatus(new EmulationStatus(mode, false, null, null, true, State: EmulationState.Creating));

    /// <summary>
    /// Marks the slot as running.
    /// </summary>
    /// <param name="mode">The running mode.</param>
    /// <param name="path">The discovered host path.</param>
    /// <param name="variant">The DualSense variant, when applicable.</param>
    /// <param name="ds4Variant">The DualShock 4 variant, when applicable.</param>
    public void MarkRunning(EmulationMode mode, string? path, DualSenseVariant? variant, DualShock4Variant? ds4Variant) =>
        SetStatus(new EmulationStatus(mode, true, null, path, false, variant, ds4Variant, EmulationState.Running));

    /// <summary>
    /// Marks the last creation attempt as failed.
    /// </summary>
    /// <param name="mode">The mode that failed.</param>
    /// <param name="detail">The failure detail.</param>
    public void MarkFailed(EmulationMode mode, string detail) => SetStatus(new EmulationStatus(mode, false, detail, null, State: EmulationState.Failed));

    /// <summary>
    /// Marks the slot as stopped after disposal.
    /// </summary>
    public void MarkStopped() => SetStatus(new EmulationStatus(EmulationMode.Off, false, null, null, State: EmulationState.Stopped));

    /// <summary>
    /// Forwards the latest input report to the virtual controller.
    /// </summary>
    /// <param name="report">The latest physical input report.</param>
    public void PushInput(InputReport report) => Virtual?.PushInput(report);

    /// <summary>
    /// Forwards the latest battery state to the virtual controller, re-stamping the
    /// latest input first when one is available.
    /// </summary>
    /// <param name="battery">The latest battery state.</param>
    /// <param name="latestInput">The latest input report, when one has been received.</param>
    public void PushBattery(BatteryState battery, InputReport? latestInput)
    {
        if (Virtual is null)
        {
            return;
        }

        if (latestInput is { } report)
        {
            Virtual.PushInput(report);
        }

        Virtual.PushBattery(battery);
    }

    /// <summary>
    /// Forwards the latest connection status to the virtual controller.
    /// </summary>
    /// <param name="status">The latest connection status.</param>
    public void PushConnectionStatus(ConnectionStatus status) => Virtual?.PushConnectionStatus(status);

    private void SetStatus(EmulationStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}