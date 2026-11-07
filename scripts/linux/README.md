# Linux controller session launcher

Install Python 3 and BlueZ, pair your controller, then run:

```sh
python3 /absolute/path/controller-session.py AA:BB:CC:DD:EE:FF -- /absolute/path/DualSenseClient
```

Replace the address and paths. An executable AppImage also works. Enable the
client's Start in tray setting if desired. Close other copies first.

The watcher starts the client on connection (including already connected at
startup), waits for three disconnected samples before stopping it, and ignores
failed BlueZ queries. It stops only its own child. A manually closed client stays
closed until the next connection. SIGTERM is followed by SIGKILL after ten seconds
if needed; save settings before disconnecting.

For desktop login startup, save this as
`~/.config/systemd/user/dualsense-session.service`, replacing paths and address:

```ini
[Unit]
Description=DualSense Bluetooth session launcher
After=graphical-session.target
PartOf=graphical-session.target

[Service]
ExecStart=/usr/bin/python3 /absolute/path/controller-session.py AA:BB:CC:DD:EE:FF -- /absolute/path/DualSenseClient
Restart=on-failure
RestartSec=5

[Install]
WantedBy=graphical-session.target
```

Run `systemctl --user daemon-reload`, then
`systemctl --user enable --now dualsense-session.service` in your desktop session.
The desktop must import its DISPLAY/WAYLAND_DISPLAY environment into the user
service manager. Inspect logs with `journalctl --user -u dualsense-session.service`.
Disable with `systemctl --user disable --now dualsense-session.service`.

This ports the connection lifecycle from rwflan/dualsense-session-launcher;
it does not enable Linux virtual controller emulation. Hardware verification is
required on the target Linux desktop.
