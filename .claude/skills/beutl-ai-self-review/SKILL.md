---
name: beutl-ai-self-review
description: |
  Cross-check the Beutl AI workflow (AGENTS.md, CLAUDE.md, .agents/skills/,
  .claude/agents/, .claude/skills/, .claude/rules/, .claude/hooks/, docs/ai-workflow/) against
  the current repo state and propose targeted improvements one item at a time.
  Use proactively after completing a substantial task — 3+ files changed,
  a new feature, a non-trivial refactor, or a finished PR review cycle — or
  when the user says "review the AI setup", "self-review", "AI設定を見直して".
allowed-tools: Read, Grep, Glob, Bash(git log:*), Bash(git diff:*), Bash(git show:*), Bash(git rev-parse:*), Bash(gh pr list:*), Bash(gh api:*), Bash(cat:*), Bash(test:*)
---

# Beutl AI workflow self-review

Refresh the AI setup so it never drifts from the codebase. The skill **only**
touches files inside the AI surface: `AGENTS.md`, `CLAUDE.md`,
`.agents/skills/beutl-*` (do NOT edit `.agents/skills/speckit-*`),
`.claude/agents/`, `.claude/skills/beutl-*` (do NOT edit
`.claude/skills/speckit-*`), `.claude/rules/`, `.claude/hooks/`,
`docs/ai-workflow/`, `.specify/memory/constitution.md`. It never edits source
code, tests, CI workflows, the linter config, or anything under
`.specify/scripts/` / `.specify/templates/`.

## 1. Pick the review window

Decide the `git` range to inspect, in this order:

1. If `$ARGUMENTS` starts with `since=`, use the value after the `=` directly
   (e.g. `since=HEAD~20`, `since=2026-04-01`).
2. Else, if `.claude/.last-self-review` exists, read its contents and use
   `<sha>..HEAD`.
3. Else, fall back to `--since="30 days ago"`.

Capture the chosen window as `$WINDOW` for the rest of the run.

## 2. Collect signals

Run these in parallel where possible.

### Git history

```bash
git log $WINDOW --no-merges --name-only --pretty=format:'%h %s'
```

Bucket changed paths:

- New top-level dirs under `src/<NewModule>/`
- New top-level dirs under `tests/<NewTestProject>/`
- New / removed files under `.github/workflows/`
- Changes to `.editorconfig`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `xamlstyler.json`, `global.json`, `Beutl.slnx`
- Anything that renames or deletes a path referenced from the AI surface

### Repo-state cross-check

For every file path, project name, or module name mentioned in the AI surface
files, confirm it still exists. Concretely:

1. List the AI surface files:
   ```bash
   ls AGENTS.md CLAUDE.md .agents/skills/beutl-*/SKILL.md \
      .agents/skills/beutl-*/references/*.md .claude/agents/*.md \
      .claude/rules/*.md .claude/skills/beutl-*/SKILL.md \
      .claude/skills/beutl-*/references/*.md \
      docs/ai-workflow/*.md .specify/memory/constitution.md 2>/dev/null
   ```
2. For each, `Grep` for tokens that look like paths or csproj names:
   - `src/Beutl.<word>` / `src/Beutl.<word>.<word>`
   - `tests/Beutl.<word>` / `tests/<word>Test`
   - `.github/workflows/<word>.yml`
   - `Beutl.<word>.csproj`
3. For each unique reference, `Glob` for the file. If the `Glob` returns
   nothing, mark the reference as **stale**.

### Recent PR feedback on AI surface

```bash
gh pr list --state all --limit 10 --json number,title
```

For each PR number `N`:

```bash
gh api "repos/{owner}/{repo}/pulls/$N/comments" \
  --jq '[.[] | select(.path | test("^(\\.agents/|\\.claude/|AGENTS\\.md|CLAUDE\\.md|docs/ai-workflow/)")) | {pr: '"$N"', path, line, body, user: .user.login}]'
```

Aggregate themes by counting how often each cluster recurs (same wording or
same target path across 2+ PRs).

### Hook log (optional)

```bash
test -f .claude/logs/instructions-loaded.log && \
  awk -F'\t' '{print $4}' .claude/logs/instructions-loaded.log | sort | uniq -c | sort -rn
```

Surface rules that load disproportionately often (description too loose) or
rules that never load (description too narrow or `paths:` too strict).

## 3. Synthesize findings

Cap at **10 findings**, ordered by severity. Each finding must include:

- **Target file(s)** with absolute paths from repo root
- **What is stale or missing**, one sentence
- **Proposed concrete edit** — the smallest diff that resolves it

Do not report findings whose only impact is style (linter territory) or
findings that ask to modify CI workflows.

## 4. Confirm each finding with the user

Use the runtime's user-question mechanism (`AskUserQuestion` in Claude Code,
`request_user_input` in Codex plan mode, or a concise direct question when no
question tool is available). Batch at most 4 questions per call. For every
finding, offer the same option set:

- **Apply the edit** (the proposed diff)
- **Skip** (acknowledge, no change this round)
- **Apply differently** (user describes the alternative)

Do not group findings unless they touch the same file with mechanically
identical edits (e.g. renaming the same project across 6 docs).

## 5. Apply the approved edits

Use `Edit` (or `Write` only for new files in the AI surface). Group edits by
file so each file is rewritten in a single pass. Keep diffs minimal. Do not
introduce new sections, new files, or new conventions that were not part of
the approved finding.

## 6. Update the marker

After the run — even if zero findings were applied — write the current SHA to
the marker so the next review starts from here:

```bash
git rev-parse HEAD > .claude/.last-self-review
```

## 7. Summarize

End the turn with a short report:

- **Applied**: N findings — list as `file:line — fix summary`
- **Skipped**: N findings
- **Open** (user chose "apply differently" but did not specify the alt yet): list these
- **Next window**: `<new marker SHA>..HEAD`

Do **not** stage, commit, or push. The user decides when to commit the
changes.

## Constraints

- Stay inside the AI surface. Never edit `src/`, `tests/`, `.github/workflows/`,
  `.specify/scripts/`, `.specify/templates/`, `.specify/integrations/`,
  `.specify/workflows/`, the linter config, `.agents/skills/speckit-*`, or
  `.claude/skills/speckit-*`.
- Do not propose stylistic-only edits — `.editorconfig`, `xamlstyler.json`,
  and `dotnet format` own that.
- Cap PR-feedback scanning at the most recent 10 PRs.
- If `gh` or `git` is unavailable, degrade: skip that signal and proceed with
  the others; do not abort.
- Never commit, push, or open a PR. Leave staging entirely to the user.
