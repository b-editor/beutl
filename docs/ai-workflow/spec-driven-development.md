# Spec-Driven Development (Spec-Kit)

Large features in Beutl start as a spec and let the AI implement them from there. We use GitHub's [Spec-Kit](https://github.com/github/spec-kit).

## One-time setup (repository)

> The `.specify/` tree is already checked in — **regular contributors do not run this**. The block below documents how the directory was bootstrapped and how a maintainer resyncs it after upstream [github/spec-kit](https://github.com/github/spec-kit) updates. The vendored files under `.specify/scripts/` and `.specify/templates/` should not be hand-edited; changes go upstream.

```bash
uv tool install specify-cli --from git+https://github.com/github/spec-kit.git
specify init . --ai claude --force   # only when resyncing against upstream
```

After every resync, **re-apply the Beutl-local `SPECS_DIR` patch** (search for `# Beutl local:` in `.specify/scripts/bash/common.sh` and `create-new-feature.sh`) — that patch is what redirects Spec-Kit output from upstream's `specs/` to Beutl's `docs/specs/`.

What it creates (as of specify-cli 0.8):

- `.specify/memory/constitution.md` — project governing principles (already rewritten for Beutl)
- `.specify/scripts/bash/*.sh` — internal scripts
- `.specify/templates/{checklist,constitution,plan,spec,tasks}-template.md`
- `.specify/workflows/`, `.specify/integrations/` — Claude integration metadata
- `.claude/skills/speckit-{constitution,specify,clarify,plan,tasks,analyze,checklist,implement,taskstoissues}/` — slash commands (skill form)

## Workflow

```
/speckit-constitution     # Confirm or update the project's governing principles
        ↓
/speckit-specify          # Describe the feature in terms of "what" and "why" (tech-agnostic)
        ↓
/speckit-clarify          # (optional) Pin down ambiguous spots with structured questions
        ↓
/speckit-plan             # Pick the tech stack and produce an implementation plan
        ↓
/speckit-tasks            # Break the plan into actionable tasks
        ↓
/speckit-analyze          # (optional) Cross-check spec/plan/tasks for consistency
        ↓
/speckit-implement        # Execute the plan
```

Because `specify init . --ai claude` installs as skills under `.claude/skills/speckit-*/`, they show up in the skills menu too (hyphenated names, e.g. `speckit-specify`).

Spec output lands under `docs/specs/<NNN>-<feature-slug>/` (Beutl-local override of the Spec-Kit default `specs/`).

## When to use which path

| Case | How |
|---|---|
| Large feature (a new filter category, an IPC protocol change, a new editor pane) | Full Spec-Kit flow |
| Mid-size feature (extending an existing spec, UI improvement) | Just `/speckit-specify`, then skip plan/tasks and implement directly |
| Bug fix | Skip Spec-Kit. Fix, then add a regression test. |
| Trivial refactor / typo fix | Skip Spec-Kit. |

## Beutl-specific constraints

The Beutl `constitution.md` records invariants that every spec must respect:

- License: MIT for the main app + a GPL boundary around `Beutl.FFmpegWorker`
- Targets: dual-target `net10.0` + `net10.0-windows`
- Tests: NUnit + Moq; new logic always ships with a test
- Style: `.editorconfig` / `xamlstyler.json` (AI never edits these)
- Quality gate: `dotnet format` passes, `dotnet test` is all green, coverage threshold from [`.github/workflows/dotnet.yml`](../../.github/workflows/dotnet.yml) is held

`/speckit-plan` reads these automatically and respects them.
