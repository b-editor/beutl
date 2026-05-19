---
description: |
  Run Beutl's NUnit test suite (full or scoped). Use when the user asks to run tests, verify a fix,
  reproduce a failure, or check coverage of a specific module. Triggers on "テスト走らせて",
  "run tests", "verify this", "is this green?". Always confirm the test scope before running —
  the full suite is slow.
allowed-tools: Bash(dotnet test:*) Bash(dotnet build:*)
argument-hint: "[FullyQualifiedName-substring|project-path]"
---

# Run Beutl tests

The full Beutl test suite is slow (minutes). **Always confirm scope with the user** before running unless they have specified one in this turn or `$ARGUMENTS` is non-empty. Use AskUserQuestion with these defaults:

- **Scope**: full suite (`Beutl.slnx`) / single project (e.g. `tests/Beutl.UnitTests/Beutl.UnitTests.csproj`) / filter by FullyQualifiedName substring (e.g. `Beutl.Engine.Tests.MyFeature`)
- **Verbosity**: `normal` (default) / `detailed` (when chasing a flaky failure)

Once scope is decided, run the matching command:

```bash
# Full suite (only when truly needed)
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings \
  --logger "console;verbosity=normal"

# Single project
dotnet test <path/to/Tests.csproj> -f net10.0 --settings coverlet.runsettings \
  --logger "console;verbosity=normal"

# Filter by FQN substring
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings \
  --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~<substring>"
```

If `$ARGUMENTS` is provided, treat it as the FQN substring and skip the prompt.

## Notes

- Framework `net10.0` matches `.github/workflows/dotnet.yml`. There is also a `net10.0-windows` target for Windows-specific tests; do not switch without reason.
- Coverlet settings live in `coverlet.runsettings`.
- Test framework: **NUnit + Moq**. Tests live under `tests/` in per-area projects (e.g. `tests/Beutl.UnitTests/`, `tests/Beutl.Engine.Tests/`, `tests/SourceGeneratorTest/`, `tests/Beutl.FFmpegIpc.Tests/`).
- Benchmark projects (`tests/Beutl.Benchmarks`, `tests/Beutl.FFmpegBenchmarks`) use BenchmarkDotNet; exclude them by selecting a single test project rather than the whole solution when iterating.
- For deeper debugging without polluting the working tree, prefer the `beutl-test-runner` subagent (it runs in an isolated worktree).

## On failure

Report up to 10 failing `FullyQualifiedName` entries with the first line of each error message, then stop and wait for direction. Do NOT loop attempting fixes without checking in.
