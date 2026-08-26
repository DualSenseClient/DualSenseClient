# Troubleshooting

Common problems and how to fix them. If you are stuck, please [open an issue](https://github.com/DualSenseClient/DualSenseClient/issues) with your log file attached (see [Logs](#logs)).

## Connection Problems

### Controller Not Detected

1. Make sure your controller is running the latest firmware
2. Try connecting via USB instead of Bluetooth (and vice versa)
3. Try a different USB cable and port — some cables are charge-only and carry no data
4. Close other applications that may claim exclusive access to the controller

If the app detects the connection type but inputs look wrong, restart the application with the controller already connected.

### Controller Not Detected on Linux

This is almost always a permissions issue: without a udev rule, only root can open the HID device.

1. Install the udev rule described in [Linux: Controller Access Without Root](installation.md#linux-controller-access-without-root)
2. Reload the rules and replug the controller:

    ```bash
    sudo udevadm control --reload-rules && sudo udevadm trigger
    ```

3. As a quick test, run the application with elevated privileges (`sudo ./DualSenseClient`). If it works with `sudo` but not without, the udev rule is missing or incorrect

!!! warning
    Avoid running the whole application as root permanently — fix permissions with the udev rule instead.

### Bluetooth Connection Issues

- Restart the Bluetooth service, or remove ("forget") the controller in your system's Bluetooth settings and pair it again
- Hold the PS button + Create button simultaneously until the lightbar rapidly flashes double blinks to put the controller into pairing mode, then pair from your system settings

<!-- REVIEW NOTE: Verify the exact pairing-mode instructions against actual behavior. -->

## Runtime Errors

### Missing .NET Runtime on Windows

If Windows reports a missing `dotnet` runtime when launching:

- Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Make sure you installed the **Desktop** runtime (not just the base runtime), since the app has a graphical interface

## Virtual Controller Emulation

### Virtual Controller Does Not Appear

libVIIPER is bundled with the app, but attaching a virtual device requires the **USB/IP driver**:

=== "Windows"

    1. Install [usbip-win2](https://github.com/vadimgrn/usbip-win2)
    2. Restart the application

=== "Linux"

    1. Install `usbip`:
        - Arch: `sudo pacman -S usbip`
        - Ubuntu/Debian: `sudo apt install linux-tools-generic`
    2. Restart the application

If the driver is installed but no device appears, check the VIIPER logs — USB/IP must be available for auto-attachment. See also the [Virtual Controller guide](guides/virtual-controller.md).

## Games & Double Input

### A Game Receives Double Input

This happens when both the physical controller and an emulated virtual controller are visible to the same game.

1. Install the [HidHide](https://github.com/nefarius/HidHide) driver (Windows only)
2. Open the physical controller's device page and enable the hide toggle

The app whitelists itself automatically, so it keeps seeing the hidden controller while games no longer do. See the [Controller Hiding guide](guides/controller-hiding.md).

## Logs

The app writes date-rotated log files next to the executable:

```
<application folder>/Logs/DualSenseClient.log
```

On Linux, if the installation directory is not writable, logs fall back to `~/.config/DualSenseClient`.

To capture more detail when reporting a problem, raise the logging level in **Settings** (for example to `Debug`) before reproducing the issue, then attach the log file to your issue report.

