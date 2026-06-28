---
description: |
  Run dotnet format against the Beutl solution. Use when the user asks to format code, check
  style, or fix lint errors. Triggers on "フォーマットして", "format", "lint", "style check".
  Always confirm scope and mode (verify vs apply) before running.
allowed-tools: Bash(dotnet format:*) Bash(git diff:*) Bash(git status:*)
argument-hint: "[verify|apply] [path|project]"
---

# Format Beutl

**Always confirm scope and mode with the user** unless they specified them in this turn or `$ARGUMENTS` is non-empty. Use AskUserQuestion with these defaults:

- **Mode**:
  - `verify` (default, recommended pre-commit) — `--verify-no-changes`, fails if formatting would change anything. Matches the CI `format-check.yml`.
  - `apply` — actually rewrites files.
- **Scope**: whole solution (`Beutl.slnx`) / a single project / specific files (`--include "<glob>"`).

Once scope is decided, run the matching command:

```bash
# verify mode (default)
dotnet format Beutl.slnx --verify-no-changes --verbosity diagnostic

# apply mode
dotnet format Beutl.slnx --verbosity diagnostic

# scoped to a single project
dotnet format <path/to/Project.csproj> --verbosity diagnostic

# scoped to specific files
dotnet format Beutl.slnx --include "<glob>" --verbosity diagnostic
```

If `$ARGUMENTS` starts with `verify` or `apply`, treat the first token as the mode and the rest as scope.

## Notes

- Style is enforced by `.editorconfig` and `Directory.Build.props` — do NOT propose style edits by hand.
- `.github/workflows/format-check.yml` runs `dotnet format Beutl.slnx --verify-no-changes`; matching it locally avoids round-trips.
- XAML formatting is handled separately by `xamlstyler.json` (run XAML Styler in your editor, or via `dotnet tool` if installed).

## After running

In `apply` mode, summarise the changed files with `git diff --stat`. Do NOT commit automatically — leave staging and commit to the user.
