using DualSenseClient.Controllers.DualSense.Input;

namespace DualSenseClient.Tests.Controllers.DualSense.Input;

public static class InputReportTestData
{
    public static readonly byte[] UsbReport =
    [
        0x01, // Report ID
        // Payload (offset 1, bytes 0-62)
        0, 255, 128, 128, // 0-3: LeftStickX=0, LeftStickY=255, RightStickX=128, RightStickY=128
        0, 255, // 4-5: L2=0, R2=255
        42, // 6: SequenceNumber
        0xF2, // 7: D-Pad Right + all face buttons
        0xFF, // 8: all shoulder + system buttons
        0xF7, // 9: PS + TouchPad + Mute + all Edge buttons
        0, 0, 0, 0, 0, // 10-14: unused
        0xE8, 0x03, // 15-16: GyroX = 1000
        0x18, 0xFC, // 17-18: GyroY = -1000
        0, 0, // 19-20: GyroZ = 0
        0x00, 0x20, // 21-22: AccelX = 8192
        0, 0, // 23-24: AccelY = 0
        0x00, 0xE0, // 25-26: AccelZ = -8192
        0x39, 0x30, 0, 0, // 27-30: Timestamp = 12345
        35, // 31: Temperature
        0x05, 0x64, 0x80, 0x0C, // 32-35: Touch1 (id=5, active, X=100, Y=200)
        0x8A, 0, 0, 0, // 36-39: Touch2 (id=10, inactive)
        0, // 40: unused
        0x01, // 41: R2Status
        0x02, // 42: L2Status
        0x9F, 0x86, 0x01, 0x00, // 43-46: HostTimestamp = 99999
        0x03, // 47: Status2
        0x32, 0x09, 0x01, 0x00, // 48-51: DeviceTimestamp = 67890
        0x18, // 52: Battery (level 8, charging)
        0x0B, // 53: Headphone + Mic + UsbData
        0, //54 carries further status bits (status2);
        0, 0, 0, 0, 0, 0, 0, 0 // 55-62 hold an AES-CMAC field (aesCmac: 55) on firmware that uses it.
    ];

    public static InputReport CreateReport() => new InputReport(UsbReport, 1);

    public static InputReport CreateReport(byte[] customBuffer) => new InputReport(customBuffer, 1);
}