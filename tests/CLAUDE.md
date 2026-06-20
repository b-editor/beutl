# tests/ — local context

The unit-test suites here are NUnit (+ Moq where needed) — but not every project under `tests/` is an NUnit suite (some are BenchmarkDotNet benchmarks, manual visual harnesses, or interactive previewers; see the notes below the table). Use this index when picking the right project for a new test.

## Where new tests go

| Production code under… | Test project |
|---|---|
| `src/Beutl.Engine/Graphics/`, `Animation/`, `Audio/`, `Composition/`, etc. (non-3D) | `tests/Beutl.UnitTests/` |
| `src/Beutl.Engine/Graphics3D/` | `tests/Beutl.UnitTests/Engine/Graphics3D/` |
| `src/Beutl.Engine.SourceGenerators/` | `tests/SourceGeneratorTest/` |
| `src/Beutl.FFmpegIpc/` and IPC-level contract tests against `Beutl.FFmpegWorker` | `tests/Beutl.FFmpegIpc.Tests/` |
| `src/Beutl.Editor*/` | `tests/Beutl.UnitTests/Editor*/` |
| `src/Beutl.NodeGraph/` | `tests/Beutl.UnitTests/NodeGraph/` |
| `src/Beutl.Extensions.AVFoundation/` (macOS only) | `tests/Beutl.Extensions.AVFoundation.Tests/` |

`tests/Beutl.Benchmarks/` and `tests/Beutl.FFmpegBenchmarks/` are BenchmarkDotNet projects, not NUnit — do not add unit tests there.

`tests/Beutl.Graphics3DTests/` is an executable visual harness for manual Graphics3D checks, not an NUnit test project. Graphics3D NUnit tests live under `tests/Beutl.UnitTests/Engine/Graphics3D/`.

`tests/DirectoryViewTest/`, `tests/PackageSample/`, `tests/TextFormattingPlayground/`, `tests/PropertyEditorViewTests/`, `tests/EnumerateFontFamilies/`, `tests/XamlPreview/` are interactive Avalonia previewers, not test harnesses — running them launches a window.

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
