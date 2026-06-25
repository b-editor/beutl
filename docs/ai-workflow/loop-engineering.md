# Loop engineering — the `/beutl-loop` board-draining loop

**Loop engineering** is the practice of designing the *autonomous feedback loop* an AI coding agent
runs in — plan → act → observe → revise — instead of hand-typing one prompt at a time. The term was
formalized in June 2026 (building on Peter Steinberger's "Ralph technique" and Claude Code); it is
the abstraction level after prompt-, context-, and harness-engineering. A loop treats software work
as an iterative system that corrects itself against **real signals** (build, tests, type checks,
linters, reviews) rather than against your patience.

Beutl already had every *ingredient* of a loop — durable memory (`.remember/`, the `.claude/`
memory), verification gates (`/beutl-build` / `/beutl-test` / `/beutl-format`, `@beutl-reviewer`,
golden tests), a work queue (Project #9), a one-shot task runner (`beutl-board-task`), and reporting
(`scheduled-code-review.yml` files findings to the board). What was missing was the **outer driver**
that ties them together and runs them to a stop condition. `/beutl-loop` is that driver.

## The failure mode this design guards against: *loopmaxxing*

Loopmaxxing is the loop-era version of "tokenmaxxing" — assuming that *more* autonomous cycles
automatically solve a problem. It happens when a loop is given a vague, unquantifiable goal with **no
binary pass/fail gate and no deterministic stop**, so it runs forever, burning cost without progress.
Every guardrail below exists to prevent it: measurable gates, a stagnation circuit-breaker, a hard
budget, and a human checkpoint.

## The Beutl loop contract

Every loop here is defined by six pillars. `/beutl-loop` fills them in as:

| Pillar | `/beutl-loop` |
|---|---|
| **TRIGGER** | Manual: `/beutl-loop [N]` in-session, or `.claude/scripts/beutl-loop.sh` headless. **No cron** — adding a scheduled GitHub Actions runner is a separate, explicitly-approved change (`.github/workflows/*` is protected by AGENTS.md rule #5). |
| **SCOPE** | Unclaimed (`Backlog`/`Todo`) items on **Project #9**, **every kind and across the full risk spectrum — features included**. Never touches `.github/workflows/*`; never crosses the GPL/MIT boundary. |
| **ACTION** | Per tick: implement → PR (sub-agent) → resolve reviews (sub-agent) → classify risk → auto-merge (low/moderate) or hand to a human (high). One item ⇒ at most one PR. |
| **BUDGET** | **Default drains the board** (`until-empty`), bounded by the stagnation breaker, the optional wall-clock `BEUTL_LOOP_MAX_MINUTES`, and a runaway backstop `BEUTL_LOOP_MAX_ITEMS` (default **50**). Pass an integer `N` for a tighter per-run budget. Per-PR review settle cap `BEUTL_LOOP_SETTLE_MINUTES` (default 20). Item count is the reliable proxy for a token/$ budget. |
| **STOP** | Board drained · `items_processed ≥ N` (explicit budget or the runaway backstop) · stagnation (2 consecutive no-progress, 3 consecutive false-positives, or a repeated item / failure signature) · wall-clock exceeded · a tick is `blocked` needing the user · a guardrail would be violated. |
| **REPORT** | A Markdown run summary (per item: PR, risk, merged/left-for-human, reviews resolved; plus false-positives, blocked, counters, stop reason), then a reminder to run `/beutl-ai-self-review`. |

## Sub-agent isolation keeps the orchestrator's context lean

`/beutl-loop` runs as an **orchestrator** in the main session and **never implements items itself**.
Each tick it dispatches sub-agents and keeps only their compact JSON results plus the run journal:

- **Dispatch A — implement → PR.** The committed `beutl-board-task-runner` agent runs the
  `beutl-board-task` flow for one item in an **isolated git worktree** (`isolation: worktree`) and
  returns `{ pr_url, commit_type, is_breaking, is_feature, diff_loc, touched_public_api / gpl_mit /
  source_gen / xaml_behavior / persistence, design_reviewer_required, test_status, … }`.
- **Dispatch B — resolve reviews.** A sub-agent running `beutl-resolve-reviews --auto` clears the
  PR's bot reviews and returns `{ threads_resolved, changes_requested_outstanding, needs_human, … }`.
- **Orchestrator (cheap).** From those signals it classifies risk, decides the merge, updates the
  board and journal, and evaluates STOP — all without holding the verbose file reads, diffs, or test
  logs that lived inside the sub-agents.

This is why a long run does not exhaust the orchestrator's context. For large parallel work, the
existing multi-agent playbook in `CLAUDE.md` (or a `Workflow`) is the heavier-duty path; `/beutl-loop`
defaults to sequential ticks with per-tick sub-agent dispatch.

## One tick, end to end

1. **Terminate?** Check budget / wall-clock / eligibility / stagnation first.
2. **Select** the next unclaimed item (re-fetch the board, exclude already-attempted ids), across the
   full spectrum — do **not** pre-filter by risk or kind.
3. **Dispatch A** → implement and open the PR. A **false-positive advances the board** (it counts as
   progress and resets `consecutive_no_progress`, but bumps `consecutive_false_positives`); **blocked /
   red ⇒ no-progress** tick.
4. **Review gate:** `@beutl-reviewer` (+ `@beutl-design-reviewer` for public surface). A blocking
   finding ⇒ high-risk.
5. **Dispatch B** (bounded settle) → resolve bot reviews; `needs_human` / red / timeout ⇒ leave for
   the human.
6. **Classify risk + merge** (below).
7. **Journal** the outcome; recompute the stagnation counter; loop.

## Risk classification (moderate policy)

**Auto-merge eligible (low / moderate)** — *all* must hold:

- `commit_type ∈ {fix, refactor, perf, test, docs, style, chore, **small feat**}` and **not**
  breaking (`!` / `BREAKING CHANGE:`).
- No public-API / extensibility design judgment required (no blocking `@beutl-design-reviewer`).
- Does **not** touch the GPL/MIT boundary, source generators, or a persistence/serialization format.
- **Moderate diff** (heuristic ≤ ~250 LOC and ≤ ~8 files).
- The runner's tests are **green** — an item that could only be `manual-verification` (no NUnit test,
  typically UI) is **not** auto-merge-eligible; it goes to the human.
- **Required checks green** (`build`, `dotnet-format`) and no other check failing, **every review
  thread resolved**, **no outstanding `CHANGES_REQUESTED`** (from any bot **or human**), no merge
  conflict, and the PR has **settled** (no new review/commit activity for ~10 min).

**High-risk → human merge** — any of: breaking change · public-API design judgment · GPL/MIT ·
source generator · persistence format · large diff · a bigger feature · a `CHANGES_REQUESTED` that
could not be cleanly auto-resolved · anything needing product or architecture judgment.

**When in doubt, leave it for the human.** This is the load-bearing fail-safe.

## Auto-merge mechanism — GitHub rulesets enforce the gate; the loop self-gates on top

`main` is protected by **repository rulesets** (verify with
`gh api repos/b-editor/beutl/rules/branches/main`): a PR is required, the **required status checks
`build` and `dotnet-format`** must pass, **every review thread must be resolved**, **code-owner review
is required** (`.github/CODEOWNERS` is `* @yuto-trd`, so every PR needs the code owner's approval), only
**squash** merges are allowed, commits must be **signed**, and history must stay **linear**. GitHub
enforces all of this server-side, so the loop is **not** the only gate — its self-check is
defense-in-depth that (a) applies the risk policy and (b) avoids attempting a merge GitHub will refuse.

**Practical consequence:** because `* @yuto-trd` owns every path and code-owner review is required, an
unattended loop generally **cannot complete a merge** — the code owner's approval is missing and an
author cannot approve their own PR. So in practice `/beutl-loop` opens PRs, resolves bot reviews, and
**leaves low/moderate-risk PRs ready for the code owner to approve + merge** rather than merging them
itself. True unattended auto-merge only happens where a PR satisfies every ruleset requirement (e.g. if
the maintainer relaxes code-owner review for some paths). Changing CODEOWNERS or the rulesets is a
maintainer decision and is never done by the loop.

For a low/moderate-risk, settled PR the loop self-gates, then **attempts** the squash merge pinned to
the reviewed head, and treats a ruleset refusal as "leave for human":

```bash
PR=<n>
HEAD_SHA=$(gh pr view "$PR" --json headRefOid -q .headRefOid)
gh pr checks "$PR"                                  # required checks (build, dotnet-format) must pass
# Unresolved review threads must be 0 (assumes ≤100; follow pageInfo for more — GitHub also enforces
# this, so the loop only checks to decide + avoid a futile merge attempt):
gh api graphql -f query='query{repository(owner:"b-editor",name:"beutl"){pullRequest(number:'"$PR"'){reviewThreads(first:100){nodes{isResolved}}}}}' \
  --jq '[.data.repository.pullRequest.reviewThreads.nodes[]|select(.isResolved==false)]|length'   # must be 0
gh pr view "$PR" --json mergeable,reviewDecision -q '.mergeable,.reviewDecision'                   # need MERGEABLE + APPROVED; REVIEW_REQUIRED (code-owner pending) / CHANGES_REQUESTED => left_for_human
# Move to Done ONLY if the merge actually succeeds; a ruleset refusal (e.g. code-owner review) leaves
# the item In Progress for the human.
if gh pr merge "$PR" --squash --delete-branch --match-head-commit "$HEAD_SHA"; then
  : # gh project item-edit … --single-select-option-id 98236657   # Done
else
  echo "merge blocked by branch ruleset — record left_for_human; do not retry/force/bypass"
fi
```

It **never** uses `gh pr merge --auto`, never merges a high-risk PR, never force-pushes `main`
(hook + ruleset enforced), and never bypasses the rulesets. A merge GitHub refuses is recorded as
`left_for_human`, not retried.

## Resolving bot reviews automatically — `beutl-resolve-reviews`

After a PR opens, CodeRabbit, GitHub Copilot, Codex, and the repo's `claude-code-review` post reviews
asynchronously. `beutl-resolve-reviews` is the committed Beutl counterpart of the global
`handle-pr-reviews` skill: it reuses the same fetch/thread/resolve mechanics but adds an **`--auto`**
mode for the loop. In `--auto` it auto-handles **bot feedback only** — a **human** review or comment
is never auto-addressed or auto-resolved; it sets `needs_human` and stays open for a person. For the
bots, it **autonomously** addresses only clearly-actionable, low-judgment comments (bug/correctness,
mechanical change-requests, nits) with the smallest possible change, **re-runs build/test/format after
every edit (never pushes red)**, replies and resolves the handled threads, and **escalates** anything
needing judgment (`needs_human`) rather than guessing — which pushes the PR to the human-merge path.
Run standalone it stays human-in-the-loop (per-comment `AskUserQuestion`), so a person can use it
safely without the autonomy.

## Relationship to `beutl-board-task`

`beutl-board-task` is **one tick**: pick → verify-not-false-positive → claim → branch → implement →
test → PR (and, standalone, a human merges). `/beutl-loop` is the **meta-driver** that runs that tick
repeatedly with a budget, a stagnation breaker, autonomous review resolution, and a risk-gated
auto-merge. The loop does not re-implement board queries or the implement/PR cycle — it delegates
them. (`beutl-board-task` was previously named `beutl-ai-review-task`; it was renamed because Project
#9 holds hand-added tasks too, not only auto-detected review findings.)

## Run journal

`/beutl-loop` keeps a small, **gitignored, ephemeral** run journal at
`.claude/logs/beutl-loop-state.json` — within-run bookkeeping for the budget / stagnation / merge
decisions only. **The board (Project #9) is the single source of truth.** If the journal is deleted
mid-run, correctness is unaffected: the next tick re-derives eligibility from the live board (claimed
items are `In Progress` and excluded). The journal never moves an item to `Done`; that happens only
on a successful auto-merge.

## Headless variant + safety

`.claude/scripts/beutl-loop.sh` runs the loop unattended ("while you sleep"). It holds **no loop
logic** — it just launches `/beutl-loop` with a deliberately conservative envelope:

- **Default has NO `--dangerously-skip-permissions`.** It passes a **scoped** `--allowedTools`
  allowlist (no bare `Bash`) so the PreToolUse deny hooks (force-push to `main`, `rm -rf`, GPL/MIT
  boundary) still fire, and a command outside the set **stalls** the unattended run (a safe failure)
  rather than executing. The dispatched sub-agents (e.g. `beutl-board-task-runner`, whose `tools`
  include `Bash`) run inside this session, so their command execution is bounded by the same session
  `--allowedTools` and the same deny hooks apply to them. `--dangerously-skip-permissions` is available
  **only** behind `BEUTL_LOOP_YOLO=1`, with a small item cap (`N ≤ 3`, never a drain) and a loud
  warning — not recommended, because it disables those guardrails for the whole session.
- It **refuses to run on `main`/`master`, a detached HEAD, or a flat (no-`/`) branch** unless
  `BEUTL_LOOP_BRANCH_PREFIX` supplies the feature-branch prefix — the per-item branch-off-`origin/main`
  logic needs a usable `<prefix>/<slug>`. Transcript goes to the already-gitignored `.claude/logs/`.
- It has **no merge path of its own** — merging only ever happens inside `/beutl-loop`'s risk gate.

## Anti-loopmaxxing checklist

- [ ] Binary verification gate every tick (build + test exit-code + format + reviewer) — no green, no PR.
- [ ] Deterministic STOP conditions only; no "until done".
- [ ] Stagnation circuit-breaker (2 strikes; immediate on a repeat).
- [ ] Bounded run: default drains the board, bounded by a runaway backstop (`BEUTL_LOOP_MAX_ITEMS`, default 50) + stagnation breaker + optional wall-clock; pass `N` for a tighter budget.
- [ ] Auto-merge is conservative, settled, low/moderate-risk only; uncertain ⇒ human.
- [ ] Heavy work in sub-agents; orchestrator keeps only structured results.
- [ ] No deferred work — each item is finished or explicitly `blocked`.

## Verifying changes to the loop itself

The loop is Markdown + an agent + a bash launcher — no compilable C#, so the NUnit requirement
(AGENTS.md rule #3) does not apply. Verify with:

1. **`/beutl-loop dry-run 1`** — selects an item and prints its predicted risk tier, merge decision,
   and stop math **without** claiming, dispatching, PRing, resolving, merging, or touching the board.
2. **`bash -n .claude/scripts/beutl-loop.sh`** and confirm it refuses to run on `main`.
3. **One live smoke test** (opens a real PR — run intentionally): `/beutl-loop 1` on a low-risk item
   should open a PR, resolve its **bot** reviews, then **attempt** the squash merge. Under the current
   `main` rulesets (code-owner review required, `* @yuto-trd`) GitHub will refuse the merge, so the
   loop records `left_for_human` and stops with `stop_reason: budget` — the PR stays open for the code
   owner to approve + merge. (If you relax code-owner review for the touched paths, the same run
   instead auto-squash-merges, moves the board item to Done, and deletes the branch.) A **feature**
   item is always **left open for a human**. Stagnation check: against a slice of known false-positives
   the run stops after three consecutive false-positives (`stop_reason: stagnation`); against blocked /
   red items, after two consecutive no-progress ticks.
