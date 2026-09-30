#!/usr/bin/env python3
"""Verify the pinned source, patch, hashes and notices of the bundled native runtimes."""

import argparse
import hashlib
import json
from pathlib import Path


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
RIDS = ("linux-x64", "linux-arm64", "win-x64", "win-arm64")


def verify(native_root, rids):
    source = json.loads((HERE / "source.json").read_text(encoding="utf-8"))
    patch_hash = hashlib.sha256((HERE / "vulkan-image-layout.patch").read_bytes()).hexdigest()
    for rid in rids:
        directory = native_root / "runtimes" / rid / "native"
        manifest = json.loads((directory / "build.json").read_text(encoding="utf-8"))
        for key, expected in {**source, "rid": rid, "patchSha256": patch_hash}.items():
            if manifest.get(key) != expected:
                raise ValueError(f"{rid}: {key} does not match the pinned source and patch; rebuild this runtime.")
        filename = "libSkiaSharp.so" if rid.startswith("linux-") else "libSkiaSharp.dll"
        digest = hashlib.sha256((directory / filename).read_bytes()).hexdigest()
        if manifest.get("binarySha256") != digest:
            raise ValueError(f"{rid}: {filename} does not match its recorded SHA-256.")
        for notice in ("Skia.LICENSE", "Skia.NOTICES"):
            if not (directory / notice).read_bytes():
                raise ValueError(f"{rid}: {notice} is empty.")
        print(f"Verified {rid}: SkiaSharp {source['skiaSharpVersion']}, SHA-256 {digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-root", type=Path, default=ROOT / "src" / "Beutl.Engine")
    parser.add_argument("--rid", choices=RIDS, action="append")
    args = parser.parse_args()
    try:
        verify(args.native_root, args.rid or RIDS)
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
