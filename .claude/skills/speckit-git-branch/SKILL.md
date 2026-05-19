---
description: |
  Create or switch to a `speckit/<NNN>-<slug>` feature branch for the Spec-Driven
  Development flow. Invoked from `.specify/extensions.yml` as the `before_specify`
  hook (`speckit.git.branch`), but you can also run it manually:
  `/speckit-git-branch <slug>` or `/speckit-git-branch <NNN>-<slug>`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git branch:*) Bash(git switch:*) Bash(git checkout:*) Bash(git for-each-ref:*) Bash(git show-ref:*) Bash(ls:*) Bash(printf:*) Bash(grep:*) Bash(awk:*) Bash(sort:*) Bash(head:*) Bash(tail:*) Bash(sed:*)
argument-hint: "<slug-or-NNN-slug>"
---

# Spec-Kit git: feature branch

Create / switch to a `speckit/<NNN>-<slug>` branch and report it back so the
calling `/speckit-*` skill can record `BRANCH_NAME`, `FEATURE_NUM`, and
`SPECIFY_FEATURE_DIRECTORY`.

## 1. Parse the slug (and the optional explicit NNN)

Resolve the input in this order:

1. **`GIT_BRANCH_NAME` env / context value, when the parent skill explicitly
   passed it.** Per the upstream `/speckit-specify` contract this is the
   **exact branch name**, not a slug. If it is set, take it verbatim:
   - `BRANCH_NAME="$GIT_BRANCH_NAME"`
   - If `BRANCH_NAME` matches `^speckit/[0-9]{3}-[a-z0-9-]+$`, derive
     `FEATURE_NUM` and `SLUG` from it and set
     `SPECIFY_FEATURE_DIRECTORY="docs/specs/${BRANCH_NAME#speckit/}"`.
   - Otherwise leave `FEATURE_NUM` / `SPECIFY_FEATURE_DIRECTORY` empty and
     note in the report that the parent skill is responsible for picking
     a feature directory that matches its own conventions.
   Skip §3 entirely in this case and jump to §4 with the supplied
   `BRANCH_NAME`. (The pre-flight checks in §2 still run.)
2. `$ARGUMENTS` (the literal text after the slash command).
3. The "short name" the parent `/speckit-specify` skill already computed (2-4
   word kebab-case). Ask the user to paste it if it is not in scope.

For sources #2 and #3 (slug-style input):

If the input matches `^[0-9]{3}-[a-z0-9-]+$` (the form advertised as
`/speckit-git-branch <NNN>-<slug>`), split it into `EXPLICIT_NNN` and `SLUG`
and **keep** the user-supplied number — Codex flagged that silently
re-allocating it makes the documented form misbehave. Otherwise treat the
whole input as `SLUG` and let §3 allocate `<NNN>`.

Reject any slug that contains characters outside `[a-z0-9-]` and stop with a
clear error. (Validation only applies to slug-style input from sources #2
and #3; the exact-name source #1 path is unrestricted by design.)

## 2. Pre-flight checks

Run pre-flight **before** allocating `<NNN>`. If the user is on an older
feature branch that is missing spec directories that already exist on
`main`, scanning for the next free number from the current branch would
pick a `<NNN>` that collides with a directory on `main`; switching to
`main` first ensures `ls docs/specs` reflects the canonical set.

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
> whether to branch from here anyway or switch to `main` first. If the user
> picks "switch to `main`", run `git switch main` here so the subsequent
> `<NNN>` scan in §3 sees the canonical spec directories.

## 3. Number the feature

If `EXPLICIT_NNN` was provided in §1:

```bash
NNN="$EXPLICIT_NNN"

# Sanity: if a different feature already claims this NNN under a different
# slug, refuse rather than silently colliding.
#
# Each `grep -E` below returns 1 on "zero matches", which is the normal
# case. We swallow *only* that exit code per-source with `|| true`, so a
# real failure inside any single source still surfaces — unlike a single
# trailing `|| true` over the whole compound, which would also hide
# pipeline failures (e.g. git for-each-ref erroring for permission
# reasons) and let the skill conclude "no clash" by accident.
clash_candidates=$( {
  ls -1 docs/specs 2>/dev/null | grep -E "^${NNN}-" || true
  ls -1      specs 2>/dev/null | grep -E "^${NNN}-" || true
  git for-each-ref --format='%(refname:short)' \
      'refs/heads/speckit/*' 'refs/remotes/*/speckit/*' 2>/dev/null \
    | sed -E 's|.*speckit/||; s|/.*||' \
    | { grep -E "^${NNN}-" || true; }
} | sort -u)

# Filter out our own intended branch — anything that remains is a clash.
clash=$(printf '%s\n' "$clash_candidates" \
        | grep -vFx "${NNN}-${SLUG}" \
        | grep -v '^$' || true)

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
numbering would re-issue the same `<NNN>`. Scan both spec roots — the
Beutl-local default (`docs/specs/`) and the upstream Spec-Kit default
(`specs/`) — because an explicit `SPECIFY_FEATURE_DIRECTORY` or a
pre-patch run can leave a feature under `specs/`.

```bash
# Highest NNN from spec directories under either root.
spec_max=$( {
    ls -1 docs/specs 2>/dev/null
    ls -1      specs 2>/dev/null
  } | grep -E '^[0-9]{3}-' \
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

## 4. Create or switch the branch

Try local first, then remote tracking, then create. The remote case matters
when `origin/speckit/<NNN>-<slug>` has been fetched (e.g. you pulled the
repo without checking the branch out) — silently creating a fresh orphan
branch with the same name leads to a rejected/divergent push later.

Each `git switch` invocation captures stderr so we can report the real
reason when something goes wrong. The auto-track path is allowed to fail
silently (multiple remotes own the same name → fall through to explicit
`--track`); every other failure aborts and surfaces git's own message.

```bash
if git show-ref --verify --quiet "refs/heads/$BRANCH_NAME"; then
  # Local branch already exists — just switch.
  if ! switch_err=$(git switch "$BRANCH_NAME" 2>&1 >/dev/null); then
    echo "Aborting — git switch failed:"
    printf '%s\n' "$switch_err"
    exit 1
  fi
elif git for-each-ref --format='%(refname)' \
       "refs/remotes/*/$BRANCH_NAME" 2>/dev/null | grep -q .; then
  # Only a remote tracking ref exists; `git switch <name>` auto-creates a
  # local branch and sets up tracking when exactly one remote has the
  # branch. When multiple remotes share the name git refuses; in that
  # case fall through to the explicit `--track` form. The auto-track
  # failure is benign here (the fallback is what handles it) so we
  # intentionally discard its stderr; the explicit --track call below
  # is the one that must surface errors.
  if ! git switch "$BRANCH_NAME" >/dev/null 2>&1; then
    remote_ref=$(git for-each-ref --format='%(refname)' \
      "refs/remotes/*/$BRANCH_NAME" 2>/dev/null | head -1)
    if ! switch_err=$(git switch --track "${remote_ref#refs/remotes/}" 2>&1 >/dev/null); then
      echo "Aborting — git switch --track failed:"
      printf '%s\n' "$switch_err"
      exit 1
    fi
  fi
else
  if ! switch_err=$(git switch -c "$BRANCH_NAME" 2>&1 >/dev/null); then
    echo "Aborting — git switch -c failed:"
    printf '%s\n' "$switch_err"
    exit 1
  fi
fi
```

Do **not** retry, do **not** force (`git checkout -B`, `git switch -C`,
or anything similar). The user can resolve whatever git is complaining
about (detached HEAD, locked index, dirty tree) and re-run the skill.

## 5. Emit the JSON contract

Print exactly this to stdout (one line, no surrounding prose) so the calling
SKILL can parse it:

```json
{"BRANCH_NAME":"speckit/<NNN>-<slug>","FEATURE_NUM":"<NNN>","SPECIFY_FEATURE_DIRECTORY":"docs/specs/<NNN>-<slug>"}
```

When §1 took the exact-name `GIT_BRANCH_NAME` path and the branch does not
match `^speckit/[0-9]{3}-[a-z0-9-]+$`, emit `"FEATURE_NUM":""` and
`"SPECIFY_FEATURE_DIRECTORY":""` rather than fabricating values; the parent
skill picks its own feature directory.

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
