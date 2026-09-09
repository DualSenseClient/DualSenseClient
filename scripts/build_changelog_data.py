#!/usr/bin/env python3
"""Aggregate every GitHub release into changelog.json for the data branch.

Lists all releases via the paginated REST API (no result cap; bodies come
along inline) and writes newest-first entries:
[{"tag", "name", "publishedAt", "prerelease", "body"}].
Drafts are skipped.
"""

import argparse
import json
import logging
import re
import subprocess

logger = logging.getLogger(__name__)

CHANGELOG_HEADING_RE = re.compile(r"^##\s+changelog\s*$", re.IGNORECASE | re.MULTILINE)


def extract_changes(body: str) -> str:
    """Return only the changes section of a release body.

    Pre-releases start with an auto-generated preamble (branch warning,
    compare URL, commit/branch lines); the actual changes follow under a
    `## Changelog` heading. Bodies without that heading (e.g. stable
    releases with GitHub-generated notes) are kept whole.
    """
    text = body.replace("\r\n", "\n")
    match = CHANGELOG_HEADING_RE.search(text)
    if not match:
        return text.strip()
    return text[match.end() :].strip()


def gh(*args: str) -> str:
    # encoding="utf-8": release bodies contain emoji; the Windows locale
    # codec (e.g. cp1252) cannot decode them.
    result = subprocess.run(
        ["gh", *args], capture_output=True, text=True, encoding="utf-8", check=True
    )
    return result.stdout


PAGE_SIZE = 100


def list_all_releases(repo: str) -> list[dict]:
    """Return every release, following REST pagination until exhausted."""
    releases = []
    page = 1
    while True:
        batch = json.loads(
            gh("api", f"repos/{repo}/releases?per_page={PAGE_SIZE}&page={page}")
        )
        if not batch:
            break
        releases.extend(batch)
        if len(batch) < PAGE_SIZE:
            break
        page += 1
    logger.info("Listed %d releases", len(releases))
    return releases


def build(repo: str) -> list[dict]:
    entries = []
    for release in list_all_releases(repo):
        if release.get("draft"):
            continue
        tag = release["tag_name"]
        entries.append(
            {
                "tag": tag,
                "name": release.get("name") or tag,
                "publishedAt": release.get("published_at") or "",
                "prerelease": bool(release.get("prerelease")),
                "body": extract_changes(release.get("body") or ""),
            }
        )
    entries.sort(key=lambda e: e["publishedAt"], reverse=True)
    logger.info("Aggregated %d releases", len(entries))
    return entries


def main():
    parser = argparse.ArgumentParser(
        description="Build changelog.json from all releases"
    )
    parser.add_argument("--repo", required=True, help="owner/repo")
    parser.add_argument("--output", required=True, help="Output JSON path")
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="[%(levelname)s] %(message)s")

    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(build(args.repo), f, ensure_ascii=False, indent=1)
    logger.info("Wrote %s", args.output)


if __name__ == "__main__":
    main()
