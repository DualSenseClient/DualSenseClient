# Linux controller launch verification — 8 November 2026

The GUI setting now owns Linux registration. The old standalone watcher instructions
were not GUI integration. Windows continues to use its existing current-user task.

## Automated checks

- Release GUI and opt-in desktop-probe builds passed without warnings/errors.
- A self-contained Linux x64 single-file publish succeeded on the Chromebox.
- 21 focused .NET launch/persistence tests passed on both the Mac and Chromebox:
  `dotnet test tests/DualSenseClient.Tests -c Release --filter 'FullyQualifiedName~ControllerLaunch|FullyQualifiedName~JsonFileStore'`.
- Five Python tests passed on both machines:
  `python3 scripts/linux/test_controller_session.py`.
- These cover controller filtering, registration/removal, rollback after a failed
  update, checked settings persistence, executable/AppImage resolution, quoting,
  the user instance lock, disconnect/reconnect retention, explicit process exit,
  multiple controller addresses, failed BlueZ queries and desktop environment refresh.
- A broader macOS run had 1,845 passes, 35 skips and five failures in audio-process
  discovery, Windows handle tests and unavailable macOS HIDAPI. It was not a green
  cross-platform full-suite claim. The target-specific checks above passed.

## Installed desktop evidence

The opt-in `tests/DualSenseClient.LinuxVerification` probe runs the actual app,
Settings ViewModel and tray Exit handler, rather than reproducing registration in
an unrelated shell script. Its temporary verification installation was
`/home/syferus/Applications/DualSenseClient-integration`.

- Enable: `TOGGLE visible=True value=True saved=True error=`. The managed systemd
  service was enabled/active, and the XDG desktop entry existed.
- Disable: `TOGGLE visible=True value=False saved=False error=`. The unit became
  unavailable/disabled and the desktop entry was removed.
- Simulated unavailable `systemctl`: the toggle and saved value stayed `True`, and
  an error named the missing command. The working service was preserved.
- Startup: `STARTUP startInTray=True mainWindowVisible=False`.
- The same production tray Exit handler completed and logged application shutdown.
- The compiled Linux Settings page was rendered to `controller-launch-setting.png`.
  This is UI evidence from the desktop probe; it is not a screenshot of a tray icon.

Final deployment uses the original installation path:

- Executable: `/home/syferus/Applications/DualSenseClient/DualSenseClient`
- Settings: `/home/syferus/Applications/DualSenseClient/Config/config.json`
- Unit: `~/.config/systemd/user/dualsense-controller-launch.service`
- Session entry: `~/.config/autostart/dualsense-controller-launch.desktop`
- Bundled watcher/configuration: `~/.local/share/DualSenseClient/controller-launch/`

The published executable was built from code revision `63c7fda`. Its saved
`startInTray` and `launchOnControllerConnect` were both true; its launch configuration
pointed to that exact executable with `--controller-connect`. The old
`dualsense-session@*.service` instances were disabled. Pairing and existing app
settings were preserved; the previous installation was copied to
`/home/syferus/Applications/DualSenseClient.pre-linux-integration`.

The published app logged `Start in tray applied; main window visible: False`.
A second `--controller-connect` invocation through `systemd-run --user --wait`
finished successfully in 40 ms. The original PID remained 14323 before and after,
with no second production app instance. This verifies the lock across desktop and
systemd sessions and the connection invocation's early exit without window activation.

## Scope of physical verification

The user explicitly accepted the earlier pairing and on/off checks and declined to
repeat physical button tests for this revision. Accordingly, fresh controller
connection/disconnection/reconnection and deliberate input are **not newly claimed**
here. New tests cover their software state transitions; the earlier hardware evidence
remains separate. A real reboot/sign-in cycle and visible tray-icon rendering were
not performed or asserted. AppImage command resolution is tested; a live AppImage
sign-in cycle was not performed.
