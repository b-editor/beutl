# Test suite guide

Most projects under `tests/` are NUnit (+ Moq where needed); the exceptions are the two BenchmarkDotNet projects (`Beutl.Benchmarks`, `Beutl.FFmpegBenchmarks`). Use this index when picking the right project for a new test.

## Where new tests go

| Production code under… | Test project |
|---|---|
| `src/Beutl.Engine/Graphics/`, `Animation/`, `Audio/`, `Composition/`, etc. (non-3D) | `tests/Beutl.UnitTests/` |
| `src/Beutl.Engine/Graphics3D/` | `tests/Beutl.UnitTests/Engine/Graphics3D/` |
| `src/Beutl.Engine.SourceGenerators/` | `tests/SourceGeneratorTest/` |
| Public authoring contracts | `tests/Beutl.PublicApiContractTests/` |
| `src/Beutl.FFmpegIpc/` and IPC-level contract tests against `Beutl.FFmpegWorker` | `tests/Beutl.FFmpegIpc.Tests/` |
| `src/Beutl.FFmpegWorker/` direct in-process FFmpeg-native types (e.g. `FFmpegEncodingController`) | `tests/Beutl.FFmpegWorker.Tests/` |
| `src/Beutl.Editor*/` | `tests/Beutl.UnitTests/Editor*/` |
| `src/Beutl.NodeGraph/` | `tests/Beutl.UnitTests/NodeGraph/` |
| `src/Beutl.Extensions.AVFoundation/` (macOS only) | `tests/Beutl.Extensions.AVFoundation.Tests/` |
| `Beutl.Controls` property editors + UI-less domain workflows | `tests/Beutl.E2ETests/` (headless, no `src/Beutl` ref) |
| Full app-shell flows (project / editor / export orchestration) | `tests/Beutl.HeadlessUITests/` (headless, references `src/Beutl`) |

`tests/Beutl.Benchmarks/` and `tests/Beutl.FFmpegBenchmarks/` are BenchmarkDotNet projects, not NUnit — do not add unit tests there.

`tests/Beutl.Graphics3DTests/` is a Vulkan-gated NUnit suite for GPU-backed Graphics3D rendering checks — its tests self-skip (`Assert.Ignore`) when no Vulkan/MoltenVK device is available, so they are safe to run in CI. GPU-free Graphics3D logic tests (hit-testing, render-scale, density, etc.) still go under `tests/Beutl.UnitTests/Engine/Graphics3D/`.

`tests/Beutl.FFmpegWorker.Tests/` covers the GPL worker's direct in-process FFmpeg-calling types (e.g. `FFmpegEncodingController`). Because the worker is GPL-3.0 and MIT projects must not `ProjectReference` it, this project reaches those types by **source-linking** them (`<Compile Include>` under `BEUTL_FFMPEG_WORKER`), the same firewall-preserving pattern `Beutl.FFmpegBenchmarks` uses — never a `ProjectReference`. It is `IsPackable=false` (never distributed), and its native tests self-skip (`Assert.Ignore`) when the FFmpeg shared libraries are not available.

The interactive Avalonia previewers / sample apps no longer live here. The sample extension package `PackageSample` was moved out of `tests/` (and out of `Beutl.slnx`, so CI does not build it) and now lives under `samples/`. Running it launches a window; it is not a test harness.

## Vulkan validation gate

Vulkan validation is off by default and enabled with `BEUTL_VULKAN_VALIDATION=1`, which requires
`VK_LAYER_KHRONOS_validation` to be installed. When it is on, `VulkanTestEnvironment.InvokeOnRenderThread`
and its `GpuTestEnvironment` twin read `VulkanValidationErrorLog.Shared` before and after every
render-thread invocation and fail the test that reported an error, so API misuse the driver is not required
to diagnose — a nested render pass instance, a handle from another device — cannot pass as green.

CI builds the solution once and shares its binaries with parallel test jobs in `.github/workflows/dotnet.yml`.
The unit and headless UI suites use exhaustive, disjoint filters in `.github/scripts/ci_tests.py`; a catch-all
shard covers new namespaces. Every `Beutl.UnitTests` and `Beutl.Graphics3DTests` test also runs in a separate
Vulkan validation job with the layer installed. Neither pass filters by category: a category has to be
remembered, and a GPU test that forgets one is exactly the test the gate would then not cover. Locally:

```bash
BEUTL_REQUIRE_GPU=1 BEUTL_VULKAN_VALIDATION=1 \
  dotnet test tests/Beutl.Graphics3DTests/Beutl.Graphics3DTests.csproj -f net10.0
```

`VulkanValidationGateTests.WhenTheJobAsksForValidation_TheInstanceEnabledIt` fails when the variable is set
but the layer did not load, because a gate that observes nothing must not report success. Without the
Vulkan SDK it will fail for that reason — install the layer before enabling the variable.

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

Skia-owned images have a separate initialization limitation in `VulkanContext`'s allocation hook.
Ganesh allocates its own filter and scratch images through the intercepted
`vkCreateImage` / `vkBindImageMemory` pair, and the hook clears them to transparent at bind time, leaving
them in `TransferDstOptimal` while Ganesh still holds `Undefined` for them. Its first use may therefore
transition out of `Undefined`, which Vulkan permits to discard the contents. Nothing can be reconciled
here: Skia never hands out a backend handle for an image it allocated itself, so the
shared mutable state route `VulkanTexture2D` uses does not apply, and SkiaSharp 4.152.1's
`GRContextOptions` — `AvoidStencilBuffers`, `RuntimeProgramCacheSize`, `GlyphCacheTextureMaximumBytes`,
`AllowPathMaskCaching`, `DoManualMipmapping`, `BufferMapThreshold` — has no clear-on-allocate switch that
would hand the clear back to Skia. The clear still zeroes the backing allocation, which is what stops
SwiftShader from handing a recycled allocation's bytes to a caller; it is not a Vulkan-level guarantee that
those zeros survive the first transition. The path raises no validation message — a transition out of
`Undefined` is always legal — so its tests stay in the validation job.

## Headless E2E tests

The end-to-end suites are built on `Avalonia.Headless.NUnit` (they run on headless CI without xvfb or a GPU). Shared helpers live in the non-test library `tests/Beutl.Testing.Headless/` (`BeutlHomeIsolation`, `HeadlessTestHelpers`).

- `tests/Beutl.E2ETests/` references only `Beutl.*` libraries — UI-less domain scenarios (Scene / serialization / undo-redo via `Beutl.Editor` services) and headless control tests for `Beutl.Controls` property editors. Its `TestApp` loads only FluentAvalonia + `Beutl.Controls` styles.
- `tests/Beutl.HeadlessUITests/` references `src/Beutl` (the only suite that does) and drives the real shell: `ProjectService` / `EditorService` / `MainViewModel` / `EditViewModel`. Its `TestApp` reproduces `App.RegisterServices` (handlers, registrars, primitive-extension load) minus `App.Initialize`'s `RunStartupTask` (auth / update / restore I/O). The notification and tutorial service handlers are settable through public registration surface; the property-editor handler is still internal, so two `InternalsVisibleTo` grants (Beutl, Beutl.Extensibility) let the harness wire it.

Conventions:
- UI tests use `[AvaloniaTest]` (not `[Test]`) and run on the Avalonia UI thread; pure domain tests use `[Test]`.
- The headless `Application` uses Skia rendering (`UseHeadlessDrawing = false`) so real templates (FontIcon glyphs, text) inflate; a layout-only suite can keep the default stub drawing.
- Shell tests that drive global singletons (`ProjectService.Current`, `EditorService.Current`, …) must reset them at the START of each `[AvaloniaTest]` body, NOT in `[SetUp]`/`[TearDown]` — those run off the UI thread, where mutating Avalonia / reactive state silently fails. Control- and domain-level tests that never touch those singletons (e.g. the `Beutl.E2ETests` property-editor tests, which don't even reference `src/Beutl`) need no such reset.
- `BEUTL_HOME` is isolated to a per-assembly temp dir via a `[SetUpFixture]` calling `BeutlHomeIsolation`.
- GPU-preview / FFmpeg-export tests must self-skip (`Assert.Ignore` + `TestContext.WriteLine` the reason) when the GPU / worker is unavailable — same spirit as `VulkanTestEnvironment` and `Beutl.Graphics3DTests`.

## NUnit conventions

- `[TestFixture]` on the class, `[Test]` on each method
- Use `[TestCase(...)]` over data-driven loops inside a single `[Test]`
- `Assert.That(...)` (constraint API), not `Assert.AreEqual` — match existing files
- Moq matchers: prefer `It.IsAny<T>()` only when the argument is genuinely irrelevant; otherwise capture and assert explicitly

## Keeping tests fast

- Run expensive preparation once per distinct scenario. Assertions about the same generated output
  belong in one `Assert.Multiple` block; keep different inputs and regression scenarios independent.
- Source-generator/analyzer tests share immutable framework metadata and baseline compilations.
  Add a fresh source tree and analyzer/driver for each scenario, and retain the compiler-error check.
  Do not cache analyzer results or discover references from the assemblies earlier tests happened to load.
- Coordinate asynchronous work with completion signals. When an async call has synchronously reached
  a blocked operation before returning, assert its pending state directly instead of sleeping first.
  Keep bounded waits for real completion/deadline behavior, and release blocked work in `finally`.
- Keep a fresh Git repository per integration test. Fixed fixture configuration is written together;
  the Git commands whose behavior is under test still run against the real executable.
- Independent Git fixtures opt into `ParallelScope.Self`: fixtures can overlap, but their test
  cases still run sequentially. Tests that change process-wide environment variables must stay
  `[NonParallelizable]`; `GitCliRunnerTests` remains sequential as a whole. Stopwatch-based
  performance fixtures also stay non-parallel so other tests cannot consume their timing budget.
- Package crash-recovery tests start the unit-test executable with `--package-install-worker`.
  This skips VSTest startup, discovery, and assembly setup in each child; the worker validates its
  isolated home before initializing the installer. Keep real process termination, lock contention,
  recovery, and repeat-recovery assertions. Normal test runs still use `dotnet test`.

## Running

```bash
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings              # all
dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj --filter "FullyQualifiedName~<substring>"
dotnet test tests/SourceGeneratorTest/SourceGeneratorTest.csproj
```

For repeated runs without source changes, add `--no-build` to the project command. A filter on the
whole solution still builds and starts unrelated test projects. To compare execution time, build
each revision first, run the same project with `--no-build --logger trx` without other builds/tests
running, and compare several runs. Report the executed test count as well as time; a skipped GPU or
native suite is not a speed improvement.
