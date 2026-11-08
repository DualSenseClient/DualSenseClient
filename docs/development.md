# Development

This page covers building DualSense Client from source. For coding standards, project structure, themes, and translations, see the full [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md).

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet restore
dotnet build
dotnet test
```

Releases are produced automatically by CI for Windows (zip) and Linux (zip and AppImage).

## Hardware smoke tests

The default `dotnet test` run uses virtual devices only. The opt-in suites below exercise real hardware or the real USB/IP attach path and are skipped unless their environment variable is set. Run them from the repository root with the controller connected and the application closed, so the test process owns the HID handles.

### Physical controller

`DualSenseHardwareSmokeTests` probes the real feature reports (`0x05`, `0x09`, `0x20`, including the Bluetooth CRCs), waits for a live input report (`0x01`), checks the descriptor-sized output transfer, and temporarily writes a lightbar color before restoring the previous one.

```powershell
$env:DUALSENSE_HARDWARE = "1"
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseHardwareSmokeTests"
```

```bash
DUALSENSE_HARDWARE=1 dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseHardwareSmokeTests"
```

The suite accepts a DualSense or DualSense Edge over USB or Bluetooth. It is ignored when no physical controller is found.

`DualSenseBluetoothAudioHardwareTests` additionally exercises the Bluetooth audio lane with silent Opus/haptics frames and verifies trigger-effect readback through the input report. It requires a Bluetooth DualSense and confirms standalone `0x31` output, the state block embedded in `0x36`, and standalone `0x31` while the audio lane is active.

```powershell
$env:DUALSENSE_HARDWARE = "1"
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseBluetoothAudioHardwareTests"
```

These tests can verify protocol acceptance and controller state readback, but not audible speaker/microphone output or felt haptics. For those, check manually over Bluetooth:

1. Play known stereo audio to the controller speaker and to a headset, and confirm the selected route is audible without dropouts.
2. Enable haptics and play a known haptics clip; confirm both grips respond and rumble remains independent.
3. Record from the controller microphone and toggle mute; confirm audio and the mute LED follow the application.
4. While audio is streaming, change the lightbar color and an adaptive-trigger effect; confirm both are applied without interrupting the audio.
5. Stop playback and confirm the last trigger and lightbar state settles cleanly.

### USB/IP emulation

`DualSenseEndToEndTests` starts the virtual DualSense, attaches it through the production `usbip.exe` strategy, and verifies enumeration, input, output, feature reports, and detach for both the base and Edge personas. It requires a working [usbip-win2](https://github.com/vadimgrn/usbip-win2) installation.

```powershell
$env:EMULATION_INTEGRATION = "1"
dotnet test tests/DualSenseClient.Tests/DualSenseClient.Tests.csproj --filter "FullyQualifiedName~DualSenseEndToEndTests"
```

Unlike the hardware suites, an attach failure is reported as a test failure because the tests are explicitly opted in.

## Project Structure

The solution follows a multi-project layout: each distinct concern lives in its own library project under `source/`, named `DualSenseClient.<Area>` (for example `DualSenseClient.Core`, `DualSenseClient.Hid`, `DualSenseClient.Settings`). **DualSenseClient.GUI** is the main application project containing Views, ViewModels, and UI-related logic.

Tests live in `tests/DualSenseClient.Tests/`, organized into one folder per source project.

## Contributing

Pull requests are welcome! Before submitting:

1. Read the [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md)
2. Create a branch (`feature/...`, `bugfix/...`, `refactor/...`, or `docs/...`)
3. Run the linter:

    ```bash
    python scripts/lint.py --check
    ```

4. Open a pull request targeting the `dev` branch with a clear description of what changed, why, and how you tested it

For bug reports and feature requests, [open an issue](https://github.com/DualSenseClient/DualSenseClient/issues) instead.

## Translations & Themes

- **Translating**: add a language file under `source/DualSenseClient.GUI/Resources/Language/` — instructions in the [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md#translating)
- **Custom themes**: copy the theme template and register it — instructions in the [Contributing Guide](https://github.com/DualSenseClient/DualSenseClient/blob/main/docs/CONTRIBUTING.md#creating-custom-themes)

These documentation pages are built with [Zensical](https://zensical.org/) from the Markdown files in `docs/` and deployed to GitHub Pages. To preview them locally:

```bash
pip install -r requirements.txt
zensical serve
```

