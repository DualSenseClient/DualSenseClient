using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public class InputStateTests
{
    [Test]
    public void InputState_Sticks_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.LeftStickX, Is.EqualTo(0));
        Assert.That(report.Input.LeftStickY, Is.EqualTo(255));
        Assert.That(report.Input.RightStickX, Is.EqualTo(128));
        Assert.That(report.Input.RightStickY, Is.EqualTo(128));
    }

    [Test]
    public void InputState_Triggers_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.L2, Is.EqualTo(0));
        Assert.That(report.Input.R2, Is.EqualTo(255));
    }

    [Test]
    public void InputState_SequenceNumber_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.SequenceNumber, Is.EqualTo(42));
    }

    [Test]
    public void InputState_DPadRight_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.DPadRight, Is.True);
        Assert.That(report.Input.DPadUp, Is.False);
        Assert.That(report.Input.DPadDown, Is.False);
        Assert.That(report.Input.DPadLeft, Is.False);
        Assert.That(report.Input.DPadNeutral, Is.False);
    }

    [Test]
    public void InputState_DPadNeutral_WhenValueIs8()
    {
        byte[] buffer = (byte[])InputReportTestData.UsbReport.Clone();
        buffer[8] = 0x08;

        InputReport report = InputReportTestData.CreateReport(buffer);

        Assert.That(report.Input.DPadNeutral, Is.True);
        Assert.That(report.Input.DPadUp, Is.False);
        Assert.That(report.Input.DPadDown, Is.False);
        Assert.That(report.Input.DPadLeft, Is.False);
        Assert.That(report.Input.DPadRight, Is.False);
    }

    [Test]
    public void InputState_FaceButtons_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.Square, Is.True);
        Assert.That(report.Input.Cross, Is.True);
        Assert.That(report.Input.Circle, Is.True);
        Assert.That(report.Input.Triangle, Is.True);
    }

    [Test]
    public void InputState_ShoulderButtons_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.L1, Is.True);
        Assert.That(report.Input.R1, Is.True);
        Assert.That(report.Input.L2Click, Is.True);
        Assert.That(report.Input.R2Click, Is.True);
        Assert.That(report.Input.Create, Is.True);
        Assert.That(report.Input.Options, Is.True);
        Assert.That(report.Input.L3, Is.True);
        Assert.That(report.Input.R3, Is.True);
    }

    [Test]
    public void InputState_SystemAndEdgeButtons_ParsesCorrectly()
    {
        InputReport report = InputReportTestData.CreateReport();

        Assert.That(report.Input.PS, Is.True);
        Assert.That(report.Input.TouchPad, Is.True);
        Assert.That(report.Input.Mute, Is.True);
        Assert.That(report.Input.EdgeFunctionLeft, Is.True);
        Assert.That(report.Input.EdgeFunctionRight, Is.True);
        Assert.That(report.Input.EdgePaddleLeft, Is.True);
        Assert.That(report.Input.EdgePaddleRight, Is.True);
    }

    [Test]
    public void InputState_AllButtons_WhenNonePressed_AllFalse()
    {
        byte[] buffer = new byte[64];
        buffer[0] = 0x01;
        buffer[8] = 0x08; // D-Pad neutral only

        InputReport report = InputReportTestData.CreateReport(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(report.Input.DPadNeutral, Is.True);
            Assert.That(report.Input.Square, Is.False);
            Assert.That(report.Input.Cross, Is.False);
            Assert.That(report.Input.Circle, Is.False);
            Assert.That(report.Input.Triangle, Is.False);
            Assert.That(report.Input.L1, Is.False);
            Assert.That(report.Input.R1, Is.False);
            Assert.That(report.Input.L2Click, Is.False);
            Assert.That(report.Input.R2Click, Is.False);
            Assert.That(report.Input.Create, Is.False);
            Assert.That(report.Input.Options, Is.False);
            Assert.That(report.Input.L3, Is.False);
            Assert.That(report.Input.R3, Is.False);
            Assert.That(report.Input.PS, Is.False);
            Assert.That(report.Input.TouchPad, Is.False);
            Assert.That(report.Input.Mute, Is.False);
            Assert.That(report.Input.EdgeFunctionLeft, Is.False);
            Assert.That(report.Input.EdgeFunctionRight, Is.False);
            Assert.That(report.Input.EdgePaddleLeft, Is.False);
            Assert.That(report.Input.EdgePaddleRight, Is.False);
        });
    }

    [Test]
    public void InputState_BluetoothOffset_ParsesCorrectly()
    {
        byte[] btReport = new byte[65];
        btReport[0] = 0x31;
        btReport[1] = 0x01;
        btReport[2] = 0; // payload byte 0 = LeftStickX
        btReport[10] = 0x01; // payload byte 8 = L1 only
        btReport[11] = 0x01; // payload byte 9 = PS button only

        InputReport report = new(btReport, 2);

        Assert.That(report.Input.LeftStickX, Is.EqualTo(0));
        Assert.That(report.Input.L1, Is.True);
        Assert.That(report.Input.R1, Is.False);
        Assert.That(report.Input.PS, Is.True);
        Assert.That(report.Input.TouchPad, Is.False);
    }
}