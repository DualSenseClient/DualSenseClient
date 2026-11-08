"""Watch one paired BlueZ controller and launch the client on connection, leaving it running."""
import argparse
import json
import os
import re
import signal
import subprocess
import threading


def connected(address):
    result = subprocess.run(['bluetoothctl', 'info', address], capture_output=True, text=True, timeout=10)
    if result.returncode:
        raise RuntimeError('BlueZ query failed')
    return bool(re.search(r'^\s*Connected:\s+yes\s*$', result.stdout, re.MULTILINE))


class ConnectionLauncher:
    """One launch per connection edge; an existing child survives disconnects."""
    def __init__(self, command, environment):
        self.command, self.environment = command, environment
        self.child = None
        self.active = set()

    def update(self, connected_addresses):
        if connected_addresses - self.active and (self.child is None or self.child.poll() is not None):
            self.child = subprocess.Popen(self.command, env=self.environment)
        self.active = connected_addresses


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config')
    parser.add_argument('--session-start', action='store_true')
    parser.add_argument('address', nargs='?')
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if args.session_start:
        names = [name for name in ('DISPLAY', 'WAYLAND_DISPLAY', 'XAUTHORITY',
                 'XDG_RUNTIME_DIR', 'DBUS_SESSION_BUS_ADDRESS', 'XDG_SESSION_TYPE')
                 if os.environ.get(name)]
        subprocess.run(['systemctl', '--user', 'import-environment', *names], check=True)
        subprocess.run(['systemctl', '--user', 'restart', 'dualsense-controller-launch.service'], check=True)
        return
    if args.config:
        with open(args.config, encoding='utf-8') as file:
            config = json.load(file)
        addresses, command = config['addresses'], config['command']
    else:
        addresses = [args.address]
        command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not addresses or any(not re.fullmatch(r'(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}', address or '') for address in addresses) or not command:
        parser.error('Supply paired controller addresses and a client command')
    # Never carry an AppImage mount or loader environment into a future launch.
    environment = {key: value for key, value in os.environ.items()
                   if key not in ('APPIMAGE', 'APPDIR', 'ARGV0', 'LD_LIBRARY_PATH', 'LD_PRELOAD')}
    stop = threading.Event()
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, lambda *_: stop.set())
    launcher = ConnectionLauncher(command, environment)
    while not stop.is_set():
        try:
            current = {address for address in addresses if connected(address)}
        except (OSError, RuntimeError, subprocess.TimeoutExpired):
            stop.wait(2)
            continue
        launcher.update(current)
        stop.wait(2)


if __name__ == '__main__':
    main()
