---
description: |
  Stage and commit the freshly generated spec / plan / tasks file under
  `docs/specs/<NNN>-<slug>/` as a single Conventional Commit
  (`docs(specs): <phase> NNN-<slug>`). Invoked from `.specify/extensions.yml`
  as the `after_specify` / `after_plan` / `after_tasks` hooks; also runnable
  manually: `/speckit-git-commit spec | plan | tasks | checklist | analyze`.
allowed-tools: Bash(git rev-parse:*) Bash(git status:*) Bash(git diff:*) Bash(git add:*) Bash(git commit:*) Bash(git log:*) Bash(git ls-files:*) Bash(ls:*) Bash(head:*) Bash(awk:*) Bash(sed:*) Bash(grep:*) Bash(basename:*) Bash(sort:*) Bash(tail:*) Bash(test:*)
argument-hint: "spec|plan|tasks|checklist|analyze"
---

# Spec-Kit git: phase commit

Make exactly one commit per phase, scoped to `docs/specs/<NNN>-<slug>/`.

## 1. Identify the phase

`$ARGUMENTS` is one of: `spec`, `plan`, `tasks`, `checklist`, `analyze`.

Each phase maps to one or more Markdown artifacts:

| Phase | Files committed |
|---|---|
| `spec` | `spec.md` |
| `plan` | `plan.md`, `research.md`, `data-model.md`, `quickstart.md` |
| `tasks` | `tasks.md` |
| `checklist` | `checklist.md` |
| `analyze` | `analysis.md` (only when `/speckit-analyze` wrote one) |

`spec` / `tasks` / `checklist` are one-file commits. `plan` is the only
multi-file phase because `/speckit-plan` produces the design pack alongside
the plan itself.

If `$ARGUMENTS` is empty, infer from `git status --porcelain --
docs/specs/` and `git ls-files --others --exclude-standard --
docs/specs/`: pick the phase whose file(s) are pending. If files from
multiple distinct phases are pending, **stop** and ask the user to specify
which phase to commit — this skill deliberately commits one phase at a time.

## 2. Locate the spec directory

Resolve `SPEC_DIR` in this order — falling through silently to the wrong
directory is the most common way this skill could commit the wrong feature:

1. **Current branch.** If `git rev-parse --abbrev-ref HEAD` matches
   `speckit/<NNN>-<slug>`, use `docs/specs/<NNN>-<slug>`.
2. **`SPECIFY_FEATURE_DIRECTORY` from context.** If the parent skill or the
   user passed it via env / argument, honour it (after a sanity check that
   it lives under `docs/specs/`).
3. **The phase file's actual location.** Look at `git status --porcelain --
   docs/specs/` and pick the directory whose `<phase>.md` is pending.
4. **Last resort** — and only when steps 1-3 yield no answer: take the
   highest-numbered `docs/specs/<NNN>-*/` directory:

   ```bash
   SPEC_DIR=$(ls -1d docs/specs/[0-9][0-9][0-9]-* 2>/dev/null | sort | tail -1)
   ```

If the resolved `SPEC_DIR` is empty or the expected `<phase>.md` does not
live under it, stop and report the mismatch — do not commit to the wrong
directory by inertia.

Extract the slug:

```bash
NNN_SLUG=$(basename "$SPEC_DIR")   # e.g. 001-add-foo-button
NNN=${NNN_SLUG%%-*}                # 001
SLUG=${NNN_SLUG#*-}                # add-foo-button
```

## 3. Verify the target file(s)

Build the `TARGETS` list from the phase → files mapping in §1. Skip any
mapped file that does not actually exist (e.g. `analyze` may produce no
file at all in some runs):

```bash
case "$PHASE" in
  spec)      MAPPED="spec.md" ;;
  plan)      MAPPED="plan.md research.md data-model.md quickstart.md" ;;
  tasks)     MAPPED="tasks.md" ;;
  checklist) MAPPED="checklist.md" ;;
  analyze)   MAPPED="analysis.md" ;;
  *)         echo "Unknown phase: $PHASE"; exit 1 ;;
esac

TARGETS=""
for f in $MAPPED; do
  p="$SPEC_DIR/$f"
  [ -f "$p" ] && TARGETS="$TARGETS $p"
done
TARGETS="${TARGETS# }"

[ -n "$TARGETS" ] || { echo "No $PHASE artifacts under $SPEC_DIR"; exit 1; }
```

Check whether at least one target file is actually pending. **Untracked
files do not show up in `git diff`**, but newly generated files are
typically untracked on their first commit. Test all three states across
all targets:

```bash
pending=""
for t in $TARGETS; do
  if git ls-files --others --exclude-standard -- "$t" | grep -q .; then
    pending="yes"; break
  fi
  if ! git diff --quiet -- "$t" ; then pending="yes"; break; fi
  if ! git diff --cached --quiet -- "$t" ; then pending="yes"; break; fi
done
[ -n "$pending" ] || { echo "No pending changes for phase $PHASE"; exit 0; }
```

## 4. Stage **only** the mapped phase files

`git add -- "$SPEC_DIR"` would sweep in any other pending edits inside the
spec directory (the user's draft notes, an old `checklist.md`, a stray
`scratch.md`). Stage only the mapped files:

```bash
# shellcheck disable=SC2086
git add -- $TARGETS
```

Then verify the staged set is exactly the targets — no more, no less:

```bash
expected=$(printf '%s\n' $TARGETS | sort -u)
actual=$(git diff --cached --name-only | sort -u)
if [ "$expected" != "$actual" ]; then
  echo "Aborting — staged set differs from the mapped phase targets."
  echo "Expected:"; echo "$expected"
  echo "Got:";      echo "$actual"
  git reset --quiet -- $TARGETS
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
# Primary file is always the first entry in $MAPPED.
PRIMARY="$SPEC_DIR/$(printf '%s' "$MAPPED" | awk '{print $1}')"
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

- Never `git add` outside `docs/specs/<NNN>-<slug>/`.
- Never `git push`. The user pushes when they are ready.
- Never amend a previous commit.
- Never run `dotnet format` / linters / tests as part of this skill — its
  scope is exactly one commit of exactly one Markdown file.
