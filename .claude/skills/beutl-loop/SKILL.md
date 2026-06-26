---
description: |
  Autonomously work multiple Project #9 board items in one bounded loop. Each tick dispatches a
  worktree-isolated sub-agent to implement ONE item and open a PR, autonomously resolves the PR's
  reviews (CodeRabbit / Copilot / Codex / Claude), classifies the PR's risk, and then auto-merges
  the low-to-moderate-risk ones (squash) while leaving higher-risk ones for a human — running
  unbounded by default and stopping on a stagnation circuit-breaker or an empty board. Use when the user says
  "ボードをループで消化して", "loop the board", "keep working AI-review items until …",
  "/beutl-loop". Sub-agent dispatch keeps this orchestrator's context lean across many ticks.
allowed-tools: Task, Read, Grep, Glob, Write, Edit, Bash(gh:*), Bash(git:*), Bash(dotnet:*), Bash(python3:*), Bash(jq:*), Bash(mktemp:*), Bash(mkdir:*), Bash(rm:*), Bash(date:*), Bash(sleep:*), Bash(timeout:*), Bash(find:*), Bash(head:*), Bash(tail:*), Bash(bash .claude/scripts/*:*)
argument-hint: "[N | dry-run | until-empty] [bug|diff|design|feature]"
---

# /beutl-loop — the board-draining loop (loop engineering)

You are the **orchestrator**. You do not implement items yourself — each tick you **dispatch
sub-agents** for the heavy work and keep only their compact JSON results plus the run journal in
context. This is the "sub-agent dispatch / fresh-context" pillar of loop engineering: the verbose
file reads, edits, diffs, and test logs stay inside the sub-agents. Read
`docs/ai-workflow/loop-engineering.md` for the full contract; this file is the executable procedure.

**Execution model.** The loop runs **in-session only** — invoke `/beutl-loop` inside an interactive
Claude Code session running on **opus** with auto-accept (`acceptEdits`) enabled. There is **no
headless `claude -p` launcher** (that path moved billing to metered API usage and was removed). The
long-running work is carried by **nested sub-agents**: each tick you dispatch a worktree-isolated
`beutl-board-task-runner` (which itself runs on opus / `acceptEdits`) and the review/merge helpers,
keeping this orchestrator's context lean so the loop can keep going indefinitely.

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
  items until none remain. **Unbounded by item count by default** — the stagnation breaker (and the
  optional wall-clock) are the stops; the work is meant to run as long as the board has eligible
  items. `BEUTL_LOOP_MAX_ITEMS` is an **optional** cap (unset = no cap).
- `N` (integer) — set a **tighter** per-run item budget instead of draining (e.g. `/beutl-loop 3`).
- `until-empty` — the explicit spelling of the default.
- `dry-run` — plan only: select, classify, and decide **without** claiming, dispatching, editing,
  PRing, resolving, merging, or touching the board (see "Dry-run").
- optional type filter `bug|diff|design|feature` — restrict selection to that kind.

## The loop contract (TRIGGER / SCOPE / ACTION / BUDGET / STOP / REPORT)

- **TRIGGER** manual, in-session (`/beutl-loop` in an interactive opus / `acceptEdits` session). No
  cron, no headless `claude -p` launcher.
- **SCOPE** unclaimed (`Backlog`/`Todo`) items on Project #9 — **every kind, across the risk
  spectrum, features included**. Never touch `.github/workflows/*`; never cross the GPL/MIT boundary.
- **ACTION** per tick: implement (sub-agent; test + self-review gated) → [design pass for
  design-sensitive items] → open PR → resolve reviews (sub-agent) → classify risk → auto-merge
  (low/mod) or hand to a human (high). One item ⇒ at most one PR.
- **BUDGET** by default **drains the board** (`until-empty`), **unbounded by item count** — bounded
  only by the stagnation breaker and the optional wall-clock `BEUTL_LOOP_MAX_MINUTES`. Pass an
  integer `N`, or set the optional `BEUTL_LOOP_MAX_ITEMS`, for a tighter cap. Per-PR settle cap
  `BEUTL_LOOP_SETTLE_MINUTES` (default 20).
- **STOP** any of: **board drained** (the default terminal) · `items_processed ≥ N` (only when an
  explicit budget or `BEUTL_LOOP_MAX_ITEMS` is set — otherwise there is no item cap) · stagnation
  (**3** no-progress with no PR in the last 3 ticks, or 3 false-positives, or a repeated
  item/signature) · wall-clock exceeded · a guardrail would be violated. A single `blocked` item
  never stops the drain — it is recorded and skipped (reported at
  the end); only repeated **systemic** blocks feed the no-progress breaker.
- **REPORT** a Markdown summary at the end, then a reminder to run `/beutl-ai-self-review`.

## Run setup

1. **Confirm scope** with `AskUserQuestion` (skip if an argument was passed, or if the user already
   said "just run it"). The default is a **full board drain** (`until-empty`) that auto-merges
   low-to-moderate-risk PRs — confirm that intent, and offer `dry-run` first or a tighter `N` budget
   as alternatives.
2. **Refresh `origin/main`** (`git fetch origin main`) before selecting: every item branches from and
   diffs against it, so a long-lived session must not work off a stale ref. Confirm the current
   branch yields a usable `<prefix>/<slug>` (it is not `main`/`master`/detached/flat) — or take the
   prefix from `BEUTL_LOOP_BRANCH_PREFIX` — and pass that prefix to every dispatched runner.
3. **Initialize the journal** at `.claude/logs/beutl-loop-state.json` (gitignored, ephemeral). Run
   `mkdir -p .claude/logs` first — on a fresh checkout the gitignored directory is absent, and the
   journal write, the coverage probe's `mktemp -d .claude/logs/...`, and the run summary all need it.
   If a file older than ~12h exists, start fresh — **this resets the stagnation counters**
   (`consecutive_no_progress`, `consecutive_false_positives`) to zero, which is intentional (a long
   gap means a new run context) but means a run that was thrashing can re-arm its no-progress budget
   after the 12h boundary. Schema:
   ```json
   {"run_id":"<stamp>","budget":"until-empty","item_cap":null,"max_minutes":null,"settle_minutes":20,"filter":"any",
    "attempted_ids":[],"items_processed":0,"last_pr_tick":0,
    "prs":[{"item_id":"","pr_url":"","pr_number":0,"risk":"low|moderate|high",
            "outcome":"merged|left_for_human","left_reason":null,"reviews_resolved":0}],
    "false_positives":[],"blocked":[{"item_id":"","kind":"item-specific|systemic","reason":""}],
    "consecutive_no_progress":0,"consecutive_false_positives":0,
    "last_chosen_item_id":null,"last_failure_signature":null,"stop_reason":null}
   ```
   When reusing a journal younger than ~12h, **reconcile `attempted_ids` against the live board
   before using it to exclude candidates**: an id is recorded at selection (Step 1), which may be
   *before* the runner claims the item, so a crash between the two leaves the item still `Backlog`/
   `Todo` while the journal still lists it. Drop from `attempted_ids` any id whose live board status
   is `Backlog`/`Todo` (it was never actually claimed) so a rerun does not skip live work or report
   the board drained prematurely.

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
for a drain) · `items_processed ≥` the budget **when one is set** (an explicit `N` or the optional
`BEUTL_LOOP_MAX_ITEMS`; by default there is **no item cap**) · wall-clock exceeded · `consecutive_no_progress ≥ 3` **and** no
PR was opened in the last 3 ticks (`items_processed − last_pr_tick >= 3` — recent shipping counts as
progress and holds the breaker open) · `consecutive_false_positives ≥ 3` (the board/selection is
mostly junk — stop and report) · the **same `item_id` or `last_failure_signature` recurs
back-to-back** (immediate stagnation stop). Record `stop_reason`.

### 1. Select up to K items (bounded parallel, budget-capped)
Re-fetch the board snapshot (as in `beutl-board-task` Step 1), apply the type filter, exclude
`attempted_ids`, and pick **up to K** items — **`K = min(${BEUTL_LOOP_PARALLEL:-1}, 3)`** (default 1
= sequential). **Clamp to 3 before batching** — the max-3 envelope is a safety bound, so an out-of-range
`BEUTL_LOOP_PARALLEL=50` must still dispatch at most 3 workers, never dozens. Pick across the full
spectrum (features and higher-risk included — do not pre-filter by risk).

**Budget cap (a batch must never overshoot a set item budget).** When a budget is set (`budget` =
the explicit `N` argument or the optional `BEUTL_LOOP_MAX_ITEMS`), compute
`remaining_budget = budget − items_processed` and the actual batch size is `B = min(K, remaining_budget)`.
**When no budget is set (the default), `B = K`** (no item cap). This keeps a parallel batch from processing
more items than a set budget allows (e.g. `/beutl-loop 1` with `BEUTL_LOOP_PARALLEL=3` picks `min(3,1)=1`
item, not 3). If `B ≤ 0`, step 0 already stopped; if `B = 1`, the batch is sequential.

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
- `false_positive: true` → a **candidate** false positive (the runner did **not** touch the board).
  **Spot-check it first:** independently verify the runner's cited refutation against the current code.
  - **Confirmed** → move the board item to `False positive` (`e6ff360e`) now — the item **leaves the
    queue**; this is **progress**: reset `consecutive_no_progress` to 0, **clear `last_failure_signature`**
    (any progress must clear it, so an unrelated later failure with the same first-error line does not
    look back-to-back), increment `consecutive_false_positives`, and **append the signature to
    `.claude/loop-memory/false-positive-signatures.json`** (D-7). Continue.
  - **Inconclusive / refuted** → do **not** write `False positive` (a misclassification would hide a
    real item forever). **Revert the item to `Todo`** (un-claim — it is still `In Progress` from the
    claim, and future runs select only `Backlog`/`Todo`, so leaving it `In Progress` would strand it),
    record it under `blocked` (`item-specific`), exclude via `attempted_ids` this run, and leave it for
    a human. Do not increment `consecutive_false_positives`.
- `already_implemented: true` → an already-shipped feature the runner moved to `Done` (not a false
  positive). This is **completed work → progress**: reset `consecutive_no_progress` to 0 **and reset
  `consecutive_false_positives` to 0** (real progress breaks the consecutive chain — otherwise
  `FP → already-implemented → FP → already-implemented → FP` would trip the three-FP breaker),
  **clear `last_failure_signature`**, but **never increment** `consecutive_false_positives` and do not
  record a false-positive signature. Continue.
- `blocked: true` → record `{reason, kind}`; **never stop the whole drain on a single item** (skip it
  via `attempted_ids` and report it at the end). Reset `consecutive_false_positives` to 0, **append
  `{item_id, kind, reason}` to `.claude/loop-memory/blocked-reasons.json`** (D-7), then by
  `blocked_kind`:
  - `"systemic"` (build won't compile, tests universally red, tooling broken) → **no-progress**:
    increment `consecutive_no_progress`, set `last_failure_signature`, **and revert the item from
    `In Progress` back to `Todo`** (un-claim) — a transient systemic failure (broken build, cache
    outage, tooling downtime) must not strand the item `In Progress` forever; future runs select only
    `Backlog`/`Todo`, and it stays in this run's `attempted_ids` so it is not re-picked this run.
    Repeated systemic blocks trip the breaker.
  - `"item-specific"` (underspecified feature, upstream/product call, UI needing a human) → the
    toolchain is fine and the item simply isn't doable now: **neutral** — do **not** increment
    `consecutive_no_progress`. **Revert the item from `In Progress` back to `Todo`** (un-claim) so it
    is not stranded after the run — future runs select only `Backlog`/`Todo`; keep it in this run's
    `attempted_ids` so it is not re-picked this run. Continue.
- `test_status: "red"` / no draft (and not `blocked`) → **no-progress**: increment
  `consecutive_no_progress`, **reset `consecutive_false_positives` to 0**, set `last_failure_signature`
  from the runner's `failure_signature`. **Un-claim the item** — revert it from `In Progress` back to
  `Todo` (the runner already claimed it; future runs select only `Backlog`/`Todo`, so leaving it
  `In Progress` would strand it), keep it in `attempted_ids` for this run, then continue.
- **No result / invalid result (runner crashed, timed out, or returned malformed/non-JSON):** the
  runner claimed the item `In Progress` in its Step 1 but produced no usable outcome, so none of the
  board actions above ran. Treat this as a **recoverable failure → no-progress**: revert the item from
  `In Progress` back to `Todo` (un-claim — do not strand it; future runs select only `Backlog`/`Todo`,
  and it stays in this run's `attempted_ids` so it is not re-picked this run), record it under `blocked`
  (default `blocked_kind: "systemic"` — a crash/timeout usually signals an environment/tooling problem,
  not the item itself; use `"item-specific"` if the failure recurs only on this item's content),
  `reason: "runner produced no usable result"`, append `{item_id, kind, reason}` to
  `.claude/loop-memory/blocked-reasons.json` (D-7), increment `consecutive_no_progress`, and set
  `last_failure_signature`. Then continue.

**Defense-in-depth on the test gate (B):** if the runner handed back a draft but its own signals show
`touched_production == true && test_files_added_count == 0 && test_status != "manual-verification"`
(the runner should have blocked instead), do **not** open a PR from this draft — treat it as
**high-risk → leave for human** and count the tick as **no-progress** (a runner-contract violation, not
a shippable item). **Do not strand the claimed item:** revert its board status from `In Progress` back
to `Todo` (un-claim) so it is not stuck `In Progress` forever — future runs select only `Backlog`/`Todo`,
so an abandoned `In Progress` item would never be revisited. Keep it in this run's `attempted_ids` (so
it is not re-picked this run) and record it under `blocked` with the reason; the stagnation breaker
handles any thrash. Likewise, if `baseline_test_green != true` (the runner skipped the baseline run
entirely — not the same as a red baseline for a bug fix, which is expected and sets
`baseline_test_green: true`), treat the tick as **no-progress** — the baseline-first discipline is
load-bearing and must not be skipped.

**Parallel-batch aggregation (C-5).** The per-result counter rules above apply **as-is only for a
sequential tick (B = 1)**. When the batch size > 1, **this aggregation is the SOLE owner of both
`consecutive_no_progress` and `consecutive_false_positives`** — apply each item's *board* action
(un-claim, Done, False positive) and `last_failure_signature` per result, but do **not** let the
per-result paths touch the two stagnation counters (summing per-item increments would let one batch
trip a three-strike breaker). Compute them once for the whole batch:
- If **any** result opened a PR (via step 2.5), was a confirmed false-positive, or was
  `already_implemented` (moved to Done) → **reset `consecutive_no_progress` to 0** (progress) and
  **clear `last_failure_signature`**.
- Only if **all** results were no-progress (red / systemic-blocked / baseline-violation) does the tick
  count as a **single** no-progress tick (increment `consecutive_no_progress` by 1, never by the count).
- `consecutive_false_positives`: if the batch had **any** non-FP progress (a PR opened or an
  `already_implemented` item), **reset it to 0** (the FPs were not consecutive). Otherwise — a batch
  that is **purely** false positives — increment it **once** (not by the count). If the running total
  ≥ 3, the step-0 breaker trips.
- Each item in the batch counts as one `items_processed` in step 5 (a batch of 3 → 3 processed, so a
  parallel drain is faster but does not inflate the per-tick budget semantics).

### 2.5 Pre-PR review round (always — machine-verify + sub-agents + rework + PR open)
The runner handed back a pushed **draft branch** (it never opens the PR itself). Set
`DRAFT_BRANCH` from the runner's `draft_branch` JSON field. **`git fetch origin "$DRAFT_BRANCH"`
first** — the runner pushed from its own worktree, so the orchestrator checkout has no local ref;
then use **`origin/$DRAFT_BRANCH`** as the head everywhere in 2.5 (diffs `origin/main...origin/$DRAFT_BRANCH`,
`git show "origin/$DRAFT_BRANCH:$path"`, and `HEAD_REF=origin/$DRAFT_BRANCH` for sub-agents) — for the
**initial** pass as well as rework passes, so the gate never reviews a missing or stale local ref. This step runs the review gate
**before** the PR exists, so the self-review axes and bot-likely findings are cleared upfront and
the post-PR settle window stays short. Up to **two** rework iterations; then the PR opens.

**2.5a. Orchestrator machine-verify (independent of the runner's self-report — do not trust
`self_review_passed`).** On `git diff origin/main...origin/$DRAFT_BRANCH`, grep for the self-review gate's
mechanical axes:
- `[Obsolete]` on a `+`-line introduced in this diff (same-change deprecate-and-replace ⇒ blocking).
- `V2` / `Ex2` / `2` type-name suffixes on `+`-lines (compat-shim smell ⇒ blocking).
- `// TODO` / `## Follow-ups` / `# Follow-ups` on `+`-lines (deferred work ⇒ blocking).
- Every changed `.axaml` UserControl declares `x:CompileBindings="True"` + `x:DataType` (missing ⇒
  blocking; suggest the fix inline).
- GPL/MIT boundary: run `bash .claude/scripts/check-gpl-mit-boundary-diff.sh origin/main "origin/$DRAFT_BRANCH"`
  — this is a **diff-side scan**, not the PreToolUse hook (the hook reads `tool_input.file_path`
  from Edit/Write JSON and is a no-op when invoked on a diff). The script reads the head-side
  file content via `git show "origin/$DRAFT_BRANCH:$f"` (the draft branch may not be checked out in the
  orchestrator's working tree). Exit 1 ⇒ blocking; the script prints `file:line` for each
  violation.
- **Workflow files (hard guardrail):** any change under `.github/workflows/` in the diff
  (`git diff --name-only origin/main...origin/$DRAFT_BRANCH -- .github/workflows/`) is a **hard guardrail
  violation** (AGENTS.md rule #5 — never touch CI workflows without explicit approval). This is not
  reworkable by the loop: stop, leave the item for a human, and do **not** open or merge a PR from it.

Collect any hits as `machine_findings`. The GPL/MIT and workflow hits are **hard guardrail**
findings: they are never auto-reworked or auto-merged — they always route to a human.

**2.5b. Sub-agent review.** Dispatch `@beutl-reviewer` and `@beutl-xaml-binder` (read-only) on
`git diff origin/main...origin/$DRAFT_BRANCH`. **Do not trust the runner's `design_reviewer_required` flag
alone** — the draft is untrusted, so re-derive it from the diff here: grep
`git diff origin/main...origin/$DRAFT_BRANCH` for changes to public surface (`src/Beutl.Engine`,
`Beutl.Api`, `Beutl.Extensibility`, `Beutl.NodeGraph`, `Beutl.FFmpegIpc`, `Beutl.ProjectSystem`,
`Beutl.Controls`), a new abstraction, or an `[Obsolete]`/compat-shim pattern. If **either** the flag
**or** this grep says design-sensitive, also dispatch `@beutl-design-reviewer`. **Pass `BASE_REF=origin/main` and `HEAD_REF=origin/$DRAFT_BRANCH` as environment
variables to each sub-agent** so they diff the actual draft branch instead of `HEAD` (which is the
loop branch in the orchestrator checkout, not the draft). Collect their blocking findings as `review_findings`.

**2.5c. Rework loop (≤ 2 passes).** **Short-circuit on hard guardrails first:** if `machine_findings`
contains a hard guardrail (a `.github/workflows/*` change or a GPL/MIT violation from 2.5a), **skip
rework entirely** and jump to the 2.5d hard-guardrail handoff — these are non-reworkable, so the runner
must **never** be dispatched to amend such a draft. Otherwise, if `machine_findings` (non-guardrail) or
`review_findings` are non-empty:
- Re-dispatch `beutl-board-task-runner` in **Rework mode** (`REWORK=true`, `draft_branch`,
  `review_findings=<combined>`, `OPEN_PR=false`); it amends the branch, re-runs the binary gates,
  pushes, and returns `draft_ready` again.
- **Re-fetch the amended branch first** (`git fetch origin "$DRAFT_BRANCH"`) — the runner pushed from
  a detached HEAD, so the local `$DRAFT_BRANCH` ref does not advance. Re-run 2.5a + 2.5b against the
  freshly-fetched ref, i.e. diff `origin/main...origin/$DRAFT_BRANCH`, or stale pre-rework content
  makes fixed findings reappear and clean drafts open as high-risk. Increment the rework count and loop.
- **2-rework budget spent with blocking findings remaining** → stop reworking; the findings are
  **unresolved**.

**2.5d. Open the PR.** **First: if any unresolved `machine_findings` is a hard guardrail (a
`.github/workflows/*` change or a GPL/MIT boundary violation from 2.5a), do NOT open a PR** — 2.5a
declared these non-reworkable, so stop here and leave the item for a human (record `blocked` /
`left_reason: "hard guardrail (workflow / GPL-MIT) — needs explicit approval"`). **Do NOT un-claim it
to `Todo`** — that is the auto-queue, and a restart/journal-expiry would let the loop re-pick it and
repeat the forbidden draft. Instead **keep it `In Progress`** (so the auto-loop skips it) **and leave a
durable, board-visible human handoff**. Most Project #9 items are DraftIssues with no repo issue
number, so `gh issue comment` / `gh pr comment` have no target — write the handoff into the item's
DraftIssue body via the project API, and post an issue comment only when a linked repo issue exists.
DraftIssues are updated through `updateProjectV2DraftIssue`; because the `body` argument overwrites,
fetch the current body, append the guardrail note, and write it back (escaping the newlines for
GraphQL). Conceptually:
```bash
# ITEM_ID is the PVTI_… project item id. Resolve it to its DraftIssue content id, read the current
# body, then append the handoff and write it back via updateProjectV2DraftIssue.
DRAFT_ISSUE_ID=$(gh api graphql -f query='query{node(id:"'"$ITEM_ID"'"){...on ProjectV2Item{content{...on DraftIssue{id body}}}}}' \
  --jq '.data.node.content.id')
#   … read .data.node.content.body, append "\n---\nHard guardrail (workflow / GPL-MIT) — needs
#   explicit approval. Left In Progress for a human. Reason: <…>.", then:
gh api graphql -f query='mutation{updateProjectV2DraftIssue(input:{projectId:"PVT_kwDOBLw8Fs4BW4g5",draftIssueId:"'"$DRAFT_ISSUE_ID"'",body:"<escaped updated body>"}){draftIssue{body}}}'
# If the item has a linked repo issue (not a pure DraftIssue), also post an issue comment there.
``` Otherwise, re-dispatch the runner
in Rework mode with `OPEN_PR=true` and empty
`review_findings` to open the PR from the (possibly amended) draft branch. This is the step-2 **"PR
opened"** outcome: reset both stagnation counters to 0, **clear `last_failure_signature`** (any
progress reset must also clear it — otherwise an unrelated later item that fails with the same first
error line trips Step 0's back-to-back repeat-signature stop despite real progress in between), and
mark the tick as PR-opened (step 5 records `last_pr_tick`). If findings were left **unresolved** (2.5c), force the PR's risk to **high → leave for
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
   executes the tasks on a feature branch and hands back a draft → continue at step 2.5. **Pass the
   generated `docs/specs/<NNN>-<slug>/` artifacts (or their paths) to the runner so it commits them
   on the draft branch alongside the implementation** — they are generated in the orchestrator
   checkout, so without this the PR would ship the implementation without the spec that drove it.

If the Spec-Kit flow cannot produce a coherent spec/plan/tasks from the item body (too underspecified
even for a spec), treat the item as `blocked` (`blocked_kind: "item-specific"`,
`blocked_reason: "feature too underspecified for spec-kit"`) — record it, **un-claim it back to `Todo`**
(the runner already claimed it `In Progress` before returning `speckit_required`; future runs select
only `Backlog`/`Todo`, so leaving it `In Progress` would strand it), and skip.

### 3. Resolve reviews (sub-agent, bounded settle)
Wait for async bot reviews, bounded by `settle_minutes`. **Block on CI without busy-waiting, and bound
the wait so a hung check cannot hang the loop:** prefer
`timeout $((settle_minutes * 60)) gh pr checks "$PR" --required --watch --interval 60` (`timeout` is in
the allowlist; exit `124` = the settle window elapsed → left for human). **Watch only `--required`
checks** — an optional job left pending must not hold the loop until timeout (the merge gate blocks
optional *failures* but not optional *pending*). If `timeout` is unavailable (some macOS installs lack
coreutils), use a bounded poll loop instead:
```bash
deadline=$((SECONDS + settle_minutes * 60))
while [ "$SECONDS" -lt "$deadline" ]; do
  # `bucket` (pass/fail/pending/skipping) is the reliable completion signal;
  # `state` values like SUCCESS/FAILURE do not equal "COMPLETE" and would
  # falsely hold the loop open. See https://cli.github.com/manual/gh_pr_checks
  # Wait only on REQUIRED pending (optional pending must not hold the loop), but
  # break early on ANY fail/cancel — no point waiting once the PR is already red.
  failed=$(gh pr checks "$PR" --json bucket --jq '[.[]|select(.bucket=="fail" or .bucket=="cancel")] | length' 2>/dev/null || echo 1)
  [ "$failed" != "0" ] && break
  pending=$(gh pr checks "$PR" --required --json bucket --jq '[.[]|select(.bucket=="pending")] | length' 2>/dev/null || echo 1)
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
authoritative** (and the step-5 merge gate re-checks them regardless). **Derive the quiet-period clock
yourself too** — take the **max** of: the latest review `submitted_at`, the head commit
`committedDate`, the newest **issue** comment `updated_at` (`gh pr view "$PR" --json reviews,comments,commits`),
**and the newest inline review-comment `updated_at`** (`gh api "repos/b-editor/beutl/pulls/$PR/comments" --paginate`)
— `gh pr view --json comments` returns only issue comments, so an inline reply (including the
resolver's own) is invisible to it and the loop could merge right after replying, before bots respond.
That max is `last_activity_at`; the resolver's `last_activity_at` is advisory (it now carries real timestamps, but
the orchestrator's own read is authoritative, consistent with CI/thread state). If `needs_human` is
set, the settle window elapses, or CI is red → the PR is **left for human** (do not merge).

### 4. Classify risk (moderate policy) and decide the merge
**First, re-verify the final PR head — Step 3 (`beutl-resolve-reviews --auto`) may have pushed
review-fix commits after the runner reported.** `git fetch origin "$DRAFT_BRANCH"`, then on
`git diff origin/main...origin/$DRAFT_BRANCH`:
- **Re-run the ENTIRE 2.5a machine-verify** on the final head — not just the diff size: all axes
  (`[Obsolete]`, `V2`/compat-shim, `// TODO`/Follow-ups, XAML compiled bindings, the GPL/MIT scan, and
  the `.github/workflows/*` hard guardrail). A small review fix can introduce any of these after the
  pre-PR audit. Any new **hard guardrail** (workflow / GPL-MIT) → do not merge, leave for human; any
  other new blocking finding → high-risk → human.
- **Recompute** `diff_loc`, `files_changed`, the `touched_*` flags, and design-review eligibility — a
  review fix can push the PR over the diff/coverage threshold or into a high-risk area.

**Auto-merge eligible (low/moderate) only if ALL hold:** `commit_type ∈
{fix,refactor,perf,test,docs,style,chore, small feat}` and **not** `is_breaking`; **not**
`is_feature` or `speckit_required` (features — even small ones — need a human merge; only
`small feat` that is an incidental improvement, not a new product behavior, is eligible); no
public-API design judgment (no blocking `@beutl-design-reviewer` finding); not `touched_gpl_mit /
touched_source_gen / touched_persistence`; **moderate diff** (≈ ≤250 LOC and ≤8 files); runner
`test_status == "green"`, **or `"none"` when `touched_production` is false** (a docs/style/chore-only
change touches no `src/`, so it has no NUnit coverage to require — but a `manual-verification` item is
**not** eligible → human); required checks
(`build`, `dotnet-format`) green and nothing else failing; **every review thread resolved**; no
outstanding `CHANGES_REQUESTED` from any bot **or human**; `needs_human` false; settled; mergeable;
and — when the **production (`src/`) diff is ≥ 100 added lines** (`touched_production && diff_loc >= 100`;
read `diff_loc` as the production `src/` count, **not** the test-inflated total) — **changed-line
coverage ≥ 70%** (the B-4 probe below; a lower coverage ⇒ high-risk, because the new code is
under-tested for an unattended merge).
**The loop runs as the code owner, so it posts its own approval** — a pending `REVIEW_REQUIRED` is
**not** a stop: run `gh pr review "$PR" --approve` first, then proceed. A `CHANGES_REQUESTED` that
could not be cleanly auto-resolved ⇒ leave for human. **Otherwise high-risk → leave for human.** When
unsure, choose human (fail safe).

**B-4 coverage probe (conditional — only when the production `src/` diff is ≥ 100 added lines).** The
100-LOC threshold targets changes large enough that a missing coverage floor is a meaningful risk;
smaller changes are gated by the binary test gate (green required) and the diff-size limit (≤250 LOC).
The probe is `src/`-scoped + Cobertura-based, so an **XAML-only production change mechanically reports
0%** (markup isn't instrumented) — such a change is **not** probe-eligible: treat it as gated by the
test gate + the diff-size limit, and do **not** block auto-merge on that 0%. Run the matching test
project with coverage, then compute changed-line coverage with the probe script:
```bash
# Identify the matching test project from the touched src/ path (heuristic):
#   src/Beutl.Engine/...  -> tests/Beutl.UnitTests/  (or tests/Beutl.Graphics3DTests/ for graphics)
#   src/Beutl.ProjectSystem/... -> tests/Beutl.ProjectSystem.Tests/ (if present)
TESTS_PROJ=<derive-from-diff>
# Coverage MUST be measured against the PR head, not the orchestrator checkout
# (the draft was built in a worker worktree; this checkout holds a different
# branch and stale build artifacts). Check out the draft into a throwaway
# worktree and build+test THERE so Cobertura matches the diff denominator below.
git fetch origin "$DRAFT_BRANCH" --quiet
COV_WT=$(mktemp -d "${TMPDIR:-/tmp}/loop-cov-wt-XXXXXX")
git worktree add --detach "$COV_WT" "origin/$DRAFT_BRANCH" --quiet
# Dedicated ABSOLUTE results dir (absolute so it resolves correctly from inside
# the worktree subshell) so a stale coverage.xml cannot be picked up. pipefail
# ensures dotnet test's failure is not masked by tail. (--build, not --no-build:
# the fresh worktree has no prior artifacts.)
COV_DIR=$(mktemp -d "${TMPDIR:-/tmp}/loop-cov-XXXXXX")
set -o pipefail
( cd "$COV_WT" && dotnet test "$TESTS_PROJ" -f net10.0 --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings --results-directory "$COV_DIR" ) 2>&1 | tail -5
TEST_RC=$?
set +o pipefail
git worktree remove --force "$COV_WT" 2>/dev/null || true
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
    # The diff is origin/main...origin/$DRAFT_BRANCH (three-dot: only head-side
    # changes since the merge-base). Use the freshly-fetched REMOTE ref, not the
    # local $DRAFT_BRANCH name — a rework/review-fix pushed from a detached HEAD
    # advances origin/$DRAFT_BRANCH while the local ref stays at the pre-fix
    # commit, which would omit those lines from the denominator. This matches the
    # remote head the worktree above was built from.
    python3 .claude/scripts/changed-line-coverage.py origin/main "origin/$DRAFT_BRANCH" "$COV" --threshold 70
    PROBE_RC=$?
    rm -rf "$COV_DIR"
    # Branch on PROBE_RC (captured BEFORE the rm so the cleanup's zero exit does not mask it):
    #   0 = >=70% (auto-merge eligible); 1 = <70% (high-risk); 2 = probe failed (high-risk, fail safe).
  fi
fi
```
If the test run failed, no coverage XML was produced, or the probe exits **non-zero** (`$PROBE_RC` — exit 1 =
under threshold, exit 2 = probe failure), mark the PR **high-risk**: post the coverage number (or
the failure reason) on the PR for the human and do not auto-merge. The probe is a **gate on
auto-merge eligibility only** — the PR still opens and goes through bot review resolution.

`main` is ruleset-protected (required checks `build`/`dotnet-format`, every thread resolved,
**code-owner review**, squash-only, signed commits). The loop **posts the code-owner approval** (it
runs as `@yuto-trd`), then **attempts** the merge pinned to the reviewed head; a ruleset refusal is
recorded as `left_for_human`, never forced or bypassed.

```bash
HEAD_SHA=$(gh pr view "$PR" --json headRefOid -q .headRefOid)

# --- Self-gate: fail-closed checks BEFORE approve/merge (defense-in-depth) ---
# The loop only proceeds if ALL hold; any failure ⇒ left_for_human (no retry/force/bypass).

# 1. Nothing failing (required OR optional), and no REQUIRED check still pending. A `skipping` bucket
# is NOT a failure (optional job that did not run). buckets: pass/fail/pending/skipping/cancel.
# Failures block regardless of required-ness; pending only blocks for required checks (do not wait on
# an optional pending job forever).
FAILED=$(gh pr checks "$PR" --json bucket --jq '[.[]|select(.bucket=="fail" or .bucket=="cancel")] | length' 2>/dev/null || echo 1)
REQ_PENDING=$(gh pr checks "$PR" --required --json bucket --jq '[.[]|select(.bucket=="pending")] | length' 2>/dev/null || echo 1)
if [ "$FAILED" != "0" ] || [ "$REQ_PENDING" != "0" ]; then
  echo "checks not clear (failed/cancelled=$FAILED, required-pending=$REQ_PENDING) — left_for_human"; exit 1
fi

# 2. Unresolved review threads must be 0 — PAGINATE (reviewThreads(first:100) silently truncates on a
#    PR with >100 threads, so an unresolved thread on a later page would otherwise slip through).
UNRESOLVED=0; CURSOR=""
while :; do
  AFTER=$([ -n "$CURSOR" ] && echo ", after: \"$CURSOR\"" || echo "")
  RESP=$(gh api graphql -f query='query{repository(owner:"b-editor",name:"beutl"){pullRequest(number:'"$PR"'){reviewThreads(first:100'"$AFTER"'){pageInfo{hasNextPage endCursor} nodes{isResolved}}}}}')
  UNRESOLVED=$((UNRESOLVED + $(echo "$RESP" | jq '[.data.repository.pullRequest.reviewThreads.nodes[]|select(.isResolved==false)]|length')))
  [ "$(echo "$RESP" | jq -r '.data.repository.pullRequest.reviewThreads.pageInfo.hasNextPage')" = "true" ] || break
  CURSOR=$(echo "$RESP" | jq -r '.data.repository.pullRequest.reviewThreads.pageInfo.endCursor')
done
if [ "$UNRESOLVED" != "0" ]; then
  echo "unresolved threads ($UNRESOLVED) — left_for_human"; exit 1
fi

# 3. No outstanding CHANGES_REQUESTED, and PR must be MERGEABLE. A summary-only CHANGES_REQUESTED leaves
#    no line thread, so check reviewDecision (and mergeable) explicitly — never approve over a requested change.
read -r MERGEABLE REVIEW_DECISION < <(gh pr view "$PR" --json mergeable,reviewDecision --jq '"\(.mergeable) \(.reviewDecision)"')
if [ "$REVIEW_DECISION" = "CHANGES_REQUESTED" ]; then
  echo "reviewDecision=CHANGES_REQUESTED — left_for_human"; exit 1
fi
if [ "$MERGEABLE" != "MERGEABLE" ]; then
  echo "not mergeable ($MERGEABLE) — left_for_human"; exit 1
fi

# --- Self-approval caveat (see docs/ai-workflow/loop-engineering.md) ---
# The loop runs as @yuto-trd (the code owner), so gh pr review --approve returns
# 422 ("Can not approve your own pull request"). That 422 is BENIGN: the squash
# merge below still completes via the account's bypass permission on the protected
# branch (auto-merge IS operational; required checks + thread resolution are still
# enforced). Don't treat the 422 as a stop — the risk classifier (step 4) is the gate.
gh pr review "$PR" --approve --body "Auto-approved by /beutl-loop (code-owner). Risk: <low|moderate>." 2>&1 || \
  echo "self-approve returned 422 (PR author == code owner) — benign; the merge below completes via bypass"

# Attempt the merge, then VERIFY the real outcome via `gh pr view --json state` — NOT the
# gh pr merge exit code, which returns non-zero when --delete-branch can't delete a local
# loop/<slug> branch a runner worktree still holds, even though the REMOTE merge succeeded.
# (Remove the runner's worktree + `git branch -D loop/<slug>` BEFORE this to avoid that.)
gh pr merge "$PR" --squash --delete-branch --match-head-commit "$HEAD_SHA" 2>&1 || true
if [ "$(gh pr view "$PR" --json state -q .state)" = "MERGED" ]; then
  # The merge succeeded; the board MUST follow. Retry the Done transition (a transient API/auth/rate
  # error here would otherwise strand a shipped item In Progress, which future runs never re-select).
  moved=0
  for _ in 1 2 3; do
    if gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 --id "$ITEM_ID" \
         --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk --single-select-option-id 98236657; then  # Done
      moved=1; break
    fi
    sleep 5
  done
  [ "$moved" = "1" ] || echo "WARN: PR $PR merged but board move to Done failed 3× — record as a recoverable failure for follow-up (do NOT treat the item as complete)."
  # Refresh local origin/main: the next item branches from and diffs against it, so a multi-item
  # drain must see the commit just merged or it will branch from stale main (conflicts / lost work).
  git fetch origin main --quiet || true
else
  echo "merge blocked by ruleset — record left_for_human; board stays In Progress; no retry/force/bypass"
fi
```
High-risk PRs — and **any merge GitHub refuses** — are **left open**; the board item stays
`In Progress` for a human review pass.

### 5. Update the journal
Append the per-item record (risk, `merged` / `left_for_human` + reason, reviews_resolved; or the
`{kind, reason}` for a blocked item). Increment `items_processed` **by the number of items that
reached a terminal state this tick** — 1 in a sequential tick, **B in a parallel batch of B items**.
Each item counts once (false-positive, blocked, or PR opened — including a draft that opened in step
2.5); a PR that then merges or is left for the human is still that same one item, never a second.
Counting a batch of B as 1 would let later ticks overshoot a set budget and skew the
`last_pr_tick`/stagnation math. **If this tick opened a PR (including via the step-2.5 pre-PR
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
  (exit code, never a console string) + `dotnet format Beutl.slnx --verify-no-changes` (whole-solution
  — a scoped run misses a new `.cs` lacking the UTF-8 BOM, which CI's whole-solution check fails with
  `CHARSET`) + `@beutl-reviewer` (+ `@beutl-design-reviewer` on public surface). No green gate ⇒ no PR
  ⇒ no-progress.
- **Stagnation breaker:** stop after **3** consecutive no-progress ticks **with no PR opened in the
  last 3 ticks** (recent shipping holds it open), or immediately on a repeated `item_id` /
  `last_failure_signature`. A `blocked` item counts toward this only when its `blocked_kind` is
  `systemic`; an `item-specific` block is skipped without penalty.
- **Budget:** default drains the board (`until-empty`) **unbounded by item count** — the stagnation
  breaker is the runaway guard; pass `N` or set the optional `BEUTL_LOOP_MAX_ITEMS` for a cap;
  optional wall-clock.
- **Deterministic STOP** only (the reasons above) — the loop runs as long as the board has eligible
  items; it ends on an empty board, the stagnation breaker, a set budget/wall-clock, or a guardrail.
- **Auto-merge is conservative and fail-safe:** low/moderate-risk only, all gates green + settled;
  high-risk and uncertain go to the human; squash + delete-branch; never auto-merge high-risk; never
  force-push `main`.
- **Context isolation:** all heavy work runs in sub-agents; keep only structured results + journal.
- **`rm` permission scope:** `Bash(rm:*)` is allowed for coverage-probe cleanup (`rm -rf "$COV_DIR"`).
  The `rm -rf` deny hook catches the literal dangerous forms, but like force-push, the hook's pattern
  match is not exhaustive — forms outside the pattern can still execute. The allowlist is deliberately
  narrow (scoped to the loop's known `rm` use); broader `rm` use outside the loop is not covered.
- **Runner false-positive spot-check (S-5):** when a runner returns `false_positive: true` (a
  candidate — the runner did **not** touch the board), the orchestrator spot-checks before accepting:
  re-read the cited `path:line` to confirm the finding is genuinely a false positive.
  - **Confirmed** → move the item to `False positive` and **append the signature to loop-memory**.
  - **Inconclusive / refuted** → treat as `blocked` (`blocked_kind: "item-specific"`), **revert the
    item to `Todo`** (un-claim, so it is not stranded), and **do NOT append the signature** — an
    unconfirmed/refuted claim must not bias later runs toward the false-positive path for matching
    items. Only the spot-check, never a single sub-agent's judgment, gates both the board write and the
    loop-memory write.
- **Do not defer work** (inherited from `beutl-board-task`): each item is finished or explicitly
  `blocked` — never "fix it next tick".
