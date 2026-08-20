using System.ComponentModel;

namespace DualSenseClient.GUI.Models.Items;

/// <summary>
/// Live controller state consumed by the reusable controller visualization
/// (<see cref="DualSenseClient.GUI.Controls.DualSenseControllerView"/>). Implemented by
/// <see cref="InputMonitorItem"/> and kept separate so any future overlay window can drive
/// the same visualization from its own state source.
/// </summary>
/// <remarks>
/// Values are read from the last <see cref="INotifyPropertyChanged.PropertyChanged"/>
/// notification; implementations must notify on the UI thread (or the consumer must marshal).
/// </remarks>
public interface IControllerMonitorState : INotifyPropertyChanged
{
    /// <summary>
    /// Whether the Cross (X) face button is currently pressed.
    /// </summary>
    bool Cross { get; }

    /// <summary>
    /// Whether the Circle (O) face button is currently pressed.
    /// </summary>
    bool Circle { get; }

    /// <summary>
    /// Whether the Square ([]) face button is currently pressed.
    /// </summary>
    bool Square { get; }

    /// <summary>
    /// Whether the Triangle (^) face button is currently pressed.
    /// </summary>
    bool Triangle { get; }

    /// <summary>
    /// Whether the D-pad up direction is currently pressed.
    /// </summary>
    bool DPadUp { get; }

    /// <summary>
    /// Whether the D-pad down direction is currently pressed.
    /// </summary>
    bool DPadDown { get; }

    /// <summary>
    /// Whether the D-pad left direction is currently pressed.
    /// </summary>
    bool DPadLeft { get; }

    /// <summary>
    /// Whether the D-pad right direction is currently pressed.
    /// </summary>
    bool DPadRight { get; }

    /// <summary>
    /// Whether the left shoulder button is currently pressed.
    /// </summary>
    bool L1 { get; }

    /// <summary>
    /// Whether the right shoulder button is currently pressed.
    /// </summary>
    bool R1 { get; }

    /// <summary>
    /// Whether the left trigger click is currently pressed.
    /// </summary>
    bool L2Click { get; }

    /// <summary>
    /// Whether the right trigger click is currently pressed.
    /// </summary>
    bool R2Click { get; }

    /// <summary>
    /// Whether the left stick is currently pressed down (L3).
    /// </summary>
    bool L3 { get; }

    /// <summary>
    /// Whether the right stick is currently pressed down (R3).
    /// </summary>
    bool R3 { get; }

    /// <summary>
    /// Whether the Create button is currently pressed.
    /// </summary>
    bool Create { get; }

    /// <summary>
    /// Whether the Options button is currently pressed.
    /// </summary>
    bool Options { get; }

    /// <summary>
    /// Whether the PlayStation button is currently pressed.
    /// </summary>
    bool PS { get; }

    /// <summary>
    /// Whether the touchpad click is currently pressed.
    /// </summary>
    bool TouchPad { get; }

    /// <summary>
    /// Whether the mute button is currently pressed.
    /// </summary>
    bool Mute { get; }

    /// <summary>
    /// Left stick horizontal position (0-255, center is 128).
    /// </summary>
    int LeftStickX { get; }

    /// <summary>
    /// Left stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    int LeftStickY { get; }

    /// <summary>
    /// Right stick horizontal position (0-255, center is 128).
    /// </summary>
    int RightStickX { get; }

    /// <summary>
    /// Right stick vertical position (0-255, center is 128, 0 is up).
    /// </summary>
    int RightStickY { get; }

    /// <summary>
    /// Left analog trigger value (0-255, released to fully pressed).
    /// </summary>
    int L2 { get; }

    /// <summary>
    /// Right analog trigger value (0-255, released to fully pressed).
    /// </summary>
    int R2 { get; }

    /// <summary>
    /// Whether a finger is currently detected at touch point 1.
    /// </summary>
    bool Touch1Active { get; }

    /// <summary>
    /// Touch point 1 horizontal position (0-1919).
    /// </summary>
    int Touch1X { get; }

    /// <summary>
    /// Touch point 1 vertical position (0-1079).
    /// </summary>
    int Touch1Y { get; }

    /// <summary>
    /// Whether a finger is currently detected at touch point 2.
    /// </summary>
    bool Touch2Active { get; }

    /// <summary>
    /// Touch point 2 horizontal position (0-1919).
    /// </summary>
    int Touch2X { get; }

    /// <summary>
    /// Touch point 2 vertical position (0-1079).
    /// </summary>
    int Touch2Y { get; }

    /// <summary>
    /// Current lightbar red channel (0-255).
    /// </summary>
    int LightbarRed { get; }

    /// <summary>
    /// Current lightbar green channel (0-255).
    /// </summary>
    int LightbarGreen { get; }

    /// <summary>
    /// Current lightbar blue channel (0-255).
    /// </summary>
    int LightbarBlue { get; }

    /// <summary>
    /// Current player LED layout as a bitmask: bit 0 = LED 1 (leftmost) through bit 4 = LED 5.
    /// </summary>
    int PlayerLeds { get; }

    /// <summary>
    /// Current mute LED mode: 0 = off, 1 = on, 2 = pulse.
    /// </summary>
    int MuteLedMode { get; }
}