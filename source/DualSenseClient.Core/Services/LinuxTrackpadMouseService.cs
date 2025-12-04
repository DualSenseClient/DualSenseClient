using DualSenseClient.Core.DualSense.Devices;
using DualSenseClient.Core.DualSense.Events;
using DualSenseClient.Core.Logging;
using DualSenseClient.Core.Settings.Models;

namespace DualSenseClient.Core.Services;

public class LinuxTrackpadMouseService : ITrackpadMouseService
{
    private DualSenseController? _controller;
    private VirtualControllerSettings? _settings;

    public void Initialize(DualSenseController controller, VirtualControllerSettings settings)
    {
        _controller = controller;
        _settings = settings;
        
        // Subscribe to touchpad events
        _controller.TouchpadChanged += OnTouchpadChanged;
        
        Logger.Info<LinuxTrackpadMouseService>($"Linux trackpad mouse service initialized (placeholder) for controller {_controller.Device.DevicePath}");
        Logger.Warning<LinuxTrackpadMouseService>("Trackpad as mouse functionality is not yet implemented for Linux");
    }

    private void OnTouchpadChanged(object? sender, TouchpadEventArgs e)
    {
        // Linux placeholder - would need to use X11 or Wayland APIs to control mouse
        if (_settings == null || !_settings.TrackpadMouse.Enabled || _controller == null)
        {
            return;
        }

        // Log that functionality is not yet available
        Logger.Debug<LinuxTrackpadMouseService>("Trackpad input detected, but Linux mouse control is not implemented yet");
    }

    public void Dispose()
    {
        if (_controller != null)
        {
            _controller.TouchpadChanged -= OnTouchpadChanged;
        }
        
        Logger.Info<LinuxTrackpadMouseService>("Linux trackpad mouse service disposed (placeholder)");
    }
}