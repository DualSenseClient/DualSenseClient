using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.Logging;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DualSenseClient.Core.DualSense.Services;

/// <summary>
/// Provides virtual controller emulation functionality using ViGEm
/// </summary>
public class ControllerEmulationService : IDisposable
{
    private ViGEmClient? _client;
    public bool IsEmulating360 { get; private set; } = false;
    public bool IsEmulating { get; private set; } = false;
    public IDualShock4Controller? DualShock4Controller { get; private set; }
    public IXbox360Controller? X360Controller { get; private set; }
    public DualSenseController? DualSenseController { get; set; }
    public int LeftTriggerThreshold { get; set; } = 0;
    public int RightTriggerThreshold { get; set; } = 0;
    public bool ForceStopRumble { get; set; } = true;
    public bool IsViGEMBusInstalled { get; private set; } = false;
    public bool IgnoreDS4Lightbar { get; set; } = false;

    public ControllerEmulationService()
    {
        InitializeViGEmClient();
    }

    private void InitializeViGEmClient()
    {
        try
        {
            _client = new ViGEmClient();
            IsViGEMBusInstalled = true;
            Logger.Info<ControllerEmulationService>("ViGEm client initialized successfully, ViGEmBus driver is installed");
        }
        catch (VigemBusNotFoundException)
        {
            IsViGEMBusInstalled = false;
            Logger.Warning<ControllerEmulationService>("ViGEmBus driver not found, virtual controller emulation will be disabled");
        }
        catch (Exception ex)
        {
            IsViGEMBusInstalled = false;
            Logger.Error<ControllerEmulationService>($"Error initializing ViGEm client: {ex.Message}");
        }
    }

    public void StartX360Emulation()
    {
        if (!IsViGEMBusInstalled || _client == null)
        {
            Logger.Warning<ControllerEmulationService>("Cannot start X360 emulation, ViGEmBus not installed or client not initialized");
            return;
        }

        StopEmulation();

        try
        {
            if (X360Controller == null)
            {
                X360Controller = _client.CreateXbox360Controller();
            }

            X360Controller.Connect();
            X360Controller.FeedbackReceived += X360Controller_FeedbackReceived;
            IsEmulating360 = true;
            IsEmulating = true;

            Task.Run(() => Emulate()).ConfigureAwait(false);

            Logger.Info<ControllerEmulationService>("X360 controller emulation started successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<ControllerEmulationService>($"Failed to start X360 emulation: {ex.Message}");
        }
    }

    private void X360Controller_FeedbackReceived(object? sender, Xbox360FeedbackReceivedEventArgs e)
    {
        if (DualSenseController != null && !ForceStopRumble)
        {
            try
            {
                // Set vibration on the actual DualSense controller
                DualSenseController.SetVibration(e.LargeMotor, e.SmallMotor);
            }
            catch (Exception ex)
            {
                Logger.Warning<ControllerEmulationService>($"Failed to set DualSense vibration: {ex.Message}");
            }
        }
    }

    public void StartDS4Emulation()
    {
        if (!IsViGEMBusInstalled || _client == null)
        {
            Logger.Warning<ControllerEmulationService>("Cannot start DS4 emulation, ViGEmBus not installed or client not initialized");
            return;
        }

        StopEmulation();

        try
        {
            if (DualShock4Controller == null)
            {
                DualShock4Controller = _client.CreateDualShock4Controller();
            }

            DualShock4Controller.Connect();
            DualShock4Controller.FeedbackReceived += Dualshock4_FeedbackReceived;
            IsEmulating360 = false;
            IsEmulating = true;

            Task.Run(() =>
            {
                Emulate();
            }).ConfigureAwait(false);

            Logger.Info<ControllerEmulationService>("DS4 controller emulation started successfully");
        }
        catch (Exception ex)
        {
            Logger.Error<ControllerEmulationService>($"Failed to start DS4 emulation: {ex.Message}");
        }
    }

    public void StopEmulation()
    {
        IsEmulating = false;

        if (X360Controller != null)
        {
            try
            {
                X360Controller.Disconnect();
            }
            catch (Exception ex)
            {
                Logger.Warning<ControllerEmulationService>($"Error disconnecting X360 controller: {ex.Message}");
            }
            X360Controller = null;
        }

        if (DualShock4Controller != null)
        {
            try
            {
                DualShock4Controller.Disconnect();
            }
            catch (Exception ex)
            {
                Logger.Warning<ControllerEmulationService>($"Error disconnecting DS4 controller: {ex.Message}");
            }
            DualShock4Controller = null;
        }

        Logger.Info<ControllerEmulationService>("Controller emulation stopped");
    }

    private void Dualshock4_FeedbackReceived(object? sender, DualShock4FeedbackReceivedEventArgs e)
    {
        if (DualSenseController != null && !ForceStopRumble)
        {
            try
            {
                // Set vibration on the actual DualSense controller
                DualSenseController.SetVibration(e.LargeMotor, e.SmallMotor);
            }
            catch (Exception ex)
            {
                Logger.Warning<ControllerEmulationService>($"Failed to set DualSense vibration: {ex.Message}");
            }
        }

        if (!IgnoreDS4Lightbar && DualSenseController != null)
        {
            try
            {
                if (e.LightbarColor.Red != 0 || e.LightbarColor.Green != 0 || e.LightbarColor.Blue != 0)
                {
                    DualSenseController.SetLightbar(e.LightbarColor.Red, e.LightbarColor.Green, e.LightbarColor.Blue);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning<ControllerEmulationService>($"Failed to set DualSense lightbar: {ex.Message}");
            }
        }
    }

    private void Emulate()
    {
        byte[] rawDS4 = new byte[63];

        while (IsEmulating)
        {
            try
            {
                if (DualSenseController == null || !DualSenseController.IsConnected)
                {
                    StopEmulation();
                    break;
                }

                if (IsEmulating360 && X360Controller != null)
                {
                    // Map DualSense input to X360 controller
                    X360Controller.SetButtonState(Xbox360Button.A, DualSenseController.Input.Cross);
                    X360Controller.SetButtonState(Xbox360Button.B, DualSenseController.Input.Circle);
                    X360Controller.SetButtonState(Xbox360Button.Y, DualSenseController.Input.Triangle);
                    X360Controller.SetButtonState(Xbox360Button.X, DualSenseController.Input.Square);
                    X360Controller.SetButtonState(Xbox360Button.Up, DualSenseController.Input.DPadUp);
                    X360Controller.SetButtonState(Xbox360Button.Left, DualSenseController.Input.DPadLeft);
                    X360Controller.SetButtonState(Xbox360Button.Right, DualSenseController.Input.DPadRight);
                    X360Controller.SetButtonState(Xbox360Button.Down, DualSenseController.Input.DPadDown);

                    // Convert analog stick range from 0-255 to -32767 to 32766
                    X360Controller.SetAxisValue(Xbox360Axis.LeftThumbX, (short)ConvertRange(DualSenseController.Input.LeftStickX, 0, 255, -32767, 32766));
                    X360Controller.SetAxisValue(Xbox360Axis.LeftThumbY, (short)ConvertRange(DualSenseController.Input.LeftStickY, 255, 0, -32767, 32766));
                    X360Controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)ConvertRange(DualSenseController.Input.RightStickX, 0, 255, -32767, 32766));
                    X360Controller.SetAxisValue(Xbox360Axis.RightThumbY, (short)ConvertRange(DualSenseController.Input.RightStickY, 255, 0, -32767, 32766));
                    X360Controller.SetButtonState(Xbox360Button.LeftThumb, DualSenseController.Input.L3);
                    X360Controller.SetButtonState(Xbox360Button.RightThumb, DualSenseController.Input.R3);

                    if (DualSenseController.Input.L2 >= LeftTriggerThreshold)
                    {
                        X360Controller.LeftTrigger = DualSenseController.Input.L2;
                    }
                    else
                    {
                        X360Controller.LeftTrigger = 0;
                    }

                    if (DualSenseController.Input.R2 >= RightTriggerThreshold)
                    {
                        X360Controller.RightTrigger = DualSenseController.Input.R2;
                    }
                    else
                    {
                        X360Controller.RightTrigger = 0;
                    }

                    X360Controller.SetButtonState(Xbox360Button.Start, DualSenseController.Input.Options);
                    X360Controller.SetButtonState(Xbox360Button.Back, DualSenseController.Input.Create);
                    X360Controller.SetButtonState(Xbox360Button.LeftShoulder, DualSenseController.Input.L1);
                    X360Controller.SetButtonState(Xbox360Button.RightShoulder, DualSenseController.Input.R1);
                    X360Controller.SetButtonState(Xbox360Button.Guide, DualSenseController.Input.PS);
                }
                else if (!IsEmulating360 && DualShock4Controller != null)
                {
                    // Map DualSense input to DS4 controller
                    rawDS4[0] = DualSenseController.Input.LeftStickX;
                    rawDS4[1] = DualSenseController.Input.LeftStickY;
                    rawDS4[2] = DualSenseController.Input.RightStickX;
                    rawDS4[3] = DualSenseController.Input.RightStickY;

                    byte xoState = 0x0;
                    xoState = (byte)(DualSenseController.Input.Triangle ? xoState | (byte)DualShock4Buttons.Triangle : xoState);
                    xoState = (byte)(DualSenseController.Input.Circle ? xoState | (byte)DualShock4Buttons.Circle : xoState);
                    xoState = (byte)(DualSenseController.Input.Cross ? xoState | (byte)DualShock4Buttons.Cross : xoState);
                    xoState = (byte)(DualSenseController.Input.Square ? xoState | (byte)DualShock4Buttons.Square : xoState);

                    // Handle DPAD
                    if (DualSenseController.Input.DPadUp && DualSenseController.Input.DPadLeft)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_NorthWest);
                    }
                    else if (DualSenseController.Input.DPadDown && DualSenseController.Input.DPadLeft)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_SouthWest);
                    }
                    else if (DualSenseController.Input.DPadDown && DualSenseController.Input.DPadRight)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_SouthEast);
                    }
                    else if (DualSenseController.Input.DPadUp && DualSenseController.Input.DPadRight)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_NorthEast);
                    }
                    else if (DualSenseController.Input.DPadLeft)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_West);
                    }
                    else if (DualSenseController.Input.DPadDown)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_South);
                    }
                    else if (DualSenseController.Input.DPadRight)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_East);
                    }
                    else if (DualSenseController.Input.DPadUp)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_North);
                    }
                    else if (!DualSenseController.Input.DPadUp && !DualSenseController.Input.DPadDown && !DualSenseController.Input.DPadLeft && !DualSenseController.Input.DPadRight)
                    {
                        xoState = (byte)(xoState | (byte)DualShock4Buttons.Dpad_Neutral);
                    }

                    rawDS4[4] = xoState;

                    byte lState = 0x0;
                    lState = (byte)(DualSenseController.Input.R3 ? lState | (byte)DualShock4Buttons.R3 : lState);
                    lState = (byte)(DualSenseController.Input.L3 ? lState | (byte)DualShock4Buttons.L3 : lState);
                    lState = (byte)(DualSenseController.Input.Options ? lState | (byte)DualShock4Buttons.Options : lState);
                    lState = (byte)(DualSenseController.Input.Create ? lState | (byte)DualShock4Buttons.Share : lState);
                    lState = (byte)(DualSenseController.Input.R2Button ? lState | (byte)DualShock4Buttons.R2 : lState);
                    lState = (byte)(DualSenseController.Input.L2Button ? lState | (byte)DualShock4Buttons.L2 : lState);
                    lState = (byte)(DualSenseController.Input.R1 ? lState | (byte)DualShock4Buttons.R1 : lState);
                    lState = (byte)(DualSenseController.Input.L1 ? lState | (byte)DualShock4Buttons.L1 : lState);
                    rawDS4[5] = lState;

                    byte tState = 0x0;
                    tState = (byte)(DualSenseController.Input.TouchPadClick ? tState | (byte)DualShock4Buttons.TouchPad : tState);
                    tState = (byte)(DualSenseController.Input.PS ? tState | (byte)DualShock4Buttons.PS : tState);
                    rawDS4[6] = tState;

                    rawDS4[7] = DualSenseController.Input.L2;
                    rawDS4[8] = DualSenseController.Input.R2;

                    // Add motion sensor data if available
                    short timestamp = (short)(Environment.TickCount / 16);
                    rawDS4[9] = (byte)(timestamp & 0xFF);
                    rawDS4[10] = (byte)(timestamp >> 8 & 0xFF);

                    rawDS4[18] = (byte)(DualSenseController.Motion.AccelX & 0xFF);
                    rawDS4[19] = (byte)(DualSenseController.Motion.AccelX >> 8 & 0xFF);
                    rawDS4[20] = (byte)(DualSenseController.Motion.AccelY & 0xFF);
                    rawDS4[21] = (byte)((DualSenseController.Motion.AccelY >> 8) & 0xFF);
                    rawDS4[22] = (byte)(DualSenseController.Motion.AccelZ & 0xFF);
                    rawDS4[23] = (byte)((DualSenseController.Motion.AccelZ >> 8) & 0xFF);

                    rawDS4[12] = (byte)(DualSenseController.Motion.GyroX & 0xFF);
                    rawDS4[13] = (byte)((DualSenseController.Motion.GyroX >> 8) & 0xFF);
                    rawDS4[14] = (byte)(DualSenseController.Motion.GyroY & 0xFF);
                    rawDS4[15] = (byte)((DualSenseController.Motion.GyroY >> 8) & 0xFF);
                    rawDS4[16] = (byte)(DualSenseController.Motion.GyroZ & 0xFF);
                    rawDS4[17] = (byte)((DualSenseController.Motion.GyroZ >> 8) & 0xFF);

                    // Add touchpad data
                    rawDS4[32] = 1;
                    rawDS4[33] = 0; // Touch packet number
                    rawDS4[34] = (byte)DualSenseController.Touchpad.Touch1.Index;
                    rawDS4[35] = (byte)(DualSenseController.Touchpad.Touch1.X & 0xFF);
                    rawDS4[36] = (byte)(((DualSenseController.Touchpad.Touch1.X >> 8) & 0x0F) |
                                        ((DualSenseController.Touchpad.Touch1.Y & 0x0F) << 4));
                    rawDS4[37] = (byte)((DualSenseController.Touchpad.Touch1.Y >> 4) & 0xFF);

                    rawDS4[38] = (byte)DualSenseController.Touchpad.Touch2.Index;
                    rawDS4[39] = (byte)(DualSenseController.Touchpad.Touch2.X & 0xFF);
                    rawDS4[40] = (byte)(((DualSenseController.Touchpad.Touch2.X >> 8) & 0x0F) |
                                        ((DualSenseController.Touchpad.Touch2.Y & 0x0F) << 4));
                    rawDS4[41] = (byte)((DualSenseController.Touchpad.Touch2.Y >> 4) & 0xFF);

                    DualShock4Controller.SubmitRawReport(rawDS4);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning<ControllerEmulationService>($"Error during emulation: {ex.Message}");
                break;
            }

            Thread.Sleep(1);
        }
    }

    private enum DualShock4Buttons
    {
        Triangle = 1 << 7,
        Circle = 1 << 6,
        Cross = 1 << 5,
        Square = 1 << 4,

        R3 = 1 << 7,
        L3 = 1 << 6,
        Options = 1 << 5,
        Share = 1 << 4,
        R2 = 1 << 3,
        L2 = 1 << 2,
        R1 = 1 << 1,
        L1 = 1 << 0,

        TouchPad = 1 << 1,
        PS = 1 << 0,

        Dpad_Neutral = 0b_1000,
        Dpad_NorthWest = 0b_0111,
        Dpad_West = 0b_0110,
        Dpad_SouthWest = 0b_0101,
        Dpad_South = 0b_0100,
        Dpad_SouthEast = 0b_0011,
        Dpad_East = 0b_0010,
        Dpad_NorthEast = 0b_0001,
        Dpad_North = 0b_0000
    }

    private int ConvertRange(int value, int oldMin, int oldMax, int newMin, int newMax)
    {
        if (oldMin == oldMax)
        {
            throw new ArgumentException("Old minimum and maximum cannot be equal.");
        }
        float ratio = (float)(newMax - newMin) / (float)(oldMax - oldMin);
        float scaledValue = (value - oldMin) * ratio + newMin;
        return Math.Clamp((int)scaledValue, newMin, newMax);
    }

    public void Dispose()
    {
        StopEmulation();
        _client?.Dispose();
    }
}