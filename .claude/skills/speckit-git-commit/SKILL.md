---
description: |
  Stage and commit the freshly generated phase artifacts under
  `docs/specs/<NNN>-<slug>/` as a single Conventional Commit. The subject is
  `docs(specs): <phase> NNN-<slug>`, with one exception: the `spec` phase
  uses the verb `scaffold` (so the subject reads `docs(specs): scaffold
  NNN-<slug>`). Invoked from `.specify/extensions.yml` as the
  `after_specify` / `after_plan` / `after_tasks` hooks; the `checklist` and
  `analyze` phases are manual-only and have no hook entry.
  Runnable manually: `/speckit-git-commit spec | plan | tasks | checklist | analyze`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git diff:*) Bash(git add:*) Bash(git commit:*) Bash(git reset:*) Bash(git log:*) Bash(git ls-files:*) Bash(ls:*) Bash(head:*) Bash(awk:*) Bash(sed:*) Bash(grep:*) Bash(basename:*) Bash(printf:*) Bash(sort:*) Bash(tail:*) Bash(test:*) Bash(jq:*) Bash(python3:*)
argument-hint: "spec|plan|tasks|checklist|analyze"
---

# Spec-Kit git: phase commit

Make exactly one commit per phase, scoped to the spec directory of the
active feature. The Beutl-local SPECS_DIR patch redirects to `docs/specs/`,
but the upstream `/speckit-specify` SKILL still defaults to `specs/` in its
documented prose, so this skill checks both layouts.

## 1. Identify the phase

`$ARGUMENTS` is one of: `spec`, `plan`, `tasks`, `checklist`, `analyze`.

Each phase maps to one or more artifact paths under the spec directory:

| Phase | Artifacts committed |
|---|---|
| `spec` | `spec.md` + `checklists/requirements.md` (the requirements checklist `/speckit-specify` writes in the same flow) |
| `plan` | `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, plus the entire `contracts/` directory (when `/speckit-plan` generated API/IPC contracts for the feature) |
| `tasks` | `tasks.md` |
| `checklist` | the entire `checklists/` directory (`/speckit-checklist` writes files like `ux.md`, `api.md`, `security.md` under it, not a single `checklist.md`) |
| `analyze` | `analysis.md` (only when `/speckit-analyze` wrote one) |

`tasks` is a one-file commit. `spec`, `plan`, `checklist` are
multi-artifact because the upstream `/speckit-*` SKILLs produce design
packs and checklist suites alongside the headline file.

If `$ARGUMENTS` is empty, infer from the union of:

- `git status --porcelain -- docs/specs/ specs/`
- `git ls-files --others --exclude-standard -- docs/specs/ specs/`

Scan both roots — the Beutl-local default (`docs/specs/`) and the upstream
Spec-Kit default (`specs/`) — for the same reason §2 step 4 scans both:
an explicit `SPECIFY_FEATURE_DIRECTORY` or a pre-patch run can leave a
feature under `specs/`. Pick the phase whose file(s) are pending. If
files from multiple distinct phases are pending, **stop** and ask the
user to specify which phase to commit — this skill deliberately commits
one phase at a time.

**Mixed-root ambiguity check.** Before picking a phase, also verify that
the pending paths all live under *one* root. If the same `<NNN>-<slug>`
feature appears under both `docs/specs/` *and* `specs/` (e.g. someone
ran `/speckit-specify` once before and once after the Beutl-local
`SPECS_DIR` patch landed), §2's resolver would pick a single root and
silently commit only half the artifacts. Detect with concrete bash —
do not eyeball the union output, because `git status --porcelain` lines
carry a 2-char `XY` status column (and a ` -> ` arrow for renames) that
must be stripped before path matching:

```bash
# Strip the porcelain XY+space prefix and split rename arrows. Spec
# slugs are kebab-case ([a-z0-9-]), so we do not bother handling
# porcelain's `-z` NUL-terminated form or quoted paths.
#
# Use awk (not `sed s/ -> /\n/`) for the rename split — BSD sed (macOS,
# default on contributor machines) treats `\n` in the *replacement* as
# the two literal characters `\n`, not a newline, which would have
# silently merged `old.md -> new.md` into a single line and dropped the
# post-rename path from `pending_features`. awk's `split(...)` is
# portable across BSD and GNU.
porcelain_paths=$(git status --porcelain -- docs/specs/ specs/ \
  | sed -E 's|^...||' \
  | awk '{ n = split($0, parts, " -> "); for (i = 1; i <= n; i++) print parts[i] }')
untracked_paths=$(git ls-files --others --exclude-standard -- docs/specs/ specs/)

# `[a-z0-9-]+` matches the slugs that §1 of speckit-git-branch validates;
# non-conforming spec directories (e.g. a user-supplied `GIT_BRANCH_NAME`
# whose suffix has uppercase or underscores) silently fall outside the
# regex and are skipped here — that is an accepted limitation, not a
# silent commit, because §2's resolver still picks the directory.
pending_features=$(printf '%s\n%s\n' "$porcelain_paths" "$untracked_paths" \
  | grep -oE '^(docs/specs|specs)/[0-9]{3}-[a-z0-9-]+/' \
  | sort -u)

# If the same NNN-slug appears under both roots, that is the ambiguity.
mixed=$(printf '%s\n' "$pending_features" \
  | sed -E 's|^(docs/specs|specs)/||' \
  | sort \
  | uniq -d)

if [ -n "$mixed" ]; then
  echo "Aborting — these features have pending files under both"
  echo "docs/specs/ AND specs/ (mixed-root):"
  printf '%s\n' "$mixed" | sed 's/^/  /'
  echo "Move or remove one copy of each before re-running this skill."
  exit 1
fi
```

If `$ARGUMENTS` is empty **and** neither command shows anything pending
under either root, exit 0 with `nothing to commit — no spec-kit
artifacts are pending`. Do not guess a phase, do not prompt; this is a
no-op completion, not a failure.

## 2. Locate the spec directory

Resolve `SPEC_DIR` in this order, mirroring how `setup-plan.sh` /
`setup-tasks.sh` resolve `SPECIFY_FEATURE_DIRECTORY` (feature.json wins
over branch name; we then add Beutl-specific fallbacks):

1. **`.specify/feature.json` `feature_directory`.** When the upstream
   `/speckit-specify` writes this file, it is the canonical handle on the
   active feature. Read it with `jq` (or `python3` / `grep+sed` fallback)
   and use the value if the directory exists.
2. **`SPECIFY_FEATURE_DIRECTORY` from env / context.** If the parent skill
   or the user passed it explicitly, honour it — but first verify the path
   lives under `docs/specs/` or `specs/` (relative or absolute under the
   repo root). A value pointing elsewhere is almost certainly a typo or an
   injected override; refuse rather than commit outside the spec tree:

   ```bash
   repo_root=$(git rev-parse --show-toplevel)
   case "$SPECIFY_FEATURE_DIRECTORY" in
     docs/specs/*|specs/*) ;;
     "$repo_root"/docs/specs/*|"$repo_root"/specs/*) ;;
     "") ;;  # not set — fall through to the next resolution step.
     *)
       echo "Refusing — SPECIFY_FEATURE_DIRECTORY ($SPECIFY_FEATURE_DIRECTORY)"
       echo "must live under docs/specs/ or specs/."
       exit 1 ;;
   esac
   ```
3. **Current branch.** If `git rev-parse --abbrev-ref HEAD` matches
   `speckit/<NNN>-<slug>`, prefer `docs/specs/<NNN>-<slug>` (Beutl
   convention) and fall back to `specs/<NNN>-<slug>` only when the former
   does not exist.
4. **The phase file's actual location.** Look at `git status --porcelain`
   and `git ls-files --others --exclude-standard` under both `docs/specs/`
   and `specs/` and pick the directory whose mapped phase file is pending.
5. **Last resort** — and only when steps 1-4 yield no answer: take the
   highest-numbered `<root>/<NNN>-*/` directory across both roots:

   ```bash
   SPEC_DIR=$( {
     ls -1d docs/specs/[0-9][0-9][0-9]-* 2>/dev/null
     ls -1d      specs/[0-9][0-9][0-9]-* 2>/dev/null
   } | sort | tail -1 )
   ```

If the resolved `SPEC_DIR` is empty or the expected `<phase>` file(s) do
not live under it, stop and report the mismatch — do not commit to the
wrong directory by inertia.

Extract the slug:

```bash
NNN_SLUG=$(basename "$SPEC_DIR")   # e.g. 001-add-foo-button
NNN=${NNN_SLUG%%-*}                # 001
SLUG=${NNN_SLUG#*-}                # add-foo-button
```

Also compute the repo-relative form of `SPEC_DIR` once so later steps can
match it against `git diff --cached --name-only` and `git ls-files --deleted`
output, both of which report repo-relative paths even when `SPEC_DIR` was
resolved from an absolute `SPECIFY_FEATURE_DIRECTORY`:

```bash
repo_root=$(git rev-parse --show-toplevel)
case "$SPEC_DIR" in
  "$repo_root"/*) SPEC_DIR_REL="${SPEC_DIR#$repo_root/}" ;;
  *)              SPEC_DIR_REL="$SPEC_DIR" ;;
esac
```

## 3. Verify the target file(s)

Build the `TARGETS` list from the phase → files mapping in §1. For the
`plan` phase, the mapping includes the whole `contracts/` directory; expand
it to its leaf files via `git ls-files` (cached + untracked, honouring
`.gitignore`) so editor scratch files / `.DS_Store` / OS junk never leak
into the staged set the way `find -type f` would. The output is also what
`git diff --cached --name-only` will report later, so the comparison in §4.3
stays apples-to-apples. Skip mapped paths that do not actually exist (e.g.
`analyze` may produce no file at all, `contracts/` may be absent for a
UI-only plan):

```bash
case "$PHASE" in
  spec)      MAPPED_FILES="spec.md checklists/requirements.md"
             MAPPED_DIRS="" ;;
  plan)      MAPPED_FILES="plan.md research.md data-model.md quickstart.md"
             MAPPED_DIRS="contracts" ;;
  tasks)     MAPPED_FILES="tasks.md"; MAPPED_DIRS="" ;;
  checklist) MAPPED_FILES=""
             MAPPED_DIRS="checklists" ;;
  analyze)   MAPPED_FILES="analysis.md"; MAPPED_DIRS="" ;;
  *)         echo "Unknown phase: $PHASE"
             echo "Valid phases: spec | plan | tasks | checklist | analyze"
             exit 1 ;;
esac

TARGETS=""
for f in $MAPPED_FILES; do
  p="$SPEC_DIR/$f"
  [ -f "$p" ] && TARGETS="$TARGETS $p"
done
for d in $MAPPED_DIRS; do
  dp="$SPEC_DIR/$d"
  [ -d "$dp" ] || continue
  # cached: tracked files under $dp; others + --exclude-standard: untracked
  # files that are NOT .gitignore-d. Together this is "files git would touch
  # if you ran `git add -A -- $dp`", which is exactly the set we want.
  while IFS= read -r leaf; do
    [ -n "$leaf" ] && TARGETS="$TARGETS $leaf"
  done < <(git ls-files --cached --others --exclude-standard -- "$dp")
done
TARGETS="${TARGETS# }"

[ -n "$TARGETS" ] || { echo "No $PHASE artifacts under $SPEC_DIR"; exit 1; }
```

Check whether at least one target file is **pending** (untracked, modified,
staged, or **deleted**). The pending subset is what we will compare against
the staged set after `git add` — Codex flagged that requiring the full
`TARGETS` list to match the staged set wrongly aborted partial-file edits
in the plan phase (e.g. editing only `plan.md` and leaving `research.md`
unchanged).

Also pick up files that **used to exist** in the mapped layout but were
deleted in the current working tree (e.g. `quickstart.md` removed during a
plan rewrite, an obsolete `contracts/*.md`). `TARGETS` is populated only
from files that still exist, so the deleted set has to come from
`git ls-files --deleted` filtered by the same mapping prefixes.

```bash
# 4a. PENDING from still-existing TARGETS.
PENDING=""
for t in $TARGETS; do
  is_pending=""
  if git ls-files --others --exclude-standard -- "$t" | grep -q .; then
    is_pending="yes"
  elif ! git diff --quiet -- "$t"; then
    is_pending="yes"
  elif ! git diff --cached --quiet -- "$t"; then
    is_pending="yes"
  fi
  [ -n "$is_pending" ] && PENDING="$PENDING $t"
done

# 4b. PENDING also includes deletions of mapped paths.
#     Compose grep alternation from MAPPED_FILES / MAPPED_DIRS so a stray
#     deletion outside the mapping is never picked up. The `grep -E
#     "^(${prefix_re})$"` below already anchors at both ends, so each
#     alternative carries no trailing `$` — adding a literal `\$` here
#     would produce a double `$$` anchor that can never match
#     `git ls-files --deleted`.
prefix_re=""
for f in $MAPPED_FILES; do
  prefix_re="${prefix_re:+$prefix_re|}$(printf '%s/%s' "$SPEC_DIR_REL" "$f" | sed 's|[].[*^$/\\]|\\&|g')"
done
for d in $MAPPED_DIRS; do
  prefix_re="${prefix_re:+$prefix_re|}$(printf '%s/%s/' "$SPEC_DIR_REL" "$d" | sed 's|[].[*^$/\\]|\\&|g').*"
done
if [ -n "$prefix_re" ]; then
  # Run `git ls-files --deleted` in its own statement and **check the
  # exit code explicitly**. A bare `deleted_all=$(git ls-files --deleted)`
  # discards git's status, so a real failure (corrupt index, lock
  # contention, permission error) would silently degrade to "no
  # deletions" — the same class of silent failure as the prior
  # `|| true` over the whole pipeline. The brace-grouped `|| true`
  # around grep below is still required: grep returning 1 on "zero
  # matches" is the normal case (most invocations have no deletions).
  #
  # `2>&1` folds stderr into the captured stream so that on the error
  # branch `$deleted_all` carries git's diagnostic; on the success
  # branch git never writes to stderr, so $deleted_all is the plain
  # newline-separated list of deleted paths that the loop below
  # consumes verbatim.
  if ! deleted_all=$(git ls-files --deleted 2>&1); then
    echo "Aborting — git ls-files --deleted failed:"
    printf '%s\n' "$deleted_all"
    exit 1
  fi
  while IFS= read -r dl; do
    [ -n "$dl" ] && PENDING="$PENDING $dl"
  done < <(printf '%s\n' "$deleted_all" | { grep -E "^(${prefix_re})$" || true; })
fi

PENDING="${PENDING# }"
[ -n "$PENDING" ] || { echo "No pending changes for phase $PHASE"; exit 0; }
```

## 4. Stage **only** the pending phase files

`git add -- "$SPEC_DIR"` would sweep in any other pending edits inside the
spec directory (the user's draft notes, an old `checklist.md`, a stray
`scratch.md`). Stage only the pending subset of the mapping.

Before staging, **snapshot the user's pre-existing index**. If the
staged-set check fails later, the abort path must only un-stage what this
skill added — Codex flagged that a blanket `git reset -- $PENDING` would
wipe phase files the user had already `git add`-ed before invoking us.

`git add -A` on a path also records deletions, which is what we want:

```bash
# 4.1 Snapshot what was already staged before we touch the index.
prestaged=$(git diff --cached --name-only | sort -u)

# 4.2 Stage the pending subset of the mapping.
# Every entry in $PENDING is a *leaf file path* (TARGETS expansion in §3
# enumerates leaves via `git ls-files`; §4b adds leaves from
# `git ls-files --deleted`). `git add -A` on a directory would sweep in
# sibling files, so never let $PENDING grow to contain a directory.
# shellcheck disable=SC2086
git add -A -- $PENDING
```

Then verify the staged set matches the pending subset — no more, no less.
**Normalize both sides to repo-relative paths** before comparing: when
`SPEC_DIR` came from an absolute `SPECIFY_FEATURE_DIRECTORY` or
`.specify/feature.json` value, `PENDING` will hold absolute paths, but
`git diff --cached --name-only` always reports repo-relative paths.

```bash
# 4.3 Use repo_root computed in §2 to normalize PENDING entries.
to_rel() {
  case "$1" in
    "$repo_root"/*) printf '%s' "${1#$repo_root/}" ;;
    *)              printf '%s' "$1" ;;
  esac
}

expected=$(for p in $PENDING; do to_rel "$p"; echo; done | sort -u | grep -v '^$')
actual=$(git diff --cached --name-only | sort -u)

# Anything that was staged *before* we ran but is also a pending phase
# target is fine — it just means the user pre-staged a phase file. Treat
# those as already accounted for when comparing.
prestaged_phase=$(printf '%s\n' "$prestaged" | grep -Fx -f <(printf '%s\n' "$expected") || true)
prestaged_other=$(printf '%s\n' "$prestaged" | grep -Fxv -f <(printf '%s\n' "$expected") || true)

# Compute "what this skill added" once, up front. §6's rollback path
# reuses the same variable so a failed `git commit` un-stages exactly
# what we added — never what the user pre-staged.
added_by_us=$(printf '%s\n' "$expected" | grep -Fxv -f <(printf '%s\n' "$prestaged_phase") || true)

if [ -n "$prestaged_other" ]; then
  echo "Aborting — unrelated files were already staged before this skill ran:"
  echo "$prestaged_other"
  if [ -n "$added_by_us" ]; then
    # shellcheck disable=SC2086
    if ! reset_err=$(git reset --quiet -- $added_by_us 2>&1); then
      echo "Warning — rollback (git reset) failed:"
      printf '%s\n' "$reset_err"
      echo "The index may be partially staged. Inspect with \`git status\`."
    fi
  fi
  exit 1
fi

if [ "$expected" != "$actual" ]; then
  echo "Aborting — staged set differs from the pending phase targets."
  echo "Expected:"; echo "$expected"
  echo "Got:";      echo "$actual"
  if [ -n "$added_by_us" ]; then
    # shellcheck disable=SC2086
    if ! reset_err=$(git reset --quiet -- $added_by_us 2>&1); then
      echo "Warning — rollback (git reset) failed:"
      printf '%s\n' "$reset_err"
      echo "The index may be partially staged. Inspect with \`git status\`."
    fi
  fi
  exit 1
fi
```

If the user wants additional files in the same commit (e.g. supplementary
notes under `notes/`), they should run a separate `git add` and `git
commit` themselves — this skill keeps phase commits clean.

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

Body (optional but recommended): copy the first non-heading line from the
**primary** file (`spec.md` / `plan.md` / `tasks.md` / `analysis.md`, or —
for the `checklist` phase — `checklists/requirements.md` when present, else
the most recently modified file under `checklists/`) so reviewers see what
the commit is about without opening it. For the multi-file `plan` phase,
list the additional artifacts after the summary line.

```bash
# For phases with a primary mapped file, use the first entry in $MAPPED_FILES.
# The `checklist` phase has no mapped file (only $MAPPED_DIRS=checklists), so
# resolve a primary file from the checklists/ directory.
if [ -n "$MAPPED_FILES" ]; then
  PRIMARY="$SPEC_DIR/$(printf '%s' "$MAPPED_FILES" | awk '{print $1}')"
else
  # checklist phase: prefer checklists/requirements.md, else newest checklist file.
  if [ -f "$SPEC_DIR/checklists/requirements.md" ]; then
    PRIMARY="$SPEC_DIR/checklists/requirements.md"
  else
    # newest checklist .md file (BSD/GNU ls both honour -t for mtime sort)
    PRIMARY=$(ls -t "$SPEC_DIR/checklists/"*.md 2>/dev/null | head -n 1)
  fi
fi
if [ -n "$PRIMARY" ] && [ -f "$PRIMARY" ]; then
  # First non-empty, non-heading line. We deliberately do NOT skip line 1 —
  # markdown files that begin with a paragraph (no leading `# Title`) would
  # otherwise yield an empty summary.
  SUMMARY=$(awk 'NF && $0 !~ /^#/ {print; exit}' "$PRIMARY" | head -c 200)
else
  SUMMARY=""
fi
```

## 6. Commit

Run the commit. Pass `$SUMMARY` as a second `-m` **only when it is
non-empty** — passing an empty `-m ""` produces a noisy trailing blank
paragraph in the commit body.

```bash
if [ -n "$SUMMARY" ]; then
  commit_status=0
  git commit -m "$SUBJECT" -m "$SUMMARY" || commit_status=$?
else
  commit_status=0
  git commit -m "$SUBJECT" || commit_status=$?
fi

if [ "$commit_status" -ne 0 ]; then
  echo "Aborting — git commit exited with status $commit_status."
  echo "A pre-commit hook, GPG signing, or the commit-msg hook likely"
  echo "rejected the commit. Re-staging is left alone, but anything"
  echo "this skill added on top of the user's pre-staged set is rolled"
  echo "back using the same path as §4.3's abort branches."
  if [ -n "$added_by_us" ]; then
    # shellcheck disable=SC2086
    if ! reset_err=$(git reset --quiet -- $added_by_us 2>&1); then
      echo "Warning — rollback (git reset) failed:"
      printf '%s\n' "$reset_err"
      echo "The index may be partially staged. Inspect with \`git status\`."
    fi
  fi
  exit "$commit_status"
fi
```

Do not use `--amend`, `--allow-empty`, or `--no-verify`. Do not push.

## 7. Report

Emit a JSON line so the calling SKILL can record the result, followed by a
short human summary. `paths` is an array because `plan` commits multiple
files in one commit:

```json
{"sha":"<short-sha>","paths":["docs/specs/<NNN>-<slug>/<file>", "..."],"subject":"docs(specs): <phase> <NNN>-<slug>"}
```

## Refusals

- Never `git add` outside the resolved `SPEC_DIR`.
- Never `git push`. The user pushes when they are ready.
- Never amend a previous commit.
- Never run `dotnet format` / linters / tests as part of this skill — its
  scope is exactly one commit of one phase's artifact(s).
