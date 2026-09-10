#!/usr/bin/env python3
"""Generate changelog from git commits since a given commit SHA."""

import argparse
import logging
import os
import re
import subprocess
import sys

logger = logging.getLogger(__name__)

IGNORE_PATTERNS = [
    re.compile(r"^chore\(localization\): Update translation progress chart$"),
    re.compile(r"^chore\(deps\)"),
    re.compile(r"^chore: Update translation progress chart$"),
    re.compile(r"^ci(\(.*?\))?:"),
    re.compile(r"^\w+\(ci\):"),
    re.compile(r"^docs?(\(.*?\))?:"),
    re.compile(r"^\w+\(docs?\):"),
    re.compile(r"^bump(:|$)"),
    re.compile(r"^test(:|$)"),
    re.compile(r"^init(:|$)"),
    re.compile(r"^style(:|$)"),
    re.compile(r"^perf(:|$)"),
    re.compile(r"^build(:|$)"),
    re.compile(r"^merge ", re.IGNORECASE),
]

PR_NUMBER_RE = re.compile(r"\s*\(#\d+\)$")
TYPE_RE = re.compile(r"^(\w+)(\(.*?\))?:")
GROUP_HEADINGS = (
    ("feat", "### Features"),
    ("fix", "### Bug Fixes"),
)
OTHER_HEADING = "### Other"
REPOSITORY = "DualSenseClient/DualSenseClient"


def get_commits(since_sha: str) -> list[tuple[str, str]]:
    """Return list of (hash, subject) commits since SHA."""
    logger.info("Fetching commits since %s...", since_sha or "repository creation")
    result = subprocess.run(
        ["git", "log", "--pretty=format:%H|%s", f"{since_sha}..HEAD"],
        capture_output=True,
        text=True,
        check=True,
    )
    commits = []
    for line in result.stdout.splitlines():
        line = line.strip()
        if not line:
            continue
        parts = line.split("|", 1)
        if len(parts) == 2:
            commits.append((parts[0], parts[1]))
    logger.info("Found %d total commits", len(commits))
    return commits


def should_ignore(title: str) -> bool:
    """Check if a commit title matches any ignore pattern."""
    return any(p.search(title) for p in IGNORE_PATTERNS)


def display_title(title: str) -> str:
    """Strip the conventional-commit type, keeping the scope.

    e.g. "feat(ui): Add X" -> "ui: Add X", "fix: Crash" -> "Crash".
    Non-conventional titles are returned unchanged.
    """
    match = TYPE_RE.match(title)
    if not match:
        return title
    rest = title[match.end() :].strip()
    if match.group(2):
        return f"{match.group(2)[1:-1]}: {rest}"
    return rest


def build_changelog(since_sha: str) -> str:
    """Build the changelog markdown string."""
    if not since_sha:
        logger.info(
            "No previous commit SHA provided - generating first release changelog"
        )
        return "## Changelog\n\nFirst release or unable to determine previous release."

    commits = get_commits(since_sha)
    if not commits:
        logger.info("No new commits found since %s", since_sha[:7])
        return "## Changelog\n\nNo new commits since last release."

    logger.info("Filtering out docs/internal commits...")
    groups: dict[str, list[str]] = {}
    ignored_count = 0
    entry_count = 0
    for commit_hash, subject in commits:
        title = PR_NUMBER_RE.sub("", subject)
        if not title.strip() or should_ignore(title):
            ignored_count += 1
            continue
        short = commit_hash[:7]
        url = f"https://github.com/{REPOSITORY}/commit/{commit_hash}"
        entry = f"- **{display_title(title)}** ([{short}]({url}))"
        match = TYPE_RE.match(title)
        group = match.group(1).lower() if match else "other"
        groups.setdefault(group, []).append(entry)
        entry_count += 1

    if ignored_count:
        logger.info("Skipped %d non-user-facing commits", ignored_count)

    sections = []
    for group, heading in GROUP_HEADINGS:
        entries = groups.pop(group, None)
        if entries:
            sections.append(heading + "\n\n" + "\n".join(entries))
    if groups:
        rest = [e for key in sorted(groups) for e in groups[key]]
        sections.append(OTHER_HEADING + "\n\n" + "\n".join(rest))

    if not sections:
        logger.info("No user-facing changelog entries found")
        return "## Changelog\n\nNo new commits since last release."

    logger.info("Generated %d changelog entries", entry_count)
    return "## Changelog\n\n" + "\n\n".join(sections)


def main():
    parser = argparse.ArgumentParser(description="Generate changelog from git commits")
    parser.add_argument(
        "since_sha", nargs="?", default="", help="Commit SHA to start from"
    )
    parser.add_argument("--debug", action="store_true", help="Enable debug logging")
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.DEBUG if args.debug else logging.INFO,
        format="[%(levelname)s] %(message)s",
    )

    try:
        changelog = build_changelog(args.since_sha)
    except subprocess.CalledProcessError as e:
        logger.error("Error generating changelog: %s", e)
        changelog = ""
        sys.exit(1)

    # Write to GitHub Actions environment file
    env_file = os.environ.get("GITHUB_ENV")
    if env_file:
        with open(env_file, "a", encoding="utf-8") as f:
            f.write(f"CHANGELOG<<EOF\n{changelog}\nEOF\n")
        logger.info("Changelog written to GITHUB_ENV")
    else:
        print(changelog)


if __name__ == "__main__":
    main()
