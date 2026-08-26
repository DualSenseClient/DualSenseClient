# Installation

## Prerequisites

=== "Windows"

    - Windows 10 or later
    - [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — the Windows build is framework-dependent

=== "Linux"

    - No .NET installation required (self-contained build)

Optional components:

- **HidHide** (optional, Windows only) — driver for hiding physical controllers from other applications; see [nefarius/HidHide](https://github.com/nefarius/HidHide). See the [Controller Hiding guide](guides/controller-hiding.md)
- **USB/IP** (optional, virtual controller emulation only) — libVIIPER is bundled with the app; it only needs the USB/IP driver to attach virtual devices:
    - Windows: [usbip-win2](https://github.com/vadimgrn/usbip-win2)
    - Linux: install `usbip` (Arch: `sudo pacman -S usbip`; Ubuntu/Debian: `sudo apt install linux-tools-generic`)

See the [Virtual Controller guide](guides/virtual-controller.md) for a full walkthrough.

You will also need a **PlayStation 5 DualSense controller**, connected via wired USB or Bluetooth.

---

## Installing the Application

=== "Windows"

    1. Download `DualSenseClient.zip` from the [releases page](https://github.com/DualSenseClient/DualSenseClient/releases)
    2. Extract the archive to your preferred location
    3. Run `DualSenseClient.exe`
    4. Connect your DualSense controller and start customizing

=== "Linux"

    1. Download `DualSenseClient-linux.zip` or the `DualSenseClient.AppImage` from the [releases page](https://github.com/DualSenseClient/DualSenseClient/releases)
    2. For the zip: extract and run the `DualSenseClient` binary
    3. For the AppImage: mark it executable and run it:

    ```bash
    chmod +x DualSenseClient.AppImage
    ./DualSenseClient.AppImage
    ```

---

## Linux: Controller Access Without Root

To access the controller without running the app as root, install a udev rule based on the official [hidapi udev rules](https://github.com/libusb/hidapi/blob/master/udev/69-hid.rules). For the DualSense (VID `054c`, PID `0ce6`), create `/etc/udev/rules.d/70-dualsense.rules` with:

```
# HIDAPI/libusb
SUBSYSTEMS=="usb", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"

# HIDAPI/hidraw
KERNEL=="hidraw*", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"
```

Then replug the controller or run:

```bash
sudo udevadm control --reload-rules && sudo udevadm trigger
```

!!! tip
    If the controller is still not detected after installing the rule, see [Controller Not Detected on Linux](troubleshooting.md#controller-not-detected-on-linux).

---

## Next Steps

- Follow the guides to set up [lighting](guides/light-control.md), [profiles](guides/profiles.md), and more
- Want to build the application yourself? See [Development](development.md)

