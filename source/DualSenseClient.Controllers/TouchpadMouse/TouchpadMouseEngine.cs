using System.Diagnostics;
using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Logging;

namespace DualSenseClient.Controllers.TouchpadMouse;

/// <summary>
/// Per-controller touchpad-mouse engine: subscribes to the controller's input reports on
/// its read-loop thread and feeds them to a <see cref="TouchpadMouseProcessor"/>, which
/// emits pointer events through a per-controller <see cref="IMouseInjector"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each attached controller owns its own engine and injector, and reports are processed on
/// that controller's read thread, so the Windows injection buffer is never shared between
/// threads. The processor is not thread-safe; every access is guarded by a private lock.
/// </para>
/// <para>
/// The subscription handler catches every exception, so a failing processor or injector can
/// never tear down the controller's read loop. The first failure is logged; later failures
/// are dropped to avoid flooding the log at report rate.
/// </para>
/// </remarks>
public sealed class TouchpadMouseEngine : IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("TouchpadMouse");

    /// <summary>
    /// Guards the processor and the attached controller.
    /// </summary>
    private readonly Lock _lock = new Lock();

    /// <summary>
    /// Turns input reports into pointer and wheel events.
    /// </summary>
    private readonly TouchpadMouseProcessor _processor;

    /// <summary>
    /// The controller currently attached, or <c>null</c> when detached.
    /// </summary>
    private DualSenseDevice? _device;

    /// <summary>
    /// Whether a handler failure has already been reported.
    /// </summary>
    private bool _failureLogged;

    /// <summary>
    /// Creates a new engine that injects pointer events through the given per-controller
    /// injector.
    /// </summary>
    /// <param name="injector">The injector that receives pointer events.</param>
    /// <param name="options">The options used by subsequent reports.</param>
    public TouchpadMouseEngine(IMouseInjector injector, TouchpadMouseOptions options)
    {
        _processor = new TouchpadMouseProcessor(injector);
        _processor.UpdateOptions(options);
    }

    /// <summary>
    /// Attaches the engine to a controller: resets the gesture state so the first report
    /// initializes cleanly, then subscribes to the controller's input reports. Detaches any
    /// previously attached controller first.
    /// </summary>
    /// <param name="device">The controller to watch.</param>
    public void Attach(DualSenseDevice device)
    {
        lock (_lock)
        {
            DetachCore();

            _device = device;
            _failureLogged = false;
            _processor.Reset();
            device.InputReportReceived += OnInputReportReceived;
            _log.Debug($"Touchpad mouse attached to {device.Info.ProductName}");
        }
    }

    /// <summary>
    /// Replaces the options used by subsequent reports.
    /// </summary>
    /// <param name="options">The new options.</param>
    public void UpdateOptions(TouchpadMouseOptions options)
    {
        lock (_lock)
        {
            _processor.UpdateOptions(options);
        }
    }

    /// <summary>
    /// Detaches the engine from the current controller, releasing a held button and
    /// clearing all gesture state.
    /// </summary>
    public void Detach()
    {
        lock (_lock)
        {
            DetachCore();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Detach();

    /// <summary>
    /// Processes one input report on the controller's read-loop thread. Never throws: the
    /// first failure is logged and later failures are dropped, so a broken injector can
    /// neither break the read loop nor flood the log at report rate.
    /// </summary>
    /// <param name="sender">The controller that raised the event.</param>
    /// <param name="report">The parsed input report.</param>
    private void OnInputReportReceived(object? sender, InputReport report)
    {
        try
        {
            lock (_lock)
            {
                // A report from a previously attached controller may still be in flight while
                // the engine detaches or reattaches; it must not reach the new controller's
                // gesture state.
                if (!ReferenceEquals(sender, _device))
                {
                    return;
                }

                long now = (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                _processor.Process(report.Touchpad, report.Input.TouchPad, now);
            }
        }
        catch (Exception ex)
        {
            bool logFailure;
            lock (_lock)
            {
                logFailure = !_failureLogged;
                _failureLogged = true;
            }

            if (logFailure)
            {
                _log.Error($"Touchpad mouse input handling failed for {_device?.Info.ProductName ?? "the controller"}", ex);
            }
        }
    }

    /// <summary>
    /// Unsubscribes from the attached controller, releases a held button, and clears the
    /// gesture state. Callers must hold <see cref="_lock"/>.
    /// </summary>
    private void DetachCore()
    {
        if (_device is null)
        {
            return;
        }

        DualSenseDevice device = _device;
        _device = null;
        device.InputReportReceived -= OnInputReportReceived;

        // Detach must finish even when the injector throws while releasing a held button.
        try
        {
            _processor.Reset();
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to reset touchpad mouse state for {device.Info.ProductName}", ex);
        }

        _log.Debug("Touchpad mouse detached");
    }
}