<h1 align="center">
DualSense Client
</h1>

<p align="center">
  <b>Unofficial DualSense client for Windows and Linux</b><br>
  Customize and monitor your PS5 DualSense controller: lighting, profiles, input monitoring, rumble, adaptive triggers, and audio to the controller's speaker and haptics.
</p>

---

<p align="center">
  <img src="https://shieldcn.dev/badge/Platform-Windows_%26_Linux.svg?color=0070D1&variant=ghost&font=fira-code&theme=zinc" alt="Platform">
  <a href="https://github.com/DualSenseClient/DualSenseClient/blob/main/LICENSE"><img src="https://shieldcn.dev/github/DualSenseClient/DualSenseClient/license.svg?color=0070D1&variant=ghost&font=fira-code&theme=zinc" alt="License"></a>
  <a href="https://github.com/DualSenseClient/DualSenseClient/actions/workflows/build_release.yml?query=branch%3Amain"><img src="https://shieldcn.dev/github/DualSenseClient/DualSenseClient/ci.svg?workflow=build_release.yml&amp;branch=main&amp;label=Build&amp;logo=github&amp;color=0070D1&amp;variant=ghost&amp;font=fira-code&amp;theme=zinc" alt="Build Status"></a>
</p>

<p align="center">
  <a href="https://dualsenseclient.github.io/DualSenseClient/"><img src="https://shieldcn.dev/badge/Documentation-Online.svg?color=0070D1&variant=ghost&font=fira-code&theme=zinc" alt="Documentation"></a>
  <a href="https://github.com/DualSenseClient/DualSenseClient/releases/latest"><img src="https://shieldcn.dev/github/DualSenseClient/DualSenseClient/release.svg?label=Stable&color=0070D1&variant=ghost&font=fira-code&theme=zinc" alt="Stable Release"></a>
  <a href="https://github.com/DualSenseClient/DualSenseClient/releases"><img src="https://shieldcn.dev/badge/dynamic/json.svg?url=https%3A%2F%2Fimg.shields.io%2Fgithub%2Fv%2Frelease%2FDualSenseClient%2FDualSenseClient.json%3Finclude_prereleases&query=%24.value&label=Nightly&color=0070D1&variant=ghost&font=fira-code&theme=zinc" alt="Nightly Release"></a>
</p>

<p align="center">
  <a href="https://github.com/sponsors/shazzaam7"><img src="https://shieldcn.dev/badge/Sponsor-GitHub_Sponsors.svg?color=0070D1&variant=ghost&font=fira-code&theme=zinc&logo=githubsponsors" alt="Github Sponsors"></a>
  <a href="https://ko-fi.com/shazzaam"><img src="https://shieldcn.dev/badge/Sponsor-Ko--fi.svg?color=0070D1&variant=ghost&font=fira-code&theme=zinc&logo=kofi" alt="Ko-fi"></a>
</p>

---

## Overview

**DualSense Client** is an open-source management tool for the **PlayStation 5 DualSense Controller** on Windows and Linux. It provides lightbar and LED control, profile management, real-time input monitoring, rumble and adaptive trigger output, and audio playback to the controller's speaker and haptics. It can also create virtual controllers (Xbox 360, DualShock 4, or DualSense; Windows only for now) through its built-in emulation engine, and hide physical controllers from other applications via HidHide (Windows only). Built with .NET 10 and Avalonia, it connects over USB, Bluetooth, or a DS5Dongle USB bridge through HIDAPI with no drivers required.

## Supported Controllers

| Controller              | Supported | Notes                                                     |
| ----------------------- | --------- | --------------------------------------------------------- |
| DualSense (USB)         | ✅        | Full support                                              |
| DualSense (Bluetooth)   | ✅        | Full support including speaker and haptics audio          |
| DualSense Edge          | ✅        | Full support including Fn buttons and paddles<sup>1</sup> |
| DualSense via DS5Dongle | ✅        | USB bridge; bridge status and on-bridge configuration     |

<sup><i>1. Implemented but not yet verified on real hardware — no Edge device was available during development.</i></sup>

DS5Dongle support tracks the latest commit in the
[official DualSenseClient/DS5Dongle repository](https://github.com/DualSenseClient/DS5Dongle).
Forked or unmerged firmware changes are not tracked.

## Features

### Light Control

- Per-profile lightbar modes: Single Color, Multi Color, Breathing, Strobing, Battery, and Audio Reactive, with palettes, brightness, and lighting that follows forwarded audio
- Player LED presets, plus individual toggles for all five LEDs on Generation 2/3 hardware
- Microphone LED configuration (off / on / pulse)

### Profile Management

- Create, rename, duplicate, and delete controller lighting profiles
- Assign profiles to specific controllers by MAC address from the Device Info page (with device path fallback)
- Profiles are applied automatically on connection, with a manual re-apply option
- Edit the lightbar, microphone LED, and player LEDs per profile

### Real-time Monitoring

- Live controller illustration for buttons, sticks, triggers, and touchpad, plus numeric trigger, stick, touch, gyro, and accelerometer data
- Connection type detection (USB / Bluetooth / Dongle), plus Bridge Status for DS5Dongle
- Polling-rate display and support for multiple controllers with an in-window controller picker

### Output Control

- Rumble output test with independent left/right motor strength
- Adaptive trigger modes for L2/R2 (Feedback, Weapon, Vibration, Bow, Galloping, Machine, Slope Feedback) with per-mode tuning

### Audio

- Play a loaded audio file through the desktop speakers, the controller's speaker or headset jack, and the controller haptics, in any combination
- Opus-encoded haptics over Bluetooth, USB audio over the controller's audio endpoint
- With a DS5Dongle, USB audio is carried by the bridge; microphone and speaker routes are configured on the **Dongle Configuration** page
- Playback volume and haptic strength sliders with transport controls

### Device Information

- Battery level with power state (charging, discharging, etc.)
- Firmware versions (main, SBL, DSP) and model revision with manual refresh
- Connection status: headphones/mic jacked in, microphone muted, USB data and power

### DS5Dongle Bridge

- Detects the DS5Dongle as a USB DualSense bridge and labels the connection **Dongle**
- Bridge Status shows dongle firmware, link signal, bridge Bluetooth address, and microphone/speaker stream state, with an optional five-second auto-refresh
- On-bridge configuration covers haptics and speaker gain, trigger feedback reduction, audio buffer and packet mode, lock-volume modes, polling rate, idle/dock behavior, microphone/speaker routes, controller mode, USB serial/keyboard/wake, and status GPIO
- Read, Save to Flash, Reconnect USB, and Reset to Defaults actions, with automatic apply and reconnect warnings
- Supports current v5 firmware (24/25-byte layout) and the original 19-byte legacy layout

See the [DS5Dongle Bridge guide](https://dualsenseclient.github.io/DualSenseClient/guides/dongle/) for details.

### Settings

- Theme selection (System / Light / Dark / AMOLED / PlayStation) with an optional Mica backdrop on Windows 11
- Language selection (English currently; additional languages supported by the localization framework)
- Desktop notification preferences for connection, low battery, and charge events
- Stable or nightly update channel with optional automatic daily checks
- Configurable logging level with console and date-rotated file logging

### Virtual Controller Emulation

- Create a virtual **Xbox 360**, **DualShock 4**, or **DualSense** (Standard or Edge) controller that mirrors the physical controller's input, with rumble, lightbar, and trigger feedback forwarded back
- Per-controller emulation settings applied on connection, adjustable from the **Emulation** page or the tray menu (currently Windows-only)
- DualSense output exposes haptics, adaptive triggers, and audio interfaces to the host; host audio can be forwarded to the physical controller's speaker or headset with volume and haptic-strength controls
- Choose the forwarded audio source: virtual controller audio, system-output loopback, or a specific application (Windows 11)
- Use the DualSense touchpad as a Windows mouse (pointer, tap-to-click, two-finger right-click and scrolling)
- Powered by the built-in **Emulation** engine, which only requires the USB/IP driver on the host system

### Controller Hiding

- Hide a physical controller from other applications with a per-controller toggle, preventing double input in games
- The app whitelists itself so it continues to see the hidden controller
- Requires the **HidHide** driver on Windows

## Getting Started

### Prerequisites

- **Windows 10 or later** with the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed (the Windows build is framework-dependent)
- **Linux** — no .NET installation required (self-contained build)
- **PlayStation 5 DualSense controller** (wired USB, Bluetooth, or DS5Dongle USB bridge)
- **DS5Dongle** (optional) — USB Bluetooth bridge; use the latest official firmware, no extra driver required
- **HidHide** (optional, Windows only) — driver for hiding physical controllers from other applications; see [nefarius/HidHide](https://github.com/nefarius/HidHide)
- **USB/IP** (optional, virtual controller emulation only; currently Windows-only) — the built-in emulation engine attaches virtual devices through it:
    - Windows: [usbip-win2](https://github.com/vadimgrn/usbip-win2)
    - Linux: support is not enabled yet; install `usbip` once it is (Arch: `sudo pacman -S usbip`; Ubuntu/Debian: `sudo apt install linux-tools-generic`)

### Installation

#### Windows

1. Download `DualSenseClient.zip` from the [releases page](https://github.com/DualSenseClient/DualSenseClient/releases)
2. Extract the archive to your preferred location and run `DualSenseClient.exe`
3. Connect your DualSense controller and start customizing

#### Linux

1. Download `DualSenseClient-linux.zip` or the `DualSenseClient.AppImage` from the [releases page](https://github.com/DualSenseClient/DualSenseClient/releases)
2. For the zip: extract and run the `DualSenseClient` binary. For the AppImage: mark it executable and run it
3. To access the controller without root, install a udev rule based on the official [hidapi udev rules](https://github.com/libusb/hidapi/blob/master/udev/69-hid.rules). The rules below cover the DualSense (`054c:0ce6`) and DualSense Edge (`054c:0df2`) over USB and Bluetooth, and are also available as [`assets/linux/70-dualsense.rules`](assets/linux/70-dualsense.rules). Drop them into `/etc/udev/rules.d/70-dualsense.rules`:

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

```
sudo udevadm control --reload-rules && sudo udevadm trigger
```

### Building from Source

```bash
dotnet restore
dotnet build
dotnet test
```

Requires the .NET 10 SDK. Releases are produced automatically by CI for Windows (zip) and Linux (zip and AppImage).

## Troubleshooting

- **Controller not detected**: Make sure you have the latest DualSense firmware and try USB instead of Bluetooth
- **Controller not detected on Linux**: Check that the udev rule is installed (see the [hidapi udev rules](https://github.com/libusb/hidapi/blob/master/udev/69-hid.rules) reference above), or run the application with elevated privileges
- **Bluetooth connection issues**: Restart the Bluetooth service or pair the controller again through your system settings
- **DS5Dongle issues**: Pair the controller to the bridge, update the bridge firmware from the [official repository](https://github.com/DualSenseClient/DS5Dongle), and use **Reconnect USB** after USB-descriptor changes. If the link signal stays at `0`, turn the controller on and enable **Auto-refresh status** on Device Info. See the [DS5Dongle Bridge guide](https://dualsenseclient.github.io/DualSenseClient/guides/dongle/)
- **Missing .NET runtime on Windows**: Install the required .NET 10 Desktop Runtime from Microsoft's website
- **Virtual controller does not appear**: Install the USB/IP driver (Windows: [usbip-win2](https://github.com/vadimgrn/usbip-win2)) and restart the app. Virtual controller emulation is currently Windows-only
- **A game receives double input**: Install the HidHide driver and enable the hide toggle for the physical controller on its Device Info page

## Credits

### Libraries

- [Avalonia](https://avaloniaui.net/) — Cross-platform .NET UI framework
- [Fluent Avalonia](https://github.com/amwx/FluentAvalonia) — Fluent Design System for Avalonia
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM framework with source generators
- [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) — Dependency injection container
- [Fluent Icons](https://github.com/davidxuang/FluentIcons) — Fluent icon set for modern interfaces
- [HIDAPI](https://github.com/libusb/hidapi) — Cross-platform HID access for USB and Bluetooth (native binary bundled on Windows; system libhidapi-hidraw on Linux)
- [Tmds.DBus](https://github.com/tmds/Tmds.DBus) — D-Bus client for BlueZ integration on Linux (Bluetooth disconnect; Windows uses the native Bluetooth radio driver via P/Invoke)
- [Concentus](https://github.com/lostromb/concentus) — Opus audio encoding for Bluetooth haptics
- [SoundFlow](https://github.com/LSXPrime/SoundFlow) — Audio playback with FFmpeg codec support
- [Nefarius.Drivers.HidHide](https://github.com/nefarius/Nefarius.Drivers.HidHide) — .NET bindings for the HidHide driver

### Research

The DualSense protocol, audio, and haptics implementations were developed using the following sources:

- [Linux kernel hid-playstation driver](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) — Protocol reference, report layouts, calibration, and battery logic
- [dualsense-tester](https://github.com/daidr/dualsense-tester) — Input/output report field maps and trigger effect logic
- [ExtendInput TriggerEffectGenerator by Nielk1](https://gist.github.com/Nielk1/6d54cc2c00d2201ccb8c2720ad7538db) — Adaptive trigger effect byte layouts, parameter packing, and validation ranges
- [vds](https://github.com/hurryman2212/vds) — Output report structs and Bluetooth audio/haptics implementation
- [VIIPER](https://github.com/hbashton/VIIPER) — Inspired the Emulation engine's USB/IP approach
- [DS4Dongle](https://github.com/snipem/DS4Dongle) — DualShock 4 V2 emulation research
- [ViGEmBus](https://github.com/nefarius/ViGEmBus) — DualShock 4 V1 and Xbox 360 emulation research
- [HidHide](https://github.com/nefarius/HidHide) — Driver for hiding physical controllers from other applications
- [DS4Windows](https://github.com/hbashton/DS4Windows) — DualSense integration and emulation
- [SAxense](https://github.com/egormanga/SAxense) — Bluetooth audio/haptics packet framing
- [DS5Dongle](https://github.com/awalol/DS5Dongle) — Bluetooth audio reports, feature reports, and firmware info
- [dualsense-bt-haptics](https://github.com/awalol/dualsense-bt-haptics) — Bluetooth haptics and speaker playback in C#
- [ViGEmBus (simple_ds5_support fork)](https://github.com/awalol/ViGEmBus/tree/simple_ds5_support) — Virtual DualSense identity and feature report handling
- [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro) — Real-device USB descriptors and profiles
- [PadForge](https://github.com/hifihedgehog/PadForge) — Output report framing and audio passthrough
- [LinuxAudio4Dualsense5](https://github.com/GeorgLegato/LinuxAudio4Dualsense5) — Working Bluetooth audio/haptics producer profile

### Inspiration

- [SharpEmu](https://github.com/sharpemu/sharpemu/) — The custom logging infrastructure was inspired by SharpEmu's logger
- [Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) — The settings persistence library was inspired by Lenovo Legion Toolkit's settings system
- [VSCView](https://github.com/Nielk1/VSCView) — The live controller visualization was inspired by VSCView's controller themes

### Assets

- [Gamepad Asset Pack](https://github.com/AL2009man/Gamepad-Asset-Pack) — DualSense controller artwork used on the device info page
- [Gamepad Prompt Asset Pack](https://github.com/AL2009man/Gamepad-Prompt-Asset-Pack) — PlayStation button prompt icons used in the input monitor

## Disclaimer

> This project is not affiliated with or endorsed by **Sony Interactive Entertainment**.
> "PlayStation", "DualSense", and related marks are trademarks of their respective owners.

## Contributing

Pull requests are welcome! If you'd like to improve this tool or report bugs, feel free to open an issue or start a discussion. See [CONTRIBUTING](docs/CONTRIBUTING.md) for details.

## License

Released under the [GPL-3.0 License](LICENSE).
