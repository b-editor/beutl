# Start AI pair-programming in 5 minutes

Beutl is set up to work with AI coding agents (Claude Code, Codex, Cursor, etc.). This page tells you what is in the repository and when to reach for each piece.

## Layout

```
beutl/
├── AGENTS.md                # Shared instructions for every AI agent (primary)
├── CLAUDE.md                # Imports @AGENTS.md + Claude Code-specific notes
├── .mcp.json                # Team-shared MCP (context7)
├── .claude/
│   ├── settings.json        # Team-shared hook config
│   ├── rules/               # Path-scoped rules (xaml / csharp / gpl-mit)
│   ├── skills/              # Domain skills + canned command skills
│   ├── agents/              # Specialized subagents
│   ├── scripts/             # Loop helper scripts (contract-check, GPL/MIT diff scan, coverage probe)
│   └── hooks/               # Dangerous-command deny, dotnet auto-allow, GPL/MIT deny, context injection
├── .specify/                # Spec-Kit templates / scripts / workflows (vendored, locally patched)
└── docs/
    ├── ai-workflow/         # This directory
    └── specs/               # Where Spec-Kit drops per-feature spec / plan / tasks
```

## When to use what

| You want to… | Use |
|---|---|
| Drive a large feature from design to implementation | `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement` |
| Implement a new FilterEffect / Drawable / ToolTab | Just describe it in plain language — the skill fires automatically |
| Build / test / format / coverage | Plain language works (the skill fires automatically); Claude asks via AskUserQuestion which scope to use (whole solution vs single project, verify vs apply, etc.) before running. Pass an argument, e.g. `/beutl-test Beutl.UnitTests.Engine.Animation`, to skip the confirmation |
| Final pre-PR review | `beutl-reviewer` subagent (auto-delegated) |
| Investigate a red test | `beutl-test-runner` subagent (runs in an isolated worktree) |
| Touched something under `src/Beutl.Engine.SourceGenerators/` | `beutl-source-generator-impact` subagent |
| "Is there a spec for this?" | `beutl-spec-explorer` subagent |
| Added or changed several `.axaml` files | `beutl-xaml-binder` subagent |
| Changed a public type / extensibility surface | `beutl-design-reviewer` subagent (orthogonality, plugin-author flexibility, no compat shims) |
| CI's Linux/SwiftShader GPU job crashed natively (no managed stack) but it's green on macOS | `beutl-gpu-crash-repro` skill (arm64-native Docker repro + native stack); delegates the noisy build+loop to the `beutl-gpu-crash-reproducer` subagent |
| Work **one** task from the Project #9 board (bug / quality / design / feature) into a PR | `beutl-board-task` skill (verify → implement → test → PR; a human merges) |
| Address & resolve a PR's reviews (CodeRabbit / Copilot / Codex / Claude) | `beutl-resolve-reviews` skill (interactive by default; `--auto` for unattended) |
| Autonomously **drain the board** (all eligible items by default) in one bounded run, auto-merging the low-to-moderate-risk ones where the branch rules permit | `/beutl-loop` skill — meta-loop over `beutl-board-task`; dispatches worktree sub-agents behind a test gate + self-review gate + a two-pass design review, resolves bot reviews, risk-gates auto-merge, stops on an empty board / runaway backstop / stagnation breaker. See [loop-engineering.md](./loop-engineering.md) |

## Detailed guides

- [coding-guidelines-for-ai.md](./coding-guidelines-for-ai.md) — only the rules that require human judgment (linters cover the rest)
- [subagents-and-hooks.md](./subagents-and-hooks.md) — walkthrough of the 8 subagents and 6 hooks
- [spec-driven-development.md](./spec-driven-development.md) — how to use Spec-Kit
- [gpl-mit-boundary.md](./gpl-mit-boundary.md) — IPC boundary around `Beutl.FFmpegWorker`
- [loop-engineering.md](./loop-engineering.md) — the `/beutl-loop` autonomous board-draining loop, its risk-gated auto-merge, and its guardrails

## First-run note

Because `.claude/hooks/*.sh` is committed to the repository, Claude Code shows a **workspace trust dialog** the first time you start a session. Review the scripts and then accept.
