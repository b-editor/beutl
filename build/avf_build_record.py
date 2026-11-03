#!/usr/bin/env python3
"""Record how libBeutlAVF.dylib was built, and check the committed copies against that record.

The dylibs are committed because only macOS can build them, and CI tests those copies without
rebuilding them. A change to the Swift sources committed without new dylibs would otherwise pass
unless a test happened to cover it. A build that runs the Swift package writes the record itself.
"""

import argparse
import hashlib
import json
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
PROJECT = Path("src/Beutl.Extensions.AVFoundation")
PACKAGE = PROJECT / "native/BeutlAVF"
RUNTIMES = PROJECT / "runtimes"
RECORD = RUNTIMES / "build.json"
RIDS = ("osx-arm64", "osx-x64")
DYLIB = "libBeutlAVF.dylib"
# What BuildSwiftDylib in BeutlAVF.targets rebuilds from, besides Package.swift.
SOURCE_SUFFIXES = {".swift", ".h", ".c", ".modulemap"}
REBUILD = f"Rebuild it on macOS with `dotnet build {PROJECT}`, then commit the dylibs together with {RECORD.name}."


def sources_hash(root):
    package = root / PACKAGE
    sources = (path for path in (package / "Sources").rglob("*") if path.suffix in SOURCE_SUFFIXES and path.is_file())
    files = sorted([package / "Package.swift", *sources], key=lambda path: path.relative_to(package).as_posix())
    digest = hashlib.sha256()
    for file in files:
        digest.update(file.relative_to(package).as_posix().encode() + b"\0")
        # A Windows checkout has CRLF line endings; the sources are the same.
        digest.update(hashlib.sha256(file.read_bytes().replace(b"\r\n", b"\n")).digest())
    return digest.hexdigest()


def dylib_path(root, rid):
    return root / RUNTIMES / rid / "native" / DYLIB


def toolchain():
    return subprocess.run(["swift", "--version"], check=True, capture_output=True, text=True).stdout.splitlines()[0]


def write_record(root, toolchain_version):
    record = {
        "sources": sources_hash(root),
        "toolchain": toolchain_version,
        "dylibs": {rid: hashlib.sha256(dylib_path(root, rid).read_bytes()).hexdigest() for rid in RIDS},
    }
    (root / RECORD).write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8", newline="\n")


def verify(root):
    """Returns what no longer matches the record, so an empty list means the record still holds."""
    try:
        record = json.loads((root / RECORD).read_text(encoding="utf-8"))
    except FileNotFoundError:
        return [f"{RECORD} is missing. {REBUILD}"]

    problems = []
    if not record.get("toolchain"):
        problems.append(f"{RECORD} does not say which toolchain built the dylibs. {REBUILD}")
    if record.get("sources") != sources_hash(root):
        problems.append(f"The sources in {PACKAGE} changed after {DYLIB} was built. {REBUILD}")
    for rid in RIDS:
        path = dylib_path(root, rid)
        if not path.is_file():
            problems.append(f"{path.relative_to(root)} is missing. {REBUILD}")
        elif record.get("dylibs", {}).get(rid) != hashlib.sha256(path.read_bytes()).hexdigest():
            problems.append(f"{path.relative_to(root)} is not the file {RECORD} records. {REBUILD}")
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--write", action="store_true", help="record the sources, toolchain and dylibs as they are now")
    args = parser.parse_args()
    if args.write:
        write_record(ROOT, toolchain())
        return
    problems = verify(ROOT)
    for problem in problems:
        print(f"::error::{problem}")
    if problems:
        parser.exit(1)
    print(f"{DYLIB} matches {RECORD}.")


if __name__ == "__main__":
    main()
