@AGENTS.md

## Claude Code-specific notes

- For large explorations, prefer the **Explore subagent**; for long-running tasks, use **Plan mode**.
- The `.claude/skills/beutl-*` skills are available:
  - `beutl-filter-effect` / `beutl-drawable` / `beutl-tooltab-extension` fire automatically on natural-language requests.
  - `beutl-build` / `beutl-test` / `beutl-format` / `beutl-coverage` also fire automatically, but each one is instructed to **confirm scope** (solution vs project, verify vs apply, etc.) via AskUserQuestion before executing. Pass arguments (e.g. `/beutl-test <FQN-substring>`) to skip the confirmation.
- Subagents under `.claude/agents/` auto-delegate based on their description. To invoke one explicitly, mention it like `@beutl-reviewer`.
- The `.claude/rules/xaml.md` / `csharp.md` / `gpl-mit-boundary.md` rules use `paths:` and load when Claude touches matching files.
- Scripts under `.claude/hooks/` take effect after you accept the workspace trust dialog on the first launch.
- Put personal settings (your own permission allowlist, etc.) into `.claude/settings.local.json`; do not commit it (already gitignored).
