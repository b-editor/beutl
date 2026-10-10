# Development requirements and quality gates

These requirements apply to all Beutl changes regardless of the editor, automation, or review tools used.

## Architecture requirements

- Keep the [GPL/MIT license boundary](gpl-mit-boundary.md) intact: MIT projects communicate with `Beutl.FFmpegWorker` only through `Beutl.FFmpegIpc`.
- Keep both `net10.0` and `net10.0-windows` targets building. A new target framework requires an explicit design decision and corresponding build configuration.
- Add NUnit coverage for new production logic in the matching project under `tests/` (see [`tests/README.md`](../../tests/README.md)).
- Every new Avalonia `UserControl` must enable compiled bindings with `x:CompileBindings="True"` and declare `x:DataType`.
- Treat `.editorconfig`, `xamlstyler.json`, and `dotnet format` as the source of truth for formatting.
- Keep `Beutl.Engine.SourceGenerators` and `tests/SourceGeneratorTest` green when generator inputs, outputs, or public generated contracts change.
- Do not change existing `.github/workflows/*` files without explicit maintainer approval.
- Never force-push `main` or `master`; push rewritten history only to a feature branch.

## Quality gates

Before merging a pull request:

1. Run `dotnet format Beutl.slnx --verify-no-changes`.
2. Run `dotnet build Beutl.slnx` for every affected target framework.
3. Run `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings` and any required platform- or GPU-specific suites.
4. Review the generated coverage report for unexpected regressions. The repository does not currently enforce a fixed coverage threshold.
5. Address all review findings and leave no unresolved review threads.
6. Leave no orphaned TODO comments; describe deferred work in the pull request instead.
