using DualSenseClient.Controllers.DualSense.Input;
using DualSenseClient.Controllers.Emulation;
using DualSenseClient.VIIPER.DualSense;
using DualSenseClient.VIIPER.DualShock4;
using DualSenseClient.VIIPER.Xbox360;

namespace DualSenseClient.Tests.Controllers.Emulation;

[TestFixture]
public sealed class VirtualInputMapperTests
{
    private static InputState State(byte stickX = 128, byte stickY = 128, byte l2 = 0, byte r2 = 0, byte byte7 = 0x08, byte byte8 = 0, byte byte9 = 0)
        => new InputState(new byte[] { stickX, stickY, 128, 128, l2, r2, 0, byte7, byte8, byte9 }, 0);

    [Test]
    public void DualSenseStick_Center_MapsToZero()
    {
        Assert.That(VirtualInputMapper.DualSenseStick(128), Is.EqualTo((sbyte)0));
    }

    [Test]
    public void DualSenseStick_FullLeft_MapsToMinus128()
    {
        Assert.That(VirtualInputMapper.DualSenseStick(0), Is.EqualTo((sbyte)-128));
    }

    [Test]
    public void DualSenseStick_FullRight_MapsTo127()
    {
        Assert.That(VirtualInputMapper.DualSenseStick(255), Is.EqualTo((sbyte)127));
    }

    [Test]
    public void X360Axis_FullLeft_MapsToShortMinValue()
    {
        Assert.That(VirtualInputMapper.X360Axis(0), Is.EqualTo(short.MinValue));
    }

    [Test]
    public void X360Axis_Center_MapsToZero()
    {
        Assert.That(VirtualInputMapper.X360Axis(128), Is.EqualTo((short)0));
    }

    [Test]
    public void X360AxisInverted_FullUp_ClampsFromOverflowToMaxValue()
    {
        Assert.That(VirtualInputMapper.X360AxisInverted(0), Is.EqualTo(short.MaxValue));
    }

    [Test]
    public void X360AxisInverted_FullDown_MapsToNegativeRange()
    {
        Assert.That(VirtualInputMapper.X360AxisInverted(255), Is.EqualTo((short)-32512));
    }

    [Test]
    public void X360Buttons_PhysicalFaceButtons_MapToOneToOne()
    {
        // byte7: DPad neutral (0x08) | Square (0x10) | Cross (0x20) | Circle (0x40) | Triangle (0x80)
        InputState input = State(byte7: 0x08 | 0x10 | 0x20 | 0x40 | 0x80);
        Xbox360Buttons buttons = VirtualInputMapper.ToXbox360Buttons(input);
        Assert.That(buttons, Is.EqualTo(Xbox360Buttons.X | Xbox360Buttons.A | Xbox360Buttons.B | Xbox360Buttons.Y));
    }

    [Test]
    public void X360Buttons_ShouldersSticksAndSystemButtons_MapToXInput()
    {
        // byte8: L1(0x01) R1(0x02) L2Click(0x04) R2Click(0x08) Create(0x10) Options(0x20) L3(0x40) R3(0x80)
        InputState input = State(byte8: 0xFF, byte9: 0x01); // byte9: PS (0x01)
        Xbox360Buttons buttons = VirtualInputMapper.ToXbox360Buttons(input);
        Assert.That(buttons, Is.EqualTo(Xbox360Buttons.LeftShoulder | Xbox360Buttons.RightShoulder
                                                                    | Xbox360Buttons.LeftThumb | Xbox360Buttons.RightThumb
                                                                    | Xbox360Buttons.Back | Xbox360Buttons.Start | Xbox360Buttons.Guide));
    }

    [Test]
    public void X360Buttons_DPad_MapsToButtons()
    {
        // byte7: DPad up-left (0x07)
        InputState input = State(byte7: 0x07);
        Xbox360Buttons buttons = VirtualInputMapper.ToXbox360Buttons(input);
        Assert.That(buttons, Is.EqualTo(Xbox360Buttons.DPadUp | Xbox360Buttons.DPadLeft));
    }

    [Test]
    public void X360Triggers_PassThrough()
    {
        InputState input = State(l2: 200, r2: 100);
        Assert.That(input.L2, Is.EqualTo(200));
        Assert.That(input.R2, Is.EqualTo(100));
    }

    [Test]
    public void DualShock4Buttons_CreateMapsToShare()
    {
        InputState input = State(byte8: 0x10); // Create
        Assert.That(VirtualInputMapper.ToDualShock4Buttons(input), Is.EqualTo(DualShock4Buttons.Share));
    }

    [Test]
    public void DualShock4Buttons_TouchpadAndPs_MapToTheirBits()
    {
        InputState input = State(byte9: 0x01 | 0x02); // PS + TouchPad
        Assert.That(VirtualInputMapper.ToDualShock4Buttons(input), Is.EqualTo(DualShock4Buttons.PS | DualShock4Buttons.Touchpad));
    }

    [Test]
    public void DualShock4Buttons_TriggerClicks_MapToTriggerBits()
    {
        InputState input = State(byte8: 0x04 | 0x08); // L2Click + R2Click
        Assert.That(VirtualInputMapper.ToDualShock4Buttons(input),
            Is.EqualTo(DualShock4Buttons.L2 | DualShock4Buttons.R2));
    }

    [Test]
    public void DualSenseButtons_EdgeControls_MapToFunctionAndPaddles()
    {
        // byte9: Edge FnL(0x10) FnR(0x20) paddle L(0x40) paddle R(0x80)
        InputState input = State(byte9: 0x10 | 0x20 | 0x40 | 0x80);
        DualSenseButtons buttons = VirtualInputMapper.ToDualSenseButtons(input);
        Assert.That(buttons, Is.EqualTo(DualSenseButtons.LeftFunction | DualSenseButtons.RightFunction
                                                                      | DualSenseButtons.L4 | DualSenseButtons.R4));
    }

    [Test]
    public void DualSenseButtons_MuteMapsToMicMute()
    {
        InputState input = State(byte9: 0x04); // Mute
        Assert.That(VirtualInputMapper.ToDualSenseButtons(input), Is.EqualTo(DualSenseButtons.MicMute));
    }

    [Test]
    public void DualSenseButtons_TouchpadAndPs_MapToTheirBits()
    {
        InputState input = State(byte9: 0x01 | 0x02); // PS + TouchPad
        Assert.That(VirtualInputMapper.ToDualSenseButtons(input),
            Is.EqualTo(DualSenseButtons.PS | DualSenseButtons.Touchpad));
    }

    [Test]
    public void DualSenseDPad_UpRight_ProducesUpAndRightFlags()
    {
        InputState input = State(byte7: 0x01); // DPad up-right
        Assert.That(VirtualInputMapper.ToDualSenseDPad(input),
            Is.EqualTo(DualSenseDPad.Up | DualSenseDPad.Right));
    }

    [Test]
    public void DualSenseDPad_Neutral_ProducesNoFlags()
    {
        InputState input = State(byte7: 0x08); // DPad neutral
        Assert.That(VirtualInputMapper.ToDualSenseDPad(input), Is.EqualTo((DualSenseDPad)0));
    }

    [Test]
    public void DualShock4DPad_DownLeft_ProducesBitmask()
    {
        InputState input = State(byte7: 0x05); // DPad down-left
        Assert.That(VirtualInputMapper.ToDualShock4DPad(input), Is.EqualTo(0x02 | 0x04));
    }

    [TestCase(0, 0)]
    [TestCase(16384, 16000)]
    [TestCase(-16384, -16000)]
    public void GyroToDs4_ScalesDpsToFixedPoint(short raw, short expected)
    {
        Assert.That(VirtualInputMapper.GyroToDs4(raw), Is.EqualTo(expected));
    }

    [TestCase(0, 0)]
    [TestCase(8192, 5023)]
    [TestCase(-8192, -5023)]
    public void AccelToDs4_ScalesGravityToMs2FixedPoint(short raw, short expected)
    {
        Assert.That(VirtualInputMapper.AccelToDs4(raw), Is.EqualTo(expected));
    }
}