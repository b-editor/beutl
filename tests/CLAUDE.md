# tests/ — local context

Most projects under `tests/` are NUnit (+ Moq where needed); the exceptions are the two BenchmarkDotNet projects (`Beutl.Benchmarks`, `Beutl.FFmpegBenchmarks`). Use this index when picking the right project for a new test.

## Where new tests go

| Production code under… | Test project |
|---|---|
| `src/Beutl.Engine/Graphics/`, `Animation/`, `Audio/`, `Composition/`, etc. (non-3D) | `tests/Beutl.UnitTests/` |
| `src/Beutl.Engine/Graphics3D/` | `tests/Beutl.UnitTests/Engine/Graphics3D/` |
| `src/Beutl.Engine.SourceGenerators/` | `tests/SourceGeneratorTest/` |
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

CI runs `Beutl.UnitTests` and `Beutl.Graphics3DTests` a second time with the layer installed (`GPU tests
under Vulkan validation` in `.github/workflows/dotnet.yml`). Neither run filters by category: a category has
to be remembered, and a GPU test that forgets one is exactly the test the gate would then not cover. A test
that never reaches the render thread costs only its own runtime on that second pass. Locally:

```bash
BEUTL_REQUIRE_GPU=1 BEUTL_VULKAN_VALIDATION=1 \
  dotnet test tests/Beutl.Graphics3DTests/Beutl.Graphics3DTests.csproj -f net10.0
```

`VulkanValidationGateTests.WhenTheJobAsksForValidation_TheInstanceEnabledIt` fails when the variable is set
but the layer did not load, because a gate that observes nothing must not report success. Without the
Vulkan SDK it will fail for that reason — install the layer before enabling the variable.

One category is held back: `TestCategories.KnownVulkanSkiaLayoutInterop`, declared in both
`Beutl.UnitTests` and `Beutl.Graphics3DTests` under the same name because the validation job filters both
assemblies on it. `RenderTarget` builds its
`SKSurface` once and Skia tracks that image's layout from there, while `VulkanTexture2D` tracks the same
image separately as the backend transitions it — so the two records drift and a barrier eventually names an
`oldLayout` the image has left. Closing that needs a way to read back or command the layout Skia holds,
which SkiaSharp 3.119 does not expose. Those tests run normally in the ordinary suite; only the
validation job skips them, so the gate still covers everything else. Tracked as
[#2263](https://github.com/b-editor/beutl/issues/2263); drop the exclusion from
`.github/workflows/dotnet.yml` once the interop keeps one record.

The same missing API caps what `VulkanContext`'s Skia image allocation hook can promise, on a path that is
*not* in that category. Ganesh allocates its own filter and scratch images through the intercepted
`vkCreateImage` / `vkBindImageMemory` pair, and the hook clears them to transparent at bind time, leaving
them in `TransferDstOptimal` while Ganesh still holds `Undefined` for them. Its first use may therefore
transition out of `Undefined`, which Vulkan permits to discard the contents. Nothing can be reconciled
here: Skia never hands out a backend handle for an image it allocated itself, so the
`GRVkImageInfo.ImageLayout` route `VulkanTexture2D` uses does not apply, and SkiaSharp 3.119.4's
`GRContextOptions` — `AvoidStencilBuffers`, `RuntimeProgramCacheSize`, `GlyphCacheTextureMaximumBytes`,
`AllowPathMaskCaching`, `DoManualMipmapping`, `BufferMapThreshold` — has no clear-on-allocate switch that
would hand the clear back to Skia. The clear still zeroes the backing allocation, which is what stops
SwiftShader from handing a recycled allocation's bytes to a caller; it is not a Vulkan-level guarantee that
those zeros survive the first transition. The path raises no validation message — a transition out of
`Undefined` is always legal — so its tests stay in the validation job.

## Headless E2E tests

The end-to-end suites are built on `Avalonia.Headless.NUnit` (they run on headless CI without xvfb or a GPU). Shared helpers live in the non-test library `tests/Beutl.Testing.Headless/` (`BeutlHomeIsolation`, `HeadlessTestHelpers`).

- `tests/Beutl.E2ETests/` references only `Beutl.*` libraries — UI-less domain scenarios (Scene / serialization / undo-redo via `Beutl.Editor` services) and headless control tests for `Beutl.Controls` property editors. Its `TestApp` loads only FluentAvalonia + `Beutl.Controls` styles.
- `tests/Beutl.HeadlessUITests/` references `src/Beutl` (the one suite that does — see `.claude/rules/csharp.md`) and drives the real shell: `ProjectService` / `EditorService` / `MainViewModel` / `EditViewModel`. Its `TestApp` reproduces `App.RegisterServices` (handlers, registrars, primitive-extension load) minus `App.Initialize`'s `RunStartupTask` (auth / update / restore I/O). The notification and tutorial service handlers are settable through public registration surface; the property-editor handler is still internal, so two `InternalsVisibleTo` grants (Beutl, Beutl.Extensibility) let the harness wire it.

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

## Running

```bash
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings              # all
dotnet test Beutl.slnx -f net10.0 --filter "FullyQualifiedName~<substring>"    # scoped
```

`/beutl-test` and the `beutl-test-runner` subagent automate the scoped case (and the subagent runs in an isolated worktree so trial-and-error does not pollute the main checkout).
