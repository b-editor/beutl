---
description: |
  Stage and commit the freshly generated spec / plan / tasks file under
  `docs/specs/<NNN>-<slug>/` as a single Conventional Commit
  (`docs(specs): <phase> NNN-<slug>`). Invoked from `.specify/extensions.yml`
  as the `after_specify` / `after_plan` / `after_tasks` hooks; also runnable
  manually: `/speckit-git-commit spec | plan | tasks | checklist | analyze`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git diff:*) Bash(git add:*) Bash(git commit:*) Bash(git reset:*) Bash(git log:*) Bash(git ls-files:*) Bash(ls:*) Bash(head:*) Bash(awk:*) Bash(sed:*) Bash(grep:*) Bash(basename:*) Bash(sort:*) Bash(tail:*) Bash(test:*) Bash(jq:*) Bash(python3:*) Bash(find:*)
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

If `$ARGUMENTS` is empty, infer from `git status --porcelain --
docs/specs/` and `git ls-files --others --exclude-standard --
docs/specs/`: pick the phase whose file(s) are pending. If files from
multiple distinct phases are pending, **stop** and ask the user to specify
which phase to commit — this skill deliberately commits one phase at a time.

## 2. Locate the spec directory

Resolve `SPEC_DIR` in this order, mirroring how `setup-plan.sh` /
`setup-tasks.sh` resolve `SPECIFY_FEATURE_DIRECTORY` (feature.json wins
over branch name; we then add Beutl-specific fallbacks):

1. **`.specify/feature.json` `feature_directory`.** When the upstream
   `/speckit-specify` writes this file, it is the canonical handle on the
   active feature. Read it with `jq` (or `python3` / `grep+sed` fallback)
   and use the value if the directory exists.
2. **`SPECIFY_FEATURE_DIRECTORY` from env / context.** If the parent skill
   or the user passed it explicitly, honour it (sanity-check that it lives
   under `docs/specs/` or `specs/`).
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

## 3. Verify the target file(s)

Build the `TARGETS` list from the phase → files mapping in §1. For the
`plan` phase, the mapping includes the whole `contracts/` directory; expand
it to its leaf files via `find` so the staged set is comparable to what
`git diff --cached --name-only` reports later. Skip mapped paths that do
not actually exist (e.g. `analyze` may produce no file at all, `contracts/`
may be absent for a UI-only plan):

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
  *)         echo "Unknown phase: $PHASE"; exit 1 ;;
esac

TARGETS=""
for f in $MAPPED_FILES; do
  p="$SPEC_DIR/$f"
  [ -f "$p" ] && TARGETS="$TARGETS $p"
done
for d in $MAPPED_DIRS; do
  dp="$SPEC_DIR/$d"
  [ -d "$dp" ] || continue
  while IFS= read -r leaf; do
    [ -n "$leaf" ] && TARGETS="$TARGETS $leaf"
  done < <(find "$dp" -type f)
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
#     deletion outside the mapping is never picked up.
prefix_re=""
for f in $MAPPED_FILES; do
  prefix_re="${prefix_re:+$prefix_re|}$(printf '%s/%s' "$SPEC_DIR" "$f" | sed 's|[].[*^$/\\]|\\&|g')\$"
done
for d in $MAPPED_DIRS; do
  prefix_re="${prefix_re:+$prefix_re|}$(printf '%s/%s/' "$SPEC_DIR" "$d" | sed 's|[].[*^$/\\]|\\&|g').*"
done
if [ -n "$prefix_re" ]; then
  while IFS= read -r dl; do
    [ -n "$dl" ] && PENDING="$PENDING $dl"
  done < <(git ls-files --deleted | grep -E "^(${prefix_re})$" || true)
fi

PENDING="${PENDING# }"
[ -n "$PENDING" ] || { echo "No pending changes for phase $PHASE"; exit 0; }
```

## 4. Stage **only** the pending phase files

`git add -- "$SPEC_DIR"` would sweep in any other pending edits inside the
spec directory (the user's draft notes, an old `checklist.md`, a stray
`scratch.md`). Stage only the pending subset of the mapping. `git add -A`
on a path also records deletions, which is what we want:

```bash
# shellcheck disable=SC2086
git add -A -- $PENDING
```

Then verify the staged set matches the pending subset — no more, no less.
**Normalize both sides to repo-relative paths** before comparing: when
`SPEC_DIR` came from an absolute `SPECIFY_FEATURE_DIRECTORY` or
`.specify/feature.json` value, `PENDING` will hold absolute paths, but
`git diff --cached --name-only` always reports repo-relative paths.

```bash
repo_root=$(git rev-parse --show-toplevel)

# Strip the repo prefix from any absolute path; leave repo-relative
# paths untouched.
to_rel() {
  case "$1" in
    "$repo_root"/*) printf '%s' "${1#$repo_root/}" ;;
    *)              printf '%s' "$1" ;;
  esac
}

expected=$(for p in $PENDING; do to_rel "$p"; echo; done | sort -u | grep -v '^$')
actual=$(git diff --cached --name-only | sort -u)

if [ "$expected" != "$actual" ]; then
  echo "Aborting — staged set differs from the pending phase targets."
  echo "Expected:"; echo "$expected"
  echo "Got:";      echo "$actual"
  # shellcheck disable=SC2086
  git reset --quiet -- $PENDING
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
**primary** file (`spec.md` / `plan.md` / `tasks.md` / `checklist.md` /
`analysis.md`) so reviewers see what the commit is about without opening
it. For the multi-file `plan` phase, list the additional artifacts after
the summary line.

```bash
# Primary file is always the first entry in $MAPPED_FILES.
PRIMARY="$SPEC_DIR/$(printf '%s' "$MAPPED_FILES" | awk '{print $1}')"
SUMMARY=$(awk 'NR>1 && NF && $0 !~ /^#/ {print; exit}' "$PRIMARY" | head -c 200)
```

## 6. Commit

```bash
git commit -m "$SUBJECT" -m "$SUMMARY"
```

Do not use `--amend` and do not use `--allow-empty`. Do not push.

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
