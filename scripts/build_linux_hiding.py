#!/usr/bin/env python3
"""Build the Linux hiding native helper and assemble its installable output."""

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
LINUX = REPO_ROOT / "extras" / "HidHideLinux"
SOURCE = LINUX / "source"
ASSETS = LINUX / "assets"
SCRIPTS = LINUX / "scripts"
TESTS = LINUX / "tests"

CC_FLAGS = ("-std=c11", "-D_GNU_SOURCE", "-O2", "-Wall", "-Wextra", "-Werror")

PACKAGED_FILES = (
    REPO_ROOT / "LICENSE",
    LINUX / "README.md",
    SCRIPTS / "udev-check.sh",
    SCRIPTS / "hiding-cleanup.sh",
    ASSETS / "72-dualsense-hiding.rules",
    ASSETS / "74-dualsense-hiding-acl.rules",
    ASSETS / "dualsense-hiding.socket",
    ASSETS / "dualsense-hiding.service",
    SCRIPTS / "install-dualsense-hiding.sh",
    SCRIPTS / "uninstall-dualsense-hiding.sh",
)


def build_launcher(cc: str, output: Path) -> None:
    """Compile the setgid launcher."""
    subprocess.run(
        [
            cc,
            *CC_FLAGS,
            str(SOURCE / "launch.c"),
            "-o",
            str(output / "dualsense-hiding-launch"),
        ],
        check=True,
    )


def build_native_test(cc: str, output: Path) -> bool:
    """Compile the native unit test when its sources exist."""
    test_source = TESTS / "test_native.c"
    sources = [
        SOURCE / "hiding_device.c",
        SOURCE / "hiding_markers.c",
        SOURCE / "hiding_permissions.c",
        SOURCE / "hiding_protocol.c",
    ]
    if not test_source.is_file() or any(not source.is_file() for source in sources):
        return False

    subprocess.run(
        [
            cc,
            *CC_FLAGS,
            "-Wno-format-truncation",
            f"-I{SOURCE}",
            str(test_source),
            *(str(source) for source in sources),
            "-o",
            str(output / "test_native"),
        ],
        check=True,
    )
    return True


def build_helper(cc: str, output: Path) -> bool:
    """Compile the helper daemon when its main source exists."""
    if not (SOURCE / "main.c").is_file():
        return False

    sources = sorted(
        str(path)
        for path in SOURCE.glob("*.c")
        if path.name not in ("launch.c", "hiding-acl-fix.c")
    )
    if not sources:
        return False

    subprocess.run(
        [
            cc,
            *CC_FLAGS,
            *sources,
            "-o",
            str(output / "dualsense-hiding-helper"),
        ],
        check=True,
    )
    return True


def build_acl_fix(cc: str, output: Path) -> bool:
    """Compile the udev post-processing ACL remover."""
    source = SOURCE / "hiding-acl-fix.c"
    if not source.is_file():
        return False

    subprocess.run(
        [
            cc,
            *CC_FLAGS,
            str(source),
            "-o",
            str(output / "dualsense-hiding-acl-fix"),
        ],
        check=True,
    )
    return True


def copy_assets(output: Path) -> None:
    """Copy assets, scripts, and documentation that exist."""
    for path in PACKAGED_FILES:
        if path.is_file():
            shutil.copy2(path, output / path.name)


def main() -> int:
    """Build all native artifacts into the requested output directory."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(tempfile.gettempdir()) / "dualsenseclient-hiding-build",
        help="Output directory (default: system temp directory)",
    )
    parser.add_argument(
        "--tests",
        action="store_true",
        help="Also build the native unit test binary",
    )
    args = parser.parse_args()

    cc = shutil.which("cc")
    if cc is None:
        print("error: C compiler 'cc' was not found", file=sys.stderr)
        return 1

    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)

    build_launcher(cc, output)
    build_helper(cc, output)
    build_acl_fix(cc, output)
    if args.tests:
        build_native_test(cc, output)
    copy_assets(output)

    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
