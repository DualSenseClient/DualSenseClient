#!/bin/sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "uninstall-dualsense-hiding.sh: run as root (sudo)" >&2
    exit 1
fi

group_created=0
if [ -f /etc/dualsenseclient/group-created ]; then
    group_created=1
fi

systemctl disable --now dualsense-hiding.socket 2>/dev/null || true
systemctl stop dualsense-hiding.service 2>/dev/null || true

if [ -x /usr/libexec/dualsenseclient/hiding-cleanup ]; then
    if ! /usr/libexec/dualsenseclient/hiding-cleanup; then
        echo "uninstall-dualsense-hiding.sh: controller cleanup failed; fix the permissions and retry" >&2
        exit 1
    fi
fi

rm -f /etc/systemd/system/dualsense-hiding.socket
rm -f /etc/systemd/system/dualsense-hiding.service
rm -f /etc/udev/rules.d/72-dualsense-hiding.rules
rm -f /etc/udev/rules.d/74-dualsense-hiding-acl.rules
rm -f /usr/share/applications/dualsenseclient.desktop
rm -rf /etc/dualsenseclient
rm -rf /usr/libexec/dualsenseclient
rm -rf /run/dualsenseclient

systemctl daemon-reload
udevadm control --reload-rules || true
udevadm trigger --action=change --subsystem-match=hidraw --subsystem-match=input || true

if [ "$group_created" -eq 1 ] && getent group dualsenseclient >/dev/null 2>&1; then
    members=$(getent group dualsenseclient | cut -d: -f4)
    if [ -z "$members" ]; then
        groupdel dualsenseclient || true
    else
        echo "Leaving group 'dualsenseclient' in place because it still has members: $members"
    fi
fi

echo "Removed the Linux controller hiding helper."
