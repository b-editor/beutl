---
description: |
  Autonomously work multiple Project #9 board items in one bounded loop. Each tick dispatches a
  worktree-isolated sub-agent to implement ONE item and open a PR, autonomously resolves the PR's
  reviews (CodeRabbit / Copilot / Codex / Claude), classifies the PR's risk, and then auto-merges
  the low-to-moderate-risk ones (squash) while leaving higher-risk ones for a human — stopping on a
  max-items budget, a stagnation circuit-breaker, or an empty board. Use when the user says
  "ボードをループで消化して", "loop the board", "keep working AI-review items until …",
  "/beutl-loop". Sub-agent dispatch keeps this orchestrator's context lean across many ticks.
allowed-tools: Task, Read, Grep, Glob, Write, Edit, Bash(gh:*), Bash(git:*), Bash(dotnet:*), Bash(python3:*), Bash(jq:*), Bash(mktemp:*), Bash(mkdir:*), Bash(rm:*), Bash(date:*), Bash(sleep:*), Bash(timeout:*), Bash(find:*), Bash(bash .claude/scripts/*:*)
argument-hint: "[N | dry-run | until-empty] [bug|diff|design|feature]"
---

# /beutl-loop — the board-draining loop (loop engineering)

You are the **orchestrator**. You do not implement items yourself — each tick you **dispatch
sub-agents** for the heavy work and keep only their compact JSON results plus the run journal in
context. This is the "sub-agent dispatch / fresh-context" pillar of loop engineering: the verbose
file reads, edits, diffs, and test logs stay inside the sub-agents. Read
`docs/ai-workflow/loop-engineering.md` for the full contract; this file is the executable procedure.

**Human checkpoint:** you are autonomous up to **opening a PR, resolving its bot reviews, posting the
code-owner approval, and squash-merging low-to-moderate-risk PRs**. Higher-risk or uncertain PRs are
left open for a human. `main` is protected by **branch rulesets** (required checks `build`/`dotnet-format`,
**code-owner review** via `* @yuto-trd`, thread resolution, squash-only, signed commits). **The loop
runs as the code owner** (`@yuto-trd`) — all commits, PRs, reviews, and merges are performed from that
account, so you **can approve your own PRs and complete the merge** without a second human; the
code-owner review requirement is satisfied by the agent acting as that owner. GitHub — not you — is
still the hard merge gate (status checks, thread resolution, squash-only, signed); your self-gate is
defense-in-depth and avoids attempting merges GitHub will refuse. You never merge a high-risk PR, never
force-push `main`, and never bypass the rulesets — be conservative and fail safe to the human.

## Arguments

- **Default (no integer argument) = `until-empty`** — **drain the board**: keep working eligible
  items until none remain, bounded by the stagnation breaker, the optional wall-clock, and a runaway
  backstop `BEUTL_LOOP_MAX_ITEMS` (default **50**).
- `N` (integer) — set a **tighter** per-run item budget instead of draining (e.g. `/beutl-loop 3`).
- `until-empty` — the explicit spelling of the default.
- `dry-run` — plan only: select, classify, and decide **without** claiming, dispatching, editing,
  PRing, resolving, merging, or touching the board (see "Dry-run").
- optional type filter `bug|diff|design|feature` — restrict selection to that kind.

## The loop contract (TRIGGER / SCOPE / ACTION / BUDGET / STOP / REPORT)

- **TRIGGER** manual (`/beutl-loop` or the headless `.claude/scripts/beutl-loop.sh`). No cron.
- **SCOPE** unclaimed (`Backlog`/`Todo`) items on Project #9 — **every kind, across the risk
  spectrum, features included**. Never touch `.github/workflows/*`; never cross the GPL/MIT boundary.
- **ACTION** per tick: implement (sub-agent; test + self-review gated) → [design pass for
  design-sensitive items] → open PR → resolve reviews (sub-agent) → classify risk → auto-merge
  (low/mod) or hand to a human (high). One item ⇒ at most one PR.
- **BUDGET** by default **drains the board** (`until-empty`), bounded by the stagnation breaker, the
  optional wall-clock `BEUTL_LOOP_MAX_MINUTES`, and a runaway backstop `BEUTL_LOOP_MAX_ITEMS`
  (default 50). Pass an integer `N` for a tighter per-run budget. Per-PR settle cap
  `BEUTL_LOOP_SETTLE_MINUTES` (default 20).
- **STOP** any of: **board drained** (the default terminal) · `items_processed ≥ N` (an explicit
  budget, or the runaway backstop) · stagnation (**3** no-progress with no PR in the last 3 ticks, or
  3 false-positives, or a repeated item/signature) · wall-clock exceeded · a guardrail would be
  violated. A single `blocked` item never stops the drain — it is recorded and skipped (reported at
  the end); only repeated **systemic** blocks feed the no-progress breaker.
- **REPORT** a Markdown summary at the end, then a reminder to run `/beutl-ai-self-review`.

## Run setup

1. **Confirm scope** with `AskUserQuestion` (skip if an argument was passed, or if the user already
   said "just run it"). The default is a **full board drain** (`until-empty`) that auto-merges
   low-to-moderate-risk PRs — confirm that intent, and offer `dry-run` first or a tighter `N` budget
   as alternatives. The headless wrapper always passes an explicit argument, so it never prompts.
2. **Initialize the journal** at `.claude/logs/beutl-loop-state.json` (gitignored, ephemeral). If a
   file older than ~12h exists, start fresh. Schema:
   ```json
   {"run_id":"<stamp>","budget":"until-empty","runaway_cap":50,"max_minutes":null,"settle_minutes":20,"filter":"any",
    "attempted_ids":[],"items_processed":0,"last_pr_tick":0,
    "prs":[{"item_id":"","pr_url":"","pr_number":0,"risk":"low|moderate|high",
            "outcome":"merged|left_for_human","left_reason":null,"reviews_resolved":0}],
    "false_positives":[],"blocked":[{"item_id":"","kind":"item-specific|systemic","reason":""}],
    "consecutive_no_progress":0,"consecutive_false_positives":0,
    "last_chosen_item_id":null,"last_failure_signature":null,"stop_reason":null}
   ```
   The **board (#9) is the source of truth**; the journal is only within-run bookkeeping. If it is
   deleted mid-run, correctness is unaffected — the next tick re-derives eligibility from the live
   board (claimed items are `In Progress` and excluded).

Board coordinates (project #9) are the same stable IDs as `beutl-board-task` — Project
`PVT_kwDOBLw8Fs4BW4g5`, Status field `PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk`
(`In Progress 47fc9ee4`, `Backlog d97cd69b`, `Todo f75ad846`, `Done 98236657`,
`False positive e6ff360e`). Re-discover with `gh project field-list 9 --owner b-editor` if they drift.
3. **Load loop memory (D-7 — cross-run, gitignored at `.claude/loop-memory/`).** `mkdir -p
   .claude/loop-memory` first. If present, read these advisory files — they never override the board
   or the binary gates, but speed up decisions and recall cross-run patterns:
   - `false-positive-signatures.json` — known false-positive signatures; if a candidate item's body
     matches a known signature, prefer the false-positive path (still verify against current code).
   - `blocked-reasons.json` — recurring `{item_id, kind, reason}` records; helps classify
     `blocked_kind` faster on similar items.
   - `area-test-commands.json` — per-area `{src_path → tests_project, filter}` hints; speeds up the
     runner's test step (advisory; the runner still derives its own).
   - `bot-false-positive-patterns.md` — patterns where bots (CodeRabbit / Copilot / Codex / Claude)
     misread Beutl code; the resolver appends to this (D-8), and the reviewers read it to avoid
     repeating a known-bot-blind-spot.

## Per-tick procedure

### 0. Termination check (before every tick — cheapest first)
Stop if: **no eligible item left** (re-fetch board, exclude `attempted_ids` — the default terminal
for a drain) · `items_processed ≥` the budget (an explicit `N`, or the runaway backstop
`BEUTL_LOOP_MAX_ITEMS`, default 50) · wall-clock exceeded · `consecutive_no_progress ≥ 3` **and** no
PR was opened in the last 3 ticks (`items_processed − last_pr_tick >= 3` — recent shipping counts as
progress and holds the breaker open) · `consecutive_false_positives ≥ 3` (the board/selection is
mostly junk — stop and report) · the **same `item_id` or `last_failure_signature` recurs
back-to-back** (immediate stagnation stop). Record `stop_reason`.

### 1. Select up to K items (bounded parallel)
Re-fetch the board snapshot (as in `beutl-board-task` Step 1), apply the type filter, exclude
`attempted_ids`, and pick **up to K** items — `K = ${BEUTL_LOOP_PARALLEL:-1}` (default 1 = sequential;
the headless wrapper may raise this to 2-3, capped at 3). Pick across the full spectrum (features and
higher-risk included — do not pre-filter by risk).

**Footprint-overlap scheduler (C-5).** To avoid parallel branches conflicting on the same files,
estimate each candidate's footprint and skip overlapping picks:
- **Review findings** (body cites `file:line`): extract the cited `src/` file paths. Two findings
  overlap if they share a `src/` file **or** map to the same `tests/` project.
- **Feature tasks**: extract a likely area from the title (heuristic — the module name if present,
  e.g. "リップル削除" → `src/Beutl.Engine/`; otherwise treat the footprint as "unknown" and allow only
  one unknown-footprint item in a parallel batch).
- Build the batch greedily: take the first candidate, then add the next non-overlapping one, up to K.
- If only one non-overlapping candidate is available, the batch is size 1 (sequential).

Capture each item's stable `ITEM_ID`. Add all selected ids to `attempted_ids` now (before dispatch, so
a parallel re-fetch does not re-pick them).

### 2. Dispatch A — implement → draft (worktree sub-agents, bounded parallel)
Dispatch **one `beutl-board-task-runner` per selected item** — in a **single message with multiple
Task calls** so they run in parallel when the batch size > 1. Each gets its own `ITEM_ID`, title,
body, `BRANCH_PREFIX`, and a unique slug (the runner derives the branch from the slug). Collect all
results; each draft is processed independently through the pre-PR review round (step 2.5).

For a single-item batch (K=1) this is identical to the sequential flow. For K>1, the worktrees are
isolated (`isolation: worktree`), so parallel edits do not collide on disk; the footprint-overlap
scheduler (step 1) already avoided same-file collisions.

It returns the structured result (a **draft branch** by default, risk signals, `test_status`, or
`false_positive` / `blocked`). **The runner always hands back a draft branch — it never opens the PR
itself**; the pre-PR review round (step 2.5) owns PR creation so the settle window stays short.
Per-result outcomes and their effect on the **stagnation counters** (the budget counter
`items_processed` is incremented once per tick in step 5, **never here**):
- `draft_ready: true` (the normal outcome) → go to **step 2.5** to run the pre-PR review round, which
  ends by opening the PR. Do not touch the counters yet — step 2.5 applies the "PR opened" reset once
  it opens the PR.
- `speckit_required: true` (F-11 — large feature needing a new public type or ≥ 3 new files) → go to
  **step 2.6** to run the Spec-Kit flow and re-dispatch the runner with a generated `tasks.md`. Do
  not touch the counters yet — the re-dispatched runner hands back a draft, which flows through
  step 2.5 as normal.
- `false_positive: true` → the runner already marked the board item `False positive`, so the item
  **leaves the queue** — this is **progress**: reset `consecutive_no_progress` to 0, increment
  `consecutive_false_positives`. **Append the false-positive signature to
  `.claude/loop-memory/false-positive-signatures.json`** (D-7) for cross-run recall. Continue.
- `blocked: true` → record `{reason, kind}`; **never stop the whole drain on a single item** (skip it
  via `attempted_ids` and report it at the end). Reset `consecutive_false_positives` to 0, **append
  `{item_id, kind, reason}` to `.claude/loop-memory/blocked-reasons.json`** (D-7), then by
  `blocked_kind`:
  - `"systemic"` (build won't compile, tests universally red, tooling broken) → **no-progress**:
    increment `consecutive_no_progress`, set `last_failure_signature`. Repeated systemic blocks trip
    the breaker.
  - `"item-specific"` (underspecified feature, upstream/product call, UI needing a human) → the
    toolchain is fine and the item simply isn't doable now: **neutral** — do **not** increment
    `consecutive_no_progress`. Continue.
- `test_status: "red"` / no draft (and not `blocked`) → **no-progress**: increment
  `consecutive_no_progress`, **reset `consecutive_false_positives` to 0**, set `last_failure_signature`
  from the runner's `failure_signature`, continue.

**Defense-in-depth on the test gate (B):** if the runner handed back a draft but its own signals show
`touched_production == true && test_files_added_count == 0 && test_status != "manual-verification"`
(the runner should have blocked instead), do **not** open a PR from this draft — treat it as
**high-risk → leave for human** and count the tick as **no-progress** (a runner-contract violation, not
a shippable item). Likewise, if `baseline_test_green != true` (the runner skipped or failed the
characterization baseline), treat the tick as **no-progress** — the baseline-first discipline is
load-bearing for behavior-preserving fixes and must not be skipped.

**Parallel-batch aggregation (C-5).** When the batch size > 1, aggregate stagnation across the batch:
- If **any** result opened a PR (via step 2.5) or was a false-positive → reset
  `consecutive_no_progress` (progress).
- Only if **all** results were no-progress (red / systemic-blocked / baseline-violation) does the tick
  count as a single no-progress tick.
- `consecutive_false_positives` increments by the number of false-positives in the batch; if the
  running total ≥ 3, the step-0 breaker trips.
- Each item in the batch counts as one `items_processed` in step 5 (a batch of 3 → 3 processed, so a
  parallel drain is faster but does not inflate the per-tick budget semantics).

### 2.5 Pre-PR review round (always — machine-verify + sub-agents + rework + PR open)
The runner handed back a pushed **draft branch** (it never opens the PR itself). Set
`DRAFT_BRANCH` from the runner's `draft_branch` JSON field. This step runs the review gate
**before** the PR exists, so the self-review axes and bot-likely findings are cleared upfront and
the post-PR settle window stays short. Up to **two** rework iterations; then the PR opens.

**2.5a. Orchestrator machine-verify (independent of the runner's self-report — do not trust
`self_review_passed`).** On `git diff origin/main...$DRAFT_BRANCH`, grep for the self-review gate's
mechanical axes:
- `[Obsolete]` on a `+`-line introduced in this diff (same-change deprecate-and-replace ⇒ blocking).
- `V2` / `Ex2` / `2` type-name suffixes on `+`-lines (compat-shim smell ⇒ blocking).
- `// TODO` / `## Follow-ups` / `# Follow-ups` on `+`-lines (deferred work ⇒ blocking).
- Every changed `.axaml` UserControl declares `x:CompileBindings="True"` + `x:DataType` (missing ⇒
  blocking; suggest the fix inline).
- GPL/MIT boundary: run `bash .claude/scripts/check-gpl-mit-boundary-diff.sh origin/main "$DRAFT_BRANCH"`
  — this is a **diff-side scan**, not the PreToolUse hook (the hook reads `tool_input.file_path`
  from Edit/Write JSON and is a no-op when invoked on a diff). The script reads the head-side
  file content via `git show "$DRAFT_BRANCH:$f"` (the draft branch may not be checked out in the
  orchestrator's working tree). Exit 1 ⇒ blocking; the script prints `file:line` for each
  violation.

Collect any hits as `machine_findings`.

**2.5b. Sub-agent review.** Dispatch `@beutl-reviewer` and `@beutl-xaml-binder` (read-only) on
`git diff origin/main...$DRAFT_BRANCH`. If `design_reviewer_required` is true, also dispatch
`@beutl-design-reviewer`. Collect their blocking findings as `review_findings`.

**2.5c. Rework loop (≤ 2 passes).** If `machine_findings` or `review_findings` are non-empty:
- Re-dispatch `beutl-board-task-runner` in **Rework mode** (`REWORK=true`, `draft_branch`,
  `review_findings=<combined>`, `OPEN_PR=false`); it amends the branch, re-runs the binary gates,
  pushes, and returns `draft_ready` again.
- Re-run 2.5a + 2.5b on the amended branch. Increment the rework count and loop.
- **2-rework budget spent with blocking findings remaining** → stop reworking; the findings are
  **unresolved**.

**2.5d. Open the PR.** Re-dispatch the runner in Rework mode with `OPEN_PR=true` and empty
`review_findings` to open the PR from the (possibly amended) draft branch. This is the step-2 **"PR
opened"** outcome: reset both stagnation counters to 0 and mark the tick as PR-opened (step 5 records
`last_pr_tick`). If findings were left **unresolved** (2.5c), force the PR's risk to **high → leave for
human** (`left_reason: "unresolved review/design findings after 2 reworks"`), **post a structured
summary of the unresolved findings as a PR comment** (below) so the human reviewer can pick up fast,
and skip the auto-merge path; otherwise continue to step 3.

**Post unresolved findings (F-12).** When the rework budget is spent, post a comment on the PR
summarizing what could not be cleared:
```
gh pr comment "$PR" --body "## Unresolved review/design findings (left for human)

This PR was opened by /beutl-loop after the 2-rework budget was spent. The following blocking
findings could not be cleared autonomously and need a human review pass:

- [machine] <finding> — \`path:line\`
- [reviewer] <finding> — \`path:line\`
- [design] <finding> — \`path:line\`

Risk: high. The board item stays In Progress."
```

### 2.6 Spec-Kit flow (only when Dispatch A returned `speckit_required`)
The feature is too large for a single minimal-change tick (needs a new public type or ≥ 3 new files).
Generate a spec, plan, and task list, then re-dispatch the runner to implement against the tasks:
1. Run `/speckit-specify` with the item title + body as the feature description (non-interactive — use
   the item body as the spec input; if the body is too thin to derive acceptance criteria, treat as
   `blocked` / `blocked_kind: "item-specific"` instead — do not fabricate scope).
2. Run `/speckit-plan` on the generated spec.
3. Run `/speckit-tasks` on the plan to produce `tasks.md`.
4. Re-dispatch `beutl-board-task-runner` with `SPECKIT=true`, `ITEM_ID`, and the `tasks.md` path. It
   executes the tasks on a feature branch and hands back a draft → continue at step 2.5.

If the Spec-Kit flow cannot produce a coherent spec/plan/tasks from the item body (too underspecified
even for a spec), treat the item as `blocked` (`blocked_kind: "item-specific"`,
`blocked_reason: "feature too underspecified for spec-kit"`) — record it and skip.

### 3. Resolve reviews (sub-agent, bounded settle)
Wait for async bot reviews, bounded by `settle_minutes`. **Block on CI without busy-waiting, and bound
the wait so a hung check cannot hang the loop:** prefer
`timeout $((settle_minutes * 60)) gh pr checks "$PR" --watch --interval 60` (`timeout` is in the
allowlist; exit `124` = the settle window elapsed → left for human). If `timeout` is unavailable (some
macOS installs lack coreutils), use a bounded poll loop instead:
```bash
deadline=$((SECONDS + settle_minutes * 60))
while [ "$SECONDS" -lt "$deadline" ]; do
  pending=$(gh pr checks "$PR" --json state --jq '[.[]|select(.state!="COMPLETE")] | length' 2>/dev/null || echo 1)
  [ "$pending" = "0" ] && break
  sleep 60
done
```
Then poll the review state, spacing polls ~90s apart with `sleep 90` (allowed via `Bash(sleep:*)`;
`python3 -c 'import time;time.sleep(90)'` is an equivalent allowed fallback if a bare `sleep` is
unavailable). Each poll: dispatch a sub-agent running **`beutl-resolve-reviews --auto`** for the PR to
address clearly-actionable bot comments, re-verify, and resolve threads. "**Settled**" = CI complete+green · zero unresolved threads · no
outstanding `CHANGES_REQUESTED` · no new review/comment/commit for ~10 min. **Re-fetch CI
(`gh pr checks`) and the thread/`reviewDecision` state yourself each poll — the resolver's
`ci_status`/`changes_requested_outstanding`/counts are advisory; the orchestrator's own `gh` reads are
authoritative** (and the step-5 merge gate re-checks them regardless). Use the resolver's
`last_activity_at` for the quiet-period clock. If `needs_human` is set, the settle window elapses, or
CI is red → the PR is **left for human** (do not merge).

### 4. Classify risk (moderate policy) and decide the merge
**Auto-merge eligible (low/moderate) only if ALL hold:** `commit_type ∈
{fix,refactor,perf,test,docs,style,chore, small feat}` and **not** `is_breaking`; no public-API
design judgment (no blocking `@beutl-design-reviewer` finding); not `touched_gpl_mit /
touched_source_gen / touched_persistence`; **moderate diff** (≈ ≤250 LOC and ≤8 files); runner
`test_status == "green"` (a `manual-verification` item is **not** eligible → human); required checks
(`build`, `dotnet-format`) green and nothing else failing; **every review thread resolved**; no
outstanding `CHANGES_REQUESTED` from any bot **or human**; `needs_human` false; settled; mergeable;
and — when `touched_production && diff_loc >= 100` — **changed-line coverage ≥ 70%** (the B-4 probe
below; a lower coverage ⇒ high-risk, because the new code is under-tested for an unattended merge).
**The loop runs as the code owner, so it posts its own approval** — a pending `REVIEW_REQUIRED` is
**not** a stop: run `gh pr review "$PR" --approve` first, then proceed. A `CHANGES_REQUESTED` that
could not be cleanly auto-resolved ⇒ leave for human. **Otherwise high-risk → leave for human.** When
unsure, choose human (fail safe).

**B-4 coverage probe (conditional — only when `touched_production && diff_loc >= 100`).** Run the
matching test project with coverage, then compute changed-line coverage with the probe script:
```bash
# Identify the matching test project from the touched src/ path (heuristic):
#   src/Beutl.Engine/...  -> tests/Beutl.UnitTests/  (or tests/Beutl.Graphics3DTests/ for graphics)
#   src/Beutl.ProjectSystem/... -> tests/Beutl.ProjectSystem.Tests/ (if present)
TESTS_PROJ=<derive-from-diff>
# Use a dedicated results directory so a stale coverage.xml from a previous
# run cannot be picked up. pipefail ensures dotnet test's failure is not
# masked by tail.
COV_DIR=$(mktemp -d .claude/logs/cov-probe-XXXXXX)
set -o pipefail
dotnet test "$TESTS_PROJ" -f net10.0 --no-build --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings --results-directory "$COV_DIR" 2>&1 | tail -5
TEST_RC=$?
set +o pipefail
if [ "$TEST_RC" -ne 0 ]; then
  echo "coverage test run failed (exit $TEST_RC) — treat as high-risk (fail safe)"
  rm -rf "$COV_DIR"
  # Skip the probe; the PR is high-risk.
else
  COV=$(find "$COV_DIR" -name 'coverage.cobertura.xml' | head -1)
  if [ -z "$COV" ]; then
    echo "no coverage.xml produced — treat as high-risk (fail safe)"
    rm -rf "$COV_DIR"
  else
    # Changed-line coverage probe: exit 0 = >=70% (auto-merge eligible),
    # exit 1 = <70% (high-risk), exit 2 = probe failed (high-risk, fail safe).
    # The diff is origin/main..$DRAFT_BRANCH (the actual PR head — not the
    # orchestrator's checkout, which may be a different branch).
    python3 .claude/scripts/changed-line-coverage.py origin/main "$DRAFT_BRANCH" "$COV" --threshold 70
    rm -rf "$COV_DIR"
  fi
fi
```
If the test run failed, no coverage XML was produced, or the probe exits **non-zero** (exit 1 =
under threshold, exit 2 = probe failure), mark the PR **high-risk**: post the coverage number (or
the failure reason) on the PR for the human and do not auto-merge. The probe is a **gate on
auto-merge eligibility only** — the PR still opens and goes through bot review resolution.

`main` is ruleset-protected (required checks `build`/`dotnet-format`, every thread resolved,
**code-owner review**, squash-only, signed commits). The loop **posts the code-owner approval** (it
runs as `@yuto-trd`), then **attempts** the merge pinned to the reviewed head; a ruleset refusal is
recorded as `left_for_human`, never forced or bypassed.

```bash
HEAD_SHA=$(gh pr view "$PR" --json headRefOid -q .headRefOid)
gh pr checks "$PR"                                          # required checks green, nothing failing
# Unresolved review threads must be 0 (assumes ≤100; paginate for more — GitHub also enforces this,
# so the loop only checks to avoid a futile merge attempt):
UNRESOLVED=$(gh api graphql -f query='query{repository(owner:"b-editor",name:"beutl"){pullRequest(number:'"$PR"'){reviewThreads(first:100){nodes{isResolved}}}}}' \
  --jq '[.data.repository.pullRequest.reviewThreads.nodes[]|select(.isResolved==false)]|length')
gh pr view "$PR" --json mergeable,reviewDecision -q '.mergeable,.reviewDecision'   # need MERGEABLE; if reviewDecision != APPROVED, post the code-owner approval below
# Post the code-owner approval (the loop runs as @yuto-trd) so the ruleset's review requirement is met:
gh pr review "$PR" --approve --body "Auto-approved by /beutl-loop (code-owner). Risk: <low|moderate>."
# Move to Done ONLY if the merge actually succeeds; a ruleset refusal => leave In Progress for the human.
if gh pr merge "$PR" --squash --delete-branch --match-head-commit "$HEAD_SHA"; then
  gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 --id "$ITEM_ID" \
    --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk --single-select-option-id 98236657   # Done
else
  echo "merge blocked by ruleset — record left_for_human; board stays In Progress; no retry/force/bypass"
fi
```
High-risk PRs — and **any merge GitHub refuses** — are **left open**; the board item stays
`In Progress` for a human review pass.

### 5. Update the journal
Append the per-item record (risk, `merged` / `left_for_human` + reason, reviews_resolved; or the
`{kind, reason}` for a blocked item). Increment `items_processed` **exactly once for this tick** —
every item that reached a terminal state (false-positive, blocked, or PR opened — including a
draft that opened in step 2.5) counts as one; a PR that then merges or is left for the human is still
that same one item, never a second. **If this tick opened a PR (including via the step-2.5 pre-PR
review round), set `last_pr_tick = items_processed`** (used by the step-0 "PR within the last 3 ticks"
guard). The stagnation counters (`consecutive_no_progress`,
`consecutive_false_positives`, `last_failure_signature`) were already set in step 2 / 2.5. Persist,
then loop to step 0.

## Dry-run

`/beutl-loop dry-run [N]` runs steps 0–1 and the **classification logic** for each item it *would*
pick, and prints: the chosen item, why it is eligible, the planned `BRANCH_PREFIX`, whether it looks
**design-sensitive** (would take the step-2.5 design pass), whether a production change would
**require a test** (the B gate) or only a manual-verification note, the predicted risk tier and merge
decision, and the current stop math — **without** claiming, dispatching A/B, editing, PRing, replying,
resolving, merging, or editing the board. It is the safe first thing to run.

## Report

End with a Markdown summary: per item — `pr_url`, risk, **merged** or **left for human** (+ why),
reviews resolved; plus false-positives, blocked items, counters, and the `stop_reason`. Then remind
the user to run `/beutl-ai-self-review` (the Stop hook also nudges this after 3+ files change).

**Also emit a JSON run summary** (H-15) at `.claude/logs/beutl-loop-run-<run_id>.json` for cross-run
trend comparison (auto-merge rate, false-positive rate, PRs/run, stop_reason). Schema:
```json
{
  "run_id": "<stamp>", "budget": "until-empty", "items_processed": 0, "stop_reason": "",
  "prs": [{"item_id":"","pr_url":"","pr_number":0,"risk":"low|moderate|high",
           "outcome":"merged|left_for_human","left_reason":null,"reviews_resolved":0}],
  "false_positives": [], "blocked": [],
  "counters": {"consecutive_no_progress": 0, "consecutive_false_positives": 0},
  "auto_merged": 0, "left_for_human": 0, "reviews_resolved_total": 0,
  "parallel_batch_count": 0, "speckit_routed": 0, "coverage_probes_run": 0
}
```
Keep the journal and the run summary separate: the journal is within-run bookkeeping (overwritten
each tick); the run summary is written once at the end and kept for trend analysis across runs.

## Guardrails (anti-loopmaxxing + auto-merge safety)

- **Binary verification gates** decide progress: `dotnet build` clean + `dotnet test` `$?==0`
  (exit code, never a console string) + `dotnet format --verify-no-changes` + `@beutl-reviewer`
  (+ `@beutl-design-reviewer` on public surface). No green gate ⇒ no PR ⇒ no-progress.
- **Stagnation breaker:** stop after **3** consecutive no-progress ticks **with no PR opened in the
  last 3 ticks** (recent shipping holds it open), or immediately on a repeated `item_id` /
  `last_failure_signature`. A `blocked` item counts toward this only when its `blocked_kind` is
  `systemic`; an `item-specific` block is skipped without penalty.
- **Hard budget:** default drains the board (`until-empty`) with a runaway backstop
  `BEUTL_LOOP_MAX_ITEMS` (default 50); pass `N` for a tighter budget; optional wall-clock.
- **Deterministic STOP** only (the reasons above) — never an open-ended "until done".
- **Auto-merge is conservative and fail-safe:** low/moderate-risk only, all gates green + settled;
  high-risk and uncertain go to the human; squash + delete-branch; never auto-merge high-risk; never
  force-push `main`.
- **Context isolation:** all heavy work runs in sub-agents; keep only structured results + journal.
- **Do not defer work** (inherited from `beutl-board-task`): each item is finished or explicitly
  `blocked` — never "fix it next tick".
