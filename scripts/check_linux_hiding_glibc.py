#!/usr/bin/env python3
"""Reject hiding-helper binaries that require a newer glibc than Ubuntu 22.04."""

import re
import subprocess
import sys
from pathlib import Path

MAX_VERSION = (2, 35)
VERSION_PATTERN = re.compile(r"GLIBC_(\d+)\.(\d+)")


def required_version(binary: Path) -> tuple[int, int]:
    result = subprocess.run(
        ["readelf", "--version-info", str(binary)],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "readelf failed")

    versions = [tuple(int(part) for part in match) for match in VERSION_PATTERN.findall(result.stdout)]
    if not versions:
        raise RuntimeError("no GLIBC symbol versions found")
    return max(versions)


def main(arguments: list[str]) -> int:
    if not arguments:
        print("usage: check_glibc.py <binary>...", file=sys.stderr)
        return 2

    failed = False
    for argument in arguments:
        binary = Path(argument)
        if not binary.is_file():
            print(f"missing binary: {binary}", file=sys.stderr)
            failed = True
            continue

        try:
            version = required_version(binary)
        except (OSError, RuntimeError) as error:
            print(f"{binary}: {error}", file=sys.stderr)
            failed = True
            continue

        print(f"{binary}: max GLIBC_{version[0]}.{version[1]}")
        if version > MAX_VERSION:
            failed = True

    if failed:
        print(
            f"ERROR: hiding binaries must not require GLIBC newer than "
            f"{MAX_VERSION[0]}.{MAX_VERSION[1]}",
            file=sys.stderr,
        )
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
