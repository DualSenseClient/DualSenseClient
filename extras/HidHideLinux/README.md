# Linux Controller Hiding Helper

This archive is the optional Linux controller-hiding backend for DualSense Client.
It is separate from the app archive so the app can be updated without reinstalling
the privileged helper.

## Install

1. Extract the archive.
2. Run the installer once with the path to your DualSense Client executable or
   AppImage:

   ```bash
   tar -xzf DualSenseClient-linux-hiding.tar.gz
   sudo ./DualSenseClient-linux-hiding/install-dualsense-hiding.sh \
     --app-path /path/to/DualSenseClient.AppImage
   ```

   The installer creates the `dualsenseclient` group, installs the helper under
   `/usr/libexec/dualsenseclient/`, writes the fixed app target to
   `/etc/dualsenseclient/app-path`, installs the systemd socket/service and the
   72/74 udev rules, and reloads udev.

3. Start DualSense Client through the application menu or:

   ```bash
   /usr/libexec/dualsenseclient/launch
   ```

   The launcher must be used: it gives the app the `dualsenseclient` group while
   keeping the GUI unprivileged.

Linux 6.12 or newer hides and unhides without reconnecting the controller.
Older kernels use a HID-driver rebind fallback, which briefly reconnects the
controller.

## Compatibility and source

The helper binaries are built for x86_64 Linux and require glibc 2.35 or newer
(Ubuntu 22.04, Debian 12, or newer). The installer canonicalizes `--app-path`
and rejects relative paths, non-regular files, and binaries writable by group or
other users.

This archive is distributed under GPL-3.0; the `LICENSE` file is included. The
corresponding source is the DualSense Client repository at
<https://github.com/DualSenseClient/DualSenseClient>.

## Uninstall

```bash
sudo ./DualSenseClient-linux-hiding/uninstall-dualsense-hiding.sh
```

See `docs/guides/controller-hiding.md` in the DualSense Client source tree for
behavior, limitations, and troubleshooting.
