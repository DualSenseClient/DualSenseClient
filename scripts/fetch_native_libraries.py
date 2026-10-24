#!/usr/bin/env python3
"""
Native Library Fetcher

Downloads the HIDAPI Windows binary (x64/hidapi.dll from the libusb/hidapi release
archive) into DualSenseClient.Hid/native/win-x64 and records its release tag in
native/version.txt. Linux uses the system libhidapi-hidraw library
(e.g. libhidapi-hidraw0) instead of a bundled binary.
"""

import argparse
import logging
import shutil
import tempfile
import urllib.request
import zipfile
from pathlib import Path

PLATFORMS = ("win-x64", "linux-x64")

HIDAPI_REPO = "libusb/hidapi"
HIDAPI_TAG = "hidapi-0.15.0"
HIDAPI_DESTINATION = Path("source") / "DualSenseClient.Hid" / "native"
HIDAPI_WIN_ASSET = "hidapi-win.zip"
HIDAPI_WIN_MEMBER = "x64/hidapi.dll"
HIDAPI_WIN_BINARY = "hidapi.dll"


def recorded_version(destination: Path) -> str | None:
    version_file = destination / "version.txt"
    if not version_file.exists():
        return None
    return version_file.read_text().strip()


def main() -> int:
    parser = argparse.ArgumentParser(description="Downloads hidapi native libraries")
    parser.add_argument(
        "platforms",
        nargs="*",
        default=list(PLATFORMS),
        choices=list(PLATFORMS),
        help="Which platforms to fetch (default: all)",
    )
    parser.add_argument(
        "-v", "--verbose", action="store_true", help="Enable verbose logging"
    )
    parser.add_argument(
        "--force", action="store_true", help="Download and overwrite even if already present"
    )
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(levelname)s: %(message)s",
    )

    for platform in args.platforms:
        fetch_hidapi_library(
            platform,
            HIDAPI_REPO,
            HIDAPI_TAG,
            HIDAPI_DESTINATION,
            force=args.force,
        )

    # Always stamped, even when no binary was fetched for the requested
    # platform (Linux uses the system library): the Hid project embeds
    # native/version.txt unconditionally, so a missing file breaks the build.
    HIDAPI_DESTINATION.mkdir(parents=True, exist_ok=True)
    hidapi_version_file = HIDAPI_DESTINATION / "version.txt"
    hidapi_previous = recorded_version(HIDAPI_DESTINATION)
    hidapi_version_file.write_text(HIDAPI_TAG + "\n")

    if hidapi_previous is not None and hidapi_previous != HIDAPI_TAG:
        logging.warning(
            "hidapi version changed: %s -> %s", hidapi_previous, HIDAPI_TAG
        )
    else:
        logging.info("hidapi version: %s", HIDAPI_TAG)

    return 0


def fetch_hidapi_library(
    platform: str, repo: str, tag: str, destination: Path, force: bool = False
) -> Path | None:
    if platform != "win-x64":
        logging.info(
            "Skipping hidapi %s: Linux uses the system libhidapi-hidraw library "
            "(e.g. sudo apt install libhidapi-hidraw0)",
            platform,
        )
        return None

    url = f"https://github.com/{repo}/releases/download/{tag}/{HIDAPI_WIN_ASSET}"

    target_dir = destination / platform
    target_binary = target_dir / HIDAPI_WIN_BINARY

    if not force and target_binary.exists() and recorded_version(destination) == tag:
        logging.info("Skipping hidapi %s: already present (%s)", platform, tag)
        return target_binary

    target_dir.mkdir(parents=True, exist_ok=True)

    logging.info("Downloading %s", url)
    with tempfile.TemporaryDirectory() as temp_dir:
        archive_path = Path(temp_dir) / HIDAPI_WIN_ASSET
        urllib.request.urlretrieve(url, archive_path)

        logging.info("Extracting %s", archive_path.name)
        with zipfile.ZipFile(archive_path) as archive:
            if HIDAPI_WIN_MEMBER not in archive.namelist():
                raise FileNotFoundError(
                    f"'{HIDAPI_WIN_MEMBER}' not found in {HIDAPI_WIN_ASSET}. Archive contains: "
                    f"{', '.join(archive.namelist())}"
                )

            with archive.open(HIDAPI_WIN_MEMBER) as source:
                with open(target_binary, "wb") as target:
                    shutil.copyfileobj(source, target)

        logging.info("Installed %s", target_binary)

    return target_binary


if __name__ == "__main__":
    raise SystemExit(main())
