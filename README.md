<h1 align="center"><img src="assets/icon.png" alt="DualSense Manager Icon" width="28" height="28"> DualSense Client</h1>

<p align="center">
  <b>Unofficial DualSense Manager for Windows</b><br>
  Advanced customization and control of your PS5 DualSense controller with real-time monitoring and custom profiles.
</p>

---

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows-blue?logo=windows" alt="Platform">
  <a href="https://github.com/shazzaam7/DualSenseClient/blob/main/LICENSE"><img src="https://img.shields.io/github/license/shazzaam7/DualSenseClient?color=green" alt="License"></a>
  <a href="https://github.com/shazzaam7/DualSenseClient/actions/workflows/builder.yml"><img src="https://img.shields.io/github/actions/workflow/status/shazzaam7/DualSenseClient/builder.yml?label=Build&logo=github" alt="Build Status"></a>
</p>

<p align="center">
  <a href="https://github.com/shazzaam7/DualSenseClient/releases/latest"><img src="https://img.shields.io/github/v/release/shazzaam7/DualSenseClient?label=Latest%20Release" alt="Latest Release"></a>
  <a href="https://github.com/shazzaam7/DualSenseClient/releases/tag/nightly"><img src="https://img.shields.io/github/v/release/shazzaam7/DualSenseClient?include_prereleases&label=Pre-Release&color=orange" alt="Pre-Release"></a>
</p>

<p align="center">
  <a href="https://ko-fi.com/shazzaam">
    <img src="https://ko-fi.com/img/githubbutton_sm.svg" alt="Support me on Ko-fi">
  </a>
</p>

<p align="center">
  <img src="assets/Screenshot.png" alt="Program Screenshot">
</p>

---

## 🧾 Overview

**DualSenseClient** is a comprehensive open-source management tool for the **PlayStation 5 DualSense Controller** on Windows. It delivers deep customization, real-time monitoring, and advanced virtual controller emulation capabilities to enhance your gaming experience. Built with modern .NET technology and a sleek Avalonia UI, it provides an intuitive interface for full control of your DualSense controller's features.

---

## 🌟 Features

### 🎨 Light Control

- Full RGB lightbar control with presets and custom colors
- Player LED control (1-5) with brightness options
- Microphone LED configuration (off/on/pulse)
- Quick light presets and reset functions

### 🕹️ Virtual Controller Emulation

- Xbox 360 and DualShock 4 controller emulation using ViGEmBus
- Advanced haptic feedback with custom rumble configuration
- Adjustable trigger thresholds for adaptive responses
- DS4 lightbar handling and management options

### 🔒 Controller Hiding

- HidHide integration to hide physical controller from other applications
- Advanced driver status monitoring and configuration

### 📁 Profile Management

- Create, edit, and manage multiple controller profiles
- Import/export profiles (JSON format)
- Profile renaming and duplication
- Assign profiles to specific controllers

### ⚡ Special Actions

- Custom button combinations for special functions
- Battery level indicators (via lightbar/LEDs)
- Controller disconnection via button combos

### 📊 Real-time Monitoring

- Monitor all controller inputs and states
- View touchpad, motion sensor, and battery data
- Track connection status and LED states

### 🎮 Device Management

- Support for multiple DualSense controllers
- Automatic MAC address-based identification
- Connection type detection (USB/Bluetooth)

### ⚙️ Settings & Configuration

- Theme selection and UI customization
- Minimize to tray and start minimized options
- Adjustable logging levels
- Tray battery tracking

---

## 📚 Libraries Used

### 🖥️ User Interface

- [Avalonia](https://avaloniaui.net/) — Cross-platform .NET UI framework
- [Fluent Avalonia](https://github.com/amwx/FluentAvalonia) — Fluent Design System for Avalonia
- [Fluent Icons](https://github.com/davidxuang/FluentIcons) — Fluent icon set for modern interfaces

### ⚙️ Functionality

- [HidSharp](https://github.com/SeekHisKingdom/HIDSharp) — Cross-platform HID device access
- [NLog](https://github.com/NLog/NLog) — Flexible and high-performance logging library
- [ViGEmBus](https://github.com/nefarius/ViGEmBus) — Virtual gamepad emulation
- [HidHide](https://github.com/nefarius/HidHide) — HID device hiding functionality

---

## 🚀 Getting Started

### Prerequisites

- **Windows 10 version 1909 or later** (Windows 11 recommended)
- **.NET 9.0 Desktop Runtime** or later [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)
- **PlayStation 5 DualSense controller** (wired USB or Bluetooth connection)
- **Administrator privileges** (for ViGEmBus and HidHide driver installation)

### Installation

1. Download the latest release from the [releases page](https://github.com/shazzaam7/DualSenseClient/releases)
2. Extract the archive to your preferred location
3. **Run as Administrator** (right-click `DualSenseClient.exe` → "Run as administrator") for full functionality
4. Connect your DualSense controller and start customizing

### Quick Start

1. Connect your DualSense controller to your PC (via USB or Bluetooth)
2. Navigate to the Profile page to customize your settings
3. Create and apply your first profile or adjust individual settings

---

## 🛠️ Troubleshooting

### Common Issues

- **Controller not detected**: Ensure you have the latest Sony DualSense firmware and try connecting via USB instead of Bluetooth
- **Virtual controller not working**: Make sure ViGEmBus driver is installed
- **HidHide not functioning**: Run the application as administrator and ensure HidHide service is properly installed
- **Missing .NET dependencies**: Install the required .NET Desktop Runtime from Microsoft's website
- **Bluetooth connection issues**: Restart the Bluetooth service or pair the controller again through Windows settings

### Driver Installation

For virtual controller emulation to work properly, you may need to manually install these drivers:

- **ViGEmBus**: Required for virtual Xbox 360/DualShock 4 controller emulation
- **HidHide**: Required for hiding the physical controller from certain applications (Steam)

Both drivers require administrator privileges for installation and operation.

---

## 🙌 Credits

- [DualSense Controller Mockup](https://www.titanui.com/106136-ps5-dualsense-controller-vector-illustration-figma) — Mockup design for DualSense controller
- [DualSense Controllers Fandom](https://controllers.fandom.com/wiki/Sony_DualSense) — Detailed documentation and reference
- [DS4Windows](https://github.com/Ryochan7/DS4Windows/), [DualSenseY](https://github.com/WujekFoliarz/DualSenseY-v2) ,[Wujek DualSense API](https://github.com/ThreeDeeJay/Wujek-Dualsense-API) & [DualSense API](https://github.com/BadMagic100/DualSenseAPI/) — API implementation for DualSense controller functionality in C#
- [ViGEm Project](https://docs.nefarius.at/projects/ViGEm/) — Virtual gamepad emulation technology (Xbox 360/DualShock 4)
- [HidHide](https://docs.nefarius.at/projects/HidHide/) — HID device filtering and hiding utilities

---

## ⚠️ Disclaimer

> This project is not affiliated with or endorsed by **Sony Interactive Entertainment**.
> "PlayStation", "DualSense", and related marks are trademarks of their respective owners.

---

## 🛠️ Contributing

Pull requests are welcome! If you'd like to improve this tool or report bugs, feel free to open an issue or start a discussion.

To contribute:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a pull request

---

## 📄 License

Released under the [BSD-3 License](LICENSE).

<p align="center">
  <b> Made with ❤️ for DualSense fans worldwide</b>
</p>
