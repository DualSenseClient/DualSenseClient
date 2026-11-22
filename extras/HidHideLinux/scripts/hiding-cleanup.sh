#!/bin/sh
set -u

run_dir=${HIDING_RUN_DIR:-/run/dualsenseclient}
hidden_dir="$run_dir/hidden"
had_markers=0
failed=0

if [ -d "$hidden_dir" ]; then
    for marker in "$hidden_dir"/*; do
        if [ -f "$marker" ]; then
            had_markers=1
        fi
    done
fi

if [ "$had_markers" -eq 1 ]; then
    # Crash fallback: the daemon could not restore its saved per-node state.
    # Restore the standard ownership/mode for Sony controller nodes, then let
    # the default udev rules reapply the uaccess ACL. Keep the markers until
    # the restore has been attempted so a crash here can retry.
    for hidraw in /sys/class/hidraw/hidraw*; do
        if [ ! -r "$hidraw/device/uevent" ]; then
            continue
        fi
        if ! grep -q 'HID_ID=.*054C' "$hidraw/device/uevent" 2>/dev/null; then
            continue
        fi
        node="/dev/$(basename "$hidraw")"
        if [ -e "$node" ]; then
            chown root:root "$node" 2>/dev/null || failed=1
            chmod 0660 "$node" 2>/dev/null || failed=1
        fi
    done

    for node in /dev/input/event* /dev/input/js* /dev/input/mouse*; do
        if [ ! -e "$node" ] || ! command -v udevadm >/dev/null 2>&1; then
            continue
        fi
        properties=$(udevadm info -q property -n "$node" 2>/dev/null) || {
            failed=1
            continue
        }
        case "$properties" in
            *"ID_VENDOR_ID=054c"*) ;;
            *) continue ;;
        esac
        chown root:input "$node" 2>/dev/null || failed=1
        case "$node" in
            */js*) chmod 0664 "$node" 2>/dev/null || failed=1 ;;
            *) chmod 0660 "$node" 2>/dev/null || failed=1 ;;
        esac
    done

    if [ "$failed" -eq 0 ]; then
        # Remove the markers before triggering udev; otherwise the 72 rule sees
        # the marker and re-applies the hidden group.
        rm -f "$hidden_dir"/*
        if command -v udevadm >/dev/null 2>&1; then
            udevadm trigger --action=change --subsystem-match=hidraw --subsystem-match=input \
                || failed=1
        fi
    else
        echo "hiding-cleanup.sh: some controller nodes could not be restored; markers kept for retry" >&2
    fi
fi

exit "$failed"
