---
description: |
  Create or switch to a `speckit/<NNN>-<slug>` feature branch for the Spec-Driven
  Development flow. Invoked from `.specify/extensions.yml` as the `before_specify`
  hook (`speckit.git.branch`), but you can also run it manually:
  `/speckit-git-branch <slug>` or `/speckit-git-branch <NNN>-<slug>`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git branch:*) Bash(git switch:*) Bash(git checkout:*) Bash(ls:*) Bash(printf:*)
argument-hint: "<slug-or-NNN-slug>"
---

# Spec-Kit git: feature branch

Create / switch to a `speckit/<NNN>-<slug>` branch and report it back so the
calling `/speckit-*` skill can record `BRANCH_NAME`, `FEATURE_NUM`, and
`SPECIFY_FEATURE_DIRECTORY`.

## 1. Parse the slug

Resolve the slug in this order:

1. `$ARGUMENTS` (the literal text after the slash command).
2. The `GIT_BRANCH_NAME` env / context value, if the parent skill passed it.
3. The "short name" the parent `/speckit-specify` skill already computed (2-4
   word kebab-case). Ask the user to paste it if it is not in scope.

Strip a leading `NNN-` if present — the numbering step below re-derives it from
`docs/specs/`. Whatever remains is `<slug>`. Reject anything that contains
characters outside `[a-z0-9-]` and stop with a clear error.

## 2. Number the feature

```bash
ls -1 docs/specs 2>/dev/null \
  | grep -E '^[0-9]{3}-' \
  | awk -F- '{print $1}' \
  | sort -n | tail -1
```

Increment by one, zero-pad to 3 digits → `<NNN>`. If `docs/specs/` is missing
or has no numbered entries yet, start at `001`.

Compose:

- `BRANCH_NAME="speckit/<NNN>-<slug>"`
- `FEATURE_NUM="<NNN>"`
- `SPECIFY_FEATURE_DIRECTORY="docs/specs/<NNN>-<slug>"`

## 3. Pre-flight checks

```bash
git rev-parse --is-inside-work-tree   # must be true
git status --porcelain                # report dirty count
git rev-parse --abbrev-ref HEAD       # current branch
```

If the working tree is **dirty** (porcelain output non-empty):

> Warn the user that uncommitted changes will follow them onto the new branch.
> Confirm via AskUserQuestion before proceeding (default: cancel).

If the current branch is **not** `main`:

> Inform the user, list the current branch, and confirm via AskUserQuestion
> whether to branch from here anyway or switch to `main` first.

## 4. Create or switch the branch

```bash
if git show-ref --verify --quiet "refs/heads/$BRANCH_NAME"; then
  git switch "$BRANCH_NAME"
else
  git switch -c "$BRANCH_NAME"
fi
```

If branch creation fails (e.g. detached HEAD, locked index), abort with the
captured stderr — do **not** retry, do **not** force.

## 5. Emit the JSON contract

Print exactly this to stdout (one line, no surrounding prose) so the calling
SKILL can parse it:

```json
{"BRANCH_NAME":"speckit/<NNN>-<slug>","FEATURE_NUM":"<NNN>","SPECIFY_FEATURE_DIRECTORY":"docs/specs/<NNN>-<slug>"}
```

Then, in human-readable text below it, summarise:

- branch name and whether it was created or switched
- the proposed spec directory path
- whether the working tree had to be carried over

## Refusals

- Never delete or rename existing branches.
- Never push — pushing happens explicitly later via `git push -u origin ...`
  or the user's own PR-creation flow.
- Never run `git checkout -B` (force re-create); always go through `switch -c`
  / `switch`.
