#!/bin/sh
set -eu

device_path=${1:-}
if [ -z "$device_path" ]; then
    exit 1
fi

sys_root=${HIDING_SYS_ROOT:-/sys}
case "$device_path" in
    /sys/*)
        uevent="$device_path/device/uevent"
        ;;
    /devices/*|/class/*|/bus/*)
        # udev passes %p relative to sysfs, e.g. /devices/.../hidraw/hidraw2.
        uevent="$sys_root$device_path/device/uevent"
        ;;
    /*)
        # Absolute test paths (including a temporary sysfs tree).
        uevent="$device_path/device/uevent"
        ;;
    *)
        uevent="$sys_root/$device_path/device/uevent"
        ;;
esac

if [ ! -r "$uevent" ]; then
    exit 1
fi

id=
while IFS= read -r line; do
    case "$line" in
        HID_UNIQ=*)
            id=${line#HID_UNIQ=}
            ;;
        UNIQ=*)
            id=${line#UNIQ=}
            ;;
    esac
done < "$uevent"

case "$id" in
    \"*\")
        id=${id#\"}
        id=${id%\"}
        ;;
esac

if [ "${#id}" -lt 1 ] || [ "${#id}" -gt 64 ]; then
    exit 1
fi

case "$id" in
    .|..)
        exit 1
        ;;
    *[!A-Za-z0-9:._-]*)
        exit 1
        ;;
esac

run_dir=${HIDING_RUN_DIR:-/run/dualsenseclient}
if [ -f "$run_dir/hidden/$id" ]; then
    exit 0
fi

exit 1
