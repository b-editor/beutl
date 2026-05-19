---
description: |
  Create or switch to a `speckit/<NNN>-<slug>` feature branch for the Spec-Driven
  Development flow. Invoked from `.specify/extensions.yml` as the `before_specify`
  hook (`speckit.git.branch`), but you can also run it manually:
  `/speckit-git-branch <slug>` or `/speckit-git-branch <NNN>-<slug>`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git branch:*) Bash(git switch:*) Bash(git checkout:*) Bash(git for-each-ref:*) Bash(git show-ref:*) Bash(ls:*) Bash(printf:*) Bash(grep:*) Bash(awk:*) Bash(sort:*) Bash(tail:*) Bash(sed:*)
argument-hint: "<slug-or-NNN-slug>"
---

# Spec-Kit git: feature branch

Create / switch to a `speckit/<NNN>-<slug>` branch and report it back so the
calling `/speckit-*` skill can record `BRANCH_NAME`, `FEATURE_NUM`, and
`SPECIFY_FEATURE_DIRECTORY`.

## 1. Parse the slug (and the optional explicit NNN)

Resolve the input in this order:

1. `$ARGUMENTS` (the literal text after the slash command).
2. The `GIT_BRANCH_NAME` env / context value, if the parent skill passed it.
3. The "short name" the parent `/speckit-specify` skill already computed (2-4
   word kebab-case). Ask the user to paste it if it is not in scope.

If the input matches `^[0-9]{3}-[a-z0-9-]+$` (the form advertised as
`/speckit-git-branch <NNN>-<slug>`), split it into `EXPLICIT_NNN` and `SLUG`
and **keep** the user-supplied number — Codex flagged that silently
re-allocating it makes the documented form misbehave. Otherwise treat the
whole input as `SLUG` and let §2 allocate `<NNN>`.

Reject any slug that contains characters outside `[a-z0-9-]` and stop with a
clear error.

## 2. Number the feature

If `EXPLICIT_NNN` was provided in §1:

```bash
NNN="$EXPLICIT_NNN"

# Sanity: if a different feature already claims this NNN under a different
# slug, refuse rather than silently colliding.
clash=$( {
  ls -1 docs/specs 2>/dev/null | grep -E "^${NNN}-"
  git for-each-ref --format='%(refname:short)' \
      'refs/heads/speckit/*' 'refs/remotes/*/speckit/*' 2>/dev/null \
    | sed -E 's|.*speckit/||; s|/.*||' \
    | grep -E "^${NNN}-"
} | sort -u | grep -v "^${NNN}-${SLUG}\$" || true)

if [ -n "$clash" ]; then
  echo "Aborting — NNN $NNN is already used by:"
  echo "$clash"
  echo "Re-run without the explicit NNN to allocate the next free number."
  exit 1
fi
```

Otherwise allocate. The next `<NNN>` must avoid collisions with **both**
existing spec directories **and** already-allocated `speckit/<NNN>-*`
branches (local or remote). A cancelled run can leave a `speckit/<NNN>-*`
branch behind without its `docs/specs/<NNN>-*` directory, so directory-only
numbering would re-issue the same `<NNN>`.

```bash
# Highest NNN from spec directories.
spec_max=$( ls -1 docs/specs 2>/dev/null \
  | grep -E '^[0-9]{3}-' \
  | awk -F- '{print $1}' \
  | sort -n | tail -1 )

# Highest NNN from local + remote speckit/ branches.
branch_max=$( git for-each-ref --format='%(refname:short)' \
    'refs/heads/speckit/*' 'refs/remotes/*/speckit/*' 2>/dev/null \
  | sed -E 's|.*speckit/||; s|/.*||' \
  | grep -E '^[0-9]{3}-' \
  | awk -F- '{print $1}' \
  | sort -n | tail -1 )

current_max=$(printf '%s\n%s\n' "$spec_max" "$branch_max" \
  | grep -E '^[0-9]+$' | sort -n | tail -1)
```

Increment by one, zero-pad to 3 digits → `<NNN>`. If both lookups are empty,
start at `001`.

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

> **Important: `SPECIFY_FEATURE_DIRECTORY` is informational.** The
> upstream `/speckit-specify` workflow generates its own spec directory
> name (`Auto-generate it under specs/` — see the SKILL's resolution
> order) and does **not** auto-consume hook output. To force matching
> directory and branch numbers, the user must either:
>
> 1. Pass `SPECIFY_FEATURE_DIRECTORY=docs/specs/<NNN>-<slug>` (or the
>    equivalent argument the parent skill accepts) before re-entering
>    `/speckit-specify`, **or**
> 2. Accept that the spec directory may use a different `<NNN>` than
>    the branch when a stray `speckit/<NNN>-*` branch already exists.
>
> The Beutl-local `SPECS_DIR` patch in `.specify/scripts/bash/common.sh`
> only redirects the *root* (`specs/` → `docs/specs/`); the numbering
> logic upstream is untouched.

Then, in human-readable text below the JSON, summarise:

- branch name and whether it was created or switched
- the proposed spec directory path (with a one-line note when it differs
  from what the parent skill is about to generate)
- whether the working tree had to be carried over

## Refusals

- Never delete or rename existing branches.
- Never push — pushing happens explicitly later via `git push -u origin ...`
  or the user's own PR-creation flow.
- Never run `git checkout -B` (force re-create); always go through `switch -c`
  / `switch`.
