#!/bin/sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "install-dualsense-hiding.sh: run as root (sudo)" >&2
    exit 1
fi

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
GROUP=dualsenseclient
TARGET=/usr/libexec/dualsenseclient
APP_PATH=
GROUP_CREATED=0

resolve_app_path()
{
    if [ ! -e "$APP_PATH" ]; then
        echo "install-dualsense-hiding.sh: $APP_PATH does not exist" >&2
        return 1
    fi

    resolved=$(realpath -e -- "$APP_PATH") || {
        echo "install-dualsense-hiding.sh: could not resolve $APP_PATH" >&2
        return 1
    }

    case "$resolved" in
        /*) ;;
        *)
            echo "install-dualsense-hiding.sh: $APP_PATH did not resolve to an absolute path" >&2
            return 1
            ;;
    esac

    if [ ! -f "$resolved" ] || [ ! -x "$resolved" ]; then
        echo "install-dualsense-hiding.sh: $resolved must be a regular executable file" >&2
        return 1
    fi

    mode=$(stat -c '%a' -- "$resolved") || {
        echo "install-dualsense-hiding.sh: could not read permissions for $resolved" >&2
        return 1
    }

    if [ $((0$mode & 0022)) -ne 0 ]; then
        echo "install-dualsense-hiding.sh: $resolved must not be group/other writable; install the app under a private directory such as ~/.local/opt" >&2
        return 1
    fi

    parent=$resolved
    while :; do
        parent=$(dirname -- "$parent")
        if [ "$parent" = "/" ]; then
            break
        fi

        mode=$(stat -c '%a' -- "$parent") || {
            echo "install-dualsense-hiding.sh: could not inspect $parent" >&2
            return 1
        }
        if [ $((0$mode & 0022)) -ne 0 ] && [ $((0$mode & 01000)) -eq 0 ]; then
            echo "install-dualsense-hiding.sh: $parent is group/other writable; install the app under a private directory such as ~/.local/opt or use the AppImage" >&2
            return 1
        fi
    done

    APP_PATH=$resolved
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --app-path)
            if [ "$#" -lt 2 ]; then
                echo "usage: $0 [--app-path <DualSenseClient executable or AppImage>]" >&2
                exit 2
            fi
            APP_PATH=$2
            shift 2
            ;;
        *)
            echo "usage: $0 [--app-path <DualSenseClient executable or AppImage>]" >&2
            exit 2
            ;;
    esac
done

for file in dualsense-hiding-helper dualsense-hiding-launch dualsense-hiding-acl-fix udev-check.sh \
            hiding-cleanup.sh 72-dualsense-hiding.rules 74-dualsense-hiding-acl.rules \
            dualsense-hiding.socket dualsense-hiding.service; do
    if [ ! -f "$SCRIPT_DIR/$file" ]; then
        echo "install-dualsense-hiding.sh: missing $SCRIPT_DIR/$file" >&2
        exit 1
    fi
done

if [ -n "$APP_PATH" ]; then
    resolve_app_path || exit 1
fi

if ! getent group "$GROUP" >/dev/null 2>&1; then
    groupadd --system "$GROUP"
    GROUP_CREATED=1
fi

install -d -m 0755 "$TARGET"
install -d -m 0755 /etc/dualsenseclient

if [ "$GROUP_CREATED" -eq 1 ]; then
    : > /etc/dualsenseclient/group-created
    chmod 0644 /etc/dualsenseclient/group-created
fi
install -m 0755 "$SCRIPT_DIR/dualsense-hiding-helper" "$TARGET/dualsense-hiding-helper"
install -m 0755 "$SCRIPT_DIR/dualsense-hiding-launch" "$TARGET/launch"
install -m 0755 "$SCRIPT_DIR/dualsense-hiding-acl-fix" "$TARGET/dualsense-hiding-acl-fix"
install -m 0755 "$SCRIPT_DIR/udev-check.sh" "$TARGET/udev-check"
install -m 0755 "$SCRIPT_DIR/hiding-cleanup.sh" "$TARGET/hiding-cleanup"

chown root:"$GROUP" "$TARGET/launch"
chmod 2755 "$TARGET/launch"

install -m 0644 "$SCRIPT_DIR/72-dualsense-hiding.rules" /etc/udev/rules.d/72-dualsense-hiding.rules
install -m 0644 "$SCRIPT_DIR/74-dualsense-hiding-acl.rules" /etc/udev/rules.d/74-dualsense-hiding-acl.rules
install -m 0644 "$SCRIPT_DIR/dualsense-hiding.socket" /etc/systemd/system/dualsense-hiding.socket
install -m 0644 "$SCRIPT_DIR/dualsense-hiding.service" /etc/systemd/system/dualsense-hiding.service

if [ -n "$APP_PATH" ]; then
    rm -f /etc/dualsenseclient/app-path
    printf '%s\n' "$APP_PATH" > /etc/dualsenseclient/app-path
    chmod 0644 /etc/dualsenseclient/app-path
    chown root:root /etc/dualsenseclient/app-path

    cat > /usr/share/applications/dualsenseclient.desktop <<EOF
[Desktop Entry]
Type=Application
Name=DualSense Client
Comment=Unofficial DualSense controller client
Exec=$TARGET/launch
Terminal=false
Categories=Utility;Game;
EOF
fi

systemctl daemon-reload
systemctl enable --now dualsense-hiding.socket
udevadm control --reload-rules
udevadm trigger --action=change --subsystem-match=hidraw --subsystem-match=input || true

echo "Installed the Linux controller hiding helper."
echo "Start DualSense Client through: $TARGET/launch"
if [ -z "$APP_PATH" ]; then
    echo "No launcher target was written; rerun with --app-path <DualSenseClient>."
fi
echo "Uninstall with: $SCRIPT_DIR/uninstall-dualsense-hiding.sh"
