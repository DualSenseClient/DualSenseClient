using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.VIIPER.DualSense;
using DualSenseClient.VIIPER.DualShock4;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.Controllers.Emulation;

/// <summary>
/// Pure translation functions between the physical DualSense input report and the
/// input state formats of the virtual controllers. All functions are stateless so
/// the mappings can be unit-tested without hardware.
/// </summary>
public static class VirtualInputMapper
{
    /// <summary>
    /// Maps a physical stick/DPad byte (0-255, center 128) to the signed sbyte used
    /// by the virtual DualSense and DualShock 4 devices (center 0).
    /// </summary>
    public static sbyte DualSenseStick(byte raw) => (sbyte)(raw - 128);

    /// <summary>
    /// Maps a physical stick byte to the signed 16-bit value used by the virtual
    /// Xbox 360 device (full range, -32768..32767).
    /// </summary>
    public static short X360Axis(byte raw) => (short)Math.Clamp((raw - 128) * 256, short.MinValue, short.MaxValue);

    /// <summary>
    /// Maps a physical stick byte to the signed 16-bit value used by the virtual
    /// Xbox 360 device with the Y axis inverted (up is positive for XInput, while
    /// the physical DualSense reports up as 0).
    /// </summary>
    public static short X360AxisInverted(byte raw) => (short)Math.Clamp((128 - raw) * 256, short.MinValue, short.MaxValue);

    /// <summary>
    /// Converts the physical DualSense gyroscope counts (16.384 LSB/dps) to the
    /// fixed-point degrees-per-second scale (16 counts/dps) used by the virtual
    /// DualShock 4. Rounds to the nearest count.
    /// </summary>
    public static short GyroToDs4(short raw) => (short)((raw * 125 + (raw >= 0 ? 64 : -64)) / 128);

    /// <summary>
    /// Converts the physical DualSense accelerometer counts (8192 LSB/g) to the
    /// fixed-point metres-per-second-squared scale (512 counts/m/s²) used by the
    /// virtual DualShock 4. Rounds to the nearest count.
    /// </summary>
    public static short AccelToDs4(short raw) => (short)((raw * 981 + (raw >= 0 ? 800 : -800)) / 1600);

    /// <summary>
    /// Maps the physical DualSense buttons to the virtual DualSense button bitmask.
    /// Edge-only buttons (Fn and paddles) map to the DualSense's function/paddle
    /// flags when present on the physical device.
    /// </summary>
    public static DualSenseButtons ToDualSenseButtons(InputState input)
    {
        DualSenseButtons buttons = 0;
        if (input.Square) { buttons |= DualSenseButtons.Square; }
        if (input.Cross) { buttons |= DualSenseButtons.Cross; }
        if (input.Circle) { buttons |= DualSenseButtons.Circle; }
        if (input.Triangle) { buttons |= DualSenseButtons.Triangle; }
        if (input.L1) { buttons |= DualSenseButtons.L1; }
        if (input.R1) { buttons |= DualSenseButtons.R1; }
        if (input.L2Click) { buttons |= DualSenseButtons.L2; }
        if (input.R2Click) { buttons |= DualSenseButtons.R2; }
        if (input.Create) { buttons |= DualSenseButtons.Create; }
        if (input.Options) { buttons |= DualSenseButtons.Options; }
        if (input.L3) { buttons |= DualSenseButtons.L3; }
        if (input.R3) { buttons |= DualSenseButtons.R3; }
        if (input.PS) { buttons |= DualSenseButtons.PS; }
        if (input.TouchPad) { buttons |= DualSenseButtons.Touchpad; }
        if (input.Mute) { buttons |= DualSenseButtons.MicMute; }
        if (input.EdgeFunctionLeft) { buttons |= DualSenseButtons.LeftFunction; }
        if (input.EdgeFunctionRight) { buttons |= DualSenseButtons.RightFunction; }
        if (input.EdgePaddleLeft) { buttons |= DualSenseButtons.L4; }
        if (input.EdgePaddleRight) { buttons |= DualSenseButtons.R4; }
        return buttons;
    }

    /// <summary>
    /// Maps the physical DualSense buttons to the virtual DualShock 4 button bitmask.
    /// The physical Create button is reported as the DS4's Share button.
    /// </summary>
    public static DualShock4Buttons ToDualShock4Buttons(InputState input)
    {
        DualShock4Buttons buttons = 0;
        if (input.Square) { buttons |= DualShock4Buttons.Square; }
        if (input.Cross) { buttons |= DualShock4Buttons.Cross; }
        if (input.Circle) { buttons |= DualShock4Buttons.Circle; }
        if (input.Triangle) { buttons |= DualShock4Buttons.Triangle; }
        if (input.L1) { buttons |= DualShock4Buttons.L1; }
        if (input.R1) { buttons |= DualShock4Buttons.R1; }
        if (input.L2Click) { buttons |= DualShock4Buttons.L2; }
        if (input.R2Click) { buttons |= DualShock4Buttons.R2; }
        if (input.Create) { buttons |= DualShock4Buttons.Share; }
        if (input.Options) { buttons |= DualShock4Buttons.Options; }
        if (input.L3) { buttons |= DualShock4Buttons.L3; }
        if (input.R3) { buttons |= DualShock4Buttons.R3; }
        if (input.PS) { buttons |= DualShock4Buttons.PS; }
        if (input.TouchPad) { buttons |= DualShock4Buttons.Touchpad; }
        return buttons;
    }

    /// <summary>
    /// Maps the physical DualSense buttons to the virtual Xbox 360 button bitmask.
    /// The physical Create and Options buttons map to Back and Start.
    /// </summary>
    public static Xbox360Buttons ToXbox360Buttons(InputState input)
    {
        Xbox360Buttons buttons = 0;
        if (input.DPadUp) { buttons |= Xbox360Buttons.DPadUp; }
        if (input.DPadDown) { buttons |= Xbox360Buttons.DPadDown; }
        if (input.DPadLeft) { buttons |= Xbox360Buttons.DPadLeft; }
        if (input.DPadRight) { buttons |= Xbox360Buttons.DPadRight; }
        if (input.Square) { buttons |= Xbox360Buttons.X; }
        if (input.Cross) { buttons |= Xbox360Buttons.A; }
        if (input.Circle) { buttons |= Xbox360Buttons.B; }
        if (input.Triangle) { buttons |= Xbox360Buttons.Y; }
        if (input.L1) { buttons |= Xbox360Buttons.LeftShoulder; }
        if (input.R1) { buttons |= Xbox360Buttons.RightShoulder; }
        if (input.L3) { buttons |= Xbox360Buttons.LeftThumb; }
        if (input.R3) { buttons |= Xbox360Buttons.RightThumb; }
        if (input.Create) { buttons |= Xbox360Buttons.Back; }
        if (input.Options) { buttons |= Xbox360Buttons.Start; }
        if (input.PS) { buttons |= Xbox360Buttons.Guide; }
        return buttons;
    }

    /// <summary>
    /// Converts the physical D-pad to the virtual DualSense D-pad direction bitmask.
    /// </summary>
    public static DualSenseDPad ToDualSenseDPad(InputState input)
    {
        DualSenseDPad dpad = 0;
        if (input.DPadUp) { dpad |= DualSenseDPad.Up; }
        if (input.DPadDown) { dpad |= DualSenseDPad.Down; }
        if (input.DPadLeft) { dpad |= DualSenseDPad.Left; }
        if (input.DPadRight) { dpad |= DualSenseDPad.Right; }
        return dpad;
    }

    /// <summary>
    /// Converts the physical D-pad to the virtual DualShock 4 D-pad value.
    /// The Go device interprets this byte as a direction bitmask (Up=1, Down=2,
    /// Left=4, Right=8) even though the C# enum defines hat values, so the bitmask
    /// constants must be used here for the wire protocol to make sense.
    /// </summary>
    public static byte ToDualShock4DPad(InputState input)
    {
        byte dpad = 0;
        if (input.DPadUp) { dpad |= 0x01; }
        if (input.DPadDown) { dpad |= 0x02; }
        if (input.DPadLeft) { dpad |= 0x04; }
        if (input.DPadRight) { dpad |= 0x08; }
        return dpad;
    }
}