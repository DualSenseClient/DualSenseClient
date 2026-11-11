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
2. Open Event Viewer. Expand **Applications and Services Logs → Microsoft →
   Windows → Bluetooth-Policy**, right-click **Operational**, select **Properties**,
   tick **Enable logging**, then click **OK**. Enabling the log may require
   administrator approval; ordinary task launches should not.
3. Reconnect the controller and find its connection event. On supported Windows
   versions this is event **9**, with a `bthAddr` field. Confirm the event on your
   own machine rather than assuming all adapters and versions log the same event.
   Under **Details → XML View**, check that `bthAddr` matches your controller.
   It uses the decimal representation of the hexadecimal Bluetooth address.
   For example, this PowerShell command converts `AA:BB:CC:DD:EE:FF` to
   `187723572702975`; replace the address with your own:

   ```powershell
   [Convert]::ToUInt64(('AA:BB:CC:DD:EE:FF' -replace '[:-]', ''), 16)
   ```

   If no matching event is logged, this trigger cannot be used as written.

### Prepare the launch action

Save this as `controller-launch.ps1` in a permanent folder, and replace `$exe` with
the full path to your installed app. It checks for that executable in the current
Windows session, so reconnecting leaves an existing window alone.

```powershell
$exe = 'C:\path\to\DualSenseClient.exe'
$sessionId = (Get-Process -Id $PID).SessionId
$running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exe)) -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -eq $sessionId -and $_.Path -eq $exe }
if (-not $running) {
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
}
```

Test it with the app closed, then again with the app open to check that it does
not create another instance. Replace the script path in this command:

```text
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "C:\path\to\controller-launch.ps1"
```

`-ExecutionPolicy RemoteSigned` allows this locally saved script for that process;
it does not change your saved execution policy. Managed policies can still take
precedence. The script passes no arguments that override **Start in tray**.

### Create the task

1. Open Task Scheduler and select **Create Task**. Give it a name such as
   `DualSense Client on connection`. On **General**, select your current user and
   **Run only when user is logged on**. Leave **Run with highest privileges** off.
2. On **Triggers**, click **New**. Set **Begin the task** to **On an event**, select
   **Custom**, then **New Event Filter**. Open **XML**, tick **Edit query manually**
   and paste the following. Replace `187723572702975` with your controller's
   decimal `bthAddr`. Click **OK** to save the filter, then **OK** to save the trigger.

   ```xml
   <QueryList>
     <Query Id="0" Path="Microsoft-Windows-Bluetooth-Policy/Operational">
       <Select Path="Microsoft-Windows-Bluetooth-Policy/Operational">
         *[System[Provider[@Name='Microsoft-Windows-Bluetooth-Policy'] and EventID=9]]
         and *[EventData[Data[@Name='bthAddr']='187723572702975']]
       </Select>
     </Query>
   </QueryList>
   ```

3. On **Actions**, click **New**, select **Start a program** and enter
   `powershell.exe` in **Program/script**. In **Add arguments**, enter the following,
   replacing the script path:

   ```text
   -NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "C:\path\to\controller-launch.ps1"
   ```

4. On **Conditions**, clear **Start the task only if the computer is on AC power**
   if you also want it to work on battery. Leave idle and network requirements off.
5. On **Settings**, set **If the task is already running** to **Do not start a new
   instance**. Save the task. Do not add a sign-in or disconnect trigger.
6. Right-click the task and select **Run** to check the saved action. Then test
   actual controller connections. **Do not start a new instance** prevents
   overlapping script runs; the script's process check prevents duplicate app
   launches after the script exits.

Test by connecting, disconnecting and reconnecting. The app should launch once,
remain running through disconnect, and remain hidden on reconnect if it was
hidden. Exit the app from its tray menu, reconnect again, and check that it starts.
To undo the automation, disable or delete the task. Leave the shared event log
alone if other tasks use it.

## Linux: BlueZ connection events

This example listens for BlueZ D-Bus connection signals instead of polling or
repeatedly running `bluetoothctl`. It uses Python with `dbus-python` and PyGObject's
GLib bindings. Install these through your distribution's package manager; on
Debian/Ubuntu the packages are `python3-dbus` and `python3-gi`. Check that the
interpreter you run can import `dbus` and `gi`. BlueZ must already be running,
and your user must be able to read its system-bus device data.

Run it from a terminal in the graphical session where the app should appear.
That session supplies the display environment needed by the GUI. The Linux
client uses X11; in a Wayland session, Xwayland must be available and `DISPLAY`
must point to it. `WAYLAND_DISPLAY` alone is not sufficient. A headless shell
may not have the display/session variables needed by the GUI.

Save the following as `controller-launch.py`. Replace `ADDRESS` with the paired
controller address and `COMMAND` with the full executable path (or an executable
AppImage's permanent path). Keep arguments as separate list items so paths with
spaces work. Close other copies of the app and temporarily stop any other
controller-connection watcher before starting this example. Restore the previous
watcher after you finish testing and stop this example.

```python
import subprocess

import dbus
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

ADDRESS = "AA:BB:CC:DD:EE:FF"
COMMAND = ["/absolute/path/to/DualSenseClient"]
DEVICE = "org.bluez.Device1"
DEVICE_SUFFIX = "/dev_" + ADDRESS.upper().replace(":", "_")
connected_paths = set()
child = None


def set_connected(path, connected):
    global child
    if not str(path).endswith(DEVICE_SUFFIX):
        return
    was_connected = bool(connected_paths)
    if connected:
        connected_paths.add(path)
    else:
        connected_paths.discard(path)
    if connected_paths and not was_connected and (child is None or child.poll() is not None):
        try:
            child = subprocess.Popen(COMMAND, start_new_session=True)
        except OSError as error:
            print(f"Could not launch the app: {error}", flush=True)


def properties_changed(interface, changed, invalidated, path=None):
    if interface == DEVICE and "Connected" in changed:
        set_connected(path, bool(changed["Connected"]))


def interfaces_added(path, interfaces):
    if DEVICE in interfaces:
        set_connected(path, bool(interfaces[DEVICE].get("Connected", False)))


def interfaces_removed(path, interfaces):
    if DEVICE in interfaces:
        set_connected(path, False)


def main():
    DBusGMainLoop(set_as_default=True)
    bus = dbus.SystemBus()
    bus.add_signal_receiver(
        properties_changed, bus_name="org.bluez",
        dbus_interface="org.freedesktop.DBus.Properties",
        signal_name="PropertiesChanged", path_keyword="path", arg0=DEVICE,
    )
    for signal, callback in [
        ("InterfacesAdded", interfaces_added),
        ("InterfacesRemoved", interfaces_removed),
    ]:
        bus.add_signal_receiver(
            callback, bus_name="org.bluez",
            dbus_interface="org.freedesktop.DBus.ObjectManager", signal_name=signal,
        )
    manager = dbus.Interface(
        bus.get_object("org.bluez", "/"), "org.freedesktop.DBus.ObjectManager",
    )
    for path, interfaces in manager.GetManagedObjects().items():
        interfaces_added(path, interfaces)
    GLib.MainLoop().run()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
```

Run `python /path/to/controller-launch.py`. It reads the initial device list once,
then waits for BlueZ's `PropertiesChanged`, `InterfacesAdded` and
`InterfacesRemoved` signals. A matching controller connected when the watcher
starts also launches the app. There is no periodic Bluetooth query or timer.
See the [BlueZ Device API](https://github.com/bluez/bluez/blob/master/doc/org.bluez.Device.rst)
and [dbus-python signal documentation](https://dbus.freedesktop.org/doc/dbus-python/tutorial.html#receiving-signals)
for the underlying interfaces.

The app starts in its own session so Ctrl+C stops the watcher without also
interrupting the app. It leaves the app running after disconnect. After
exiting the app, disconnect/reconnect the controller to launch it again.
A failed launch is reported in the terminal; correct the executable path and
reconnect to retry.

This small example tracks only the child it started. Run one watcher, and avoid
starting another copy of the app manually while it runs. Restarting the watcher
while the app is running can also start another copy. Exit the app before
restarting the watcher. After restarting the Bluetooth service, check a real
disconnect/reconnect; if the watcher stops responding, exit the app and restart
the watcher. If you need a persistent service or broader
single-instance handling, adapt the example to your desktop
and service manager, including its graphical-session environment and shutdown
behavior. No login or service configuration is provided here.

## Verify your configuration

Check normal and tray startup separately, then confirm disconnect keeps the same
app process alive, reconnect does not duplicate it or show its window, and exiting
allows a subsequent connection to launch again. Check actual stick/button input
in **Device Info → Input Data**. Task/service status alone does not verify that the
app can read the controller. Advanced feedback still depends on the game and the
app's controller/emulation configuration.
