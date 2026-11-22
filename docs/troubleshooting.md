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

### DS5Dongle Issues

The DS5Dongle is a USB bridge: the host sees a wired DualSense and DualSense Client
labels it **Dongle** while keeping the transport USB.

- **The dongle is not detected** — make sure the controller is paired to the bridge, then
  reconnect the bridge over USB. The bridge must expose its firmware/configuration vendor
  reports; update the bridge firmware if it is very old. Support tracks the latest commit in
  the official [DualSenseClient/DS5Dongle](https://github.com/DualSenseClient/DS5Dongle)
  repository.
- **Dongle Configuration is unavailable** — update the bridge firmware. The page supports
  current v5 firmware (24/25-byte layout) and the original 19-byte legacy layout, and
  rejects unsupported intermediate layouts.
- **Link signal stays at 0** — the firmware returns its cached reading and starts a new
  measurement in the background. Turn the controller on, then open Device Info with
  **Auto-refresh status** enabled. See
  [Bridge Status](guides/dongle.md#bridge-status).
- **Volume changes are ignored** — check **Customization → Dongle Configuration → Lock
  volume**. **Games & software** ignores HID volume changes; **System volume (UAC)** ignores
  USB Audio changes. See [Lock volume modes](guides/dongle.md#lock-volume-modes).
- **A USB setting did not take effect** — polling rate, controller mode, USB serial number,
  keyboard interface, and USB wake need **Reconnect USB** from the Dongle Configuration
  footer.
- **Extra keyboard or consumer-control controllers appear** — the app filters the bridge's
  non-gamepad HID interfaces. Update to a build that includes the dongle HID fix if you
  still see phantom controllers.

## Runtime Errors

### Missing .NET Runtime on Windows

If Windows reports a missing `dotnet` runtime when launching:

- Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- Make sure you installed the **Desktop** runtime (not just the base runtime), since the app has a graphical interface

## Virtual Controller Emulation

### Virtual Controller Does Not Appear

Attaching a virtual device requires the **USB/IP driver**:

=== "Windows"

    1. Install [usbip-win2](https://github.com/vadimgrn/usbip-win2)
    2. Restart the application

=== "Linux"

    Virtual controller emulation is not yet supported on Linux. The USB/IP package will be required once support is enabled.

If the driver is installed but no device appears, check the application logs — USB/IP must be available for auto-attachment. See also the [Emulation guide](guides/virtual-controller.md).

## Games & Double Input

### A Game Receives Double Input

This happens when both the physical controller and an emulated virtual controller are visible to the same game.

=== "Windows"

    1. Install the [HidHide](https://github.com/nefarius/HidHide) driver
    2. Open the physical controller's Device Info page and enable the hide toggle

    The app whitelists itself automatically, so it keeps seeing the hidden controller while games no longer do.

=== "Linux"

    1. Install the Linux hiding helper once — see [Controller Hiding](guides/controller-hiding.md#linux-setup)
    2. Start DualSense Client from the application menu or the hiding launcher
    3. Open the physical controller's Device Info page and enable the hide toggle

    If the toggle says hiding is unavailable, see [Linux hiding is unavailable](#linux-hiding-is-unavailable).

See the [Controller Hiding guide](guides/controller-hiding.md).

## Linux Hiding Is Unavailable

The Linux hiding row always appears, but the toggle is disabled when the backend cannot be used. The application log records the exact cause; the common ones are:

- **Helper not installed or outdated.** Run the installer from the `DualSenseClient-linux-hiding` release asset or source build output with `sudo`, then restart the app. If the helper protocol is too old, hiding stays unavailable until you re-run the installer.
- **App started without the `dualsenseclient` group.** Start it from the application menu or `/usr/libexec/dualsenseclient/launch`. Check the effective group with `id`; the process should show `dualsenseclient` as its effective group, not just a supplementary group.
- **Helper socket missing.** Check `systemctl status dualsense-hiding.socket` and `ls -l /run/dualsenseclient/hiding.sock`. The socket must be group `dualsenseclient` and mode `0660`.
- **Kernel too old for the no-reset path.** Linux 6.12+ uses `HIDIOCREVOKE`; older kernels fall back to rebinding the HID driver, which briefly reconnects the controller. This is slower but should still hide the device.
- **Installer rejects the app path.** `--app-path` must resolve to an absolute regular executable that is not writable by group or other users, and its parent directories must not be group/other writable unless they are sticky. For a source build, publish to a private directory such as `~/.local/opt/dualsenseclient` and pass `~/.local/opt/dualsenseclient/DualSenseClient`; otherwise use the AppImage.
- **Helper fails with `GLIBC_2.xx not found`.** The release helper targets x86_64 Linux with glibc 2.35 or newer (Ubuntu 22.04, Debian 12, or newer). Use the matching release asset or build the helper from source on that machine.
- **Uninstall stops during cleanup.** The cleanup script could not restore a controller node, so the uninstaller leaves the helper installed. Fix the node permissions, then retry the uninstall.

### The Controller Still Appears in a Game

Access is denied, but the device may still be listed by some applications because this is not a kernel filter driver. If the game reacts to a ghost entry, make sure the entry cannot be opened (it should fail with `EACCES`) and that the game is using the virtual controller. True enumeration removal is not implemented on Linux.

### The Controller Reconnects or Its Lights Reset When Hiding

That is expected on kernels older than 6.12, where the helper rebinds the HID driver to revoke existing handles. Update to Linux 6.12+ to use the no-reset `HIDIOCREVOKE` path.

### Phantom Input While the Virtual Microphone Is Active

While a virtual microphone is active on a Bluetooth controller, the physical controller sends microphone packets alongside its normal input reports. An application that reads the physical controller directly may misparse those packets as gamepad input, producing phantom stick movement or button presses.

Hide the physical controller so only DualSense Client reads it — see [Controller Hiding](guides/controller-hiding.md). See [Audio Playback: Microphone](guides/audio.md#microphone) for the microphone controls and limitations.

## Logs

The app writes date-rotated log files next to the executable:

```
<application folder>/Logs/DualSenseClient.log
```

On Linux, if the installation directory is not writable, logs fall back to `~/.config/DualSenseClient`.

To capture more detail when reporting a problem, raise the logging level in **Settings** (for example to `Debug`) before reproducing the issue, then attach the log file to your issue report.

