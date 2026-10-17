#!/usr/bin/env python3
"""Writes the release notes of the version in package.json.

When CHANGELOG.md has a section headed `## [<version>]` or `## <version>`,
that section is the notes. Otherwise they are made from the commit subjects
since the previous release, grouped by their Conventional Commits type. The
previous release is the tag v<previous version>, or, before any tag exists,
the newest commit whose package.json holds another version.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

SUBJECT_PATTERN = re.compile(r"^(?P<type>\w+)(?:\((?P<scope>[^)]+)\))?(?P<breaking>!)?:\s*(?P<text>.+)$")
SECTIONS = [
    ("feat", "Features"),
    ("fix", "Fixes"),
    ("perf", "Performance"),
    (None, "Other changes"),
]
SKIPPED_TYPES = {"chore", "ci", "test", "docs", "style", "build", "refactor"}
VERSION_BUMP = re.compile(r"^Update version to ", re.IGNORECASE)


def git(*args):
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True, encoding="utf-8").stdout


def version_at(commit):
    try:
        return json.loads(git("show", f"{commit}:package.json"))["version"]
    except (subprocess.CalledProcessError, ValueError, KeyError):
        return None


def previous_release(version):
    tags = [tag for tag in git("tag", "--list", "v*", "--sort=-v:refname", "--merged", "HEAD").split() if tag != f"v{version}"]
    if tags:
        return tags[0]
    for commit in git("log", "--format=%H", "--", "package.json").split():
        if version_at(commit) not in (None, version):
            return commit
    return None


def changelog_section(path, version):
    if not path.is_file():
        return None
    heading = re.compile(rf"^##\s+\[?v?{re.escape(version)}\]?(\s|$)")
    lines, inside = [], False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            if inside:
                break
            inside = bool(heading.match(line))
            continue
        if inside:
            lines.append(line)
    text = "\n".join(lines).strip()
    return text or None


def commit_notes(since):
    log_range = f"{since}..HEAD" if since else "HEAD"
    grouped = {title: [] for _, title in SECTIONS}
    breaking = []
    for line in git("log", "--no-merges", "--format=%h%x09%s", log_range).splitlines():
        sha, subject = line.split("\t", 1)
        if VERSION_BUMP.match(subject):
            continue
        match = SUBJECT_PATTERN.match(subject)
        if match:
            kind = match.group("type").lower()
            if kind in SKIPPED_TYPES:
                continue
            scope = match.group("scope")
            text = match.group("text")
            entry = f"- {'**' + scope + '**: ' if scope else ''}{text} ({sha})"
            if match.group("breaking"):
                breaking.append(entry)
                continue
        else:
            kind, entry = None, f"- {subject} ({sha})"
        title = next((title for key, title in SECTIONS if key == kind), "Other changes")
        grouped[title].append(entry)

    parts = []
    if breaking:
        parts.append("### Breaking changes\n\n" + "\n".join(breaking))
    for _, title in SECTIONS:
        if grouped[title]:
            parts.append(f"### {title}\n\n" + "\n".join(grouped[title]))
    return "\n\n".join(parts) or "No notable changes."


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--repository", help="owner/name, for the install URL and compare link")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    root = Path(args.root).resolve()
    package = json.loads((root / "package.json").read_text(encoding="utf-8"))
    version = package["version"]
    since = previous_release(version)

    body = changelog_section(root / "CHANGELOG.md", version) or commit_notes(since)

    install = [
        "### Installation",
        "",
        f"Import `ALPS-{version}.unitypackage` into a VCC world project. It installs the package under `Packages/{package['name']}`. "
        "The project must already resolve these packages:",
        "",
        *[f"- `{name}` `{dependency}`" for name, dependency in package.get("dependencies", {}).items()],
    ]
    if args.repository:
        install += [
            "",
            f"Or add `https://github.com/{args.repository}.git#v{version}` with the Package Manager's Git URL install.",
        ]
    notes = body + "\n\n" + "\n".join(install)
    if args.repository and since:
        previous = since if since.startswith("v") else since[:12]
        notes += f"\n\n**Full changelog**: https://github.com/{args.repository}/compare/{previous}...v{version}"

    Path(args.output).write_text(notes + "\n", encoding="utf-8")
    print(f"Notes for {version} since {since or 'the first commit'} written to {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
