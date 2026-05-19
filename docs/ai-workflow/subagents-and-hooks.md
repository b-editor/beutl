# Subagent and hook reference

## Subagents (`.claude/agents/`)

| Name | When to call it | Model | Notes |
|---|---|---|---|
| `beutl-reviewer` | Pre-PR or post-large-change review | sonnet | Covers GPL/MIT, XAML, NUnit, SourceGen. `memory: project` accumulates lessons. |
| `beutl-test-runner` | Investigating a failing test | sonnet | Runs with **`isolation: worktree`** so trial fixes don't touch main. |
| `beutl-source-generator-impact` | Before/after editing `src/Beutl.Engine.SourceGenerators/` | sonnet | Reports blast radius, dependencies, test coverage. |
| `beutl-spec-explorer` | "Is there a spec for this?" | haiku | Walks `docs/specs/` (the Spec-Kit output dir for Beutl). Preloads the Beutl skills. |
| `beutl-xaml-binder` | After adding/changing many `.axaml` files | haiku | Confirms compiled bindings are in place. |

### Invoking them

Speak naturally — Claude Code auto-delegates based on each agent's description (especially the ones that say "use proactively"). To force a specific agent:

```
@beutl-reviewer Review the changes on this branch
@beutl-test-runner The BeutlEngineTest is red — find out why
```

### When it triggers too often or not enough

- Triggering when you do not want it → tighten the `description`.
- Not triggering when you do want it → add more trigger phrases to the `description`.

## Hooks (`.claude/settings.json` + `.claude/hooks/`)

All hooks are team-shared (loaded via `.claude/settings.json`). On first launch, Claude Code asks you to trust the workspace before they activate. Each hook is a small bash script using only `jq` and shell `case` / `grep` patterns — no external interpreters.

| Hook | Role | Blocking? |
|---|---|---|
| `SessionStart` → `session-start-context.sh` | At startup, inject branch, last 5 commits, and uncommitted changes into context. | no |
| `PreToolUse(Bash)` → `block-dangerous-bash.sh` | Deny obvious literal forms of `rm -rf /`, `rm -rf ~`, `rm -rf $HOME` / `${HOME}`, and `git push (--force / -f / --force-with-lease) origin (main / master)`. | **deny** |
| `PreToolUse(Bash)` → `allow-dotnet-commands.sh` | Auto-allow `dotnet build/test/format/restore/run`, `./build.sh`. | **allow** |

**Ordering invariant** (`.claude/settings.json`): the deny hook (`block-dangerous-bash.sh`) **must run before** the allow hook (`allow-dotnet-commands.sh`) for the `Bash` matcher. Claude Code evaluates hooks in declaration order, and a later allow can override an earlier deny on the same call. Do not reorder the array without updating this doc.

**Fail-closed contract** for `block-dangerous-bash.sh` and `check-gpl-mit-boundary.sh`: both scripts use `set -euo pipefail` plus an `ERR` trap that emits a deny JSON, so any unexpected error (malformed input, missing `jq`, etc.) is converted to a deny rather than silently allowing.
| `PreToolUse(Edit\|Write\|MultiEdit)` → `check-gpl-mit-boundary.sh` | Inspect the new content fragments of a `.csproj` edit (`new_string`, `content`, `edits[].new_string`) and deny when they include a `<ProjectReference ... Beutl.FFmpegWorker`. | **deny** |

### Scope

The hooks are guardrails against obvious AI mistakes, **not** a watertight
security boundary. The patterns above intentionally cover only literal
forms. Sophisticated bypass routes — shell variable expansion, clustered
short flags, multi-line XML edits, Bash-based `.csproj` rewrites via
`sed` / `tee` / `dotnet add reference`, refspec gymnastics in `git push`,
and so on — are **out of scope** and are guarded by PR review plus
GitHub branch protection on `main` / `master`. Trying to close every
bypass route in a pre-tool hook quickly produces brittle code that drifts
out of date as new bypass forms surface; we deliberately stop at "block
the obvious mistake".

### Per-user opt-in (`.claude/settings.local.json`)

#### InstructionsLoaded logger

Useful for debugging which CLAUDE.md / rules files load:

```json
{
  "hooks": {
    "InstructionsLoaded": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "bash",
            "args": ["${CLAUDE_PROJECT_DIR}/.claude/hooks/log-instructions-loaded.sh"]
          }
        ]
      }
    ]
  }
}
```

Output goes to `.claude/logs/instructions-loaded.log` (gitignored).

#### PostToolUse auto-`dotnet format`

Slow; we keep it **out of the team-shared config**. If you want it personally:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Edit|Write",
        "hooks": [
          {
            "type": "command",
            "command": "bash",
            "args": ["-c", "f=$(jq -r '.tool_input.file_path // \"\"' < /dev/stdin); case \"$f\" in *.cs) dotnet format Beutl.slnx --include \"$f\" --verbosity quiet ;; esac"]
          }
        ]
      }
    ]
  }
}
```

### Disabling hooks entirely

In `.claude/settings.local.json`:

```json
{ "disableAllHooks": true }
```

(Do not flip `disableAllHooks` in the team-shared `.claude/settings.json`.)
