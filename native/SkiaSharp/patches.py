"""Patches and manifest hashes shared by the native builder and verifier."""

import hashlib
from pathlib import Path


HERE = Path(__file__).resolve().parent


def patches_for(rid):
    patches = {"patchSha256": HERE / "vulkan-image-layout.patch",
               "surfaceContentChangePatchSha256": HERE / "surface-content-change.patch"}
    if rid.startswith("linux-"):
        patches["fontconfigPatchSha256"] = HERE / "fontconfig-missing-family.patch"
    if rid.startswith("win-"):
        patches["freetypeFontMgrPatchSha256"] = HERE / "freetype-fontmgr.patch"
    if rid == "osx":
        patches["macosLinkerPatchSha256"] = HERE / "macos-linker-version.patch"
    return patches


def patch_hashes(rid):
    return {key: hashlib.sha256(path.read_bytes()).hexdigest()
            for key, path in patches_for(rid).items()}
