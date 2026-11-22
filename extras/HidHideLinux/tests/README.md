# Linux Controller Hiding Tests

This folder contains the native, shell, and root-integration tests for the Linux
hiding helper in `extras/HidHideLinux/`. It is intentionally not
a .NET test project; the helper is native C with udev/systemd integration.

## Files

| File | Purpose |
| --- | --- |
| `test_native.c` | Native unit tests for protocol parsing, device discovery, marker persistence, permission snapshots, and ACL capture/restore. |
| `test_linux_hiding.sh` | Non-root gate: builds the helper, checks the launcher target rules, runs the native tests, exercises the daemon protocol, checks the glibc baseline, and validates the udev/systemd assets. |
| `test_linux_hiding_integration.py` | Root-only integration test against a real controller. Verifies group/mode/ACL changes, revocation, duplicate/shared hides, cleanup, and restoration. |

## Running

From the repository root:

```bash
uv run python scripts/build_linux_hiding.py --output /tmp/opencode/dualsenseclient-hiding-build
bash extras/HidHideLinux/tests/test_linux_hiding.sh
```

The root integration test requires `DUALSENSE_HIDING_INTEGRATION=1`, root, a
connected controller, and no running app or Steam instance holding the device:

```bash
sudo env "PATH=$PATH" DUALSENSE_HIDING_INTEGRATION=1 uv run python \
  extras/HidHideLinux/tests/test_linux_hiding_integration.py
```

The integration test temporarily disables the installed production udev rules
so it tests the template in this checkout, then restores them.
