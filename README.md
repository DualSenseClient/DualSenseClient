<h1 align="center">
DualSense Client
</h1>

<p align="center">
  <b>Unofficial DualSense client for Windows and Linux</b><br>
  Customize and monitor your PS5 DualSense controller: lighting, profiles, input monitoring, rumble, adaptive triggers, and audio to the controller's speaker and haptics.
</p>

---

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%20%26%20Linux-blue" alt="Platform">
  <a href="https://github.com/DualSenseClient/DualSenseClient/blob/main/LICENSE"><img src="https://img.shields.io/github/license/DualSenseClient/DualSenseClient?color=green" alt="License"></a>
  <a href="https://github.com/DualSenseClient/DualSenseClient/actions/workflows/build_release.yml"><img src="https://img.shields.io/github/actions/workflow/status/DualSenseClient/DualSenseClient/build_release.yml?label=Build&logo=github" alt="Build Status"></a>
</p>

<p align="center">
  <a href="https://github.com/DualSenseClient/DualSenseClient/releases/latest"><img src="https://img.shields.io/github/v/release/DualSenseClient/DualSenseClient?label=Latest%20Release" alt="Latest Release"></a>
  <a href="https://github.com/DualSenseClient/DualSenseClient/releases/tag/nightly"><img src="https://img.shields.io/github/v/release/DualSenseClient/DualSenseClient?include_prereleases&label=Pre-Release&color=orange" alt="Pre-Release"></a>
</p>

---

## Overview

**DualSense Client** is an open-source management tool for the **PlayStation 5 DualSense Controller** on Windows and Linux. It provides lightbar and LED control, profile management, real-time input monitoring, rumble and adaptive trigger output, and audio playback to the controller's speaker and haptics. Built with .NET 10 and Avalonia, it connects over USB or Bluetooth through SDL3 HID with no drivers required.

## Supported Controllers

| Controller            | Supported | Notes                                            |
| --------------------- | --------- | ------------------------------------------------ |
| DualSense (USB)       | ✅        | Full support                                     |
| DualSense (Bluetooth) | ✅        | Full support including speaker and haptics audio |
| DualSense Edge        | ⏳        | Coming soon                                      |

## Features

### Light Control

- Full RGB lightbar control with color picker, sliders, presets, and reset
- Player LED control (individual toggles for all five LEDs)
- Microphone LED configuration (off / on / pulse)

### Profile Management

- Create, rename, duplicate, and delete controller profiles
- Bind profiles to specific controllers by MAC address (with device path fallback)
- Profiles are applied automatically on connection, with a manual reapply option
- Save the selected controller's current light state as a profile

### Real-time Monitoring

- Live input monitoring: all buttons, sticks, triggers, motion sensors (gyro/accelerometer graphs), and touchpad
- Connection type detection (USB / Bluetooth)
- Support for multiple controllers with an in-window controller picker

### Output Control

- Rumble output test with independent left/right motor strength
- Adaptive trigger modes for L2/R2 (Resistance, Trigger, Automatic) with force, frequency, start, and end adjustments

### Audio

- Play audio files to the desktop speakers, the DualSense speaker, or the controller haptics, each independently
- Opus-encoded haptics over Bluetooth, USB audio over the controller's audio endpoint
- Speaker volume and haptic strength sliders with transport controls

### Device Information

- Battery level with power state (charging, discharging, etc.)
- Firmware versions (main, SBL, DSP) and model revision with manual refresh
- Connection status: headphones/mic jacked in, microphone muted, USB data and power

### Settings

- Theme selection (System / Light / Dark)
- Language selection (English currently; additional languages supported by the localization framework)
- Configurable logging level with console and date-rotated file logging

## Getting Started

### Prerequisites

- **Windows 10 or later** with the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed (the Windows build is framework-dependent)
- **Linux** — no .NET installation required (self-contained build)
- **PlayStation 5 DualSense controller** (wired USB or Bluetooth connection)

### Installation

#### Windows

1. Download `DualSenseClient.zip` from the [releases page](https://github.com/DualSenseClient/DualSenseClient/releases)
2. Extract the archive to your preferred location and run `DualSenseClient.exe`
3. Connect your DualSense controller and start customizing

#### Linux

1. Download `DualSenseClient-linux.zip` or the `DualSenseClient.AppImage` from the [releases page](https://github.com/DualSenseClient/DualSenseClient/releases)
2. For the zip: extract and run the `DualSenseClient` binary. For the AppImage: mark it executable and run it
3. To access the controller without root, install a udev rule based on the official [hidapi udev rules](https://github.com/libusb/hidapi/blob/master/udev/69-hid.rules). For the DualSense (VID `054c`), drop a file like `70-dualsense.rules` into `/etc/udev/rules.d/`:

```
# HIDAPI/libusb
SUBSYSTEMS=="usb", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"

# HIDAPI/hidraw
KERNEL=="hidraw*", ATTRS{idVendor}=="054c", ATTRS{idProduct}=="0ce6", TAG+="uaccess"
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
- **Missing .NET runtime on Windows**: Install the required .NET 10 Desktop Runtime from Microsoft's website

## Credits

### Libraries

- [Avalonia](https://avaloniaui.net/) — Cross-platform .NET UI framework
- [Fluent Avalonia](https://github.com/amwx/FluentAvalonia) — Fluent Design System for Avalonia
- [Fluent Icons](https://github.com/davidxuang/FluentIcons) — Fluent icon set for modern interfaces
- [SDL3](https://github.com/libsdl-org/SDL) (via [ppy.SDL3-CS](https://github.com/ppy/SDL3-CS)) — Cross-platform HID access for USB and Bluetooth
- [Concentus](https://github.com/lostromb/concentus) — Opus audio encoding for Bluetooth haptics
- [SoundFlow](https://github.com/LSXPrime/SoundFlow) — Audio playback with FFmpeg codec support
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM framework with source generators
- [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) — Dependency injection container

### Research

The DualSense protocol, audio, and haptics implementations were developed using the following sources:

- [Linux kernel hid-playstation driver](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) — Protocol reference, report layouts, calibration, and battery logic
- [dualsense-tester](https://github.com/daidr/dualsense-tester) — Input/output report field maps and trigger effect logic
- [vds](https://github.com/hurryman2212/vds) — Output report structs and Bluetooth audio/haptics implementation
- [VIIPER](https://github.com/hbashton/VIIPER) — Virtual DualSense emulation, stream protocol, and haptics
- [SAxense](https://github.com/egormanga/SAxense) — Bluetooth audio/haptics packet framing
- [DS5Dongle](https://github.com/awalol/DS5Dongle) — Bluetooth audio reports, feature reports, and firmware info
- [dualsense-bt-haptics](https://github.com/awalol/dualsense-bt-haptics) — Bluetooth haptics and speaker playback in C#
- [DS4Windows](https://github.com/hbashton/DS4Windows) — DualSense integration and emulation
- [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro) — Real-device USB descriptors and profiles
- [PadForge](https://github.com/hifihedgehog/PadForge) — Output report framing and audio passthrough
- [ViGEmBus (simple_ds5_support fork)](https://github.com/awalol/ViGEmBus/tree/simple_ds5_support) — Virtual DualSense identity and feature report handling
- [LinuxAudio4Dualsense5](https://github.com/GeorgLegato/LinuxAudio4Dualsense5) — Working Bluetooth audio/haptics producer profile
- [SoundFlow](https://github.com/LSXPrime/SoundFlow) — Audio engine research and playback behavior

### Inspiration

- [SharpEmu](https://github.com/sharpemu/sharpemu/) — The custom logging infrastructure was inspired by SharpEmu's logger
- [Lenovo Legion Toolkit](https://github.com/LenovoLegionToolkit-Team/LenovoLegionToolkit) — The settings persistence library was inspired by Lenovo Legion Toolkit's settings system

### Assets

- [DualSense Controller Mockup](https://www.titanui.com/106136-ps5-dualsense-controller-vector-illustration-figma) — Mockup design used for the controller illustration

## Disclaimer

> This project is not affiliated with or endorsed by **Sony Interactive Entertainment**.
> "PlayStation", "DualSense", and related marks are trademarks of their respective owners.

## Contributing

Pull requests are welcome! If you'd like to improve this tool or report bugs, feel free to open an issue or start a discussion. See [CONTRIBUTING](docs/CONTRIBUTING.md) for details.

## License

Released under the [GPL-3.0 License](LICENSE).
