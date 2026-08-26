# FAQ

Frequently asked questions about DualSense Client. If your question is not answered here, check [Troubleshooting](troubleshooting.md) or [open an issue](https://github.com/DualSenseClient/DualSenseClient/issues).

## General

### Is DualSense Client free?

Yes. It is open source under the [GPL-3.0 license](https://github.com/DualSenseClient/DualSenseClient/blob/main/LICENSE).

### Which controllers are supported?

The PlayStation 5 **DualSense** and **DualSense Edge** controllers, over USB and Bluetooth. Other controllers (such as the DualShock 4) are not supported as physical devices. See [Supported Controllers](index.md#supported-controllers).

!!! note
    DualSense Edge support (Fn buttons and paddles) is implemented but not yet verified on real hardware.

### Can I use multiple controllers at once?

Yes. The app supports multiple connected controllers, with an in-window controller picker for switching between them. Profiles can be bound to specific controllers by MAC address so each one gets its own settings automatically.

### Is it safe for my controller?

The app uses the controller's standard HID protocol — the same interfaces used by games and official software on the PS5. That said, this is unofficial software provided without warranty of any kind under its license.

---

## Installation

### Do I need to install drivers?

No driver is required just to connect a controller over USB or Bluetooth — HID access is provided through SDL3/hidapi. Optional components that do need drivers or packages:

- **[HidHide](https://github.com/nefarius/HidHide)** (Windows only) for [controller hiding](guides/controller-hiding.md)
- **[USB/IP](https://github.com/vadimgrn/usbip-win2)** for [virtual controller emulation](guides/virtual-controller.md)

### Why does Windows need the .NET runtime?

The Windows build is framework-dependent to keep downloads small. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). The Linux build is self-contained and needs nothing extra.

### Does the app need administrator/root privileges?

Normally no:

- On **Linux**, install the [udev rule](installation.md#linux-controller-access-without-root) instead of running as root.
- On **Windows**, the app itself runs unprivileged; only installing the optional HidHide/USB/IP drivers requires admin (once per driver).

---

## Features

### Can I play game audio through the controller?

Yes — audio can be played to your desktop speakers, the DualSense speaker, or the haptics, each independently. See the [Audio Playback guide](guides/audio.md).

### Can the app update my controller's firmware?

No. The app can _display_ firmware versions on the [Device Information](guides/device-info.md) page, but firmware updates must be performed through Sony's official tools.

### How does virtual controller emulation help in games?

Games that only recognize certain controller types (or that handle rumble/trigger feedback better for one type) can be given an emulated Xbox 360, DualShock 4, or DualSense device that mirrors your physical controller. See the [Virtual Controller guide](guides/virtual-controller.md).

### My game sees two controllers / inputs are doubled

Enable [controller hiding](guides/controller-hiding.md) so games only see the virtual controller while the app keeps access to the real one.

### What are Special Actions?

Button combinations (exact match) or single-finger touchpad swipes that trigger effects: disconnect over Bluetooth, set the lightbar/LEDs, play a sound (speaker or headset, with optional haptics), or show the battery level as 10 lightbar colors. Each action is enabled per controller and has its own hold time, duration, and apply-while-held behavior. See the [Special Actions guide](guides/special-actions.md).

### What themes are available?

System (follows the OS), Light, Dark, Amoled (true black for OLED), and Playstation (deep blue PS5-style accent). They are listed on the [Settings](guides/settings.md) page.

### Can I control the app without opening the window?

Yes. The tray icon has per-controller submenus to select the active controller, switch its profile, change the virtual controller mode, and disconnect Bluetooth controllers. With **Close to tray** and **Start in tray**, the app can run entirely from the tray — see [Settings](guides/settings.md#window-tray).

### Can I rename my controller or change its illustration?

Yes — both are on the [Device Information](guides/device-info.md) page. The name and skin are stored per controller (by MAC address with an HID-path fallback).

### How does button remapping work?

On the [Virtual Controller](guides/virtual-controller.md) page, select source buttons on the illustrated controller (including Edge paddles on a DualSense Edge), pick one or more virtual targets, optionally choose the trigger output mode for single L2/R2, and assign. Multiple targets can be pressed together; use **None** to suppress a source button.

---

## Troubleshooting

My problem isn't listed here — where do I start?

Head to the [Troubleshooting](troubleshooting.md) page. If nothing helps, please [open an issue](https://github.com/DualSenseClient/DualSenseClient/issues) and attach your log file (see [Logs](troubleshooting.md#logs)).

