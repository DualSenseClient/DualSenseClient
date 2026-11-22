#!/usr/bin/env python3
"""Root integration test for Linux controller hiding permissions.

Requires DUALSENSE_HIDING_INTEGRATION=1, root, a connected DualSense, and no
running DualSense Client or Steam instance holding the controller.
"""

import atexit
import errno
import glob
import grp
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
LINUX = ROOT / "extras" / "HidHideLinux"
BUILD_SCRIPT = ROOT / "scripts" / "build_linux_hiding.py"
RULE_TEMPLATE = LINUX / "assets" / "72-dualsense-hiding.rules"
ACL_RULE_TEMPLATE = LINUX / "assets" / "74-dualsense-hiding-acl.rules"
GROUP_NAME = "dualsenseclient"
TEST_RULE = Path("/etc/udev/rules.d/72-dualsense-hiding-test.rules")
TEST_ACL_RULE = Path("/etc/udev/rules.d/74-dualsense-hiding-test-acl.rules")
PRODUCTION_RULE = Path("/etc/udev/rules.d/72-dualsense-hiding.rules")
PRODUCTION_ACL_RULE = Path("/etc/udev/rules.d/74-dualsense-hiding-acl.rules")


def skip(message: str) -> None:
    print(f"SKIP: {message}")
    sys.exit(77)


def run(command: list[str], **kwargs) -> subprocess.CompletedProcess:
    return subprocess.run(command, check=True, text=True, **kwargs)


def ensure_group() -> None:
    result = subprocess.run(["getent", "group", GROUP_NAME], check=False)
    if result.returncode != 0:
        run(["groupadd", "--system", GROUP_NAME])


def read_uevent(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" in line:
            key, value = line.split("=", 1)
            values[key] = value
    return values


def find_controller() -> tuple[str, str, list[Path]]:
    for sys_path in sorted(glob.glob("/sys/class/hidraw/hidraw*")):
        uevent = read_uevent(Path(sys_path) / "device" / "uevent")
        hid_id = uevent.get("HID_ID", "")
        parts = hid_id.split(":")
        if len(parts) != 3:
            continue
        vendor = int(parts[1], 16)
        product = int(parts[2], 16)
        uniq = uevent.get("HID_UNIQ", "")
        if vendor != 0x054C or product not in (0x0CE6, 0x0DF2) or not uniq:
            continue

        name = Path(sys_path).name
        hid_device = Path(sys_path) / "device"
        nodes = [Path("/dev") / name]
        for input_dir in glob.glob(str(hid_device / "input" / "input*")):
            for child in os.listdir(input_dir):
                if child.startswith(("event", "js", "mouse")):
                    nodes.append(Path("/dev/input") / child)
        return uniq, name, [node for node in nodes if node.exists()]

    raise RuntimeError("no Sony DualSense hidraw device found")


class Client:
    """Tiny line client for the helper protocol."""

    def __init__(self, path: Path) -> None:
        self.socket = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        self.socket.settimeout(5)
        self.socket.connect(str(path))
        self.stream = self.socket.makefile("rwb", buffering=0)

    def request(self, line: str) -> str:
        self.stream.write((line + "\n").encode())
        response = self.stream.readline()
        if not response:
            raise RuntimeError(f"no response for {line}")
        return response.decode().rstrip("\r\n")

    def close(self) -> None:
        self.stream.close()
        self.socket.close()


def stat_state(path: Path) -> tuple[int, int, int]:
    status = path.stat()
    return status.st_mode & 0o7777, status.st_uid, status.st_gid


def acl_state(path: Path) -> str:
    return subprocess.check_output(["getfacl", "-p", str(path)], text=True)


def has_named_user_acl(path: Path) -> bool:
    for line in acl_state(path).splitlines():
        if line.startswith("user:") and not line.startswith("user::"):
            return True
    return False


def acl_report(nodes: list[Path], expected: dict[Path, str]) -> str:
    parts = []
    for node in nodes:
        actual = acl_state(node)
        if actual != expected[node]:
            parts.append(f"--- {node}\nEXPECTED:\n{expected[node]}ACTUAL:\n{actual}")
    return "\n".join(parts)


def wait_for(predicate, what: str, timeout: float = 5.0) -> None:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if predicate():
            return
        time.sleep(0.1)
    raise AssertionError(f"timed out waiting for {what}")


VICTIM_CODE = """
import os
import sys

fd = os.open(sys.argv[1], os.O_RDWR | os.O_NONBLOCK)
print("READY", flush=True)
sys.stdin.readline()
try:
    os.read(fd, 64)
    print("OK", flush=True)
except OSError as error:
    print(f"ERR {error.errno}", flush=True)
"""


def start_victim(path: Path) -> subprocess.Popen:
    return subprocess.Popen(
        [sys.executable, "-c", VICTIM_CODE, str(path)],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        text=True,
    )


def main() -> int:
    if os.environ.get("DUALSENSE_HIDING_INTEGRATION") != "1":
        skip("set DUALSENSE_HIDING_INTEGRATION=1 to run")
    if os.geteuid() != 0:
        skip("requires root")

    ensure_group()
    uniq, hidraw_name, nodes = find_controller()
    target_gid = grp.getgrnam(GROUP_NAME).gr_gid
    for node in nodes:
        if node.stat().st_gid == target_gid:
            raise RuntimeError(
                f"{node} is still owned by {GROUP_NAME}; unplug/replug the controller "
                "or run hiding-cleanup.sh before rerunning the integration test"
            )
    print(f"controller {uniq} ({hidraw_name}), {len(nodes)} node(s)")

    build_dir = Path(tempfile.mkdtemp(prefix="dualsense-hiding-integration-"))
    run([sys.executable, str(BUILD_SCRIPT), "--output", str(build_dir / "build")])
    helper = build_dir / "build" / "dualsense-hiding-helper"
    udev_check = build_dir / "build" / "udev-check.sh"
    acl_fix = build_dir / "build" / "dualsense-hiding-acl-fix"

    run_dir = Path("/run/dualsenseclient")
    run_dir.mkdir(parents=True, exist_ok=True)
    socket_path = run_dir / "hiding-test.sock"
    if socket_path.exists():
        socket_path.unlink()

    production_backups: list[tuple[Path, Path]] = []
    for production_rule in (PRODUCTION_RULE, PRODUCTION_ACL_RULE):
        backup = production_rule.with_name(f"{production_rule.name}.test-backup")
        if backup.exists() and not production_rule.exists():
            backup.replace(production_rule)
        if production_rule.exists():
            backup.unlink(missing_ok=True)
            production_rule.rename(backup)
            production_backups.append((production_rule, backup))

    if production_backups:
        print(f"temporarily disabling {len(production_backups)} installed hiding udev rule(s)")

    def restore_production_rules() -> None:
        changed = False
        for production_rule, backup in production_backups:
            if backup.exists() and not production_rule.exists():
                backup.replace(production_rule)
                changed = True
        if changed:
            subprocess.run(["udevadm", "control", "--reload-rules"], check=False)

    atexit.register(restore_production_rules)

    rule_text = RULE_TEMPLATE.read_text(encoding="utf-8").replace(
        "/usr/libexec/dualsenseclient/udev-check", str(udev_check)
    )
    TEST_RULE.write_text(rule_text, encoding="utf-8")
    acl_rule_text = ACL_RULE_TEMPLATE.read_text(encoding="utf-8")
    acl_rule_text = acl_rule_text.replace(
        "/usr/libexec/dualsenseclient/udev-check", str(udev_check)
    ).replace(
        "/usr/libexec/dualsenseclient/dualsense-hiding-acl-fix", str(acl_fix)
    )
    TEST_ACL_RULE.write_text(acl_rule_text, encoding="utf-8")
    run(["udevadm", "control", "--reload-rules"])

    env = os.environ.copy()
    env["HIDING_SYSFS_ROOT"] = "/sys"
    env["HIDING_DEV_ROOT"] = "/dev"
    env["HIDING_RUN_DIR"] = str(run_dir)
    env["HIDING_SOCKET"] = str(socket_path)

    original_stat = {node: stat_state(node) for node in nodes}
    original_acl = {node: acl_state(node) for node in nodes}

    process = subprocess.Popen(
        [str(helper)],
        env=env,
        stderr=subprocess.PIPE,
        text=True,
    )
    client: Client | None = None
    second_client: Client | None = None
    hidden = False
    failed = False
    client_fd: int | None = None
    victim: subprocess.Popen | None = None
    try:
        wait_for(socket_path.exists, "helper socket", 10)
        client = Client(socket_path)
        hello = client.request("HELLO 1")
        assert hello.startswith("OK 1 "), hello

        client_fd = os.open(nodes[0], os.O_RDWR | os.O_NONBLOCK)
        victim = start_victim(nodes[0])
        assert victim.stdout is not None
        assert victim.stdout.readline().strip() == "READY"

        target_gid = grp.getgrnam(GROUP_NAME).gr_gid
        hide = client.request(f"HIDE {uniq}")
        assert hide == "OK", hide
        hidden = True

        wait_for(
            lambda: all(stat_state(node) == (0o660, 0, target_gid) for node in nodes),
            "group-only controller permissions",
        )
        wait_for(
            lambda: all(not has_named_user_acl(node) for node in nodes),
            "named-user ACL removal",
            10,
        )

        try:
            os.read(client_fd, 64)
        except OSError as error:
            if error.errno == errno.ENODEV:
                raise AssertionError("the app's own fd was revoked") from error
            if error.errno != errno.EAGAIN:
                raise

        assert victim.stdin is not None
        assert victim.stdout is not None
        victim.stdin.write("\n")
        victim.stdin.flush()
        victim_result = victim.stdout.readline().strip()
        assert victim_result == "ERR 19", victim_result
        victim.stdin.close()
        victim.wait(timeout=5)
        victim = None

        # Repeating HIDE for the same client must be idempotent.
        assert client.request(f"HIDE {uniq}") == "OK", "duplicate HIDE failed"

        # A second client can share the hide. The controller stays hidden until
        # both owners have unhidden it.
        second_client = Client(socket_path)
        assert second_client.request("HELLO 1").startswith("OK 1 ")
        assert second_client.request(f"HIDE {uniq}") == "OK", "second HIDE failed"

        assert client.request(f"UNHIDE {uniq}") == "OK", "first UNHIDE failed"
        wait_for(
            lambda: all(stat_state(node) == (0o660, 0, target_gid) for node in nodes),
            "shared hide to remain after the first owner unhides",
        )

        assert second_client.request(f"UNHIDE {uniq}") == "OK", "second UNHIDE failed"
        hidden = False

        wait_for(
            lambda: all(stat_state(node) == original_stat[node] for node in nodes),
            "restored controller permissions",
        )
        try:
            wait_for(
                lambda: all(acl_state(node) == original_acl[node] for node in nodes),
                "restored controller ACLs",
                10,
            )
        except AssertionError:
            print(acl_report(nodes, original_acl), file=sys.stderr)
            raise

        probe = start_victim(nodes[0])
        assert probe.stdout is not None
        assert probe.stdout.readline().strip() == "READY", "controller did not reopen after unhide"
        probe.stdin.close()
        probe.wait(timeout=5)
    except Exception:
        failed = True
        raise
    finally:
        if client_fd is not None:
            os.close(client_fd)
        if victim is not None:
            victim.kill()
            victim.wait(timeout=5)
        if hidden:
            for owning_client in (second_client, client):
                if owning_client is None:
                    continue
                try:
                    owning_client.request(f"UNHIDE {uniq}")
                except Exception:
                    pass
        if second_client is not None:
            second_client.close()
        if client is not None:
            client.close()
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)

        if failed and process.stderr is not None:
            helper_output = process.stderr.read()
            if helper_output:
                print("--- helper stderr ---", file=sys.stderr)
                print(helper_output, file=sys.stderr)

        if TEST_RULE.exists():
            TEST_RULE.unlink()
        if TEST_ACL_RULE.exists():
            TEST_ACL_RULE.unlink()
        restore_production_rules()

        hidden_dir = run_dir / "hidden"
        markers = hidden_dir.glob("*") if hidden_dir.exists() else []
        for marker in markers:
            marker.unlink()

        for node, (mode, uid, gid) in original_stat.items():
            try:
                os.chown(node, uid, gid)
                os.chmod(node, mode)
            except FileNotFoundError:
                pass

        run(["udevadm", "control", "--reload-rules"])
        run(["udevadm", "trigger", "--action=change"] + [str(node) for node in nodes])
        if socket_path.exists():
            socket_path.unlink()

        shutil.rmtree(build_dir, ignore_errors=True)

    print("PASS: Linux hiding permissions")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
