# Linux Hiding Helper Development

This guide is for contributors working on the standalone Linux hiding helper.
For installing a released helper, see [`README.md`](README.md).

The helper is a native C program with systemd, udev, and shell integration. It
runs as root, while the DualSense Client GUI stays unprivileged. The app talks
to the helper over a Unix socket; the helper changes hidraw/input node
permissions and restores the original owner, group, mode, and ACL when hiding
ends.

## Project layout

```text
extras/HidHideLinux/
├── source/      # C helper, launcher, protocol, device, marker, permission code
├── assets/      # udev rules and systemd socket/service units
├── scripts/     # install, uninstall, udev-check, and crash cleanup scripts
├── tests/       # native, shell, daemon, and root integration tests
└── README.md    # release and installation documentation
```

The build script lives at `scripts/build_linux_hiding.py` in the repository
root. It assembles a flat release directory: the installer expects all binaries,
assets, and scripts in the same directory.

## Prerequisites

- Linux with systemd and udev for installation and manual end-to-end testing.
- A C11 compiler available as `cc`.
- Python 3 with `uv` (the repository's standard Python workflow).
- The `acl` package (`getfacl`/`setfacl`) for the root integration test.
- `sudo`/root for installation and the root integration test.
- A connected DualSense controller for the integration test.
- .NET 10 SDK only if you also want to build or publish the GUI.

Close the DualSense Client app and Steam before running tests that touch a real
controller. They can hold the hidraw handles and distort the result.

## Build

```bash
uv run python scripts/build_linux_hiding.py \
  --output /tmp/opencode/dualsenseclient-hiding-build \
  --tests
```

`--tests` also builds the native unit test binary. Omit it when producing a
release asset.

The output contains:

- `dualsense-hiding-helper`
- `dualsense-hiding-launch`
- `dualsense-hiding-acl-fix`
- `test_native` when `--tests` is used
- `LICENSE`, `README.md`, the udev/systemd assets, and the shell scripts

Do not run the installer from `extras/HidHideLinux/scripts/`. It expects the
flat build output, not the repository layout.

## Non-root test gate

Run this before every commit:

```bash
bash extras/HidHideLinux/tests/test_linux_hiding.sh
```

The gate builds into a temporary directory and checks:

- shell syntax for the installer, uninstaller, udev check, and cleanup scripts
- `udev-check.sh` against a fake sysfs/dev tree, including marker gating
- launcher target validation: absolute path, regular file, executable,
  non-writable by group/other, and symlink rejection
- native unit tests for protocol parsing, device discovery, marker persistence,
  permission snapshots, and ACL capture/restore
- daemon protocol behavior with fake devices, markers, and a Unix socket
- the glibc baseline of the built binaries
- the expected udev rule and systemd unit contents

The gate is safe to run without root. It removes its temporary directory when
it exits.

## Native unit tests

`tests/test_native.c` covers the pure and filesystem-level pieces that do not
need a real controller. The shell gate builds and runs it through
`scripts/build_linux_hiding.py --tests`.

When changing protocol parsing, marker files, device discovery, or permission
snapshots, add or update a case in `tests/test_native.c` and run the gate.

## Root integration test

The root test is opt-in and uses a real controller:

```bash
sudo env "PATH=$PATH" DUALSENSE_HIDING_INTEGRATION=1 \
  uv run python extras/HidHideLinux/tests/test_linux_hiding_integration.py
```

It verifies:

- group, mode, and ACL changes while hidden
- an already-open app handle survives hiding
- another process's handle is revoked
- duplicate and shared hides
- exact owner/group/mode/ACL restoration after unhide
- cleanup of stale markers and permissions

The test temporarily disables any installed production 72/74 hiding rules so
it exercises the checkout's templates, then restores them. It modifies real
device nodes and ACLs, so use a spare controller and do not run it while the
app or Steam owns the device.

The forced-rebind fallback is not covered by the integration test. Setting
`HIDING_FORCE_REBIND=1` makes the helper revoke every open handle, including
the app's own, and can renumber nodes. Verify that path separately on a spare
controller.

## End-to-end manual test

Publish the app to a private directory. The launcher rejects binaries and
parent directories that are writable by group or other users, so avoid
world-writable locations:

```bash
mkdir -p "$HOME/.local/opt/dualsenseclient"

dotnet publish source/DualSenseClient.GUI/DualSenseClient.GUI.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$HOME/.local/opt/dualsenseclient"
```

Build the helper, then install it with the app path:

```bash
uv run python scripts/build_linux_hiding.py \
  --output /tmp/opencode/dualsenseclient-hiding-build

sudo /tmp/opencode/dualsenseclient-hiding-build/install-dualsense-hiding.sh \
  --app-path "$HOME/.local/opt/dualsenseclient/DualSenseClient"
```

Launch through the installed launcher:

```bash
/usr/libexec/dualsenseclient/launch
```

Enable **Hide this controller** and verify from another terminal:

```bash
stat -c '%n %U %G %a' /dev/hidraw*
getfacl -p /dev/hidrawN
```

While hidden, the controller's nodes should be `root dualsenseclient 660` with
no named-user ACL. Disable hiding and confirm the original owner, group, mode,
and ACL return.

Also test hotplug and fail-open behavior:

1. Hide the controller, unplug it, and plug it back in. It should remain hidden.
2. Hide it again, then exit the app. The helper should restore the nodes when
   the last client disconnects or the service stops.

Uninstall when finished:

```bash
sudo /tmp/opencode/dualsenseclient-hiding-build/uninstall-dualsense-hiding.sh
```

## How the pieces fit together

- The GUI's `LinuxControllerHidingService` resolves the controller ID from the
  hidraw `HID_UNIQ` value and connects to `/run/dualsenseclient/hiding.sock`.
- The line protocol is `HELLO 1`, `HIDE <id>`, `UNHIDE <id>`, and
  `STATUS <id>`. Success responses are `OK ...`; status responses are
  `HIDDEN` or `VISIBLE`; failures are `ERR <message>`.
- `dualsense-hiding.socket` is a root-owned socket in the `dualsenseclient`
  group, mode `0660`. systemd activates the helper service on first connection.
- The helper snapshots each node's owner, group, mode, and ACL in
  `/run/dualsenseclient/hidden/<id>`. It hides every matching HID transport,
  reapplies the hidden permissions after the udev change event, and restores
  the snapshot on unhide, disconnect, shutdown, or stale-marker startup.
- The 72 udev rule only applies the hidden group/mode when the marker exists,
  preventing queued udev events from re-hiding a node after unhide. The 74 rule
  removes the seat's `uaccess` ACL.
- `dualsense-hiding-launch` is a setgid `root:dualsenseclient` launcher. It
  reads the fixed target from `/etc/dualsenseclient/app-path` and starts the
  GUI with the hiding group without giving the GUI root privileges.
- `hiding-cleanup.sh` is the crash fallback. systemd runs it as
  `ExecStopPost`; it restores standard controller permissions and keeps the
  markers if restoration fails so a later run can retry.

## Debugging

Useful commands on an installed system:

```bash
systemctl status dualsense-hiding.socket
systemctl status dualsense-hiding.service
journalctl -u dualsense-hiding.service -b
udevadm monitor --udev --property
ls -l /run/dualsenseclient/hidden/
getfacl -p /dev/hidrawN
```

If a crash leaves markers behind, stop the service and run the installed
cleanup script:

```bash
sudo systemctl stop dualsense-hiding.service
sudo /usr/libexec/dualsenseclient/hiding-cleanup
```

Test-only environment variables:

- `HIDING_RUN_DIR` — marker and runtime directory (default
  `/run/dualsenseclient`)
- `HIDING_SYSFS_ROOT` — sysfs root for device resolution (default `/sys`)
- `HIDING_DEV_ROOT` — device root (default `/dev`)
- `HIDING_SOCKET` — bind a socket directly instead of using systemd socket
  activation; intended for tests and direct helper runs
- `HIDING_FORCE_REBIND` — force the rebind fallback and revoke all open handles

## CI and release expectations

The Linux job in `.github/workflows/build_job.yml` builds the helper, runs the
non-root test gate, checks the glibc baseline, and uploads the flat release
archive.

- Keep the release archive flat. `scripts/build_linux_hiding.py` copies
  `assets/` and `scripts/` files into the output root, and the installer
  expects that layout.
- Do not raise the glibc requirement above 2.35. The release targets Ubuntu
  22.04 and newer; `scripts/check_linux_hiding_glibc.py` enforces the limit.
- The helper is built with `-std=c11 -D_GNU_SOURCE -O2 -Wall -Wextra -Werror`.
  Treat new warnings as failures.
- Do not commit build output, the release archive, or generated documentation.

## Before opening a pull request

- Run `bash extras/HidHideLinux/tests/test_linux_hiding.sh`.
- Run the root integration test when changing permissions, revocation, device
  discovery, marker handling, or service lifecycle.
- Add or update native tests for parser, marker, device, and snapshot changes.
- Update this guide, `README.md`, or `docs/development.md` when the workflow or
  layout changes.
- Report the exact commands you ran, whether you needed root or a controller,
  your distribution and kernel, and the controller connection type.
