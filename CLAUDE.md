@AGENTS.md

## Claude Code-specific notes

- For large explorations, prefer Claude Code's built-in **Explore** subagent (read-only code search); for long-running tasks, use **Plan mode**. The Beutl-specific `beutl-spec-explorer` is narrower — it only walks `docs/specs/`.
- The `.claude/skills/beutl-*` skills are available:
  - `beutl-filter-effect` / `beutl-drawable` / `beutl-tooltab-extension` fire automatically on natural-language requests.
  - `beutl-build` / `beutl-test` / `beutl-format` / `beutl-coverage` also fire automatically, but each one is instructed to **confirm scope** (solution vs project, verify vs apply, etc.) via AskUserQuestion before executing. Pass arguments (e.g. `/beutl-test <FQN-substring>`) to skip the confirmation.
- Subagents under `.claude/agents/` auto-delegate based on their description. To invoke one explicitly, mention it like `@beutl-reviewer`.
- The `.claude/rules/xaml.md` / `csharp.md` / `gpl-mit-boundary.md` rules use `paths:` and load when Claude touches matching files.
- Scripts under `.claude/hooks/` take effect after you accept the workspace trust dialog on the first launch.
- Put personal settings (your own permission allowlist, etc.) into `.claude/settings.local.json`; do not commit it (already gitignored).

## Multi-agent execution with main-session audit (playbook)

For large, parallelizable work that splits into independent units — dead-code/asset sweeps, broad mechanical migrations, multi-file audits — use this division of labor. It requires opted-in multi-agent orchestration (a `Workflow`/ultracode session). The main session **orchestrates and audits; it never delegates the audit itself.** (Worked example: Phase 1 dead-code sweep, PRs #1895–#1910.)

1. **Verify / scope (read-only fan-out).** Before changing anything, fan out one read-only agent per finding/cluster to re-confirm it against the *current* code, with adversarial double-checking of every "safe to delete/change" verdict. Treat `docs/refactoring-plan.md` (and any prior audit) as a hypothesis to re-verify, not ground truth — Phase 1 found several of its items were false positives (e.g. live styles/packages/types listed as dead). Emit a concrete, file-level work-list as structured output (a JSON schema), including explicit **KEEP** lists.
2. **Implement (one isolated agent per branch).** One agent per cluster, each with `isolation: 'worktree'`, on its own `phase<N>/<cluster>` branch off `main`. It applies only the verified edits, honors the KEEP lists, builds the affected projects, runs `dotnet format` on edited files, and commits — **no push, no PR.** Use `refactor!:` + a `BREAKING CHANGE:` footer for public-surface removals; never leave `[Obsolete]` shims (migrate call sites in the same change).
3. **Review (one reviewer per branch).** One `beutl-reviewer` per branch (add `beutl-design-reviewer` for plugin-facing / breaking changes), read-only via `git diff <base>...<branch>`, including a repo-wide liveness grep for every removed symbol. Reviewers may legitimately overturn a verdict (Phase 1: one flagged `using` was actually live and was correctly kept).
4. **Audit (main session — do this yourself, do not delegate).** Read every implement report + review. Then **cherry-pick all branches onto one throwaway integration branch and run the full `dotnet build Beutl.slnx` + `dotnet test Beutl.slnx -f net10.0`.** This step is mandatory: per-branch builds and symbol greps miss cross-branch / transitive breakage (Phase 1: removing a transitively-relied-upon package compiled fine per-branch but broke the solution — only the integration build caught it). Fix issues, re-amend the affected branches, then push + open one PR per cluster **only after the user confirms.**

<!-- SPECKIT START -->
## Active Spec-Kit feature

- **003 — Resolution-Independent Rendering Pipeline**: plan at [`docs/specs/003-resolution-independent-pipeline/plan.md`](docs/specs/003-resolution-independent-pipeline/plan.md) (spec + research + data-model + contracts in the same dir). **Supply-driven** scale model (logical properties; output scale `RenderNodeContext.OutputScale` = final target only; per-op `EffectiveScale`, vector = `Unbounded`; computed working scale `w` via `ResolveWorkingScale` + per-effect `ResolutionPolicy`; root surface `ceil(FrameSize × s_out)`); byte-identical at `s_out = 1.0` with unit-scale inputs. **Do NOT revert to top-down single-scale or output-capped intermediates** — `s_out` never clamps an intermediate (FR-016/FR-036). Breaking public surface (`refactor!`/`feat!` + `BREAKING CHANGE:`). When touching `Beutl.Engine` graphics rendering / filter effects, consult that plan and the contracts.
<!-- SPECKIT END -->
