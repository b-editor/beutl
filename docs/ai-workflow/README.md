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
│   ├── agents/              # 5 specialized subagents
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

## Detailed guides

- [coding-guidelines-for-ai.md](./coding-guidelines-for-ai.md) — only the rules that require human judgment (linters cover the rest)
- [subagents-and-hooks.md](./subagents-and-hooks.md) — walkthrough of the 5 subagents and 5 hooks
- [spec-driven-development.md](./spec-driven-development.md) — how to use Spec-Kit
- [gpl-mit-boundary.md](./gpl-mit-boundary.md) — IPC boundary around `Beutl.FFmpegWorker`

## First-run note

Because `.claude/hooks/*.sh` is committed to the repository, Claude Code shows a **workspace trust dialog** the first time you start a session. Review the scripts and then accept.
