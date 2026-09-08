## Guidelines for Contributing to Beutl

Thanks for your interest in contributing! This guide covers how to set up a
local build, how to test and format your changes, and the conventions we follow.

### Prerequisites

- **.NET SDK** — version `10.0.100` or newer is pinned in
  [`global.json`](global.json) (`rollForward: latestFeature`). Install the
  matching SDK so the build resolves the same version CI uses.
- **Git** with the ability to push a feature branch.
- FFmpeg is **not** a build prerequisite: it runs as a separate
  `Beutl.FFmpegWorker` process reached over IPC, and is installed at runtime.

Beutl dual-targets `net10.0` and `net10.0-windows`. Windows-only targets build
on Windows; on Linux/macOS use `-f net10.0` where a single framework is needed.

### Build / Test / Format

```bash
dotnet build Beutl.slnx                                            # build
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings  # test
dotnet format Beutl.slnx                                           # format
./build.sh <Target>                                                # Nuke (same as CI)
```

`dotnet format` is enforced in CI ([Format check](.github/workflows/format-check.yml)),
so run it before pushing.

### Running / Debugging

The application entry point is the `src/Beutl` project. Run it with
`dotnet run --project src/Beutl` (or set it as the startup project in your IDE).

### Pull request

To avoid duplicating work that may already be in progress, it is recommended to
open an issue before submitting a PR. If the changes are minor, you may not need
to open an issue.

To keep the history clean, **do not forget to rebase and force push.**

The PR template asks for a description, affected areas, a test plan, and any
breaking changes — please fill it in. A few rules CI and reviewers enforce:

- **New logic ships with a NUnit test** under `tests/` (e.g.
  `tests/Beutl.UnitTests/`, `tests/SourceGeneratorTest/`).
- **New XAML uses compiled bindings** (`x:CompileBindings="True"` +
  `x:DataType`).
- **Do not cross the GPL/MIT boundary**: MIT projects must not take a
  `ProjectReference` to `Beutl.FFmpegWorker`; reach it only via IPC.

The complete build, test, coverage, and architecture requirements are listed in
the [development quality gates](docs/development/quality-gates.md).
Packaging changes that include the FFmpeg worker must also follow the
[GPL/MIT distribution requirements](docs/development/gpl-mit-boundary.md).

### Public API design

Prefer a clearly better design over preserving an awkward API by default. Keep
public abstractions orthogonal, and favor interfaces, virtual hooks, and
composable primitives that let plugin authors support use cases outside the
current application.

Do not add `[Obsolete]` shims, duplicate `V2` types, legacy parameters, or
compatibility wrappers merely to avoid updating call sites. Update in-tree call
sites in the same change. A published extensibility contract may use a
deprecation window only when maintainers explicitly choose one and the PR
documents the removal target. When the trade-off is non-obvious, explain it in
the PR instead of silently choosing either compatibility or a sweeping rewrite.

### Change scope and branch safety

Finish defects and regressions that belong to the stated change. If work is
genuinely separate or blocked by information only a maintainer can provide,
describe that boundary explicitly instead of leaving an undocumented TODO.

Do not change existing files under `.github/workflows/` without explicit
maintainer approval. Never force-push `main` or `master`; use a feature branch.

### Commit messages

We follow [Conventional Commits](https://www.conventionalcommits.org/):

- `fix:` — bug fix
- `feat:` — new feature
- `refactor:` — behavior-preserving refactor
- `docs:` — documentation

Breaking changes use a `feat!:` / `refactor!:` subject and a `BREAKING CHANGE:`
footer describing the migration.

### Code Guidelines

We use the [.NET Coding Style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md).

**UI Implementation**
- If the event handler of a UserControl becomes complex, separate it into a Behavior or split the file using `partial`.

XAML Files
- Use four spaces for indentation.
- When adding properties to a control, place the first property on the same line as the element, and align all subsequent properties on separate lines with the first property.
- When using `Binding`, use [compiled bindings](https://docs.avaloniaui.net/docs/next/basics/data/data-binding/compiled-bindings).
```xaml
<UserControl x:CompileBindings="True"
             x:DataType="viewModel:MyViewModel">
    <TextBox Foreground="White"
             MaxWidth="240"
             Text="{Binding Text.Value}" />
</UserControl>
```

Packaging materials or object templates for the store is described in
[`docs/data-packages.md`](docs/data-packages.md).

The [project structure guide](docs/development/project-structure.md) describes
module responsibilities and dependency boundaries.

Extension authors should also read the
[resolution-independent rendering guide](docs/extension-authoring/resolution-independent-rendering.md)
before implementing custom drawables, filter effects, brushes, or shaders, and
the [tool-tab extension guide](docs/extension-authoring/tool-tabs.md) before
adding a dockable editor tool.
