using System.Runtime.InteropServices;
using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Events;
using DualSenseClient.Core.DualSense.Reports;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.Core.Services;

public class TrackpadMouseService : ITrackpadMouseService
{
    private DualSenseController? _controller;
    private VirtualControllerSettings? _settings;

    // Trackpad state tracking
    private TouchPoint _previousTouch1 = new TouchPoint();
    private TouchPoint _previousTouch2 = new TouchPoint();
    private bool _isDragging = false;

    // Button state tracking
    private bool _previousTouchPadClick = false;
    private bool _clickProcessed = false;  // To prevent multiple clicks during hold


    public void Initialize(DualSenseController controller, VirtualControllerSettings settings)
    {
        _controller = controller;
        _settings = settings;

        // Subscribe to touchpad events
        _controller.TouchpadChanged += OnTouchpadChanged;

        Logger.Info<TrackpadMouseService>($"Trackpad mouse service initialized for controller {_controller.Device.DevicePath}");
    }

    private void OnTouchpadChanged(object? sender, TouchpadEventArgs e)
    {
        if (_settings == null || !_settings.TrackpadMouse.Enabled || _controller == null)
        {
            return;
        }

        // Handle touch release - reset tracking state
        if (!e.CurrentState.Touch1.IsActive && _previousTouch1.IsActive)
        {
            // Touch ended, reset previous position tracking
            _previousTouch1.IsActive = false;
            _previousTouch1.X = 0;
            _previousTouch1.Y = 0;
        }
        // Handle single-finger touch for mouse movement
        else if (e.CurrentState.Touch1.IsActive && !e.CurrentState.Touch2.IsActive)
        {
            // Initialize first touch position if this is a new touch
            if (!_previousTouch1.IsActive)
            {
                _previousTouch1.X = e.CurrentState.Touch1.X;
                _previousTouch1.Y = e.CurrentState.Touch1.Y;
                _previousTouch1.IsActive = true;
            }

            // Always use relative movement now
            HandleRelativeMovement(e);
        }

        // Handle touchpad click (button press) - only trigger on button down (transition from released to pressed)
        bool currentTouchPadClick = _controller.Input.TouchPadClick;

        // Only process click if button is pressed AND it's a new press (transition from not pressed to pressed)
        // Also ensure we haven't already processed a click during this button hold
        if (currentTouchPadClick && !_previousTouchPadClick && !_clickProcessed)
        {
            HandleTouchpadClick();
            _clickProcessed = true;  // Mark that click has been processed during this hold
        }
        // If button was released, reset the flag to allow next click
        else if (!currentTouchPadClick && _previousTouchPadClick)
        {
            _clickProcessed = false;  // Allow next click when button is pressed again
        }

        // Update previous state for next comparison
        _previousTouchPadClick = currentTouchPadClick;
    }

    private void HandleRelativeMovement(TouchpadEventArgs e)
    {
        if (_settings == null)
        {
            return;
        }

        // Calculate movement delta from previous position
        int deltaX = e.CurrentState.Touch1.X - _previousTouch1.X;
        int deltaY = e.CurrentState.Touch1.Y - _previousTouch1.Y;

        // Apply sensitivity and inversion settings
        if (_settings.TrackpadMouse.InvertX)
        {
            deltaX = -deltaX;
        }

        if (_settings.TrackpadMouse.InvertY)
        {
            deltaY = -deltaY;
        }

        // Adjust with sensitivity
        double adjustedX = deltaX * _settings.TrackpadMouse.Sensitivity;
        double adjustedY = deltaY * _settings.TrackpadMouse.Sensitivity;

        // Move mouse cursor by the calculated amount
        MoveMouseRelative((int)adjustedX, (int)adjustedY);

        // Update previous position to current for next delta calculation
        _previousTouch1.X = e.CurrentState.Touch1.X;
        _previousTouch1.Y = e.CurrentState.Touch1.Y;
    }


    private void HandleTouchpadClick()
    {
        // For now, treat touchpad click as left mouse button
        // In the future, could implement right-click with two fingers, etc.
        ClickMouse();
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // Mouse input constants
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private void MoveMouseRelative(int deltaX, int deltaY)
    {
        INPUT[] inputs = new INPUT[1];
        
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dx = deltaX;
        inputs[0].u.mi.dy = deltaY;
        inputs[0].u.mi.mouseData = 0;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE;
        inputs[0].u.mi.time = 0;
        inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private void MoveMouseAbsolute(int x, int y)
    {
        // Get screen dimensions
        GetCursorPos(out POINT currentPos);
        
        // Convert to normalized coordinates (0-65535 range for absolute mouse movement)
        int absoluteX = (x * 65535) / GetSystemMetrics(0); // SM_CXSCREEN
        int absoluteY = (y * 65535) / GetSystemMetrics(1); // SM_CYSCREEN

        INPUT[] inputs = new INPUT[1];
        
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dx = absoluteX;
        inputs[0].u.mi.dy = absoluteY;
        inputs[0].u.mi.mouseData = 0;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;
        inputs[0].u.mi.time = 0;
        inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private void ClickMouse()
    {
        INPUT[] inputs = new INPUT[2];
        
        // Mouse down
        inputs[0].type = INPUT_MOUSE;
        inputs[0].u.mi.dx = 0;
        inputs[0].u.mi.dy = 0;
        inputs[0].u.mi.mouseData = 0;
        inputs[0].u.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        inputs[0].u.mi.time = 0;
        inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

        // Mouse up
        inputs[1].type = INPUT_MOUSE;
        inputs[1].u.mi.dx = 0;
        inputs[1].u.mi.dy = 0;
        inputs[1].u.mi.mouseData = 0;
        inputs[1].u.mi.dwFlags = MOUSEEVENTF_LEFTUP;
        inputs[1].u.mi.time = 0;
        inputs[1].u.mi.dwExtraInfo = IntPtr.Zero;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    public void Dispose()
    {
        // Unsubscribe from events
        _controller.TouchpadChanged -= OnTouchpadChanged;
        
        Logger.Info<TrackpadMouseService>("Trackpad mouse service disposed");
    }
}