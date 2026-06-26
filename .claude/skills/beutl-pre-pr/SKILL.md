---
description: |
  Run the same checks locally that `claude-code-review.yml` (CI) and the `beutl-reviewer`
  subagent will run, so feedback arrives in 30 seconds instead of after a push. Use before
  opening a PR. Triggers on "PR出す前に確認", "pre-PR check", "ready to push?", "before
  I open the PR". Always confirm the scope before running — the full pass is slow.
allowed-tools: Bash(dotnet:*) Bash(git:*) Bash(./build.sh:*) Bash(bash .claude/scripts/*:*)
argument-hint: "[quick|full]"
---

# Pre-PR checks for Beutl

Mirror the CI review surface so the loop "push → wait → fix" collapses into "fix → push".

## Confirm the scope first

Unless `$ARGUMENTS` already specifies `quick` or `full`, ask with AskUserQuestion:

- **quick** (default, ~30s): format verify + build the projects whose files changed + GPL/MIT scan on the diff
- **full** (~2–5min): also run the relevant test projects and delegate `@beutl-reviewer` + `@beutl-xaml-binder`

Skip the prompt if the user already said which one in this turn, or if `$ARGUMENTS` was passed.

## Procedure

### 0. Discover the diff

`/beutl-pre-pr` should also catch changes the user has not committed yet — otherwise running it before the final commit silently skips the most likely cause of CI failures. Union four sets: committed-but-unpushed, staged, unstaged, and untracked.

```bash
git fetch origin main --quiet
BASE=$(git merge-base HEAD origin/main)
CHANGED=$( {
  git diff --name-only "$BASE"...HEAD        # committed on this branch
  git diff --name-only --cached              # staged
  git diff --name-only                       # unstaged
  git ls-files --others --exclude-standard   # untracked
} | sort -u )
```

If `CHANGED` is empty, report `Nothing to check — branch and working tree are clean against origin/main` and stop.

### 1. Format verification (always)

```bash
dotnet format Beutl.slnx --verify-no-changes --no-restore
```

If this fails: stop here and offer to run `dotnet format Beutl.slnx` (writing). Do not auto-apply.

### 2. GPL/MIT boundary scan (always)

The PreToolUse hook `.claude/hooks/check-gpl-mit-boundary.sh` only inspects fragments from `Edit` / `Write` tool calls (`new_string`, `content`, `edits[].new_string`) — it cannot scan the working tree retroactively. So this step runs the shared diff-side scan script, which targets only the two forbidden linkage forms (`ProjectReference` and `Compile Include`); a bare mention of the `Beutl.FFmpegWorker` namespace in a comment, doc string, or already-linked source file is fine.

The scan mirrors the hook's one sanctioned exception: `src/Beutl/Beutl.csproj` may carry a build-order-only `<ProjectReference ... ReferenceOutputAssembly="false" />` (paired with its `CopyFFmpegWorkerForApp` target), which keeps the GPL assembly out of the MIT compile closure. That exemption is path-specific — any other project, and any `Compile Include`, is still forbidden.

```bash
# $CHANGED already unions committed + staged + unstaged + untracked (step 0).
bash .claude/scripts/check-gpl-mit-boundary-diff.sh --files $CHANGED
```

Exit 0 = clean; exit 1 = violation(s), printed as `file:line`. Only `src/Beutl.FFmpegWorker/` itself ships `Beutl.FFmpegWorker` linkages freely; `src/Beutl/Beutl.csproj` may use the sanctioned build-order-only `ProjectReference` described above; `tests/Beutl.FFmpegBenchmarks` is exempted (a BenchmarkDotNet project that intentionally link-compiles worker source files for direct-call benchmarking — not shipped in the MIT app). Every other project — including the MIT IPC layer at `src/Beutl.FFmpegIpc/` — must reach the worker through the IPC protocol, never via `ProjectReference` (even with `ReferenceOutputAssembly="false"`) or `<Compile Include="...FFmpegWorker..." />`. Any violation — stop.

### 3. Targeted build (always)

Pick the smallest set of projects that cover the diff:

- changed file under `src/X/...` → build `src/X/X.csproj`
- changed `.axaml` → build the project that owns it
- multiple subtrees → build `Beutl.slnx`

```bash
dotnet build <projects> -c Debug --nologo
```

Report the first 5 unique CS-codes plus `file:line` on failure.

### 4. (full only) Targeted tests

Map each touched `src/` project to its test project (see `tests/CLAUDE.md`). Run the union:

```bash
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings \
  --filter "FullyQualifiedName~<substring-per-touched-area>" \
  --logger "console;verbosity=normal"
```

On failure, list failing FQN + first error line. Do not auto-fix.

### 5. (full only) Delegate audits

In parallel (separate Task tool calls in one message):

- `@beutl-xaml-binder` if any `.axaml` is in `CHANGED`
- `@beutl-reviewer` always

Wait for both, then merge their findings under "### Audit" sections.

## Report format

```
## Pre-PR checks — <quick|full>

- Format: PASS|FAIL (<details>)
- GPL/MIT boundary: PASS|FAIL (<details>)
- Build: PASS|FAIL (<details>)
- Tests: PASS|FAIL|SKIPPED (<details>)

### Audit (full only)
- XAML compiled bindings: …
- 4-axis review: …
```

## Notes

- This skill never pushes, never opens a PR, never auto-fixes. It only reports.
- For a one-off scope, pass arguments: `/beutl-pre-pr full`.
- If you want the full CI parity including coverage, run `/beutl-coverage` afterwards — that workflow is heavier and intentionally separate.
