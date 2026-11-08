# Launch on Bluetooth connection

You can use your operating system's automation tools to open DualSense Client when
a paired controller connects. This is optional, manual configuration; the app does
not install tasks, services, desktop entries or Bluetooth rules for you.

Enable **Start in tray** in the app if you want connection-triggered launches to
stay hidden. Disconnecting a controller should leave the app running. Avoid
launch commands that restore an existing window or start another instance.

These examples assume the controller is already paired and works in the app.
They do not change pairing or start the app at sign-in.

## Windows: Task Scheduler

1. Find the controller's Bluetooth address on the app's **Device Info** page.
2. Open Event Viewer and enable **Applications and Services Logs → Microsoft →
   Windows → Bluetooth-Policy → Operational** if it is disabled. Enabling the log
   may require administrator approval; ordinary task launches should not.
3. Reconnect the controller and find its connection event. On supported Windows
   versions this is event **9**, with a `bthAddr` field. Confirm the event on your
   own machine rather than assuming all adapters and versions log the same event.
4. Create a Task Scheduler task for your current user, using **Run only when user
   is logged on**, without **Run with highest privileges**. Add an event trigger
   filtered to that connection event and the controller's address. `bthAddr` uses
   the decimal representation of the hexadecimal Bluetooth address.
5. Set the action to a launcher that checks whether this installation of DualSense
   Client is already running before starting it. A direct executable action can
   create duplicates on reconnect. The separately maintained
   [Windows session launcher](https://github.com/rwflan/dualsense-session-launcher)
   is one option; review its source and installation instructions before use.
   Do not add a sign-in trigger if you only want connection-triggered launches.
6. Set **If the task is already running** to **Do not start a new instance**, and
   remove the default time limit if your chosen launcher remains running. Note
   that this setting alone does not prevent duplicates after a launcher exits.

Test by connecting, disconnecting and reconnecting. The app should launch once,
remain running through disconnect, and remain hidden on reconnect if it was
hidden. Exit the app from its tray menu, reconnect again, and check that it starts.
To undo the automation, disable or delete the task. Leave the shared event log
alone if other tasks use it.

## Linux: choose an approach for your desktop

Bluetooth stacks, desktop sessions and service managers vary. The example below
uses Python 3 and BlueZ's `bluetoothctl`; check both are installed first. It does
not require a particular display server or systemd, and does not install itself.
Run it from a terminal in the graphical session where the app should appear.
That session supplies the appropriate Xorg or Wayland environment. A headless
shell may not have the display/session variables needed by the GUI.

Save the following as `controller-launch.py`. Replace `ADDRESS` with the paired
controller address and `COMMAND` with the full executable path (or an executable
AppImage's permanent path). Keep arguments as separate list items so paths with
spaces work. Close other copies of the app before starting this example.

```python
import re
import subprocess
import time

ADDRESS = "AA:BB:CC:DD:EE:FF"
COMMAND = ["/absolute/path/to/DualSenseClient"]


def is_connected():
    result = subprocess.run(
        ["bluetoothctl", "info", ADDRESS],
        capture_output=True, text=True, timeout=10, check=True,
    )
    return bool(re.search(r"^\s*Connected:\s+yes\s*$", result.stdout, re.MULTILINE))


def main():
    child = None
    was_connected = False
    while True:
        try:
            connected = is_connected()
        except (OSError, subprocess.SubprocessError) as error:
            print(f"Bluetooth query failed: {error}", flush=True)
            time.sleep(2)
            continue

        if connected and not was_connected and (child is None or child.poll() is not None):
            try:
                child = subprocess.Popen(COMMAND)
            except OSError as error:
                print(f"Could not launch the app: {error}", flush=True)
                time.sleep(2)
                continue
        was_connected = connected
        time.sleep(2)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
```

Run `python3 /path/to/controller-launch.py`. It checks every two seconds and
launches on connection, including a controller connected when the watcher starts.
It leaves the app running after disconnect or when you stop the watcher. After
exiting the app, disconnect/reconnect the controller to launch it again.

This small example tracks only the child it started. Run one watcher, and avoid
starting another copy of the app manually while it runs. Restarting the watcher
while the app is running can also start another copy. If you need a persistent
service or broader single-instance handling, adapt the example to your desktop
and service manager, including its graphical-session environment and shutdown
behavior. No login or service configuration is provided here.

## Verify your configuration

Check normal and tray startup separately, then confirm disconnect keeps the same
app process alive, reconnect does not duplicate it or show its window, and exiting
allows a subsequent connection to launch again. Check actual stick/button input
in **Device Info → Input Data**. Task/service status alone does not verify that the
app can read the controller. Advanced feedback still depends on the game and the
app's controller/emulation configuration.
