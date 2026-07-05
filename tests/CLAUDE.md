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
