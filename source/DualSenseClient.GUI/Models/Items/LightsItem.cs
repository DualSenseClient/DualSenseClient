using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DualSenseClient.Controllers.DualSense.Output;
using DualSenseClient.Controllers.Devices;
using DualSenseClient.Hid;
using DualSenseClient.Logging;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Display model for the lights section of the profile page. Wraps a <see cref="ControllerItem"/>
/// and exposes the controller lightbar color, microphone LED mode, and player LEDs for editing.
/// Each change is sent to the controller immediately.
/// </summary>
/// <remarks>
/// <para>
/// The DualSense protocol provides no way to read the current light state back, so the
/// item starts from a default state (PS-blue lightbar, all LEDs off) and only writes when
/// a value is changed. The full light state is sent on every change, so the page fully
/// controls the lights once the user interacts with it.
/// </para>
/// <para>
/// Writing is done with explicit <see cref="ValidFlags"/> (rather than
/// <see cref="OutputReportBuilder"/>) so the player LED field is always allowed to be
/// applied, which lets the user clear all player LEDs to off.
/// </para>
/// </remarks>
public sealed partial class LightsItem : ObservableObject, IDisposable
{
    /// <summary>
    /// Logger instance.
    /// </summary>
    private static readonly DualSenseClientLogger _log = DualSenseClientLogger.For("ProfilePage");

    /// <summary>
    /// The concrete controller the light state is written to, or <c>null</c> for
    /// non-DualSense devices.
    /// </summary>
    private readonly DualSenseDevice? _device;

    /// <summary>
    /// Tracks whether the item has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// The controller item being displayed.
    /// </summary>
    public ControllerItem Controller { get; }

    /// <summary>
    /// Human-readable product name.
    /// </summary>
    public string DisplayName => Controller.DisplayName;

    /// <summary>
    /// Lightbar red channel (0-255).
    /// </summary>
    [ObservableProperty] private double _ledRed;

    /// <summary>
    /// Lightbar green channel (0-255).
    /// </summary>
    [ObservableProperty] private double _ledGreen;

    /// <summary>
    /// Lightbar blue channel (0-255).
    /// </summary>
    [ObservableProperty] private double _ledBlue = 255;

    /// <summary>
    /// Microphone LED mode: <c>0</c> off, <c>1</c> on, <c>2</c> pulse.
    /// Doubles as the ComboBox selection index.
    /// </summary>
    [ObservableProperty] private int _muteLedMode;

    /// <summary>
    /// Whether player LED 1 (leftmost) is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed1;

    /// <summary>
    /// Whether player LED 2 is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed2;

    /// <summary>
    /// Whether player LED 3 (center) is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed3;

    /// <summary>
    /// Whether player LED 4 is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed4;

    /// <summary>
    /// Whether player LED 5 (rightmost) is lit.
    /// </summary>
    [ObservableProperty] private bool _playerLed5;

    /// <summary>
    /// Brush for the lightbar color preview swatch.
    /// </summary>
    public IBrush LightbarBrush => new SolidColorBrush(Color.FromRgb(Channel(LedRed), Channel(LedGreen), Channel(LedBlue)));

    /// <summary>
    /// Lightbar color as a "#RRGGBB" string.
    /// </summary>
    public string ColorHex => $"#{Channel(LedRed):X2}{Channel(LedGreen):X2}{Channel(LedBlue):X2}";

    /// <summary>
    /// Tracks whether a color update is in progress to avoid feedback loops
    /// between <see cref="LightbarColor"/> and the <see cref="LedRed"/>/
    /// <see cref="LedGreen"/>/<see cref="LedBlue"/> channel properties.
    /// </summary>
    private bool _syncingColor;

    /// <summary>
    /// Tracks whether output writes are temporarily suppressed (used by <see cref="SetPreview"/>
    /// so preview-only updates do not re-send state to the controller).
    /// </summary>
    private bool _suppressWrite;

    /// <summary>
    /// Lightbar color as an <see cref="Avalonia.Media.Color"/>, bridged two-way
    /// with the channel doubles for binding to <c>ColorView</c>.
    /// </summary>
    public Color LightbarColor
    {
        get => Color.FromRgb(Channel(LedRed), Channel(LedGreen), Channel(LedBlue));
        set
        {
            if (_syncingColor)
            {
                return;
            }

            _syncingColor = true;
            try
            {
                LedRed = value.R;
                LedGreen = value.G;
                LedBlue = value.B;
            }
            finally
            {
                _syncingColor = false;
            }

            OnPropertyChanged(nameof(LightbarColor));
        }
    }

    /// <summary>
    /// Creates a new lights item for the given controller.
    /// </summary>
    /// <param name="controller">The controller item to display.</param>
    public LightsItem(ControllerItem controller)
    {
        Controller = controller;
        _device = controller.Device as DualSenseDevice;
    }

    /// <summary>
    /// Updates the preview-only color state (swatch, hex, and color) from a profile without
    /// writing anything to the controller. Used by the profile manager so the controller card
    /// reflects the profile the controller is currently using.
    /// </summary>
    /// <param name="red">Lightbar red channel (0-255).</param>
    /// <param name="green">Lightbar green channel (0-255).</param>
    /// <param name="blue">Lightbar blue channel (0-255).</param>
    public void SetPreview(byte red, byte green, byte blue)
    {
        if (_disposed)
        {
            return;
        }

        _suppressWrite = true;
        try
        {
            LedRed = red;
            LedGreen = green;
            LedBlue = blue;
        }
        finally
        {
            _suppressWrite = false;
        }

        OnPropertyChanged(nameof(LightbarBrush));
        OnPropertyChanged(nameof(ColorHex));
        OnPropertyChanged(nameof(LightbarColor));
    }

    /// <summary>
    /// Re-raises the derived color properties and sends the new state.
    /// </summary>
    partial void OnLedRedChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Re-raises the derived color properties and sends the new state.
    /// </summary>
    partial void OnLedGreenChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Re-raises the derived color properties and sends the new state.
    /// </summary>
    partial void OnLedBlueChanged(double value) => NotifyColorChanged();

    /// <summary>
    /// Sends the new state when the microphone LED mode changes.
    /// </summary>
    partial void OnMuteLedModeChanged(int value) => ApplyState();

    /// <summary>
    /// Sends the new state when a player LED is toggled.
    /// </summary>
    partial void OnPlayerLed1Changed(bool value) => ApplyState();

    /// <summary>
    /// Sends the new state when a player LED is toggled.
    /// </summary>
    partial void OnPlayerLed2Changed(bool value) => ApplyState();

    /// <summary>
    /// Sends the new state when a player LED is toggled.
    /// </summary>
    partial void OnPlayerLed3Changed(bool value) => ApplyState();

    /// <summary>
    /// Sends the new state when a player LED is toggled.
    /// </summary>
    partial void OnPlayerLed4Changed(bool value) => ApplyState();

    /// <summary>
    /// Sends the new state when a player LED is toggled.
    /// </summary>
    partial void OnPlayerLed5Changed(bool value) => ApplyState();

    /// <summary>
    /// Re-raises the derived color properties and sends the new state.
    /// </summary>
    private void NotifyColorChanged()
    {
        // Skip the LightbarColor raise when the change originates from its own setter
        // to avoid a feedback loop with the bound ColorView.
        if (!_syncingColor)
        {
            OnPropertyChanged(nameof(LightbarColor));
        }

        OnPropertyChanged(nameof(LightbarBrush));
        OnPropertyChanged(nameof(ColorHex));
        ApplyState();
    }

    /// <summary>
    /// Builds and sends the current light state to the controller. A no-op for
    /// non-DualSense devices, after disposal, or while a preview-only update is in progress.
    /// </summary>
    private void ApplyState()
    {
        if (_device is null || _disposed || _suppressWrite)
        {
            return;
        }

        PlayerLedMask playerLeds = PlayerLedMask.None;
        if (PlayerLed1)
        {
            playerLeds |= PlayerLedMask.Led1;
        }

        if (PlayerLed2)
        {
            playerLeds |= PlayerLedMask.Led2;
        }

        if (PlayerLed3)
        {
            playerLeds |= PlayerLedMask.Led3;
        }

        if (PlayerLed4)
        {
            playerLeds |= PlayerLedMask.Led4;
        }

        if (PlayerLed5)
        {
            playerLeds |= PlayerLedMask.Led5;
        }

        // The RGB bytes are gated by ValidFlag1.AllowLedColor, but taking over the
        // lightbar from the controller's default (BT-connect blue) additionally requires
        // ValidFlag2.AllowColorFadeAnim plus the lightbar-setup byte (payload offset 41)
        // written to 0x02 ("light out"). This mirrors the hid-playstation driver.
        // SetStateData.LightFadeAnimation targets that setup byte despite its name.
        SetStateData payload = new SetStateData
        {
            ValidFlag1 = ValidFlags.AllowMuteLight | ValidFlags.AllowLedColor | ValidFlags.AllowPlayerIndicators,
            ValidFlag2 = ValidFlags.AllowColorFadeAnim,
            MuteLedMode = (byte)MuteLedMode,
            LightFadeAnimation = 0x02,
            PlayerLeds = playerLeds,
            LedRed = Channel(LedRed),
            LedGreen = Channel(LedGreen),
            LedBlue = Channel(LedBlue)
        };

        _log.Debug($"Sending light state: RGB({Channel(LedRed)}, {Channel(LedGreen)}, {Channel(LedBlue)}), mic LED {MuteLedMode}, player LEDs {playerLeds}");

        try
        {
            _device.SendOutputState(payload);
        }
        catch (HidException ex)
        {
            _log.Error($"Failed to send light state: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a slider value to the 0-255 channel byte.
    /// </summary>
    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));

    /// <summary>
    /// Releases the item. Nothing to release; kept for parity with the page lifecycle.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}