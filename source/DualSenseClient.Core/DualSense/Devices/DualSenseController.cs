using System.ComponentModel;
using DualSenseClient.Core.Bluetooth;
using DualSenseClient.Core.DualSense.Actions;
using DualSenseClient.Core.DualSense.Enums;
using DualSenseClient.Core.DualSense.Events;
using DualSenseClient.Core.DualSense.Models;
using DualSenseClient.Core.DualSense.Reports;
using DualSenseClient.Core.DualSense.Services;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Utils;
using HidSharp;

namespace DualSenseClient.Core.DualSense.Devices;

/// <summary>
/// Represents a connected DualSense controller and manages input/output operations
/// </summary>
public class DualSenseController : IDisposable
{
    // Fields
    private readonly HidStream _stream;
    private readonly CancellationTokenSource _cts;
    private readonly Task _readTask;
    private readonly Lock _writeLock = new Lock();

    // Previous states for change detection
    private InputState _previousInputState = new InputState();
    private BatteryState _previousBatteryState = new BatteryState();
    private ConnectionStatus _previousConnectionStatus = new ConnectionStatus();
    private TouchpadState _previousTouchpadState = new TouchpadState();
    private MotionState _previousMotionState = new MotionState();

    // Properties
    public HidDevice Device { get; }
    public ConnectionType ConnectionType { get; }
    public bool IsConnected { get; private set; } = true;
    public string? MacAddress { get; }

    // Current Controller State
    public BatteryState Battery { get; private set; } = new BatteryState();
    public ConnectionStatus ConnectionStatus { get; private set; } = new ConnectionStatus();
    public LightbarColor CurrentLightbarColor { get; private set; } = new LightbarColor(0, 0, 255);
    public LightbarBehavior CurrentLightbarBehavior { get; private set; } = LightbarBehavior.Custom;
    public PlayerLed CurrentPlayerLeds { get; private set; }
    public PlayerLedBrightness CurrentPlayerLedBrightness { get; private set; }
    public MicLed CurrentMicLed { get; private set; }
    public InputState Input { get; private set; } = new InputState();
    public TouchpadState Touchpad { get; private set; } = new TouchpadState();
    public MotionState Motion { get; private set; } = new MotionState();
    public byte LargeMotorValue { get; private set; } = 0;
    public byte SmallMotorValue { get; private set; } = 0;
    public uint? RawFirmwareVersion { get; private set; }
    public uint? RawHardwareVersion { get; private set; }
    public FirmwareVersion? ParsedFirmwareVersion { get; private set; }
    public ControllerEmulationService? ControllerEmulationService { get; private set; }
    public SpecialActionService? SpecialActionService { get; }

    // Events
    // Input
    public event EventHandler<InputStateEventArgs>? InputChanged;
    public event EventHandler<BatteryStateEventArgs>? BatteryChanged;
    public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;
    public event EventHandler<TouchpadEventArgs>? TouchpadChanged;
    public event EventHandler<MotionEventArgs>? MotionChanged;
    public event EventHandler<ButtonEventArgs>? ButtonPressed;
    public event EventHandler<ButtonEventArgs>? ButtonReleased;
    public event EventHandler<TriggerEventArgs>? TriggerChanged;
    public event EventHandler<StickEventArgs>? StickMoved;

    // Output
    public event EventHandler<LightbarChangedEventArgs>? LightbarChanged;
    public event EventHandler<PlayerLedsChangedEventArgs>? PlayerLedsChanged;
    public event EventHandler<MicLedChangedEventArgs>? MicLedChanged;

    public event EventHandler? Disconnected;

    // Constructor
    public DualSenseController(HidDevice device, HidStream stream, SpecialActionService? specialActionService = null)
    {
        Logger.Debug<DualSenseController>($"Initializing DualSense controller: {device.GetProductName()}");

        Device = device;
        _stream = stream;
        SpecialActionService = specialActionService;
        ConnectionType = device.GetMaxOutputReportLength() > 64 ? ConnectionType.Bluetooth : ConnectionType.USB;

        Logger.Info<DualSenseController>($"Controller mode detected: {ConnectionType}");
        Logger.Debug<DualSenseController>($"Max output report length: {device.GetMaxOutputReportLength()}");

        // Try to extract MAC address for both USB and Bluetooth
        MacAddress = BluetoothHelper.ExtractMacAddress(device);

        if (MacAddress != null)
        {
            Logger.Info<DualSenseController>($"Hardware MAC address: {MacAddress}");
        }
        else
        {
            Logger.Warning<DualSenseController>("Failed to extract MAC address from device");

            // For USB, try to get it from the controller itself via feature report
            MacAddress = TryGetMacAddressFromController();

            if (MacAddress != null)
            {
                Logger.Info<DualSenseController>($"MAC address from controller: {MacAddress}");
            }
        }

        // Initialize Controller Emulation Service
        try
        {
            ControllerEmulationService = new ControllerEmulationService();
            ControllerEmulationService.DualSenseController = this;
        }
        catch (Exception ex)
        {
            Logger.Warning<DualSenseController>($"Could not initialize ControllerEmulationService: {ex.Message}");
            ControllerEmulationService = null;
        }

        // Retrieve firmware and hardware version information
        (uint? firmwareVersion, uint? hardwareVersion)? versionInfo = GetHardwareAndFirmwareVersion();
        if (versionInfo.HasValue)
        {
            RawHardwareVersion = versionInfo.Value.hardwareVersion;
            RawFirmwareVersion = versionInfo.Value.firmwareVersion;

            // Parse the firmware version into human-readable format
            if (versionInfo.Value.firmwareVersion.HasValue)
            {
                ParsedFirmwareVersion = FirmwareVersion.FromRawData(versionInfo.Value.firmwareVersion.Value, versionInfo.Value.hardwareVersion ?? 0);
            }
        }

        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(_cts.Token));

        Logger.Info<DualSenseController>("DualSense controller initialized successfully");
    }

    // Functions
    /// <summary>
    /// Attempts to retrieve MAC address directly from the controller via feature report (In case BluetoothHelper fails)
    /// </summary>
    private string? TryGetMacAddressFromController()
    {
        try
        {
            // DualSense stores MAC address in feature report 0x09
            byte[] report = new byte[20];
            report[0] = 0x09;

            _stream.GetFeature(report);

            // MAC address is at bytes 1-6
            if (report.Length >= 7)
            {
                string mac = $"{report[6]:X2}:{report[5]:X2}:{report[4]:X2}:{report[3]:X2}:{report[2]:X2}:{report[1]:X2}";
                Logger.Debug<DualSenseController>($"Extracted MAC from feature report: {mac}");
                return mac;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug<DualSenseController>($"Could not retrieve MAC from feature report: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Retrieves the hardware and firmware version information from the controller via feature report
    /// </summary>
    private (uint? firmwareVersion, uint? hardwareVersion)? GetHardwareAndFirmwareVersion()
    {
        try
        {
            // DualSense stores firmware version in feature report 0x20
            byte[] report = new byte[64]; // Report size is 63 bytes + 1 for report ID
            report[0] = 0x20;

            _stream.GetFeature(report);
            
            // Hardware version is at bytes 24-27 (32-bit little-endian value)
            // Firmware version is at bytes 28-31 (32-bit little-endian value)
            if (report.Length >= 32) // Make sure we have enough bytes
            {
                uint hardwareVersion = BitConverter.ToUInt32(report, 24);
                uint firmwareVersion = BitConverter.ToUInt32(report, 28);

                Logger.Debug<DualSenseController>($"Hardware version: 0x{hardwareVersion:X8}, Firmware version: 0x{firmwareVersion:X8}");
                return (firmwareVersion, hardwareVersion);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug<DualSenseController>($"Could not retrieve hardware and firmware versions from feature report: {ex.Message}");
        }

        return null;
    }

    private async Task ReadLoop(CancellationToken ct)
    {
        Logger.Debug<DualSenseController>("Read loop started");
        byte[] buffer = new byte[128];
        int reportCount = 0;

        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                int read = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);

                if (read <= 0)
                {
                    Logger.Warning<DualSenseController>($"Read returned {read} bytes, disconnecting");
                    HandleDisconnection();
                    break;
                }

                reportCount++;
                Logger.Trace<DualSenseController>($"Report #{reportCount}: Read {read} bytes, ID: 0x{buffer[0]:X2}");

                ProcessInputReport(buffer, read);
            }
            catch (OperationCanceledException)
            {
                Logger.Debug<DualSenseController>("Read loop cancelled");
                break;
            }
            catch (IOException ioEx) when (IsDisconnectionException(ioEx))
            {
                // Expected disconnection - log at Info level without full details
                Logger.Info<DualSenseController>($"Controller disconnected: {ioEx.Message}");
                HandleDisconnection();
                break;
            }
            catch (Exception ex)
            {
                // Unexpected exception - log full details
                Logger.Error<DualSenseController>("Unexpected error in read loop");
                Logger.LogExceptionDetails<DualSenseController>(ex, includeEnvironmentInfo: false);
                HandleDisconnection();
                break;
            }
        }

        Logger.Debug<DualSenseController>("Read loop ended");
    }

    /// <summary>
    /// Determines if an exception is a normal disconnection event
    /// </summary>
    private static bool IsDisconnectionException(IOException ex)
    {
        // Check for common disconnection error messages
        string message = ex.Message.ToLowerInvariant();

        // HidSharp timeout/disconnection messages
        if (message.Contains("operation failed after some time") || message.Contains("device is not connected") || message.Contains("device not found") || message.Contains("no such device") || message.Contains("device removed"))
        {
            return true;
        }

        // Check inner exception for Win32 disconnection errors
        if (ex.InnerException is not Win32Exception win32Ex)
        {
            return false;
        }

        // ERROR_DEVICE_NOT_CONNECTED = 1167 (0x48F)
        // ERROR_NOT_READY = 21 (0x15)
        // ERROR_GEN_FAILURE = 31 (0x1F)
        return win32Ex.NativeErrorCode is 1167 or 21 or 31;
    }

    private void ProcessInputReport(byte[] data, int length)
    {
        byte reportId = data[0];

        if (ConnectionType == ConnectionType.Bluetooth)
        {
            switch (reportId)
            {
                case 0x31:
                    if (length < 10)
                    {
                        Logger.Warning<DualSenseController>($"Bluetooth report too short: {length} bytes (minimum 10)");
                        return;
                    }
                    byte[] stripped31 = new byte[length - 2];
                    Array.Copy(data, 2, stripped31, 0, length - 2);
                    Logger.Trace<DualSenseController>($"Stripped BT 0x31 headers: {length} -> {stripped31.Length} bytes");
                    ParseInputData(stripped31);
                    break;
                case 0x01:
                    // DualSense is in "simple" Bluetooth state (0x01)
                    // Sending out an "Output Report" resets it to the "normal" Bluetooth state (0x31)
                    Logger.Warning<DualSenseController>("Controller is in simple Bluetooth state");
                    SendOutputReport();
                    break;
                default:
                    Logger.Warning<DualSenseController>($"Unknown Bluetooth report ID: 0x{reportId:X2}");
                    break;
            }
        }
        else
        {
            // USB mode (only 0x01 expected)
            if (reportId != 0x01)
            {
                Logger.Warning<DualSenseController>($"Invalid USB report ID: 0x{reportId:X2} (expected 0x01)");
                return;
            }

            byte[] strippedUsb = new byte[length - 1];
            Array.Copy(data, 1, strippedUsb, 0, length - 1);
            ParseInputData(strippedUsb);
        }
    }

    private void ParseInputData(byte[] data)
    {
        if (data.Length < 63)
        {
            Logger.Warning<DualSenseController>($"Data too short for parsing: {data.Length} bytes (expected 63)");
            return;
        }

        Logger.Trace<DualSenseController>("Parsing input data");

        // Store current state for comparison
        InputState oldInput = CopyInputState(Input);

        // Analog sticks
        Input.LeftStickX = data[0];
        Input.LeftStickY = data[1];
        Input.RightStickX = data[2];
        Input.RightStickY = data[3];

        // Check for stick movement
        CheckStickMovement(oldInput);

        // Triggers
        Input.L2 = data[4];
        Input.R2 = data[5];

        // Check for trigger changes
        CheckTriggerChanges(oldInput);

        // Buttons - Byte 7
        byte btnBlock1 = data[7];
        ParseDPadAndFaceButtons(btnBlock1, oldInput);

        // Buttons - Byte 8
        byte btnBlock2 = data[8];
        ParseShoulderButtons(btnBlock2, oldInput);

        // Buttons - Byte 9
        byte btnBlock3 = data[9];
        ParseSystemButtons(btnBlock3, oldInput);

        // Gyro and Accelerometer
        if (data.Length > 26)
        {
            MotionState oldMotion = CopyMotionState(Motion);

            Motion.GyroX = BitConverter.ToInt16(data, 15);
            Motion.GyroZ = BitConverter.ToInt16(data, 17);
            Motion.GyroY = BitConverter.ToInt16(data, 19);
            Motion.AccelX = BitConverter.ToInt16(data, 21);
            Motion.AccelY = BitConverter.ToInt16(data, 23);
            Motion.AccelZ = BitConverter.ToInt16(data, 25);

            CheckMotionChanges(oldMotion);
        }

        // Touchpad
        if (data.Length > 40)
        {
            TouchpadState oldTouchpad = CopyTouchpadState(Touchpad);

            TouchPoint touch1 = ParseTouchData(data, 32);
            TouchPoint touch2 = ParseTouchData(data, 36);

            // Update individual properties instead of replacing the entire object
            Touchpad.Touch1.Index = touch1.Index;
            Touchpad.Touch1.IsActive = touch1.IsActive;
            Touchpad.Touch1.X = touch1.X;
            Touchpad.Touch1.Y = touch1.Y;

            Touchpad.Touch2.Index = touch2.Index;
            Touchpad.Touch2.IsActive = touch2.IsActive;
            Touchpad.Touch2.X = touch2.X;
            Touchpad.Touch2.Y = touch2.Y;

            CheckTouchpadChanges(oldTouchpad);
        }

        // Battery
        if (data.Length > 52)
        {
            ParseBatteryInfo(data[52]);
        }

        // Connection status
        if (data.Length > 53)
        {
            ParseConnectionStatus(data[53]);
        }

        // Fire general input changed event if any input changed
        if (HasInputChanged(oldInput))
        {
            InputChanged?.Invoke(this, new InputStateEventArgs(Input, oldInput));
        }
    }

    private void ParseDPadAndFaceButtons(byte btnBlock1, InputState oldInput)
    {
        // DPad (lower 4 bits)
        byte dpadValue = (byte)(btnBlock1 & 0x0F);
        bool oldUp = Input.DPadUp, oldRight = Input.DPadRight, oldDown = Input.DPadDown, oldLeft = Input.DPadLeft;

        Input.DPadUp = dpadValue == 0x00;
        Input.DPadRight = dpadValue == 0x02;
        Input.DPadDown = dpadValue == 0x04;
        Input.DPadLeft = dpadValue == 0x06;

        // Diagonals: Up-Right=1, Down-Right=3, Down-Left=5, Up-Left=7
        Input.DPadUp = Input.DPadUp || dpadValue == 0x01 || dpadValue == 0x07;
        Input.DPadRight = Input.DPadRight || dpadValue == 0x01 || dpadValue == 0x03;
        Input.DPadDown = Input.DPadDown || dpadValue == 0x03 || dpadValue == 0x05;
        Input.DPadLeft = Input.DPadLeft || dpadValue == 0x05 || dpadValue == 0x07;

        // Check DPad button events
        CheckButtonEvent(ButtonType.DPadUp, oldUp, Input.DPadUp);
        CheckButtonEvent(ButtonType.DPadRight, oldRight, Input.DPadRight);
        CheckButtonEvent(ButtonType.DPadDown, oldDown, Input.DPadDown);
        CheckButtonEvent(ButtonType.DPadLeft, oldLeft, Input.DPadLeft);

        // Face buttons (upper 4 bits)
        bool oldSquare = Input.Square, oldCross = Input.Cross, oldCircle = Input.Circle, oldTriangle = Input.Triangle;

        Input.Square = (btnBlock1 & 0x10) != 0;
        Input.Cross = (btnBlock1 & 0x20) != 0;
        Input.Circle = (btnBlock1 & 0x40) != 0;
        Input.Triangle = (btnBlock1 & 0x80) != 0;

        // Check face button events
        CheckButtonEvent(ButtonType.Square, oldSquare, Input.Square);
        CheckButtonEvent(ButtonType.Cross, oldCross, Input.Cross);
        CheckButtonEvent(ButtonType.Circle, oldCircle, Input.Circle);
        CheckButtonEvent(ButtonType.Triangle, oldTriangle, Input.Triangle);

        Logger.Trace<DualSenseController>($"DPad: {dpadValue:X}, Face: Square={Input.Square}, Cross={Input.Cross}, Circle={Input.Circle}, Triangle={Input.Triangle}");
    }

    private void ParseShoulderButtons(byte btnBlock2, InputState oldInput)
    {
        bool oldL1 = Input.L1, oldR1 = Input.R1, oldL2Button = Input.L2Button, oldR2Button = Input.R2Button;
        bool oldCreate = Input.Create, oldOptions = Input.Options, oldL3 = Input.L3, oldR3 = Input.R3;

        Input.L1 = (btnBlock2 & 0x01) != 0;
        Input.R1 = (btnBlock2 & 0x02) != 0;
        Input.L2Button = (btnBlock2 & 0x04) != 0;
        Input.R2Button = (btnBlock2 & 0x08) != 0;
        Input.Create = (btnBlock2 & 0x10) != 0;
        Input.Options = (btnBlock2 & 0x20) != 0;
        Input.L3 = (btnBlock2 & 0x40) != 0;
        Input.R3 = (btnBlock2 & 0x80) != 0;

        // Check shoulder button events
        CheckButtonEvent(ButtonType.L1, oldL1, Input.L1);
        CheckButtonEvent(ButtonType.R1, oldR1, Input.R1);
        CheckButtonEvent(ButtonType.L2, oldL2Button, Input.L2Button);
        CheckButtonEvent(ButtonType.R2, oldR2Button, Input.R2Button);
        CheckButtonEvent(ButtonType.Create, oldCreate, Input.Create);
        CheckButtonEvent(ButtonType.Options, oldOptions, Input.Options);
        CheckButtonEvent(ButtonType.L3, oldL3, Input.L3);
        CheckButtonEvent(ButtonType.R3, oldR3, Input.R3);

        Logger.Trace<DualSenseController>($"Shoulders: L1={Input.L1}, R1={Input.R1}, L2={Input.L2Button}, R2={Input.R2Button}, L3={Input.L3}, R3={Input.R3}");
    }

    private void ParseSystemButtons(byte btnBlock3, InputState oldInput)
    {
        bool oldPS = Input.PS, oldTouchPadClick = Input.TouchPadClick, oldMute = Input.Mute;

        Input.PS = (btnBlock3 & 0x01) != 0;
        Input.TouchPadClick = (btnBlock3 & 0x02) != 0;
        Input.Mute = (btnBlock3 & 0x04) != 0;

        // Check system button events
        CheckButtonEvent(ButtonType.PS, oldPS, Input.PS);
        CheckButtonEvent(ButtonType.TouchPad, oldTouchPadClick, Input.TouchPadClick);
        CheckButtonEvent(ButtonType.Mute, oldMute, Input.Mute);

        Logger.Trace<DualSenseController>($"System: PS={Input.PS}, TouchPad={Input.TouchPadClick}, Mute={Input.Mute}");
    }

    private void ParseBatteryInfo(byte batteryByte)
    {
        byte rawLevel = (byte)(batteryByte & 0x0F);
        float newLevel = Math.Min((rawLevel * 100) / 8, 100);

        byte powerState = (byte)((batteryByte >> 4) & 0x0F);
        bool charging = powerState == 0x01;
        bool fullyCharged = powerState == 0x02;

        // Check for battery changes
        if (Math.Abs(Battery.BatteryLevel - newLevel) > 1 || Battery.IsCharging != charging || Battery.IsFullyCharged != fullyCharged)
        {
            BatteryState oldBattery = new BatteryState
            {
                BatteryLevel = Battery.BatteryLevel,
                IsCharging = Battery.IsCharging,
                IsFullyCharged = Battery.IsFullyCharged
            };

            Battery.BatteryLevel = newLevel;
            Battery.IsCharging = charging;
            Battery.IsFullyCharged = fullyCharged;

            Logger.Debug<DualSenseController>($"Battery: {newLevel:F0}%, State=0x{powerState:X} (Charging={charging}, Full={fullyCharged})");

            BatteryChanged?.Invoke(this, new BatteryStateEventArgs(Battery, oldBattery));
        }
    }

    private void ParseConnectionStatus(byte statusByte)
    {
        ConnectionStatus oldStatus = new ConnectionStatus
        {
            IsHeadphoneConnected = ConnectionStatus.IsHeadphoneConnected,
            IsMicConnected = ConnectionStatus.IsMicConnected,
            IsMicMuted = ConnectionStatus.IsMicMuted,
            IsUsbDataConnected = ConnectionStatus.IsUsbDataConnected,
            IsUsbPowerConnected = ConnectionStatus.IsUsbPowerConnected
        };

        ConnectionStatus.IsHeadphoneConnected = (statusByte & 0x01) != 0;
        ConnectionStatus.IsMicConnected = (statusByte & 0x02) != 0;
        ConnectionStatus.IsMicMuted = (statusByte & 0x04) != 0;
        ConnectionStatus.IsUsbDataConnected = (statusByte & 0x08) != 0;
        ConnectionStatus.IsUsbPowerConnected = (statusByte & 0x10) != 0;

        // Check for connection status changes
        if (oldStatus.IsHeadphoneConnected != ConnectionStatus.IsHeadphoneConnected ||
            oldStatus.IsMicConnected != ConnectionStatus.IsMicConnected ||
            oldStatus.IsMicMuted != ConnectionStatus.IsMicMuted ||
            oldStatus.IsUsbDataConnected != ConnectionStatus.IsUsbDataConnected ||
            oldStatus.IsUsbPowerConnected != ConnectionStatus.IsUsbPowerConnected)
        {
            Logger.Trace<DualSenseController>($"Status: Headphone={ConnectionStatus.IsHeadphoneConnected}, Mic={ConnectionStatus.IsMicConnected}, USB={ConnectionStatus.IsUsbDataConnected}");

            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusEventArgs(ConnectionStatus, oldStatus));
        }
    }

    private TouchPoint ParseTouchData(byte[] data, int offset)
    {
        if (data.Length < offset + 4)
        {
            Logger.Trace<DualSenseController>($"Insufficient data for touchpad at offset {offset}");
            return new TouchPoint();
        }

        uint touchData = BitConverter.ToUInt32(data, offset);

        TouchPoint point = new TouchPoint
        {
            Index = (byte)(touchData & 0x7F),
            IsActive = ((touchData >> 7) & 0x01) == 0,
            X = (ushort)((touchData >> 8) & 0xFFF),
            Y = (ushort)((touchData >> 20) & 0xFFF)
        };

        if (point.IsActive)
        {
            Logger.Trace<DualSenseController>($"Touch at offset {offset}: ({point.X}, {point.Y}), Index={point.Index}");
        }

        return point;
    }

    private void CheckButtonEvent(ButtonType buttonType, bool oldState, bool newState)
    {
        if (oldState != newState)
        {
            SpecialActionService?.ProcessButtonEvent(this, buttonType, newState);
            if (newState)
            {
                ButtonPressed?.Invoke(this, new ButtonEventArgs(buttonType));
            }
            else
            {
                ButtonReleased?.Invoke(this, new ButtonEventArgs(buttonType));
                SpecialActionService?.CheckForActiveSpecialActionRelease(this);
            }
        }
    }

    private void CheckTriggerChanges(InputState oldInput)
    {
        if (oldInput.L2 != Input.L2)
        {
            TriggerChanged?.Invoke(this, new TriggerEventArgs(TriggerType.L2, Input.L2, oldInput.L2));
        }

        if (oldInput.R2 != Input.R2)
        {
            TriggerChanged?.Invoke(this, new TriggerEventArgs(TriggerType.R2, Input.R2, oldInput.R2));
        }
    }

    private void CheckStickMovement(InputState oldInput)
    {
        bool leftStickMoved = oldInput.LeftStickX != Input.LeftStickX || oldInput.LeftStickY != Input.LeftStickY;
        bool rightStickMoved = oldInput.RightStickX != Input.RightStickX || oldInput.RightStickY != Input.RightStickY;

        if (leftStickMoved)
        {
            StickMoved?.Invoke(this, new StickEventArgs(
                StickType.Left,
                Input.LeftStickX,
                Input.LeftStickY,
                oldInput.LeftStickX,
                oldInput.LeftStickY));
        }

        if (rightStickMoved)
        {
            StickMoved?.Invoke(this, new StickEventArgs(
                StickType.Right,
                Input.RightStickX,
                Input.RightStickY,
                oldInput.RightStickX,
                oldInput.RightStickY));
        }
    }

    private void CheckTouchpadChanges(TouchpadState oldTouchpad)
    {
        bool touch1Changed = oldTouchpad.Touch1.IsActive != Touchpad.Touch1.IsActive ||
                             (Touchpad.Touch1.IsActive && (oldTouchpad.Touch1.X != Touchpad.Touch1.X || oldTouchpad.Touch1.Y != Touchpad.Touch1.Y));

        bool touch2Changed = oldTouchpad.Touch2.IsActive != Touchpad.Touch2.IsActive ||
                             (Touchpad.Touch2.IsActive && (oldTouchpad.Touch2.X != Touchpad.Touch2.X || oldTouchpad.Touch2.Y != Touchpad.Touch2.Y));

        if (touch1Changed || touch2Changed)
        {
            TouchpadChanged?.Invoke(this, new TouchpadEventArgs(Touchpad, oldTouchpad));
        }
    }

    private void CheckMotionChanges(MotionState oldMotion)
    {
        bool gyroChanged = oldMotion.GyroX != Motion.GyroX ||
                           oldMotion.GyroY != Motion.GyroY ||
                           oldMotion.GyroZ != Motion.GyroZ;

        bool accelChanged = oldMotion.AccelX != Motion.AccelX ||
                            oldMotion.AccelY != Motion.AccelY ||
                            oldMotion.AccelZ != Motion.AccelZ;

        if (gyroChanged || accelChanged)
        {
            MotionChanged?.Invoke(this, new MotionEventArgs(Motion, oldMotion));
        }
    }

    private bool HasInputChanged(InputState oldInput)
    {
        return oldInput.LeftStickX != Input.LeftStickX || oldInput.LeftStickY != Input.LeftStickY ||
               oldInput.RightStickX != Input.RightStickX || oldInput.RightStickY != Input.RightStickY ||
               oldInput.L2 != Input.L2 || oldInput.R2 != Input.R2 ||
               oldInput.DPadUp != Input.DPadUp || oldInput.DPadDown != Input.DPadDown ||
               oldInput.DPadLeft != Input.DPadLeft || oldInput.DPadRight != Input.DPadRight ||
               oldInput.Cross != Input.Cross || oldInput.Circle != Input.Circle ||
               oldInput.Square != Input.Square || oldInput.Triangle != Input.Triangle ||
               oldInput.L1 != Input.L1 || oldInput.R1 != Input.R1 ||
               oldInput.L2Button != Input.L2Button || oldInput.R2Button != Input.R2Button ||
               oldInput.L3 != Input.L3 || oldInput.R3 != Input.R3 ||
               oldInput.Create != Input.Create || oldInput.Options != Input.Options ||
               oldInput.PS != Input.PS || oldInput.TouchPadClick != Input.TouchPadClick || oldInput.Mute != Input.Mute;
    }

    private InputState CopyInputState(InputState state)
    {
        return new InputState
        {
            LeftStickX = state.LeftStickX,
            LeftStickY = state.LeftStickY,
            RightStickX = state.RightStickX,
            RightStickY = state.RightStickY,
            L2 = state.L2,
            R2 = state.R2,
            L2Button = state.L2Button,
            R2Button = state.R2Button,
            DPadUp = state.DPadUp,
            DPadDown = state.DPadDown,
            DPadLeft = state.DPadLeft,
            DPadRight = state.DPadRight,
            Cross = state.Cross,
            Circle = state.Circle,
            Square = state.Square,
            Triangle = state.Triangle,
            L1 = state.L1,
            R1 = state.R1,
            L3 = state.L3,
            R3 = state.R3,
            Create = state.Create,
            Options = state.Options,
            PS = state.PS,
            TouchPadClick = state.TouchPadClick,
            Mute = state.Mute
        };
    }

    private TouchpadState CopyTouchpadState(TouchpadState state)
    {
        return new TouchpadState
        {
            Touch1 = new TouchPoint
            {
                Index = state.Touch1.Index,
                IsActive = state.Touch1.IsActive,
                X = state.Touch1.X,
                Y = state.Touch1.Y
            },
            Touch2 = new TouchPoint
            {
                Index = state.Touch2.Index,
                IsActive = state.Touch2.IsActive,
                X = state.Touch2.X,
                Y = state.Touch2.Y
            }
        };
    }

    private MotionState CopyMotionState(MotionState state)
    {
        return new MotionState
        {
            GyroX = state.GyroX,
            GyroY = state.GyroY,
            GyroZ = state.GyroZ,
            AccelX = state.AccelX,
            AccelY = state.AccelY,
            AccelZ = state.AccelZ
        };
    }

    /// <summary>
    /// Sets the lightbar behavior
    /// </summary>
    public bool SetLightbarBehavior(LightbarBehavior behavior)
    {
        Logger.Debug<DualSenseController>($"Setting lightbar behavior: {behavior}");

        // Store previous state
        LightbarColor previousColor = CurrentLightbarColor;
        LightbarBehavior previousBehavior = CurrentLightbarBehavior;

        // Update current state
        CurrentLightbarBehavior = behavior;

        // Send the output report
        bool success = SendOutputReport();

        // Fire event if successful
        if (success)
        {
            LightbarChanged?.Invoke(this, new LightbarChangedEventArgs(
                CurrentLightbarColor,
                previousColor,
                CurrentLightbarBehavior,
                previousBehavior));
        }

        return success;
    }

    /// <summary>
    /// Sets the lightbar color
    /// </summary>
    public bool SetLightbar(byte red, byte green, byte blue)
    {
        Logger.Debug<DualSenseController>($"Setting lightbar color: RGB({red}, {green}, {blue})");

        // Store previous state
        LightbarColor previousColor = CurrentLightbarColor;
        LightbarBehavior previousBehavior = CurrentLightbarBehavior;

        // Update current state
        CurrentLightbarColor = new LightbarColor(red, green, blue);
        CurrentLightbarBehavior = LightbarBehavior.Custom;

        // Send the output report
        bool success = SendOutputReport();

        // Fire event if successful
        if (success)
        {
            LightbarChanged?.Invoke(this, new LightbarChangedEventArgs(
                CurrentLightbarColor,
                previousColor,
                CurrentLightbarBehavior,
                previousBehavior));
        }

        return success;
    }

    /// <summary>
    /// Sets the player LEDs
    /// </summary>
    public bool SetPlayerLeds(PlayerLed leds, PlayerLedBrightness brightness = PlayerLedBrightness.High)
    {
        Logger.Debug<DualSenseController>($"Setting player LEDs: {leds}, Brightness={brightness}");

        // Store previous state
        PlayerLed previousLeds = CurrentPlayerLeds;
        PlayerLedBrightness previousBrightness = CurrentPlayerLedBrightness;

        // Update current state
        CurrentPlayerLeds = leds;
        CurrentPlayerLedBrightness = brightness;

        // Send the output report
        bool success = SendOutputReport();

        // Fire event if successful
        if (success)
        {
            PlayerLedsChanged?.Invoke(this, new PlayerLedsChangedEventArgs(
                CurrentPlayerLeds,
                previousLeds,
                CurrentPlayerLedBrightness,
                previousBrightness));
        }

        return success;
    }

    /// <summary>
    /// Sets the vibration intensity for both motors
    /// </summary>
    /// <param name="largeMotor">Large motor (left) vibration intensity (0-255)</param>
    /// <param name="smallMotor">Small motor (right) vibration intensity (0-255)</param>
    public bool SetVibration(byte largeMotor, byte smallMotor)
    {
        Logger.Debug<DualSenseController>($"Setting vibration: Large={largeMotor}, Small={smallMotor}");

        // Store previous values
        byte previousLargeMotor = LargeMotorValue;
        byte previousSmallMotor = SmallMotorValue;

        // Update current values
        LargeMotorValue = largeMotor;
        SmallMotorValue = smallMotor;

        // Send the output report
        bool success = SendOutputReport();

        return success;
    }

    /// <summary>
    /// Sets the microphone LED
    /// </summary>
    public bool SetMicLed(MicLed led)
    {
        Logger.Debug<DualSenseController>($"Setting mic LED: {led}");

        // Store previous state
        MicLed previousLed = CurrentMicLed;

        // Update current state
        CurrentMicLed = led;

        // Send the output report
        bool success = SendOutputReport();

        // Fire event if successful
        if (success)
        {
            MicLedChanged?.Invoke(this, new MicLedChangedEventArgs(CurrentMicLed, previousLed));
        }

        return success;
    }

    private bool SendOutputReport()
    {
        if (!IsConnected)
        {
            Logger.Warning<DualSenseController>("Cannot send output report: Controller not connected");
            return false;
        }

        lock (_writeLock)
        {
            try
            {
                byte[] report = BuildOutputReport(ConnectionType == ConnectionType.Bluetooth);
                Logger.Trace<DualSenseController>($"Sending {ConnectionType} output report: {report.Length} bytes");

                _stream.Write(report);
                Logger.Trace<DualSenseController>("Output report sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error<DualSenseController>("Failed to send output report");
                Logger.LogExceptionDetails<DualSenseController>(ex, includeEnvironmentInfo: false);
                HandleDisconnection();
                return false;
            }
        }
    }

    private byte[] BuildOutputReport(bool isBluetooth)
    {
        // Initialize report with appropriate size
        byte[] report = new byte[isBluetooth ? 78 : 48];

        // Calculate offset (BT has 1 extra byte at the beginning)
        int offset = isBluetooth ? 1 : 0;

        // Report ID
        report[0] = isBluetooth ? (byte)0x31 : (byte)0x02;

        // BT-specific header
        if (isBluetooth)
        {
            report[1] = 0x02;
        }

        // Feature mask
        report[1 + offset] = 0xFF;
        report[2 + offset] = 0xF7;

        // Rumble (vibration) data
        report[3 + offset] = SmallMotorValue; // Light motor
        report[4 + offset] = LargeMotorValue; // Heavy motor

        // Mic LED
        report[9 + offset] = (byte)CurrentMicLed;

        // Lightbar enable flags
        // AllowLightBrightnessChange (0x01), AllowColorLightFadeAnimation (0x02)
        report[39 + offset] = 0x03;

        // Lightbar behavior
        report[42 + offset] = (byte)CurrentLightbarBehavior;

        // Player LED brightness
        report[43 + offset] = (byte)CurrentPlayerLedBrightness;

        // Player LEDs (0x20 for immediate change, remove for fade-in animation)
        report[44 + offset] = (byte)(0x20 | (byte)CurrentPlayerLeds);

        // RGB colors
        report[45 + offset] = CurrentLightbarColor.Red;
        report[46 + offset] = CurrentLightbarColor.Green;
        report[47 + offset] = CurrentLightbarColor.Blue;

        if (!isBluetooth)
        {
            // USB Output Report
            return report;
        }

        // Calculate and append CRC32 for Bluetooth
        uint crc = CRC32DualSense.Compute(report, 0, 74);
        report[74] = (byte)(crc & 0xFF);
        report[75] = (byte)((crc >> 8) & 0xFF);
        report[76] = (byte)((crc >> 16) & 0xFF);
        report[77] = (byte)((crc >> 24) & 0xFF);

        Logger.Trace<DualSenseController>($"BT Report - CRC: 0x{crc:X8}, Bytes: [{report[74]:X2} {report[75]:X2} {report[76]:X2} {report[77]:X2}]");

        return report; // BT Output Report
    }

    /// <summary>
    /// Disconnects the Bluetooth connection (if connected via Bluetooth)
    /// </summary>
    public bool DisconnectBluetooth()
    {
        if (ConnectionType != ConnectionType.Bluetooth)
        {
            Logger.Warning<DualSenseController>("Cannot disconnect Bluetooth: Controller is connected via USB");
            return false;
        }

        if (MacAddress == null)
        {
            Logger.Warning<DualSenseController>("Cannot disconnect Bluetooth: MAC address not available");
            return false;
        }

        Logger.Info<DualSenseController>($"Disconnecting Bluetooth device: {MacAddress}");
        return BluetoothHelper.Disconnect(MacAddress);
    }

    private void HandleDisconnection()
    {
        if (!IsConnected)
        {
            Logger.Trace<DualSenseController>("HandleDisconnection called but already disconnected");
            return;
        }

        IsConnected = false;
        Logger.Info<DualSenseController>($"Controller disconnected: {Device.GetProductName()} ({ConnectionType})");

        try
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Warning<DualSenseController>($"Exception in Disconnected event handler: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Logger.Debug<DualSenseController>("Disposing DualSense controller");
        _cts.Cancel();

        try
        {
            if (!_readTask.Wait(1000))
            {
                Logger.Warning<DualSenseController>("Read task did not complete within timeout");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning<DualSenseController>($"Exception while waiting for read task: {ex.Message}");
        }

        // Dispose Controller Emulation Service
        if (ControllerEmulationService != null)
        {
            try
            {
                ControllerEmulationService.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warning<DualSenseController>($"Exception while disposing ControllerEmulationService: {ex.Message}");
            }
        }

        _cts.Dispose();
        _stream.Dispose();

        Logger.Debug<DualSenseController>("DualSense controller disposed");
    }
}