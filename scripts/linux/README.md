# Linux controller connection launch

Use **Settings → Window → Launch when a controller connects** in the installed
app. It applies immediately and uses the existing **Start in tray** preference.
See [the settings guide](../../docs/guides/settings.md#launch-when-a-controller-connects-windows-and-linux).

The GUI bundles this watcher as an embedded resource, copies it to the user's
application data directory, and configures its systemd user unit and XDG session
entry. It records paired controller addresses and the permanent executable or
AppImage command as JSON; no shell interpolation is used for the app command.
Disconnects do not stop the client. A running instance is protected by the app's
user-scoped lock. After Exit, the next connection can launch the app again.

The positional `controller-session.py ADDRESS -- EXECUTABLE` interface remains
available for existing manual setups, but the GUI's managed service replaces
this project's older `dualsense-session@*.service` instances on successful enable.

Automated watcher checks: `python3 scripts/linux/test_controller_session.py`.
The opt-in desktop probe in `tests/DualSenseClient.LinuxVerification` runs the
actual settings ViewModel and Exit handler on a Linux desktop, including service
registration and persistence. Run it only with the app closed, in a separate
installation containing a copy of your Config directory. It can capture the
compiled settings page and observe real controller button events. It does not
prove reboot behavior; perform that check separately.
