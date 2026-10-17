#!/usr/bin/env python3
"""Builds a .unitypackage of the package without Unity.

A .unitypackage is a gzipped tar with one folder per asset, named by the
asset's GUID, holding `asset` (the file, absent for folders), `asset.meta`
and `pathname` (where the asset lands in the project). The package is laid
out under Packages/<name>/, so importing it gives an embedded package, the
same files a Git URL install resolves.

Only files tracked by git are packed, filtered the way Unity imports them:
names starting with a dot or ending in ~ are skipped along with all below.
"""

import argparse
import gzip
import io
import json
import re
import subprocess
import sys
import tarfile
import time
from pathlib import Path

GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.MULTILINE)


def is_importable(rel):
    return all(not part.startswith(".") and not part.endswith("~") for part in rel.split("/"))


def tracked_files(root):
    output = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z"], check=True, capture_output=True
    ).stdout.decode("utf-8")
    return [path for path in output.split("\0") if path and is_importable(path)]


def read_guid(meta_path):
    match = GUID_PATTERN.search(meta_path.read_text(encoding="utf-8"))
    if not match:
        raise ValueError(f"No GUID in {meta_path}")
    return match.group(1)


def add_bytes(archive, name, data, mtime):
    info = tarfile.TarInfo(name)
    info.size = len(data)
    info.mode = 0o644
    info.mtime = mtime
    archive.addfile(info, io.BytesIO(data))


def add_folder(archive, name, mtime):
    info = tarfile.TarInfo(name)
    info.type = tarfile.DIRTYPE
    info.mode = 0o755
    info.mtime = mtime
    archive.addfile(info)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".", help="package root (holds package.json)")
    parser.add_argument("--output", required=True, help="the .unitypackage to write")
    args = parser.parse_args()

    root = Path(args.root).resolve()
    package_name = json.loads((root / "package.json").read_text(encoding="utf-8"))["name"]

    files = [path for path in tracked_files(root) if not path.endswith(".meta")]
    folders = sorted({str(Path(path).parent.as_posix()) for path in files} - {"."})
    # Parents of nested folders that hold no file directly still need their own entry.
    for folder in list(folders):
        parent = Path(folder).parent
        while parent.as_posix() != ".":
            folders.append(parent.as_posix())
            parent = parent.parent
    folders = sorted(set(folders))

    entries = [(folder, True) for folder in folders] + [(path, False) for path in sorted(files)]
    missing = [rel for rel, _ in entries if not (root / f"{rel}.meta").is_file()]
    if missing:
        print("Missing .meta files:", *missing, sep="\n  ", file=sys.stderr)
        return 1

    seen = {}
    for rel, is_folder in entries:
        guid = read_guid(root / f"{rel}.meta")
        if guid in seen:
            print(f"Duplicate GUID {guid}: {seen[guid][0]} and {rel}", file=sys.stderr)
            return 1
        seen[guid] = (rel, is_folder)

    # Laid out the way Unity's own export is: by GUID, then asset, asset.meta, pathname.
    mtime = int(time.time())
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    # Unity imports nothing, without an error, when the name stored in the gzip
    # header is not that of a tar, and tarfile stores the output's own name, so
    # the gzip stream is opened here with the name Unity's own export stores.
    with open(output, "wb") as file, gzip.GzipFile(
        filename="archtemp.tar", mode="wb", fileobj=file, mtime=mtime
    ) as stream, tarfile.open(fileobj=stream, mode="w", format=tarfile.GNU_FORMAT) as archive:
        for guid, (rel, is_folder) in sorted(seen.items()):
            add_folder(archive, guid, mtime)
            if not is_folder:
                add_bytes(archive, f"{guid}/asset", (root / rel).read_bytes(), mtime)
            add_bytes(archive, f"{guid}/asset.meta", (root / f"{rel}.meta").read_bytes(), mtime)
            add_bytes(archive, f"{guid}/pathname", f"Packages/{package_name}/{rel}".encode("utf-8"), mtime)

    print(f"Wrote {output} ({len(folders)} folders, {len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
