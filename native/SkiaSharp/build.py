#!/usr/bin/env python3
"""Build Beutl's libSkiaSharp from the source pinned by SkiaSharp 4.152.1.

Linux needs git, Python 3, clang, lld, ninja-build and libfontconfig1-dev.
Windows needs Python 3, Git, Ninja and a VS C++ developer shell for the target
architecture (including the Windows 11 SDK). macOS needs Python 3, Git, Ninja and
Xcode, and builds one universal (arm64 + x86_64) library for the "osx" runtime.
"""

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

from patches import patch_hashes, patches_for


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SOURCE = json.loads((HERE / "source.json").read_text())
EXPORTS = (
    "gr_beutl_backendrendertarget_get_vk_image_layout",
    "gr_beutl_backendrendertarget_set_vk_image_layout",
)
# Windows builds FreeType in for content text; DirectWrite stays the default font manager.
WINDOWS_EXPORTS = ("sk_beutl_fontmgr_create_freetype",)


def exports_for(rid):
    return EXPORTS + (WINDOWS_EXPORTS if rid.startswith("win-") else ())


# The macOS deployment targets upstream SkiaSharp uses for each slice.
MACOS_MIN_VERSIONS = {"arm64": "11.0", "x64": "10.13"}


def run(*args, cwd=None, **kwargs):
    return subprocess.run([str(arg) for arg in args], cwd=cwd, check=True, **kwargs)


def build_slice(source, args, target_os, arch):
    settings = {
        "is_official_build": True,
        "skia_enable_tools": False,
        "target_os": "mac" if target_os == "osx" else target_os,
        "target_cpu": arch,
        "skia_enable_ganesh": True,
        "skia_use_vulkan": True,
        "skia_enable_graphite": True,
        "skia_use_harfbuzz": False,
        "skia_use_icu": False,
        "skia_use_partition_alloc": False,
        "skia_use_piex": True,
        "skia_use_system_expat": False,
        "skia_use_system_freetype2": False,
        "skia_use_system_libjpeg_turbo": False,
        "skia_use_system_libpng": False,
        "skia_use_system_libwebp": False,
        "skia_use_system_zlib": False,
        "skia_enable_skottie": True,
        "extra_cflags": ["-DSKIA_C_DLL", "-DSK_AVOID_SLOW_RASTER_PIPELINE_BLURS",
                         "-DSK_ENABLE_LEGACY_SHADERCONTEXT"],
    }
    if target_os == "osx":
        # Match upstream SkiaSharp's macOS slices: Metal and CoreText, no Vulkan or FreeType.
        del settings["skia_use_vulkan"], settings["skia_use_system_freetype2"]
        settings.update(skia_use_metal=True, min_macos_version=MACOS_MIN_VERSIONS[arch])
        settings["extra_cflags"] += ["-DHAVE_ARC4RANDOM_BUF", "-stdlib=libc++"]
        settings["extra_ldflags"] = ["-stdlib=libc++"]
        filename = "libSkiaSharp.dylib"
    elif target_os == "linux":
        settings.update(cc="clang", cxx="clang++")
        settings["linux_soname_version"] = SOURCE["nativeVersion"]
        settings["extra_cflags"][1:1] = ["-DHAVE_SYSCALL_GETRANDOM", "-DXML_DEV_URANDOM"]
        settings["extra_cflags"].append("-mretpoline" if arch == "x64" else "-mharden-sls=all")
        settings["extra_ldflags"] = [
            "-fuse-ld=lld", "-static-libstdc++", "-static-libgcc",
            f"-Wl,--version-script={(HERE / 'libSkiaSharp.map').as_posix()}",
        ]
        filename = "libSkiaSharp.so"
    else:
        if "VCINSTALLDIR" in os.environ:
            settings["win_vc"] = Path(os.environ["VCINSTALLDIR"]).as_posix().rstrip("/")
        if "VCToolsVersion" in os.environ:
            settings["win_toolchain_version"] = os.environ["VCToolsVersion"]
        # DirectWrite rasterizes grayscale glyphs with coarse coverage, so content text that scales
        # flickers. FreeType comes in only behind the empty custom font manager Beutl creates itself.
        settings.update(skia_enable_fontmgr_win_gdi=False, skia_use_direct3d=True,
                        skia_use_freetype=True, skia_enable_fontmgr_win=True,
                        skia_enable_fontmgr_custom_directory=False, skia_enable_fontmgr_custom_embedded=False)
        settings["extra_cflags"] += ["/MT", "/EHsc", "/guard:cf", "-D_HAS_AUTO_PTR_ETC=1"]
        settings["extra_ldflags"] = ["/guard:cf", "/DELAYLOAD:d3d12.dll", "/DELAYLOAD:dxgi.dll",
                                     "/DELAYLOAD:D3DCOMPILER_47.dll", "/DEFAULTLIB:delayimp"]
        filename = "libSkiaSharp.dll"

    output = source / "out" / (f"{args.rid}-{arch}" if target_os == "osx" else args.rid)
    output.mkdir(parents=True, exist_ok=True)
    (output / "args.gn").write_text("".join(f"{k} = {json.dumps(v)}\n" for k, v in settings.items()))
    run(source / "bin" / ("gn.exe" if os.name == "nt" else "gn"), "gen", output,
        f"--script-executable={sys.executable}", cwd=source)
    run("ninja", "-C", output, "-j", args.jobs, "SkiaSharp", cwd=source)
    return output / (filename + "." + SOURCE["nativeVersion"] if target_os == "linux" else filename)


def build(args):
    target_os, arch = args.rid.split("-") if "-" in args.rid else (args.rid, None)
    host_os = "win" if os.name == "nt" else "linux" if sys.platform == "linux" else "osx"
    host_arch = "arm64" if platform.machine().lower() in ("aarch64", "arm64") else "x64"
    if target_os != host_os or (target_os == "linux" and arch != host_arch):
        raise SystemExit("Build on the target OS; Linux also requires the target architecture.")

    source = args.source_dir.resolve()
    if not (source / ".git").exists():
        source.mkdir(parents=True, exist_ok=True)
        run("git", "init", source)
        run("git", "config", "core.autocrlf", "false", cwd=source)
        run("git", "remote", "add", "origin", SOURCE["repository"], cwd=source)
        run("git", "fetch", "--depth=1", "origin", SOURCE["commit"], cwd=source)
        run("git", "checkout", "--detach", "FETCH_HEAD", cwd=source)
    commit = run("git", "rev-parse", "HEAD", cwd=source, capture_output=True, text=True).stdout.strip()
    if commit != SOURCE["commit"]:
        raise SystemExit(f"Expected Skia {SOURCE['commit']}, found {commit} in {source}.")

    for patch in patches_for(args.rid).values():
        applied = subprocess.run(
            ["git", "apply", "--reverse", "--check", str(patch)], cwd=source,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        ).returncode == 0
        if not applied:
            run("git", "apply", "--check", patch, cwd=source)
            run("git", "apply", patch, cwd=source)

    gn = source / "bin" / ("gn.exe" if os.name == "nt" else "gn")
    stamp = source / ".beutl-deps"
    if not stamp.exists() or stamp.read_text() != commit or not gn.exists():
        run(sys.executable, "tools/git-sync-deps", cwd=source,
            env={**os.environ, "GIT_SYNC_DEPS_SKIP_EMSDK": "1"})
        stamp.write_text(commit)

    if target_os == "osx":
        slices = [build_slice(source, args, target_os, slice_arch) for slice_arch in ("arm64", "x64")]
        library = source / "out" / args.rid / "libSkiaSharp.dylib"
        library.parent.mkdir(parents=True, exist_ok=True)
        run("lipo", "-create", *slices, "-output", library)
        filename = "libSkiaSharp.dylib"
    else:
        library = build_slice(source, args, target_os, arch)
        filename = library.name.split(".so.")[0] + ".so" if target_os == "linux" else library.name

    if target_os == "osx" or arch == host_arch:
        native = ctypes.CDLL(str(library))
        for export in exports_for(args.rid):
            getattr(native, export)
        native.sk_version_get_milestone.restype = ctypes.c_int
        native.sk_version_get_increment.restype = ctypes.c_int
        expected = tuple(map(int, SOURCE["nativeVersion"].split(".")[:2]))
        if (native.sk_version_get_milestone(), native.sk_version_get_increment()) != expected:
            raise SystemExit("Built libSkiaSharp has an unexpected native ABI version.")
        if target_os == "win":
            # The export links even when the FreeType font manager is compiled out; it then returns NULL.
            native.sk_beutl_fontmgr_create_freetype.restype = ctypes.c_void_p
            manager = native.sk_beutl_fontmgr_create_freetype()
            if not manager:
                raise SystemExit("Built libSkiaSharp has no FreeType font manager.")
            native.sk_fontmgr_unref(ctypes.c_void_p(manager))
    else:
        exports = run("dumpbin", "/exports", library, capture_output=True, text=True).stdout
        if any(export not in exports for export in exports_for(args.rid)):
            raise SystemExit("Built libSkiaSharp is missing Beutl's exports.")

    destination = args.output_dir.resolve() / "runtimes" / args.rid / "native"
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copy2(library, destination / filename)
    # Carry the upstream notices with every independently distributed native artifact.
    shutil.copy2(source / "LICENSE", destination / "Skia.LICENSE")
    notices = []
    for directory, children, files in os.walk(source / "third_party"):
        children[:] = sorted(name for name in children if name not in (".git", "out"))
        for name in sorted(files):
            if name.upper().startswith(("LICENSE", "COPYING", "NOTICE", "FTL.TXT")):
                path = Path(directory) / name
                notices.append(f"{path.relative_to(source)}\n\n{path.read_text(encoding='utf-8', errors='replace')}\n")
    if target_os == "linux":
        # The Linux build statically links GCC's C++ runtime.
        copyright_file = Path("/usr/share/doc/libstdc++6/copyright")
        if copyright_file.exists():
            notices.append("GCC runtime\n\n" + copyright_file.read_text())
    (destination / "Skia.NOTICES").write_text("\n".join(notices), encoding="utf-8")
    manifest = {**SOURCE, "rid": args.rid, **patch_hashes(args.rid),
                "binarySha256": hashlib.sha256(library.read_bytes()).hexdigest()}
    (destination / "build.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Built {destination / filename}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=("linux-x64", "linux-arm64", "win-x64", "win-arm64", "osx"))
    parser.add_argument("--source-dir", type=Path, default=ROOT / "artifacts/skia-source")
    parser.add_argument("--output-dir", type=Path, default=ROOT / "src" / "Beutl.Engine")
    parser.add_argument("--jobs", type=int, default=min(os.cpu_count() or 2, 8))
    build(parser.parse_args())
