#!/usr/bin/env python3
"""
Native Library Fetcher

Downloads libVIIPER release archives for Windows and Linux, extracts the
native binaries into DualSenseClient.VIIPER/native/<platform> and deletes
the remaining archive content (headers, licenses, import libs). Records the
release tag in native/version.txt.

Also downloads the HIDAPI Windows binary (x64/hidapi.dll from the
libusb/hidapi release archive) into DualSenseClient.Hid/native/win-x64 and
records its release tag in native/version.txt. Linux uses the system
libhidapi-hidraw library (e.g. libhidapi-hidraw0) instead of a bundled binary.
"""

import argparse
import logging
import shutil
import tempfile
import urllib.request
import zipfile
from pathlib import Path

REPO = "DualSenseClient/VIIPER"
TAG = "v1.0.1"
DEV_TAG = "dev-snapshot"
DESTINATION = Path("source") / "DualSenseClient.VIIPER" / "native"

PLATFORMS = {
    "win-x64": {"asset": "libVIIPER-windows-amd64.zip", "binary": "libVIIPER.dll"},
    "linux-x64": {"asset": "libVIIPER-linux-amd64.zip", "binary": "libVIIPER.so"},
}

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


def fetch_native_library(
    platform: str, repo: str, tag: str, destination: Path, force: bool = False
) -> Path:
    if platform not in PLATFORMS:
        raise ValueError(
            f"Unknown platform '{platform}'. Expected one of: {', '.join(PLATFORMS)}"
        )

    asset = PLATFORMS[platform]["asset"]
    binary = PLATFORMS[platform]["binary"]
    url = f"https://github.com/{repo}/releases/download/{tag}/{asset}"

    target_dir = destination / platform
    target_binary = target_dir / binary

    if not force and target_binary.exists() and recorded_version(destination) == tag:
        logging.info("Skipping %s: already present (%s)", platform, tag)
        return target_binary

    target_dir.mkdir(parents=True, exist_ok=True)

    logging.info("Downloading %s", url)
    with tempfile.TemporaryDirectory() as temp_dir:
        archive_path = Path(temp_dir) / asset
        urllib.request.urlretrieve(url, archive_path)

        extract_dir = Path(temp_dir) / "extract"
        logging.info("Extracting %s", archive_path.name)
        with zipfile.ZipFile(archive_path) as archive:
            archive.extractall(extract_dir)

        extracted_binary = extract_dir / binary
        if not extracted_binary.exists():
            raise FileNotFoundError(
                f"'{binary}' not found in {asset}. Archive contains: "
                f"{', '.join(str(p.relative_to(extract_dir)) for p in extract_dir.rglob('*') if p.is_file())}"
            )

        for file in target_dir.iterdir():
            if file.is_file():
                file.unlink()
        shutil.copy2(extracted_binary, target_binary)
        logging.info("Installed %s", target_binary)

    return target_binary


def main() -> int:
    parser = argparse.ArgumentParser(description="Downloads libVIIPER and hidapi native libraries")
    parser.add_argument(
        "platforms",
        nargs="*",
        default=list(PLATFORMS),
        choices=list(PLATFORMS),
        help="Which platforms to fetch (default: all)",
    )
    parser.add_argument("--repo", default=REPO, help="GitHub repository (owner/name)")
    parser.add_argument("--tag", default=TAG, help="Release tag to download from")
    parser.add_argument(
        "--dev",
        dest="tag",
        action="store_const",
        const=DEV_TAG,
        help=f"Download the dev release instead ({DEV_TAG}); overrides --tag",
    )
    parser.add_argument(
        "--output", type=Path, default=DESTINATION, help="Output directory"
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
        fetch_native_library(
            platform, args.repo, args.tag, args.output, force=args.force
        )

    version_file = args.output / "version.txt"
    previous_version = recorded_version(args.output)
    version_file.write_text(args.tag + "\n")

    if previous_version is not None and previous_version != args.tag:
        logging.warning("Version changed: %s -> %s", previous_version, args.tag)
    else:
        logging.info("Version: %s", args.tag)

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
