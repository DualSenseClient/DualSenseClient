# Installation

## Prerequisites

=== "Windows"

    - Windows 10 or later
    - [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — the Windows build is framework-dependent

=== "Linux"

    - No .NET installation required (self-contained build)
    - HIDAPI system library for controller access (Ubuntu/Debian: `sudo apt install libhidapi-hidraw0`)

Optional components:

- **HidHide** (optional, Windows only) — driver for hiding physical controllers from other applications; see [nefarius/HidHide](https://github.com/nefarius/HidHide). See the [Controller Hiding guide](guides/controller-hiding.md)
- **Linux hiding helper** (optional, Linux only) — one-time root helper that hides physical controllers from other applications; see [Controller Hiding](guides/controller-hiding.md#linux-setup)
- **USB/IP** (optional, virtual controller emulation only; currently Windows-only) — required by the built-in emulation engine to attach virtual devices:
    - Windows: [usbip-win2](https://github.com/vadimgrn/usbip-win2)
    - Linux: install `usbip` (Arch: `sudo pacman -S usbip`; Ubuntu/Debian: `sudo apt install linux-tools-generic`) — required once virtual controller support is enabled on Linux

See the [Emulation guide](guides/virtual-controller.md) for a full walkthrough.

You will also need a **PlayStation 5 DualSense controller**, connected via wired USB or Bluetooth. A **DS5Dongle** USB bridge also works and needs no extra driver — see the [DS5Dongle Bridge guide](guides/dongle.md).

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

To access the controller without running the app as root, install a udev rule based on the official [hidapi udev rules](https://github.com/libusb/hidapi/blob/master/udev/69-hid.rules). The rules cover the DualSense (`054c:0ce6`) and DualSense Edge (`054c:0df2`) over USB and Bluetooth. They are also available as [`assets/linux/70-dualsense.rules`](https://github.com/DualSenseClient/DualSenseClient/blob/main/assets/linux/70-dualsense.rules). Create `/etc/udev/rules.d/70-dualsense.rules` with:

```
# HIDAPI/libusb
SUBSYSTEMS=="usb", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"
SUBSYSTEMS=="usb", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0df2", TAG+="uaccess"

# HIDAPI/hidraw over USB
KERNEL=="hidraw*", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"
KERNEL=="hidraw*", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0df2", TAG+="uaccess"

# HIDAPI/hidraw over Bluetooth. The hidraw node's parent is a HID device named like
# "0005:054C:0CE6.0003" rather than a USB device, so idVendor/idProduct never match; match the name instead.
KERNEL=="hidraw*", KERNELS=="*054C:0CE6*", TAG+="uaccess"
KERNEL=="hidraw*", KERNELS=="*054C:0DF2*", TAG+="uaccess"
```

Then replug the controller or run:

```bash
sudo udevadm control --reload-rules && sudo udevadm trigger
```

!!! tip
    If the controller is still not detected after installing the rule, see [Controller Not Detected on Linux](troubleshooting.md#controller-not-detected-on-linux).

---

## Linux: Controller Hiding (Optional)

Controller hiding on Linux uses a small root helper, a `dualsenseclient` group, a systemd socket, and udev rules. It ships as a separate release asset:

1. Download `DualSenseClient-linux-hiding.tar.gz` from the same release as the app.
2. Extract it and run the installer once:

   ```bash
   tar -xzf DualSenseClient-linux-hiding.tar.gz
   sudo ./DualSenseClient-linux-hiding/install-dualsense-hiding.sh --app-path /path/to/DualSenseClient.AppImage
   ```

The installer canonicalizes and validates the app path, then writes it to `/etc/dualsenseclient/app-path`, which the setgid launcher reads. It also writes an application-menu entry. After installation, start DualSense Client through the application menu or `/usr/libexec/dualsenseclient/launch`. Starting the app directly does not give it the `dualsenseclient` group, so the hide toggle reports that the backend is unavailable.

The release helper is built for x86_64 Linux with glibc 2.35 or newer (Ubuntu 22.04, Debian 12, or newer).

For source builds, produce the same archive with `scripts/build_linux_hiding.py`; see the [Controller Hiding guide](guides/controller-hiding.md#linux-setup).

Linux 6.12 or newer hides and unhides without reconnecting the controller. Older kernels use a HID-driver rebind fallback, which briefly reconnects the controller and resets its lights/triggers until the app reapplies them. See [Controller Hiding](guides/controller-hiding.md#linux-behavior-and-limits) for the full limitations.

---

## Next Steps

- Follow the guides to set up [lighting](guides/light-control.md), [profiles](guides/profiles.md), and more
- Want to build the application yourself? See [Development](development.md)

