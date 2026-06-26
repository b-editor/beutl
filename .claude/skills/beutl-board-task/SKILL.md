---
description: |
  Pick up one task from the GitHub Project #9 task board (any kind — bug, perf/quality,
  design improvement, or feature), verify it is not a false positive, then plan, implement,
  test, and open a PR. Use when the user says "ボードからタスクを選んで作業して",
  "projects/9 のタスクをやって", "pick a task from the board", "consume a backlog item",
  or similar. If the chosen item turns out to be a false positive, set its Status to
  "False positive" and move to the next candidate.
argument-hint: "[item-number | title-keyword | bug|diff|design|feature]"
---

# Work a task board item

GitHub Project **#9** (`b-editor/projects/9`, display name "AI Review") is the team's task board.
It holds both **auto-detected findings** (filed by `scheduled-code-review.yml` /
`claude-code-review.yml`) and **hand-added tasks** — including features. This skill turns any one
of those items into a merge-ready PR, end to end.

> Running this skill standalone opens a PR and leaves merging to a human. When it is dispatched as
> one tick of **`/beutl-loop`**, the loop pre-authorizes the per-item actions, classifies the
> resulting PR's risk, and decides auto-merge vs. human-merge — this skill itself never merges.
> See `docs/ai-workflow/loop-engineering.md`.

## Board coordinates (project #9)

Stable IDs (re-discover with the commands in the next section if they ever drift):

- Project: owner `b-editor`, number `9`, project-id `PVT_kwDOBLw8Fs4BW4g5`
- `Status` field: `PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk`
  - `In Progress` → `47fc9ee4`
  - `Todo` → `f75ad846`
  - `Backlog` → `d97cd69b`
  - `Done` → `98236657`
  - `False positive` → `e6ff360e`

Most items are **DraftIssue** (no repo issue number). Draft issues cannot carry a "Linked
pull requests" value automatically, so cross-link by **referencing the board in the PR body**
and by moving the item's `Status`.

## Step 1 — List candidates and pick one

Fetch the whole board **once** to a file, then drive everything (table, selection, item id, body)
off that single snapshot. `gh project item-list --limit` is the *maximum number of items fetched*,
not a page offset — the default is 100, so a board that has grown past that silently drops the tail.
Pass a limit comfortably above the current item count and warn if the snapshot looks truncated.

```bash
BOARD=$(mktemp /tmp/board-items.XXXX.json)
gh project item-list 9 --owner b-editor --limit 2000 --format json > "$BOARD"

# Status + type + title, indexed. Warn if we may have hit the fetch ceiling.
python3 -c "
import json
d=json.load(open('$BOARD'))
items=d['items']
for i,it in enumerate(items):
    c=it.get('content',{})
    print(i,'|',it.get('status','?'),'|',c.get('type','?'),'|',c.get('title','')[:90])
if len(items) >= 2000:
    print('WARNING: hit the --limit ceiling; raise --limit and re-fetch before trusting this list.')
"
```

**Candidate filter.** Only **unclaimed** items are candidates: `Backlog` and `Todo`. Skip `Done` /
`False positive`, and skip `In Progress` too — on this board `In Progress` means another
agent/contributor has already claimed it, so treating it as a candidate invites duplicate work.

**Select across the full spectrum — do not pre-filter by risk or kind.** Every kind is in scope:
bugs (`[Bug]`), review findings (`[YYYY-MM-DD][diff]`), design improvements (`設計改善:`), **and
plain feature titles** (e.g. "リップル削除", "マルチカム編集", "ショートカット: …"). Do **not** skip an item
merely because it is a feature, higher-risk, or UI-layer. Risk is handled downstream, not by
excluding work here: when this skill runs inside `/beutl-loop`, the loop classifies each PR's risk
and routes higher-risk ones to a human for merge (see `docs/ai-workflow/loop-engineering.md`); when
run standalone, a human reviews and merges every PR anyway.

Two guards on selection:
- **Underspecified features are `blocked`, not silently skipped.** If a feature item lacks enough
  detail to implement and verify a concrete outcome, surface it as blocked (and, in a loop, let the
  stagnation/blocked path handle it) — do not fabricate scope.
- **Hard-to-unit-test items (often UI) still need verification.** If a fix cannot carry an NUnit
  test, document a concrete **manual verification** procedure in the plan and PR instead of shipping
  untested.

If `$ARGUMENTS` is an index/number or a title keyword, jump straight to that item. Otherwise,
present the top candidates to the user with AskUserQuestion before committing to one (unless the
user already told you to just pick one, e.g. when dispatched by `/beutl-loop`).

Resolve the pick against the **same snapshot** and capture its **stable item id** now. Do not
re-run `item-list` and re-select by numeric index later — the shared board can have items added,
archived, or reordered between calls, so the same index can point at a different task. Everything
downstream (verify, claim, board update) uses `$ITEM_ID`, never the index.

```bash
# INDEX = the leading integer printed for the chosen row above.
INDEX=12
read -r ITEM_ID ITEM_TITLE <<EOF
$(python3 -c "
import json
it=json.load(open('$BOARD'))['items'][$INDEX]
print(it['id'], it['content'].get('title',''))
")
EOF
echo "Picked: $ITEM_ID — $ITEM_TITLE"

# Read the chosen item's full body (file:line + suggested fix) from the snapshot.
python3 -c "
import json
it=next(x for x in json.load(open('$BOARD'))['items'] if x['id']=='$ITEM_ID')
print(it['content'].get('title','')); print(); print(it['content'].get('body',''))
"
```

## Step 2 — Validate the item before planning (mandatory)

What "validate" means depends on the item's **kind** — take the matching path. (The `In Progress`
claim below applies to both.) A board item is either an auto-detected **review finding** (it cites a
`file:line`) or a hand-added **product/feature task** (a feature title, no cited code location).

### Path A — review findings (`[Bug]`, `[YYYY-MM-DD][diff]`, `設計改善:` — anything citing a `file:line`)

These are heuristic and sometimes wrong, so confirm the finding against the **current** code:

- Open the cited `file:line` and confirm the described code still exists and the reasoning holds.
- Trace the claim. Scheduled-review findings are heuristic and sometimes wrong. Common false
  positives seen on this board:
  - A "use-after-dispose / NRE" where an eager token / guard already aborts the path.
  - A "race" on state that is in practice single-threaded, or already guarded elsewhere.
  - A perf claim on a path that is not actually hot.
- If the surrounding code makes the finding **invalid**, set Status → `False positive` and move on:

```bash
# Set Status -> "False positive"
gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 \
  --id "$ITEM_ID" \
  --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk \
  --single-select-option-id e6ff360e
```

`$ITEM_ID` is the stable id captured in Step 1 — reuse it, do not re-derive from an index. Then
return to Step 1 for another candidate (re-fetch the snapshot). **Do not** silently keep the
smaller diff or fabricate a fix for a wrong finding.

### Path B — product / feature tasks (hand-added: a feature title, no cited code location)

A feature item has no `file:line` to refute and **no false-positive concept** — do **not** force it
through Path A, or you will end up inventing verification evidence for a finding that does not exist.
Validate it on its own terms instead:

- **Already implemented or obsolete?** Grep the codebase / recent history for the feature. If it is
  already present, or a newer decision dropped it, it is not work to do — set Status → `Done`
  (already shipped) or `False positive` (no longer wanted) with a one-line note, and return to
  Step 1. This is the feature analog of the false-positive exit.
- **Specified enough to implement and verify?** If the title is a bare idea with no acceptance
  criteria you can turn into a test or a concrete manual-verification procedure, it is `blocked`
  (Step 1's "underspecified features are blocked" guard) — surface it; do not fabricate scope.
- Otherwise it is valid, well-specified work: there is nothing to refute — proceed to claim and plan.

### Claim the item immediately

Once the item is validated (a finding confirmed real, or a feature confirmed not-already-done and
specified enough) — and after the user has confirmed the pick — move the item to `In Progress`
**right away**, before planning/implementing, not after the PR opens. The board is shared, so
claiming late leaves a long window where another agent can start the same task.

```bash
gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 \
  --id "$ITEM_ID" \
  --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk \
  --single-select-option-id 47fc9ee4   # In Progress
```

## Step 3 — Plan

Enter plan mode (`EnterPlanMode`) and produce a focused plan. Include:

- **Context**: why the change is needed — for a finding, the real problem quoted from it + your
  verification; for a feature, the desired behavior and its acceptance criteria.
- The Step-2 validation result: for a finding, a note it was confirmed **not** a false positive with
  the evidence; for a feature, a one-line confirmation it is not already implemented and is specified
  enough to verify.
- The concrete edit(s): the `file:line` you will change — or, for a feature, the new files/types you
  will add — and the before/after shape.
- A **test** plan (see Step 5) — this repo requires new logic to ship with an NUnit test
  (`.claude/rules/csharp.md`, AGENTS.md rule #3).
- A quick **recent-merge scan**: skim `git log --oneline -15 origin/main` for commits tagged
  `Refs: Project #9`; if a just-merged PR removed or refactored a pattern this task would reintroduce,
  adjust the plan (a soft signal, not a gate).
- Verification commands and the commit/PR/board updates.

Surface non-obvious design trade-offs to the user (AGENTS.md "adopt better designs eagerly").
For public-surface / extensibility changes, auto-delegate `@beutl-design-reviewer`.
For a feature that needs a new public type or ≥ 3 new files, route through the Spec-Kit flow
(`/speckit-specify → /speckit-plan → /speckit-tasks → /speckit-implement`) instead of a single
minimal-change tick — it is too large to verify as one root-cause fix. When dispatched by
`/beutl-loop`, the runner returns `speckit_required` and the orchestrator runs the flow; standalone,
run the Spec-Kit skills yourself.

### Create the work branch now — before any edits

Create/switch to the feature branch **before** Step 4/5 touch any files. If you edit and run the
tests first and only branch at commit time, `git switch -c <branch> origin/main` either drags the
dirty edits onto a different base (so the PR is built on code you never actually tested) or aborts
on conflict. Branch first, then edit and test on that branch — what you verify is exactly what ships.

Pick the branch name from the branch you are currently on:

- **Already on a personal feature branch shaped `<prefix>/<feature>`** (e.g. a worktree branch like
  `yuto-trd/tmp-1`): **keep the `<prefix>/` and replace only the `<feature>` part** with a
  descriptive slug — e.g. `yuto-trd/tmp-1` → `yuto-trd/speednode-arraypool`. Do not invent a new
  top-level prefix.
- **On `main`/`master` or a detached/unrelated branch**: create a fresh `<prefix>/<slug>` using the
  same personal prefix convention.

```bash
# Derive the prefix from the current branch and swap the feature part.
CURRENT=$(git branch --show-current)
case "$CURRENT" in
  */*)            PREFIX=${CURRENT%%/*} ;;   # personal feature branch -> reuse its prefix segment
  *)              PREFIX="<your-prefix>" ;;  # main/master OR a flat/worktree name (no "/") -> use
                                             #   your personal prefix; never reuse a flat branch
                                             #   name (e.g. "error-missing-task-input") as a prefix
esac
FEATURE=<descriptive-slug>                  # e.g. speednode-arraypool
BRANCH="$PREFIX/$FEATURE"

git switch -c "$BRANCH" origin/main         # branch off the latest main, before editing
```

Note the `*/*` case is *only* a true `<prefix>/<feature>` branch. A branch name with no `/`
(common in worktrees, e.g. `error-missing-task-input`) is treated like `main` — do **not** splice
the whole flat name in as a prefix, or you get a branch like `error-missing-task-input/<slug>`.

## Step 4 — Implement

Apply the minimal change that fixes the root cause. Match surrounding style; let `.editorconfig`
own formatting. Respect subtree CLAUDE.md rules (e.g. `Beutl.Engine` forbids UI references and
asks for pooled arrays on the render hot path).

## Step 5 — Test with a baseline-first (characterization) loop

This is the highest-value habit for behavior-preserving fixes (refactors, perf, pooling):

1. **Write the test first**, in the matching test project. Reuse existing helpers — e.g. animated
   `IProperty<T>` setup mirrors `tests/Beutl.UnitTests/Engine/AnimationSamplerTests.cs`
   (`Property.CreateAnimatable` + `KeyFrameAnimation<T>` + `LinearEasing`). Note: `src/Beutl/`
   ViewModels/Views are not referenced by any test project — testable logic must live in a
   referenced library (`Beutl.Engine`, `Beutl.Editor`, …).
2. **Run it against the UNMODIFIED code** and confirm green — this proves the test captures
   current behavior (a characterization baseline):
   ```bash
   dotnet test <path/to/Tests.csproj> -f net10.0 \
     --filter "FullyQualifiedName~<YourNewTestFixture>"
   ```
3. **Apply the production change**, then re-run the new test **plus** the surrounding regression
   filters. They must stay green and unchanged for a behavior-preserving fix:
   ```bash
   dotnet test <path/to/Tests.csproj> -f net10.0 \
     --filter "FullyQualifiedName~<YourFixture>|FullyQualifiedName~<RelatedArea>"
   ```

**Trap — stateful nodes.** Some components carry internal state across calls (e.g. `SpeedNode`'s
`WdlResampler` retains filter state), so "run the same instance twice and expect identical output"
will fail legitimately. For a determinism assertion, compare **two freshly constructed instances**
fed identical input instead.

Then verify formatting only on the touched files (fast):

```bash
dotnet format Beutl.slnx --include <changed-file-1> <changed-file-2> --verify-no-changes
```

Tests are long-running — prefer launching them with `run_in_background: true`; the harness
re-invokes you on completion, so the task-completion notification is the primary signal.

**Decide pass/fail from the exit code, never from a console string.** A localized marker grep is
unreliable two ways: under a non-Japanese locale `dotnet test` prints `Passed!` / `Failed!` instead
of `成功!` / `失敗!` (the loop can spin forever), and matching `Passed!`-or-`Failed!` tells you the run
*finished*, not that it *succeeded* — proceeding to commit/PR on a failed run. Wait on the process
and check `$?`:

```bash
# Launch in the background, then wait and propagate the real exit status.
dotnet test ... > "$f" 2>&1 &
PID=$!
wait "$PID"; status=$?
if [ "$status" -ne 0 ]; then
    echo "tests FAILED (exit $status) — stop here, do not commit/PR"; tail -40 "$f"
fi
# status == 0 → green; otherwise fix before continuing.
```

(If you only want an interim progress peek while it runs, `grep` the output file — but still gate
the commit/PR decision on `status`, not on any matched string.)

**A production change must ship a test.** If your diff touches `src/` (production code) but adds or
modifies no test under `tests/`, that is not done: go back and add coverage. A new
`[Test]`/`[TestCase]` in an existing fixture counts — you do not have to create a new file; the gate
is on whether the diff adds or changes a test, not on a new file. Only when the change genuinely
cannot carry a unit test (typically UI) may you substitute a concrete **manual-verification**
procedure — and you must write that procedure down (it goes in the plan and the PR's Test plan). A
production change with neither a test nor a manual-verification note is **blocked**, not a PR — the
Step 7 gate enforces this.

## Step 6 — Do not defer work (gate before you open the PR)

Run this check **before** the commit/push/PR in Step 7 — it is a gate on opening the PR, not a
post-PR afterthought. **Deferring is forbidden** (AGENTS.md "Do not defer work"). Whatever this
task surfaced — an edge case, a known TODO, a refactor you scoped out, a test you could not add
yet — **finish it on this branch now.** Do not park in-scope work behind a `## Follow-ups` list, a
`// TODO`, or a fresh Draft item on the board; those are not a substitute for doing the work.

The only two exits, and **both mean telling the user — never silently filing it away:**

- **Genuinely out of scope** (a different feature/area that does not belong in this fix): raise it
  with the user via `AskUserQuestion` and let them decide whether to widen the scope or track it
  separately. Do not auto-create a board item to dodge the decision.
- **Blocked** on something only the user can provide (access, an upstream fix, a product call):
  state the blocker explicitly in your reply.

If nothing was surfaced beyond the fix itself, there is nothing to do here — proceed to Step 7.

## Step 7 — Commit, push, PR

You are already on `$BRANCH` (created in Step 3 before any edits), so just commit and push it.
`git push -u origin "$BRANCH"` pushes that existing branch — the refspec form does not create or
move a branch, which is exactly why the branch had to exist before you started editing.

### Pre-commit gate — self-review + tests (do this before `git commit`)

Two binary checks gate the commit; both must pass (prefer fixing in-branch; otherwise it is `blocked`):

- **Test gate.** Must NOT be (production code under `src/` changed **and** no test under `tests/`
  added **or modified** **and** not a documented manual-verification). If it trips, add the test (Step 5) or, for
  genuinely untestable UI, write the manual-verification note — else stop and report `blocked`
  ("rule #3: production change without test").
- **Self-review (six checks).** (1) new/changed XAML declares `x:CompileBindings="True"` + `x:DataType`;
  (2) no `[Obsolete]` shim / "v2" duplicate / compat overload added to dodge call-site updates;
  (3) no leftover `// TODO` / `## Follow-ups` / Draft-board deferral; (4) the change fixes the **root
  cause**, not a symptom; (5) the GPL/MIT boundary is intact; (6) subtree `CLAUDE.md` rules honored.
  (The `.claude/hooks/check-gpl-mit-boundary.sh` hook also enforces #5 mechanically; this check is the
  human-judgment complement.)

```bash
git add <changed files>
# -S: sign explicitly — the main ruleset requires signed commits; do not rely on commit.gpgsign being
# configured (an unsigned commit pushed here leaves the PR unmergeable). Never pass --no-gpg-sign.
git commit -S -F - <<'EOF'
perf(engine): <imperative summary>

<what was wrong + why the fix is safe + behavior-preserving note>

Refs: Project #9 board item "<exact item title>"
EOF
git push -u origin "$BRANCH"                # never push to main/master
gh pr create --base main --head "$BRANCH" \
  --title "<conventional-commit title>" \
  --body "<filled-in .github/PULL_REQUEST_TEMPLATE.md>"
```

Fill the PR template's **Affected areas**, **Breaking changes** (`None` for behavior-preserving),
**Test plan** (mention the baseline-first verification + pass counts), and **References**
(`Project board: b-editor/projects/9 ("<title>")`). Opening the PR triggers the automatic
`claude-code-review.yml` review.

## Step 8 — Update the board

The item was already moved to `In Progress` when you claimed it (see "Claim the item immediately"
in Step 2), so nothing changes here while the PR is open. **After the PR merges**, move it to `Done`:

```bash
gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 \
  --id "$ITEM_ID" \
  --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk \
  --single-select-option-id 98236657   # Done
```

(If for some reason the item was not claimed earlier — e.g. it was already `In Progress` because you
resumed work — just ensure it is `In Progress` now, option id `47fc9ee4`.)

## Guardrails

- **Confirm outward-facing actions.** Push / PR / board edits change shared state — confirm with
  the user (AskUserQuestion) before doing them unless they already authorized it this turn.
- **Do not defer work.** Finish everything the task surfaced on the branch before opening the PR
  (Step 6). Do not auto-file a board Draft or a `## Follow-ups` entry to avoid doing in-scope work;
  raise genuinely out-of-scope or blocked items with the user instead.
- One task per invocation by default. After finishing, offer the next candidate rather than
  batching many.
- Never force-push to `main`/`master` (hook-enforced). Work on a feature branch.
- Re-discover IDs if the project schema changed:
  ```bash
  gh project field-list 9 --owner b-editor --format json   # field + option ids
  gh project list --owner b-editor                          # project ids
  ```
