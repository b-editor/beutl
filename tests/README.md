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
| `src/Beutl.AgentToolkit/` | `tests/Beutl.AgentToolkit.Tests/` |
| `src/Beutl.Extensions.AVFoundation/` (macOS only) | `tests/Beutl.Extensions.AVFoundation.Tests/` |
| `src/Beutl.Extensions.MediaFoundation/` (pure-logic tests run on every OS; native fixtures use `[Platform("Win")]`) | `tests/Beutl.Extensions.MediaFoundation.Tests/` |
| `Beutl.Controls` property editors + UI-less domain workflows | `tests/Beutl.E2ETests/` (headless, no `src/Beutl` ref) |
| Full app-shell flows (project / editor / export orchestration) | `tests/Beutl.HeadlessUITests/` (headless, references `src/Beutl`) |

`tests/Beutl.Graphics3DTests/` is a Vulkan-gated NUnit suite for GPU-backed Graphics3D rendering checks — its tests self-skip (`Assert.Ignore`) when no Vulkan/MoltenVK device is available, so they are safe to run in CI. GPU-free Graphics3D logic tests (hit-testing, render-scale, density, etc.) still go under `tests/Beutl.UnitTests/Engine/Graphics3D/`.

`tests/Beutl.FFmpegWorker.Tests/` covers the GPL worker's direct in-process FFmpeg-calling types (e.g. `FFmpegEncodingController`). Because the worker is GPL-3.0 and MIT projects must not `ProjectReference` it, this project reaches those types by **source-linking** them (`<Compile Include>` under `BEUTL_FFMPEG_WORKER`), the same firewall-preserving pattern `Beutl.FFmpegBenchmarks` uses — never a `ProjectReference`. It is `IsPackable=false` (never distributed), and its native tests self-skip (`Assert.Ignore`) when the FFmpeg shared libraries are not available.

CI's `other` suite, which runs `Beutl.FFmpegWorker.Tests` and `Beutl.FFmpegIpc.Tests`, installs the FFmpeg 8 shared libraries and sets `BEUTL_REQUIRE_FFMPEG=1`. `.github/scripts/ci_tests.py` then fails the suite when a test skips because FFmpeg is missing. It recognizes such a skip by its reason: "FFmpeg native…", "FFmpeg natives", "FFmpeg shared libraries" or "FFmpeg worker…", followed later by "unavailable", "not available" or "not present", as in "FFmpeg native libraries are not available.". Keep that wording for new FFmpeg-backed tests, and word other reasons, such as a missing encoder, differently.

GPU-backed tests go through `tests/TestGpu.cs`, which each GPU test project links. A test calls `TestGpu.RequireAnyDevice()` when any Vulkan device will do, SwiftShader included; `VulkanTestEnvironment.EnsureAvailable()` and `GpuTestEnvironment.EnsureAvailable()` call it. Without a device the test skips with a reason that starts with "No GPU device:". An expectation that holds only on a hardware GPU calls `TestGpu.RequireHardwareGpu(...)` instead, which skips on SwiftShader or another CPU implementation with a reason that starts with "Needs a hardware GPU:". CI's test shards and its Linux ARM64 job provide SwiftShader and set `BEUTL_REQUIRE_GPU=1`, so `ci_tests.py` fails them when a test skipped with "No GPU device:". Hardware-only skips do not fail them: no CI job has a hardware GPU. Only `ci_tests.py` reads `BEUTL_REQUIRE_GPU`; a plain `dotnet test` reports the skips and passes.

For each shard and assembly, `ci_tests.py` prints how many tests were not executed, grouped by reason, to the log and to the job summary. `--check-results <directory>` does the same for the TRX files of `dotnet test` runs that did not go through the script, and like a shard it fails a run that found no tests. Skips for a reason other than a capability the job declares are only counted.

`samples/PackageSample` is a sample app, not a test harness.

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
BEUTL_VULKAN_VALIDATION=1 dotnet test tests/Beutl.Graphics3DTests/Beutl.Graphics3DTests.csproj -f net10.0
```

`VulkanValidationGateTests.WhenTheJobAsksForValidation_TheInstanceEnabledIt` fails when the variable is set
but the layer did not load, because a gate that observes nothing must not report success. Without the
Vulkan SDK it will fail for that reason — install the layer before enabling the variable.

Vulkan builds use the libSkiaSharp binaries pinned under `src/Beutl.Engine/runtimes/`. Their patches and
how to update them are described in `native/SkiaSharp/README.md`.

## Headless E2E tests

The end-to-end suites are built on `Avalonia.Headless.NUnit` (they run on headless CI without xvfb or a GPU). Shared helpers live in the non-test library `tests/Beutl.Testing.Headless/`.

- `tests/Beutl.E2ETests/` references only `Beutl.*` libraries — UI-less domain scenarios (Scene / serialization / undo-redo via `Beutl.Editor` services) and headless control tests for `Beutl.Controls` property editors. Its `TestApp` loads only FluentAvalonia + `Beutl.Controls` styles.
- `tests/Beutl.HeadlessUITests/` references `src/Beutl` (the only suite that does) and drives the real shell: `ProjectService` / `EditorService` / `MainViewModel` / `EditViewModel`. Its `TestApp` reproduces `App.RegisterServices` (handlers, registrars, primitive-extension load) minus `App.Initialize`'s `RunStartupTask` (auth / update / restore I/O). The notification and tutorial service handlers are settable through public registration surface; the property-editor handler is still internal, so two `InternalsVisibleTo` grants (Beutl, Beutl.Extensibility) let the harness wire it.

Conventions:
- UI tests use `[AvaloniaTest]` (not `[Test]`) and run on the Avalonia UI thread; pure domain tests use `[Test]`.
- The headless `Application` uses Skia rendering (`UseHeadlessDrawing = false`) so real templates (FontIcon glyphs, text) inflate; a layout-only suite can keep the default stub drawing.
- Shell tests that drive global singletons (`ProjectService.Current`, `EditorService.Current`, …) must reset them at the START of each `[AvaloniaTest]` body, NOT in `[SetUp]`/`[TearDown]` — those run off the UI thread, where mutating Avalonia / reactive state silently fails. Control- and domain-level tests that never touch those singletons (e.g. the `Beutl.E2ETests` property-editor tests, which don't even reference `src/Beutl`) need no such reset.
- `BEUTL_HOME` is isolated to a per-assembly temp dir via a `[SetUpFixture]` calling `BeutlHomeIsolation`.
- GPU-preview tests start with `TestGpu.RequireAnyDevice()`, which skips them when no GPU is available. FFmpeg-export tests must self-skip (`Assert.Ignore` + `TestContext.WriteLine` the reason) when the worker is unavailable.

## Format manifest

`tests/Beutl.HeadlessUITests/FormatManifest.txt` lists the names that Beutl writes into project files and its stores
and resolves by exact match when it reads them back (#2875). For every `ICoreSerializable` class in the assemblies
built from `src/` it records the `$type`, whether a member declared as the class writes `$type` (not when the class is
sealed) and the top-level keys of a default instance. It also records the names that built-in extensions put into
dock layouts, tool tab pins, `keymap.json`, the decoder and editor priorities, output profiles and presets, and
`extensionsSettings.json`. `FormatManifestTests` builds the list by reflection and fails when it differs from the file,
so a rename shows up in the diff of the pull request that makes it.

When the change is intended, regenerate the file and commit it with the change:

```bash
BEUTL_UPDATE_FORMAT_MANIFEST=1 dotnet test tests/Beutl.HeadlessUITests/Beutl.HeadlessUITests.csproj -f net10.0 \
  --filter "FullyQualifiedName~FormatManifestTests"
```

A new project under `src/`, at any depth, goes into `FormatManifest.ScannedProjects`, or into `SkippedProjects` with the
reason it is left out. `Beutl.HeadlessUITests` references `Beutl.Extensions.MediaFoundation` so that the manifest is the
same on every OS. Like the app, a Windows ARM64 build leaves that x64-only decoder out, and the comparison is skipped
there.

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

No CI job runs these platform-only tests, so run them locally when you change what they cover:
`MediaFoundationFallbackIntegrationTests` in `Beutl.FFmpegIpc.Tests` (Windows with FFmpeg installed) and the
`[Platform("MacOSX")]` tests in `Beutl.Graphics3DTests/ShaderDescriptionSpirvEquivalenceTests.cs` (macOS).
