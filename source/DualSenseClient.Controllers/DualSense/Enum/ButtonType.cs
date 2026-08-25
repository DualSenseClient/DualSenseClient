namespace DualSenseClient.Controllers.DualSense.Enum;

/// <summary>
/// All digital buttons on the DualSense controller.
/// </summary>
public enum ButtonType
{
    /// <summary>
    /// Cross (bottom face button).
    /// </summary>
    Cross,

    /// <summary>
    /// Circle (right face button).
    /// </summary>
    Circle,

    /// <summary>
    /// Square (left face button).
    /// </summary>
    Square,

    /// <summary>
    /// Triangle (top face button).
    /// </summary>
    Triangle,

    /// <summary>
    /// D-Pad up.
    /// </summary>
    DPadUp,

    /// <summary>
    /// D-Pad down.
    /// </summary>
    DPadDown,

    /// <summary>
    /// D-Pad left.
    /// </summary>
    DPadLeft,

    /// <summary>
    /// D-Pad right.
    /// </summary>
    DPadRight,

    /// <summary>
    /// Left bumper.
    /// </summary>
    L1,

    /// <summary>
    /// Right bumper.
    /// </summary>
    R1,

    /// <summary>
    /// Left trigger (analog, but digital press detected).
    /// </summary>
    L2,

    /// <summary>
    /// Right trigger (analog, but digital press detected).
    /// </summary>
    R2,

    /// <summary>
    /// Left stick click (L3).
    /// </summary>
    L3,

    /// <summary>
    /// Right stick click (R3).
    /// </summary>
    R3,

    /// <summary>
    /// Create button (left of touchpad).
    /// </summary>
    Create,

    /// <summary>
    /// Options button (right of touchpad).
    /// </summary>
    Options,

    /// <summary>
    /// PlayStation button (centre, PS logo).
    /// </summary>
    PS,

    /// <summary>
    /// Touchpad click (press down on touchpad surface).
    /// </summary>
    TouchPad,

    /// <summary>
    /// Mute button (below PS button).
    /// </summary>
    Mute,

    /// <summary>
    /// DualSense Edge FnL Button
    /// </summary>
    Edge_LeftFunction,

    /// <summary>
    /// DualSense Edge FnR Button
    /// </summary>
    Edge_RightFunction,

    /// <summary>
    /// DualSense Edge L4 Paddle
    /// </summary>
    Edge_LeftPaddle,

    /// <summary>
    /// DualSense Edge R4 Paddle
    /// </summary>
    Edge_RightPaddle
}