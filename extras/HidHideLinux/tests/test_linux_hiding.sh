#!/bin/sh
set -u

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../../.." && pwd)
cd "$ROOT" || exit 1
PROJECT="$ROOT/extras/HidHideLinux"
SCRIPT="$PROJECT/scripts/udev-check.sh"
BUILD="$ROOT/scripts/build_linux_hiding.py"
CHECK="$ROOT/scripts/check_linux_hiding_glibc.py"
TMP=$(mktemp -d "${TMPDIR:-/tmp}/dualsense-hiding-test.XXXXXX")
HELPER_PID=
cleanup()
{
    if [ -n "$HELPER_PID" ]; then
        kill "$HELPER_PID" 2>/dev/null || true
        wait "$HELPER_PID" 2>/dev/null || true
    fi
    rm -rf "$TMP"
}
trap cleanup EXIT HUP INT TERM

fail()
{
    echo "FAIL: $*" >&2
    exit 1
}

run_python()
{
    if command -v uv >/dev/null 2>&1; then
        uv run python "$@"
    else
        python3 "$@"
    fi
}

assert_exit()
{
    expected=$1
    shift
    "$@"
    actual=$?
    if [ "$actual" -ne "$expected" ]; then
        fail "expected exit $expected, got $actual: $*"
    fi
}

assert_ok()
{
    expected=$1
    shift
    if "$@"; then
        actual=0
    else
        actual=$?
    fi
    if [ "$actual" -ne "$expected" ]; then
        fail "expected exit $expected, got $actual: $*"
    fi
}

SYS="$TMP/sys"
RUN="$TMP/run"
HIDRAW="$SYS/class/hidraw/hidraw2"
EVENT="$SYS/class/input/event8"

mkdir -p "$HIDRAW/device" "$EVENT/device" "$RUN/hidden"

printf 'HID_ID=0003:0000054C:00000CE6\nHID_UNIQ=d0:bc:c1:17:55:63\n' > "$HIDRAW/device/uevent"
printf 'PRODUCT=3/54c/ce6/8111\nUNIQ="d0:bc:c1:17:55:63"\n' > "$EVENT/device/uevent"

assert_ok 0 sh -n "$SCRIPT"

touch "$RUN/hidden/d0:bc:c1:17:55:63"
assert_ok 0 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"
assert_ok 0 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$EVENT"

# udev passes %p relative to sysfs, not as an absolute /sys path.
RELATIVE="$SYS/devices/pci-test/0003:054C:0CE6.0001/hidraw/hidraw2"
mkdir -p "$RELATIVE/device"
printf 'HID_ID=0003:0000054C:00000CE6\nHID_UNIQ=d0:bc:c1:17:55:63\n' > "$RELATIVE/device/uevent"
assert_ok 0 env HIDING_SYS_ROOT="$SYS" HIDING_RUN_DIR="$RUN" "$SCRIPT" \
    /devices/pci-test/0003:054C:0CE6.0001/hidraw/hidraw2

rm -f "$RUN/hidden/d0:bc:c1:17:55:63"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$EVENT"

assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$SYS/class/hidraw/hidraw9"

printf 'HID_UNIQ=\n' > "$HIDRAW/device/uevent"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"

printf 'HID_UNIQ=..\n' > "$HIDRAW/device/uevent"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"

printf 'HID_UNIQ=../../x\n' > "$HIDRAW/device/uevent"
touch "$TMP/x"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"

printf 'HID_UNIQ=bad id\n' > "$HIDRAW/device/uevent"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"

printf 'HID_UNIQ=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n' > "$HIDRAW/device/uevent"
assert_ok 1 env HIDING_RUN_DIR="$RUN" "$SCRIPT" "$HIDRAW"

for shell_script in "$PROJECT/scripts/"*.sh; do
    if [ -e "$shell_script" ]; then
        assert_ok 0 sh -n "$shell_script"
    fi
done

ASSETS="$PROJECT/assets"

grep -q 'chown root:dualsenseclient' "$ASSETS/72-dualsense-hiding.rules" \
    || fail "udev rule is missing the hiding group"
grep -q 'chmod 0660' "$ASSETS/72-dualsense-hiding.rules" \
    || fail "udev rule is missing the hidden mode"
grep -q 'TAG-="uaccess"' "$ASSETS/72-dualsense-hiding.rules" \
    || fail "udev rule is missing the uaccess removal"
grep -q 'PROGRAM=.*udev-check' "$ASSETS/72-dualsense-hiding.rules" \
    || fail "udev rule is missing the marker check"
grep -q 'ExecStopPost=.*hiding-cleanup' "$ASSETS/dualsense-hiding.service" \
    || fail "service is missing the cleanup hook"
grep -q 'dualsense-hiding-acl-fix' "$ASSETS/74-dualsense-hiding-acl.rules" \
    || fail "post-udev ACL rule is missing the ACL fixer"

if command -v systemd-analyze >/dev/null 2>&1; then
    systemd-analyze verify \
        "$ASSETS/dualsense-hiding.socket" \
        "$ASSETS/dualsense-hiding.service" >/dev/null 2>&1 || true
fi

OUT="$TMP/build"
assert_ok 0 run_python "$BUILD" --output "$OUT" --tests

for asset in 72-dualsense-hiding.rules 74-dualsense-hiding-acl.rules dualsense-hiding.socket \
             dualsense-hiding.service hiding-cleanup.sh install-dualsense-hiding.sh \
             uninstall-dualsense-hiding.sh dualsense-hiding-acl-fix; do
    if [ ! -f "$OUT/$asset" ]; then
        fail "build output is missing $asset"
    fi
done

assert_ok 0 run_python "$CHECK" \
    "$OUT/dualsense-hiding-helper" "$OUT/dualsense-hiding-launch" "$OUT/dualsense-hiding-acl-fix"

LAUNCH="$OUT/dualsense-hiding-launch"
if [ ! -x "$LAUNCH" ]; then
    fail "missing executable launcher at $LAUNCH"
fi

assert_ok 2 "$LAUNCH" /bin/true

if [ -x "$OUT/test_native" ]; then
    assert_ok 0 "$OUT/test_native"
else
    fail "missing native test binary at $OUT/test_native"
fi

HELPER="$OUT/dualsense-hiding-helper"
if [ ! -x "$HELPER" ]; then
    fail "missing helper at $HELPER"
fi

DAEMON_SYS="$TMP/daemon-sys"
DAEMON_DEV="$TMP/daemon-dev"
DAEMON_RUN="$TMP/daemon-run"
DAEMON_SOCKET="$TMP/daemon.sock"
DAEMON_ID="d0:bc:c1:17:55:63"
mkdir -p "$DAEMON_SYS/bus/hid/devices/0003:054C:0CE6.0001" "$DAEMON_DEV"
printf 'HID_ID=0003:0000054C:00000CE6\nHID_UNIQ=%s\nDRIVER=playstation\n' "$DAEMON_ID" \
    > "$DAEMON_SYS/bus/hid/devices/0003:054C:0CE6.0001/uevent"

HIDING_SYSFS_ROOT="$DAEMON_SYS" HIDING_DEV_ROOT="$DAEMON_DEV" \
HIDING_RUN_DIR="$DAEMON_RUN" HIDING_SOCKET="$DAEMON_SOCKET" "$HELPER" &
HELPER_PID=$!

i=0
while [ ! -S "$DAEMON_SOCKET" ] && [ "$i" -lt 50 ]; do
    sleep 0.1
    i=$((i + 1))
done
if [ ! -S "$DAEMON_SOCKET" ]; then
    fail "helper did not create $DAEMON_SOCKET"
fi

SOCKET="$DAEMON_SOCKET" RUN_DIR="$DAEMON_RUN" ID="$DAEMON_ID" run_python - <<'PY'
import os
import socket
import time

socket_path = os.environ["SOCKET"]
run_dir = os.environ["RUN_DIR"]
controller_id = os.environ["ID"]

client = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
client.settimeout(5)
client.connect(socket_path)
stream = client.makefile("rwb", buffering=0)


def request(line):
    stream.write((line + "\n").encode())
    response = stream.readline()
    assert response, f"no response for {line}"
    return response.decode().rstrip("\r\n")


hello = request("HELLO 1")
assert hello.startswith("OK 1 "), hello
assert request("STATUS " + controller_id) == "VISIBLE"
assert request("LIST") == "END"

marker_dir = os.path.join(run_dir, "hidden")
os.makedirs(marker_dir, exist_ok=True)
marker = os.path.join(marker_dir, controller_id)
open(marker, "w", encoding="utf-8").close()
assert request("STATUS " + controller_id) == "HIDDEN"
assert request("QUIT") == "OK"

stream.close()
client.close()

deadline = time.time() + 10
while time.time() < deadline and os.path.exists(marker):
    time.sleep(0.1)
assert not os.path.exists(marker), "helper did not clear the marker on exit"
PY
if [ "$?" -ne 0 ]; then
    fail "daemon protocol client failed"
fi

i=0
while kill -0 "$HELPER_PID" 2>/dev/null && [ "$i" -lt 120 ]; do
    sleep 0.1
    i=$((i + 1))
done
if kill -0 "$HELPER_PID" 2>/dev/null; then
    fail "helper did not exit after the last client disconnected"
fi
wait "$HELPER_PID" 2>/dev/null || true
HELPER_PID=

NO_SOCKET_RUN="$TMP/no-socket-run"
mkdir -p "$NO_SOCKET_RUN"
touch "$NO_SOCKET_RUN/hiding.sock"
if env -u LISTEN_PID -u LISTEN_FDS -u HIDING_SOCKET \
    HIDING_SYSFS_ROOT="$DAEMON_SYS" HIDING_DEV_ROOT="$DAEMON_DEV" \
    HIDING_RUN_DIR="$NO_SOCKET_RUN" "$HELPER" >"$TMP/no-socket.out" 2>&1; then
    fail "helper started without socket activation or HIDING_SOCKET"
else
    code=$?
fi
if [ "$code" -ne 1 ]; then
    fail "helper without a socket returned $code, expected 1"
fi
grep -q "HIDING_SOCKET" "$TMP/no-socket.out" \
    || fail "helper did not explain the missing socket"
if [ ! -f "$NO_SOCKET_RUN/hiding.sock" ]; then
    fail "helper removed a socket it did not create"
fi

if "$LAUNCH" /bin/true >"$TMP/launch.out" 2>&1; then
    fail "launcher accepted an arbitrary program path"
else
    code=$?
fi
if [ "$code" -ne 2 ]; then
    fail "launcher with an argument returned $code, expected 2"
fi
grep -q "usage:" "$TMP/launch.out" || fail "launcher did not print usage"

TEST_CONFIG="$TMP/app-path"
TEST_LAUNCH="$TMP/launch-test"
cc -std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror \
    -DHIDING_LAUNCH_TEST -DHIDING_LAUNCH_CONFIG="\"$TEST_CONFIG\"" \
    "$PROJECT/source/launch.c" -o "$TEST_LAUNCH" \
    || fail "could not compile the launcher test binary"

VALID_TARGET="$TMP/valid-target"
cp -L /bin/true "$VALID_TARGET"
chmod 0755 "$VALID_TARGET"
printf '%s\n' "$VALID_TARGET" > "$TEST_CONFIG"
assert_ok 0 "$TEST_LAUNCH"

printf 'relative-target\n' > "$TEST_CONFIG"
assert_ok 2 "$TEST_LAUNCH"

cp -L /bin/true "$TMP/writable-target"
chmod 0777 "$TMP/writable-target"
printf '%s\n' "$TMP/writable-target" > "$TEST_CONFIG"
assert_ok 2 "$TEST_LAUNCH"

chmod 0775 "$TMP/writable-target"
assert_ok 2 "$TEST_LAUNCH"

ln -s /bin/true "$TMP/linked-target"
printf '%s\n' "$TMP/linked-target" > "$TEST_CONFIG"
assert_ok 2 "$TEST_LAUNCH"

rm -f "$TEST_CONFIG"
ln -s /bin/true "$TEST_CONFIG"
assert_ok 2 "$TEST_LAUNCH"

echo "PASS: udev-check"
echo "PASS: launcher/build"
echo "PASS: native"
echo "PASS: daemon"
