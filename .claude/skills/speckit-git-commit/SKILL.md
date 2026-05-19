---
description: |
  Stage and commit the freshly generated spec / plan / tasks file under
  `docs/specs/<NNN>-<slug>/` as a single Conventional Commit
  (`docs(specs): <phase> NNN-<slug>`). Invoked from `.specify/extensions.yml`
  as the `after_specify` / `after_plan` / `after_tasks` hooks; also runnable
  manually: `/speckit-git-commit spec | plan | tasks | checklist | analyze`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git diff:*) Bash(git add:*) Bash(git commit:*) Bash(git log:*) Bash(ls:*) Bash(head:*) Bash(awk:*) Bash(sed:*)
argument-hint: "spec|plan|tasks|checklist|analyze"
---

# Spec-Kit git: phase commit

Make exactly one commit per phase, scoped to `docs/specs/<NNN>-<slug>/`.

## 1. Identify the phase

`$ARGUMENTS` is one of: `spec`, `plan`, `tasks`, `checklist`, `analyze`.

If empty, infer from `git status --porcelain -- docs/specs/` — pick the most
recently modified `<phase>.md` under the latest `docs/specs/<NNN>-<slug>/`.
If multiple phases changed, **stop** and ask the user to specify; this skill
deliberately commits one phase at a time.

## 2. Locate the spec directory

```bash
SPEC_DIR=$(ls -1d docs/specs/[0-9][0-9][0-9]-* 2>/dev/null | sort | tail -1)
```

This selects the highest-numbered directory — typically the one just created
by `/speckit-git-branch` or the previous phase. If `SPEC_DIR` is empty or
the expected `<phase>.md` does not live under it, stop and report the
mismatch (do not silently fall back).

Extract the slug:

```bash
NNN_SLUG=$(basename "$SPEC_DIR")   # e.g. 001-add-foo-button
NNN=${NNN_SLUG%%-*}                # 001
SLUG=${NNN_SLUG#*-}                # add-foo-button
```

## 3. Verify the target file

```bash
TARGET="$SPEC_DIR/$PHASE.md"
[ -f "$TARGET" ] || { echo "No $PHASE.md under $SPEC_DIR"; exit 1; }
```

Check whether it is actually pending:

```bash
git diff --quiet -- "$TARGET" && git diff --cached --quiet -- "$TARGET" \
  && { echo "No changes to commit for $TARGET"; exit 0; }
```

## 4. Stage **only** the spec directory

```bash
git add -- "$SPEC_DIR"
```

Then verify nothing outside that directory ended up staged:

```bash
extras=$(git diff --cached --name-only | grep -v "^${SPEC_DIR%/}/" || true)
[ -z "$extras" ] || { echo "Aborting — extra files staged: $extras"; exit 1; }
```

This is the safety rail: we never want this skill to commit unrelated work
that happens to be sitting in the working tree.

## 5. Compose the commit message

Use a Conventional Commit:

```
docs(specs): <phase> <NNN>-<slug>
```

Examples:

- `docs(specs): scaffold 001-add-foo-button` (after_specify)
- `docs(specs): plan 001-add-foo-button` (after_plan)
- `docs(specs): tasks 001-add-foo-button` (after_tasks)

For `<phase>=spec`, use the verb `scaffold` instead of `spec` — it reads more
naturally for the very first commit of a spec. For other phases, use the
phase name as-is.

Body (optional but recommended): copy the first non-heading line from
`<phase>.md` so reviewers see what the file is about without opening it.

```bash
SUMMARY=$(awk 'NR>1 && NF && $0 !~ /^#/ {print; exit}' "$TARGET" | head -c 200)
```

## 6. Commit

```bash
git commit -m "$SUBJECT" -m "$SUMMARY"
```

Do not use `--amend` and do not use `--allow-empty`. Do not push.

## 7. Report

Emit a JSON line so the calling SKILL can record the result, followed by a
short human summary:

```json
{"sha":"<short-sha>","path":"docs/specs/<NNN>-<slug>/<phase>.md","subject":"docs(specs): <phase> <NNN>-<slug>"}
```

## Refusals

- Never `git add` outside `docs/specs/<NNN>-<slug>/`.
- Never `git push`. The user pushes when they are ready.
- Never amend a previous commit.
- Never run `dotnet format` / linters / tests as part of this skill — its
  scope is exactly one commit of exactly one Markdown file.
