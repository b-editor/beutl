---
description: |
  Autonomously work multiple Project #9 board items in one bounded loop. Each tick dispatches a
  worktree-isolated sub-agent to implement ONE item and open a PR, autonomously resolves the PR's
  reviews (CodeRabbit / Copilot / Codex / Claude), classifies the PR's risk, and then auto-merges
  the low-to-moderate-risk ones (squash) while leaving higher-risk ones for a human — stopping on a
  max-items budget, a stagnation circuit-breaker, or an empty board. Use when the user says
  "ボードをループで消化して", "loop the board", "keep working AI-review items until …",
  "/beutl-loop". Sub-agent dispatch keeps this orchestrator's context lean across many ticks.
allowed-tools: Task, Read, Grep, Glob, Write, Edit, Bash(gh:*), Bash(git:*), Bash(dotnet:*), Bash(python3:*), Bash(jq:*), Bash(mktemp:*), Bash(date:*)
argument-hint: "[N | dry-run | until-empty] [bug|diff|design|feature]"
---

# /beutl-loop — the board-draining loop (loop engineering)

You are the **orchestrator**. You do not implement items yourself — each tick you **dispatch
sub-agents** for the heavy work and keep only their compact JSON results plus the run journal in
context. This is the "sub-agent dispatch / fresh-context" pillar of loop engineering: the verbose
file reads, edits, diffs, and test logs stay inside the sub-agents. Read
`docs/ai-workflow/loop-engineering.md` for the full contract; this file is the executable procedure.

**Human checkpoint:** you are autonomous up to **opening a PR, resolving its bot reviews, and
_attempting_ a squash merge for low-to-moderate-risk PRs**. Higher-risk or uncertain PRs are left open
for a human. `main` is protected by **branch rulesets** (required checks `build`/`dotnet-format`,
**code-owner review** via `* @yuto-trd`, thread resolution, squash-only, signed commits), so GitHub —
not you — is the hard merge gate; your self-gate is defense-in-depth and avoids attempting merges
GitHub will refuse. In practice code-owner review means most merges are **left for the code owner**.
You never merge a high-risk PR, never force-push `main`, and never bypass the rulesets — be
conservative and fail safe to the human.

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

## Per-tick procedure

### 0. Termination check (before every tick — cheapest first)
Stop if: **no eligible item left** (re-fetch board, exclude `attempted_ids` — the default terminal
for a drain) · `items_processed ≥` the budget (an explicit `N`, or the runaway backstop
`BEUTL_LOOP_MAX_ITEMS`, default 50) · wall-clock exceeded · `consecutive_no_progress ≥ 3` **and** no
PR was opened in the last 3 ticks (`items_processed − last_pr_tick > 3` — recent shipping counts as
progress and holds the breaker open) · `consecutive_false_positives ≥ 3` (the board/selection is
mostly junk — stop and report) · the **same `item_id` or `last_failure_signature` recurs
back-to-back** (immediate stagnation stop). Record `stop_reason`.

### 1. Select the next item
Re-fetch the board snapshot (as in `beutl-board-task` Step 1), apply the type filter, exclude
`attempted_ids`, and pick one **across the full spectrum (features and higher-risk included — do not
pre-filter by risk)**. Capture the stable `ITEM_ID`. Add it to `attempted_ids` now.

### 2. Dispatch A — implement → PR (worktree sub-agent)
Dispatch the **`beutl-board-task-runner`** agent with the item (`ITEM_ID`, title, body) and a
`BRANCH_PREFIX` — use `$BEUTL_LOOP_BRANCH_PREFIX` if set, otherwise the current branch's `<prefix>/`
segment. If the current branch is flat/detached (no `/`) **and** the env var is unset, that is a setup
error: STOP and ask the user for a prefix (the headless wrapper already refuses this case up front). It
returns the structured result (`pr_url`, risk signals, `test_status`, or `draft_ready` /
`false_positive` / `blocked`). Outcomes and their effect on the **stagnation counters** (the budget
counter `items_processed` is incremented once per tick in step 6, **never here** — so a tick that
opens a PR and then merges it is still one processed item, not two):
- `draft_ready: true` (design-sensitive) → **not** a terminal outcome: go to **step 2.5** to run the
  design pass, which ends by opening the PR. Do not touch the counters yet — step 2.5 applies the
  "PR opened" reset once it opens the PR.
- `false_positive: true` → the runner already marked the board item `False positive`, so the item
  **leaves the queue** — this is **progress**: reset `consecutive_no_progress` to 0, increment
  `consecutive_false_positives`. Continue.
- `blocked: true` → record `{reason, kind}`; **never stop the whole drain on a single item** (skip it
  via `attempted_ids` and report it at the end). Reset `consecutive_false_positives` to 0, then by
  `blocked_kind`:
  - `"systemic"` (build won't compile, tests universally red, tooling broken) → **no-progress**:
    increment `consecutive_no_progress`, set `last_failure_signature`. Repeated systemic blocks trip
    the breaker.
  - `"item-specific"` (underspecified feature, upstream/product call, UI needing a human) → the
    toolchain is fine and the item simply isn't doable now: **neutral** — do **not** increment
    `consecutive_no_progress`. Continue.
- `test_status: "red"` / no PR (and not `blocked`) → **no-progress**: increment
  `consecutive_no_progress`, **reset `consecutive_false_positives` to 0**, set `last_failure_signature`
  from the runner's `failure_signature`, continue.
- PR opened (any risk) → **progress**: reset `consecutive_no_progress` **and**
  `consecutive_false_positives` to 0, mark this tick as having opened a PR (step 6 records
  `last_pr_tick`), and go on.

**Defense-in-depth on the test gate (B):** if the runner opened a PR but its own signals show
`touched_production == true && test_files_added_count == 0 && test_status != "manual-verification"`
(the runner should have blocked instead), treat the PR as **high-risk → leave for human** and count
the tick as **no-progress** (a runner-contract violation, not a shippable item).

### 2.5 Design pass (only when Dispatch A returned `draft_ready`)
The runner handed back a pushed **draft branch** instead of a PR because the change is design-sensitive
(`design_reviewer_required`). Run up to **two** rework iterations:

1. Dispatch **`@beutl-design-reviewer`** (read-only) on `git diff origin/main...<draft_branch>`.
2. **No blocking design finding** → design approved; open the PR (below).
3. **Blocking finding(s) with reworks left (`< 2` done)** → re-dispatch `beutl-board-task-runner` in
   **Rework mode** (`REWORK=true`, `draft_branch`, `design_findings=<findings>`, `OPEN_PR=false`); it
   amends the branch, re-runs the binary gates, pushes, and returns `draft_ready` again. Increment the
   rework count and loop to (1).
4. **Blocking finding(s) but the 2-rework budget is spent** → stop reworking; the design is
   **unresolved**.

**Open the PR** from the (possibly amended) draft branch by re-dispatching the runner in Rework mode
with `OPEN_PR=true` and empty `design_findings`. This is the step-2 **"PR opened"** outcome: reset both
stagnation counters to 0 and mark the tick as PR-opened (step 6 records `last_pr_tick`). If the design
was left **unresolved** (case 4), force the PR's risk to **high → leave for human**
(`left_reason: "unresolved design findings after 2 reworks"`) and skip the auto-merge path; otherwise
continue to step 3.

### 3. Review gate (orchestrator-level sub-agents)
Dispatch `@beutl-reviewer` on the PR diff. (`@beutl-design-reviewer` was already run in step 2.5 for
design-sensitive items — do not re-run it here; for non-design-sensitive items it is not needed.) A
**blocking** finding from `@beutl-reviewer` (GPL/MIT, XAML bindings, NUnit, source-gen) — or an
unresolved design finding carried over from step 2.5 — ⇒ mark the PR **high-risk** (its findings go to
the human along with the PR).

### 4. Resolve reviews (sub-agent, bounded settle)
Wait for async bot reviews, bounded by `settle_minutes`. Poll (~every 90s): dispatch a sub-agent
running **`beutl-resolve-reviews --auto`** for the PR to address clearly-actionable bot comments,
re-verify, and resolve threads. "**Settled**" = CI complete+green · zero unresolved threads · no
outstanding `CHANGES_REQUESTED` · no new review/comment/commit for ~10 min. **Re-fetch CI
(`gh pr checks`) and the thread/`reviewDecision` state yourself each poll — the resolver's
`ci_status`/`changes_requested_outstanding`/counts are advisory; the orchestrator's own `gh` reads are
authoritative** (and the step-5 merge gate re-checks them regardless). Use the resolver's
`last_activity_at` for the quiet-period clock. If `needs_human` is set, the settle window elapses, or
CI is red → the PR is **left for human** (do not merge).

### 5. Classify risk (moderate policy) and decide the merge
**Auto-merge eligible (low/moderate) only if ALL hold:** `commit_type ∈
{fix,refactor,perf,test,docs,style,chore, small feat}` and **not** `is_breaking`; no public-API
design judgment (no blocking `@beutl-design-reviewer` finding); not `touched_gpl_mit /
touched_source_gen / touched_persistence`; **moderate diff** (≈ ≤250 LOC and ≤8 files); runner
`test_status == "green"` (a `manual-verification` item is **not** eligible → human); required checks
(`build`, `dotnet-format`) green and nothing else failing; **every review thread resolved**; no
outstanding `CHANGES_REQUESTED` from any bot **or human**; `needs_human` false; settled; mergeable;
and **`reviewDecision == APPROVED`** — a `REVIEW_REQUIRED` (e.g. code-owner approval still pending,
which is the usual case under `* @yuto-trd`) means **leave for human and do not even attempt the
merge**. **Otherwise high-risk → leave for human.** When unsure, choose human (fail safe).

`main` is ruleset-protected (required checks `build`/`dotnet-format`, every thread resolved,
**code-owner review**, squash-only, signed commits). The loop self-gates, then **attempts** the merge
pinned to the reviewed head; a ruleset refusal (e.g. missing code-owner approval — `* @yuto-trd` owns
every path) is recorded as `left_for_human`, never forced or bypassed.

```bash
HEAD_SHA=$(gh pr view "$PR" --json headRefOid -q .headRefOid)
gh pr checks "$PR"                                          # required checks green, nothing failing
# Unresolved review threads must be 0 (assumes ≤100; paginate for more — GitHub also enforces this,
# so the loop only checks to avoid a futile merge attempt):
UNRESOLVED=$(gh api graphql -f query='query{repository(owner:"b-editor",name:"beutl"){pullRequest(number:'"$PR"'){reviewThreads(first:100){nodes{isResolved}}}}}' \
  --jq '[.data.repository.pullRequest.reviewThreads.nodes[]|select(.isResolved==false)]|length')
gh pr view "$PR" --json mergeable,reviewDecision -q '.mergeable,.reviewDecision'   # need MERGEABLE + APPROVED; REVIEW_REQUIRED/CHANGES_REQUESTED => left_for_human, skip the merge
# Move to Done ONLY if the merge actually succeeds; a ruleset refusal => leave In Progress for the human.
if gh pr merge "$PR" --squash --delete-branch --match-head-commit "$HEAD_SHA"; then
  gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 --id "$ITEM_ID" \
    --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk --single-select-option-id 98236657   # Done
else
  echo "merge blocked by ruleset — record left_for_human; board stays In Progress; no retry/force/bypass"
fi
```
High-risk PRs — and **any merge GitHub refuses** — are **left open**; the board item stays
`In Progress` (the code owner reviews and merges).

### 6. Update the journal
Append the per-item record (risk, `merged` / `left_for_human` + reason, reviews_resolved; or the
`{kind, reason}` for a blocked item). Increment `items_processed` **exactly once for this tick** —
every item that reached a terminal state (false-positive, blocked, or PR opened — including a
design-sensitive item that drafted then opened in step 2.5) counts as one; a PR that then merges or is
left for the human is still that same one item, never a second. **If this tick opened a PR (including
via the step-2.5 design pass), set `last_pr_tick = items_processed`** (used by the step-0 "PR within
the last 3 ticks" guard). The stagnation counters (`consecutive_no_progress`,
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
