# libSkiaSharp

Vulkan builds require Beutl's libSkiaSharp, built from the exact Skia commit pinned by SkiaSharp 4.152.1.
`native/SkiaSharp/vulkan-image-layout.patch` exposes the layout state shared by a backend texture
and its Skia surfaces. The backend retains that handle, reads its state after flushing Skia, and reports
its own transitions through the same state. This includes allocation clears, snapshots and reused 3D
surfaces; an initial `Undefined` layout must be replaced when the backend clears the image, before Skia
can discard that clear. These paths are covered by `SkiaVulkanLayoutInteropTests` and
`SkiaImageState_FollowsInitializationBeforeUntouchedSnapshot`.
`native/SkiaSharp/surface-content-change.patch` lets the engine drop a surface's cached snapshot once
its draws are recorded, so Skia can skip the copy it schedules for a snapshot of a wrapped texture.

The pinned Linux x64/ARM64, Windows x64/ARM64 and universal macOS binaries, build manifests and notices
are committed under `src/Beutl.Engine/runtimes/`. Ordinary `dotnet build`, `dotnet test` and `dotnet run`
use those files directly; no native build or artifact download is needed. Published apps and the engine's
NuGet package include them too. CI verifies the pinned source, patch and binary hashes, target architecture,
and required exports with `python3 native/SkiaSharp/verify.py`. The ELF/PE/Mach-O checks do not load the
libraries, so all five runtimes can be verified on any host. The macOS runtime renders through Skia's Metal
backend. It carries both patches above, with the layout exports compiled as no-ops because it has no
Vulkan, plus `native/SkiaSharp/macos-linker-version.patch` for the Xcode linker.

Linux also applies `native/SkiaSharp/fontconfig-missing-family.patch`, which stops family enumeration
when Fontconfig returns `FcResultNoMatch` for a missing `family` property (for example, some WOFF
fonts). Each Linux manifest records this patch separately; the Windows runtimes do not use it.
`python3 native/SkiaSharp/test_fontconfig.py -v` checks the real native font manager in subprocesses
with a 20-second timeout, using isolated Fontconfig configurations and the existing test fonts.
It covers missing families mixed with usable fonts, only missing families, and normal family aliases
and deduplication. It also checks that the listed families can still create typefaces. No system fonts
or configuration are changed. The native Linux build workflow runs these checks for both architectures,
and the ordinary .NET workflow includes them in Python test discovery.

When updating `native/SkiaSharp/source.json` or the Vulkan patch, rebuild all four runtimes with the manual
`Build libSkiaSharp` workflow. For the Fontconfig patch, select only `["linux-x64", "linux-arm64"]`
in the workflow's `rids` input.
Extract each artifact's `runtimes/` directory into `src/Beutl.Engine/`,
run the verification script, and commit the binaries together with their `build.json` and notices.
To rebuild locally on Linux, install `clang`, `lld`, `ninja-build`, `libfontconfig1-dev`,
`libgl1-mesa-dev` and `libegl1-mesa-dev`, then run `python3 native/SkiaSharp/build.py --rid linux-x64`
(or `linux-arm64` on ARM). On Windows, use `--rid win-x64` or `win-arm64` from the matching Visual
Studio C++ developer shell, with Python 3 and Ninja installed. The script updates the matching committed
runtime by default; `--output-dir` selects a separate directory. Builds can use such a directory via an
absolute `BeutlSkiaSharpNativeRoot` ending in a directory separator.
