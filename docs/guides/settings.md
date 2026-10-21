# Settings

The **Settings** page controls updates, appearance, window behavior, notifications, and diagnostics. You can also access many of these options from the system tray without opening the main window.

## Updates

- **Release channel** — off follows stable releases; on follows nightly pre-release builds. The label shows the active channel.
- **Automatically check for updates** — checks once per day during startup when enabled.
- **Check for updates** — the button in the Settings header checks immediately.
- When an update is available, the same header button becomes **Update** and starts the download.

## UI

### Theme

| Theme | Description |
| ----- | ----------- |
| System | Follows the operating system's light/dark preference. |
| Light | Light backgrounds with dark text. |
| Dark | Dark backgrounds with light text. |
| AMOLED | True-black backgrounds that switch off pixels on OLED displays. |
| PlayStation | Deep blue-tinted surfaces with a PlayStation-blue accent, inspired by the PS5 home screen. |

The selected theme is applied immediately and persisted. Only the themes listed above are available — see [Creating Custom Themes](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md#creating-custom-themes) if you want to build one.

### Mica Backdrop

On Windows 11, **Mica backdrop** blends the window with the desktop. It is available for the System, Light, and Dark themes; the AMOLED and PlayStation themes use their own solid backgrounds and hide the option. The toggle has no effect on other platforms.

### Language

Choose the display language from the dropdown. The app currently ships in English; additional translations can be contributed — see [Translating](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md#translating).

![Settings page UI section](../assets/images/settings-appearance.png)

## Window & Tray

The app provides a system tray icon so it can run in the background.

| Setting | What it does |
| ------- | ------------ |
| **Close to tray** | Closing the window hides it to the tray instead of quitting. Reopen it from the tray menu. |
| **Start in tray** | Launch the app already hidden to the tray. |
| **Show battery percentage** | When enabled, the tray icon shows the active controller's battery as plain numbers. When disabled (or no controller has a known battery level), the app icon is shown instead, tinted with the current theme's accent color. |

### Tray Icon Menu

- **Show** — restores the main window.
- **Per-controller submenu** — each connected controller appears with its display name and battery percentage. Inside you can:
    - **Select** the active controller (radio item).
    - **Profiles** — switch the controller's assigned profile.
    - **Emulation** — switch the virtual controller mode (disabled while a virtual device is being created).
    - **Disconnect** — Bluetooth-only: disconnects the controller.
- When no controllers are connected, a disabled *No controllers connected* item is shown.
- **Exit** — quits the application.

![Tray icon menu](../assets/images/settings-tray-menu.png)

!!! tip
    You can change profiles and virtual controller modes entirely from the tray, without opening the main window — useful during a game.

## Notifications

Desktop notification popups appear even when the main window is hidden to the tray.

| Setting | What it does |
| ------- | ------------ |
| **Notifications** | Master switch for all desktop popups. |
| **Position** | Where popups appear: top/bottom left, center, or right, or middle left/right. |
| **Duration** | How long each popup stays visible (1 – 30 seconds). |
| **Connection notifications** | Show a popup when a controller connects or disconnects. |
| **Low battery notifications** | Show a popup when a controller battery runs low. |
| **Low battery threshold** | Battery percentage that triggers the low-battery popup (5 – 50). |
| **Charge notifications** | Show a popup when a charging controller reaches its target. |
| **Charge threshold** | Battery percentage that triggers the charge popup while charging (50 – 100). |
| **Send test notification** | Shows a sample popup with the current position and duration. |

## Diagnostics

### Log Level

Choose the minimum logging verbosity:

`Trace` → `Debug` → `Info` → `Warning` → `Error` → `Critical` → `None`

- The default is `Info`.
- Raising verbosity (e.g. to `Debug`) is useful when reporting a problem — see [Logs](../troubleshooting.md#logs) for where log files are stored.
- Changes apply immediately at runtime.

## Other Window Controls

Outside the Settings page, the title bar also exposes:

- A **controller picker** dropdown (connected controllers with their name and connection type — Bluetooth/USB) for switching which device the pages act on.
- A **Scan** toggle that starts/stops HID scanning for newly connected controllers.
- A **Disconnect** button (visible only for Bluetooth controllers) that disconnects the selected controller.

Connection, battery, charge, update, and special-action notices appear as desktop popups according to the notification settings above. Clicking an update popup opens the release page.

![Title-bar controller picker with Scan and Disconnect controls](../assets/images/settings-titlebar.png)
