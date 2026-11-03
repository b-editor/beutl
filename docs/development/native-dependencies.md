# Forks and prebuilt native dependencies

Everything Beutl uses that is not an ordinary managed package from NuGet is listed here: what it is, where it comes from, and what would let us stop carrying it. Add a row when you add a fork or a native binary, and remove it when its exit condition is met.

## What we build or fork

| Dependency | What we carry | Where | Upstream | Exit condition |
| --- | --- | --- | --- | --- |
| XTerm.NET | Fork, as a submodule on the `beutl` branch: Kitty keyboard protocol, parser and erase fixes | `external/XTerm.NET` | [tomlm/XTerm.NET](https://github.com/tomlm/XTerm.NET) | Upstream takes these changes |
| Iciclecreek.Avalonia.Terminal | Fork, as a submodule on the `beutl` branch: Avalonia 12, glyph and clip fixes | `external/Iciclecreek.Avalonia.Terminal` | [tomlm/Iciclecreek.Avalonia.Terminal](https://github.com/tomlm/Iciclecreek.Avalonia.Terminal) | Upstream supports Avalonia 12 and takes these changes |
| libSkiaSharp | Patched build for every runtime, with binaries committed | `native/SkiaSharp`, `src/Beutl.Engine/runtimes` | [mono/SkiaSharp](https://github.com/mono/SkiaSharp), [mono/skia](https://github.com/mono/skia) | Every patch below is gone |
| libBeutlDataChannel | Our C ABI over a patched libdatachannel and Mbed TLS, with binaries committed | `native/libdatachannel` | [paullouisageneau/libdatachannel](https://github.com/paullouisageneau/libdatachannel), [Mbed-TLS/mbedtls](https://github.com/Mbed-TLS/mbedtls) | The patch and `packetizer.hpp` go as below; the C ABI in `preview.cpp` stays ours |
| libBeutlAVF | Our own Swift library, with binaries committed because only macOS can build it | `src/Beutl.Extensions.AVFoundation` | None | A macOS job builds it for every package, so nothing has to be committed |

### What each patch waits for

| Patch | Exit condition |
| --- | --- |
| `native/SkiaSharp/vulkan-image-layout.patch` | SkiaSharp's C API exposes a backend texture's Vulkan image layout |
| `native/SkiaSharp/surface-content-change.patch` | SkiaSharp's C API exposes `SkSurface::notifyContentWillChange` |
| `native/SkiaSharp/freetype-fontmgr.patch` | SkiaSharp's C API can create a FreeType font manager on Windows |
| `native/SkiaSharp/fontconfig-missing-family.patch` | Skia stops family enumeration on `FcResultNoMatch` by itself |
| `native/SkiaSharp/macos-linker-version.patch` | The pinned Skia passes its minimum macOS version in a form the Xcode linker accepts |
| `native/libdatachannel/color-space-id.patch` | Upstream takes the two-byte RTP extension ID fix |
| `native/libdatachannel/packetizer.hpp` (not a patch) | Upstream fragments VP8 and VP9 payloads |

## What others build

These are not ours to patch, so they have no exit condition: they move with their package version, and FFmpeg with the user's install.

| Native code | Comes from | Upstream |
| --- | --- | --- |
| MoltenVK | `Silk.NET.MoltenVK.Native` | [KhronosGroup/MoltenVK](https://github.com/KhronosGroup/MoltenVK) |
| Vulkan loader | `Silk.NET.Vulkan.Loader.Native` | [KhronosGroup/Vulkan-Loader](https://github.com/KhronosGroup/Vulkan-Loader) |
| SwiftShader | `Silk.NET.Vulkan.SwiftShader.Native` | [google/swiftshader](https://github.com/google/swiftshader) |
| shaderc | `Silk.NET.Shaderc.Native` | [google/shaderc](https://github.com/google/shaderc) |
| OpenAL Soft | `Silk.NET.OpenAL.Soft.Native` | [kcat/openal-soft](https://github.com/kcat/openal-soft) |
| Assimp | `Ultz.Native.Assimp` | [assimp/assimp](https://github.com/assimp/assimp) |
| HarfBuzz | `HarfBuzzSharp`, `HarfBuzzSharp.NativeAssets.Linux` | [harfbuzz/harfbuzz](https://github.com/harfbuzz/harfbuzz) |
| Avalonia's platform libraries | Avalonia's packages, transitively | [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) |
| FFmpeg | Not bundled: downloaded from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) on Windows and Linux and installed with Homebrew on macOS, for the GPL worker only (see the [license boundary](gpl-mit-boundary.md)) | [FFmpeg/FFmpeg](https://github.com/FFmpeg/FFmpeg) |

## How the committed binaries are checked

Each binary we commit carries a record of what built it, and CI compares the two.

- **libSkiaSharp:** `python3 native/SkiaSharp/verify.py` checks the source, patch and binary hashes against `build.json` in each runtime, and the architecture and exports of the binary. See `native/SkiaSharp/README.md`.
- **libBeutlDataChannel:** the source pins and SHA-256 manifests sit next to each runtime. See `native/libdatachannel/README.md`.
- **libBeutlAVF:** `python3 build/avf_build_record.py` checks a hash of the Swift sources and of each dylib against `src/Beutl.Extensions.AVFoundation/runtimes/build.json`, which also names the toolchain. `build/test_avf_build_record.py` runs that check in CI, so a change to the Swift sources committed without new dylibs fails there.

To change libBeutlAVF, edit the Swift sources and run `dotnet build src/Beutl.Extensions.AVFoundation` on macOS. The Swift build overwrites both dylibs and rewrites `build.json`; commit all three with the source change.
