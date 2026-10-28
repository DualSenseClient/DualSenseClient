using System.Diagnostics;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Hid;
using DualSenseClient.Logging;
using DualSenseClient.Settings.Sections;

namespace DualSenseClient.Controllers.DualSense.Lightbar;

/// <summary>
/// Runs a profile's animated lightbar effect on a 30 Hz timer and writes each frame to
/// the controller. Merges the current frame into consumers via <see cref="TryGetFrame"/>.
/// </summary>
public sealed class LightbarEffectController : IDisposable
{
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("LightbarEffectController");

    /// <summary>
    /// Frame period for animated effects.
    /// </summary>
    private const int TickPeriodMs = 33;

    private readonly DualSenseDevice _device;

    /// <summary>
    /// Guards the snapshot, clock, seed, battery band, chase state and timer.
    /// </summary>
    private readonly Lock _lock = new Lock();

    private readonly LightbarEffectClock _clock = new LightbarEffectClock();

    private Timer? _timer;
    private LightbarSettings? _settings;
    private int _batteryBand = 5;
    private int _randomSeed;
    private PlayerLedMask _playerLedMask;

    /// <summary>
    /// The chase mask currently shown, or null when the effect is not driving player LEDs.
    /// </summary>
    private PlayerLedMask? _chaseMask;

    private bool _disposed;

    /// <summary>
    /// Creates a controller that drives the lightbar of the given device.
    /// </summary>
    /// <param name="device">The device to evaluate and send for.</param>
    public LightbarEffectController(DualSenseDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Gets whether an animated effect is currently running.
    /// </summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _settings is not null;
            }
        }
    }

    /// <summary>
    /// Starts or updates the effect for the given settings. The settings are stored as-is,
    /// so callers must pass a snapshot they do not mutate afterwards.
    /// </summary>
    /// <param name="settings">Snapshot of the lightbar settings.</param>
    /// <param name="playerLedMask">Profile player LED mask to restore when a chase ends.</param>
    public void Apply(LightbarSettings settings, byte playerLedMask)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            LightbarMode? previousMode = _settings?.Mode;

            if (settings.Mode == LightbarMode.SingleColor)
            {
                StopLocked();
                return;
            }

            if (previousMode != settings.Mode)
            {
                long now = Stopwatch.GetTimestamp();
                _clock.Reset(now);
                _batteryBand = 5;
                _chaseMask = null;
                _randomSeed = Random.Shared.Next();
                _log.Debug($"Lightbar effect {settings.Mode} started");
            }

            _settings = settings;
            _playerLedMask = (PlayerLedMask)playerLedMask;

            if (_timer is null)
            {
                _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(TickPeriodMs));
            }
            else
            {
                _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickPeriodMs));
            }
        }
    }

    /// <summary>
    /// Stops the effect and clears all running state. No frame can be sent after this
    /// returns.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            StopLocked();
        }
    }

    /// <summary>
    /// Gets the current effect frame, evaluating it on demand.
    /// </summary>
    /// <param name="frame">The evaluated frame when the effect is active.</param>
    /// <returns><c>true</c> when a frame is available.</returns>
    public bool TryGetFrame(out LightbarEffectFrame frame)
    {
        lock (_lock)
        {
            if (_disposed || _settings is null || _device.LightbarPassthroughEnabled || _device.LightbarEffectSuspended)
            {
                frame = default;
                return false;
            }

            frame = EvaluateLocked();

            if (_device.PlayerLedPassthroughEnabled)
            {
                frame = frame with
                {
                    PlayerLeds = null
                };
            }

            return true;
        }
    }

    /// <summary>
    /// Re-arms the timer to evaluate immediately, so unpausing writes within one tick
    /// instead of waiting up to a full period.
    /// </summary>
    public void RequestImmediateFrame()
    {
        lock (_lock)
        {
            if (_disposed || _settings is null)
            {
                return;
            }

            _timer?.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(TickPeriodMs));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            StopLocked();
        }
    }

    /// <summary>
    /// Stops the timer and clears the running state. Caller must hold <see cref="_lock"/>.
    /// </summary>
    private void StopLocked()
    {
        if (_settings is not null)
        {
            _log.Debug($"Lightbar effect {_settings.Mode} stopped");
        }

        _settings = null;
        _chaseMask = null;
        _clock.Reset(Stopwatch.GetTimestamp());
        _batteryBand = 5;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Evaluates the effect and sends one frame. Caller must hold <see cref="_lock"/>.
    /// </summary>
    private void Tick()
    {
        lock (_lock)
        {
            if (_disposed || _settings is null)
            {
                return;
            }

            // Always evaluate so the clock records when passthrough started, but do not
            // send while the game owns the lights. Guard everything: this callback also
            // runs app-supplied code (audio level, output-event subscribers), and an
            // exception escaping a Timer callback would terminate the process.
            try
            {
                LightbarEffectFrame frame = EvaluateLocked();
                if (_device.LightbarPassthroughEnabled || _device.LightbarEffectSuspended)
                {
                    return;
                }

                SendFrame(frame);
            }
            catch (Exception ex)
            {
                _log.LogExceptionDetails(ex);
                StopLocked();
            }
        }
    }

    /// <summary>
    /// Evaluates the current settings, folding the timestamp, battery, audio and player
    /// LED capability into the context. Caller must hold <see cref="_lock"/>.
    /// </summary>
    private LightbarEffectFrame EvaluateLocked()
    {
        _clock.Suspended = _device.LightbarPassthroughEnabled || _device.LightbarEffectSuspended;

        LightbarEffectResult result = LightbarEffectEvaluator.Evaluate(new LightbarEffectContext
        {
            Settings = _settings!,
            ElapsedSeconds = _clock.GetElapsedSeconds(Stopwatch.GetTimestamp()),
            Battery = _device.CurrentBatteryState,
            LastBatteryBand = _batteryBand,
            AudioLevel = _device.AudioLevelProvider?.Invoke() ?? 0f,
            FullPlayerLedSupport = _device.FirmwareInfo?.HasFullPlayerLedSupport == true,
            RandomSeed = _randomSeed
        });

        _batteryBand = result.BatteryBand;
        return result.Frame;
    }

    /// <summary>
    /// Sends one effect frame, adding the player LEDs only while the effect drives them
    /// and restores the profile mask once after a chase ends.
    /// </summary>
    private void SendFrame(LightbarEffectFrame frame)
    {
        ValidFlags flag1 = ValidFlags.AllowLedColor;
        PlayerLedMask playerLeds = _playerLedMask;

        // While the game owns the player LEDs the chase state is left untouched, so
        // releasing them restores the chase or the profile mask on the next tick.
        if (!_device.PlayerLedPassthroughEnabled)
        {
            if (frame.PlayerLeds is PlayerLedMask chase)
            {
                _chaseMask = chase;
                flag1 |= ValidFlags.AllowPlayerIndicators;
                playerLeds = chase;
            }
            else if (_chaseMask is not null)
            {
                _chaseMask = null;
                flag1 |= ValidFlags.AllowPlayerIndicators;
            }
        }

        SetStateData payload = new SetStateData
        {
            ValidFlag1 = flag1,
            ValidFlag2 = ValidFlags.AllowBrightnessChange | ValidFlags.AllowColorFadeAnim,
            LightFadeAnimation = 0x02,
            LightBrightness = 0,
            PlayerLeds = playerLeds,
            LedRed = frame.Red,
            LedGreen = frame.Green,
            LedBlue = frame.Blue
        };

        try
        {
            _device.SendOutputState(payload);
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to send lightbar effect frame: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            // Disposing stops the timer first; this only covers a send already in flight.
            _log.Debug("Lightbar effect send skipped: device disposed");
        }
    }
}