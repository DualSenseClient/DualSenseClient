"""Watch one paired BlueZ controller and launch the client on connection, leaving it running."""
import argparse
import re
import signal
import subprocess
import threading


def connected(address):
    result = subprocess.run(['bluetoothctl', 'info', address], capture_output=True, text=True, timeout=10)
    if result.returncode:
        raise RuntimeError('BlueZ query failed')
    return bool(re.search(r'^\s*Connected:\s+yes\s*$', result.stdout, re.MULTILINE))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('address')
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not re.fullmatch(r'(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}', args.address) or not command:
        parser.error('Supply a MAC address followed by -- and the client executable')
    stop = threading.Event()
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, lambda *_: stop.set())
    child = None
    active = False
    while not stop.is_set():
        try:
            current = connected(args.address)
        except (OSError, RuntimeError, subprocess.TimeoutExpired):
            stop.wait(2)
            continue
        if current and not active and (child is None or child.poll() is not None):
            child = subprocess.Popen(command)
        active = current
        stop.wait(2)


if __name__ == '__main__':
    main()
