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
| **ACTION** | Per tick: implement → draft (sub-agent) → pre-PR review round (machine-verify + reviewers + rework) → open PR → resolve reviews (sub-agent) → classify risk → auto-merge (low/moderate, code-owner approval posted by the loop) or hand to a human (high). One item ⇒ at most one PR. A parallel batch (C-5) runs up to 3 items concurrently with footprint-overlap scheduling. |
| **BUDGET** | **Default drains the board** (`until-empty`), bounded by the stagnation breaker, the optional wall-clock `BEUTL_LOOP_MAX_MINUTES`, and a runaway backstop `BEUTL_LOOP_MAX_ITEMS` (default **50**). Pass an integer `N` for a tighter per-run budget. Per-PR review settle cap `BEUTL_LOOP_SETTLE_MINUTES` (default 20). Parallel batch size `BEUTL_LOOP_PARALLEL` (default 1 = sequential, max 3 — C-5). |
| **STOP** | Board drained · `items_processed ≥ N` (explicit budget or the runaway backstop) · stagnation (**3** consecutive no-progress with no PR in the last 3 ticks, 3 consecutive false-positives, or a repeated item / failure signature) · wall-clock exceeded · a guardrail would be violated. A single `blocked` item is recorded and skipped (not a stop); only repeated **systemic** blocks feed the no-progress breaker. |
| **REPORT** | A Markdown run summary + a JSON run summary (H-15) at `.claude/logs/beutl-loop-run-<run_id>.json` for cross-run trend comparison. Per item: PR, risk, merged/left-for-human, reviews resolved; plus false-positives, blocked, counters, stop reason. Optional Gist progress post (H-16). Then a reminder to run `/beutl-ai-self-review`. |

## Sub-agent isolation keeps the orchestrator's context lean

`/beutl-loop` runs as an **orchestrator** in the main session and **never implements items itself**.
Each tick it dispatches sub-agents and keeps only their compact JSON results plus the run journal:

- **Dispatch A — implement → draft (always).** The committed `beutl-board-task-runner` agent runs the
  `beutl-board-task` flow for one item in an **isolated git worktree** (`isolation: worktree`) and
  returns `{ draft_branch, commit_type, is_breaking, baseline_test_green, speckit_required, ... }`. It
  **always hands back a draft branch** (never opens the PR itself) so the orchestrator can run the
  pre-PR review round (machine-verify + reviewers) before the PR exists. For a large feature it
  returns `speckit_required` instead, and the orchestrator runs the Spec-Kit flow then re-dispatches.
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
2. **Select up to K items** (re-fetch the board, exclude already-attempted ids), across the full
   spectrum — do **not** pre-filter by risk or kind. The footprint-overlap scheduler (C-5) avoids
   picking parallel items that touch the same `src/` file or `tests/` project.
3. **Dispatch A** (parallel up to K) → each runner scans recent `Refs: Project #9` merges, implements,
   and passes two binary gates before handing back a **draft branch**: a **test gate** (a production
   change must ship an NUnit test + a green characterization baseline, or a documented
   manual-verification, else it is `blocked`) and a six-point **self-review gate**. Outcomes: a
   **false-positive advances the board**; a **draft** goes to the pre-PR review round (step 3.5);
   **blocked** is recorded and skipped — only a `systemic` block counts as no-progress, an
   `item-specific` one is neutral; **red / no draft ⇒ no-progress**. A **large feature**
   (`speckit_required`) goes to the Spec-Kit flow (step 3.6).
3.5. **Pre-PR review round** (always, on the draft): the orchestrator runs an **independent
   machine-verify** (grep for `[Obsolete]`/v2/TODO/Follow-ups, XAML `CompileBindings`, GPL/MIT hook —
   B-2, do not trust the runner's self-report) + `@beutl-reviewer` + `@beutl-xaml-binder` (+
   `@beutl-design-reviewer` when design-sensitive). Up to **two** rework iterations on the draft
   branch, then open the PR — high-risk to a human if findings are still unresolved after the budget
   (with a structured findings comment — F-12).
3.6. **Spec-Kit flow** (only for `speckit_required`): `/speckit-specify → plan → tasks`, then
   re-dispatch the runner with `tasks.md` → draft → step 3.5.
4. **Dispatch B** (bounded settle) → resolve bot reviews, including replying-and-resolving clear bot
   **false positives** with a `path:line` refutation (and recording the pattern to loop-memory — D-8);
   `needs_human` / red / timeout ⇒ leave for the human.
5. **Classify risk + merge** (below) — the loop **posts its own code-owner approval** then
   squash-merges low/mod-risk; a conditional **coverage probe** (B-4) gates auto-merge when
   `touched_production && diff_loc >= 100`.
6. **Journal** the outcome; recompute the stagnation counter (3 no-progress strikes, held open by a
   PR in the last 3 ticks); loop.

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

**Two upstream gates run inside Dispatch A, before the draft reaches the pre-PR review round** (so a
draft that reaches step 3.5 has already cleared them): the **test gate** — a production change
(`src/`) must add an NUnit test under `tests/` **and** a green characterization baseline
(`baseline_test_green`), or carry a concrete manual-verification note, else the runner returns
`blocked` rather than handing back a draft — and the six-point **self-review gate** (compiled XAML
bindings, no `[Obsolete]`/"v2"/compat-overload shim, no leftover `// TODO`/Follow-up, root-cause fix,
GPL/MIT boundary intact, subtree `CLAUDE.md` honored). As defense-in-depth, the orchestrator
**independently re-verifies the mechanical axes** (B-2) in step 3.5 — it does not trust the runner's
`self_review_passed`. If a runner hands back a draft whose own signals show a production change with
no test and no manual-verification note (or a missing/failed baseline), the orchestrator treats that
draft as high-risk (human) and the tick as no-progress.

## Auto-merge mechanism — GitHub rulesets enforce the gate; the loop self-gates on top

`main` is protected by **repository rulesets** (verify with
`gh api repos/b-editor/beutl/rules/branches/main`): a PR is required, the **required status checks
`build` and `dotnet-format`** must pass, **every review thread must be resolved**, **code-owner review
is required** (`.github/CODEOWNERS` is `* @yuto-trd`), only **squash** merges are allowed, commits must
be **signed**, and history must stay **linear**. GitHub enforces all of this server-side, so the loop is
**not** the only gate — its self-check is defense-in-depth that (a) applies the risk policy and (b)
avoids attempting a merge GitHub will refuse.

**The loop runs as the code owner.** All commits, PRs, reviews, and merges are performed from the
code-owner account (`@yuto-trd`), so the loop **can approve its own PRs and complete the squash merge**
without a second human in the loop — the code-owner review requirement is satisfied by the agent
acting as that owner. The loop therefore **does** auto-merge low/moderate-risk PRs end to end;
high-risk and uncertain PRs are still left for a human review pass. Changing CODEOWNERS or the rulesets
is a maintainer decision and is never done by the loop.

For a low/moderate-risk, settled PR the loop self-gates, **posts its own approval as the code owner**,
then **attempts** the squash merge pinned to the reviewed head, and treats a ruleset refusal as "leave
for human":

```bash
PR=<n>
HEAD_SHA=$(gh pr view "$PR" --json headRefOid -q .headRefOid)
gh pr checks "$PR"                                  # required checks (build, dotnet-format) must pass
# Unresolved review threads must be 0 (assumes ≤100; follow pageInfo for more — GitHub also enforces
# this, so the loop only checks to decide + avoid a futile merge attempt):
gh api graphql -f query='query{repository(owner:"b-editor",name:"beutl"){pullRequest(number:'"$PR"'){reviewThreads(first:100){nodes{isResolved}}}}}' \
  --jq '[.data.repository.pullRequest.reviewThreads.nodes[]|select(.isResolved==false)]|length'   # must be 0
gh pr view "$PR" --json mergeable,reviewDecision -q '.mergeable,.reviewDecision'                   # need MERGEABLE; if reviewDecision != APPROVED, post the code-owner approval first (below)
# Post the code-owner approval as the same account that authored the PR (the loop runs as @yuto-trd):
gh pr review "$PR" --approve --body "Auto-approved by /beutl-loop (code-owner). Risk: <low|moderate>."
# Move to Done ONLY if the merge actually succeeds; a ruleset refusal leaves the item In Progress.
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
every edit (never pushes red)**, and replies and resolves the handled threads. A **clear bot false
positive** (the bot misread code that already handles its concern) is answered with a neutral, factual
reply that cites the exact `path:line` and then resolved **without a code change** — but only when the
refutation is certain; an uncertain one **escalates** (`needs_human`) rather than guessing. Anything
needing product/architecture judgment likewise escalates, which pushes the PR to the human-merge path.
Run standalone it stays human-in-the-loop (per-comment `AskUserQuestion`), so a person can use it
safely without the autonomy.

## Relationship to `beutl-board-task`

`beutl-board-task` is **one tick**: pick → validate (a finding: not a false positive; a feature: not
already implemented & specified enough to verify) → claim → branch → implement →
test (a production change must ship a test) → self-review gate → PR (and, standalone, a human merges). `/beutl-loop` is the **meta-driver** that runs that tick
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
- **Optional periodic progress post (H-16).** `BEUTL_LOOP_PROGRESS_GIST=1` starts a background
  monitor that posts journal progress to a Gist every ~5 min, and posts the final JSON run summary
  (H-15) when the loop exits. `BEUTL_LOOP_PROGRESS_GIST_ID=<id>` reuses an existing Gist so you can
  watch one stable URL across runs. Off by default — the loop runs silently unless you opt in.

## Anti-loopmaxxing checklist

- [ ] Binary verification gate every tick (build + test exit-code + format + reviewer) — no green, no PR.
- [ ] Test gate: a production change ships an NUnit test or a documented manual-verification, else `blocked` — no untested PR.
- [ ] Six-point self-review gate before commit (XAML bindings, no compat shim, no leftover TODO, root-cause, GPL/MIT, subtree rules).
- [ ] Design-sensitive items take a bounded two-pass `@beutl-design-reviewer` review before the PR opens.
- [ ] Deterministic STOP conditions only; no "until done".
- [ ] Stagnation circuit-breaker (3 strikes, held open by a PR in the last 3 ticks; immediate on a repeat). A single `blocked` item is skipped, not a stop; only `systemic` blocks count.
- [ ] Bounded run: default drains the board, bounded by a runaway backstop (`BEUTL_LOOP_MAX_ITEMS`, default 50) + stagnation breaker + optional wall-clock; pass `N` for a tighter budget.
- [ ] Auto-merge is conservative, settled, low/moderate-risk only; uncertain ⇒ human.
- [ ] Heavy work in sub-agents; orchestrator keeps only structured results.
- [ ] No deferred work — each item is finished or explicitly `blocked`.

## Verifying changes to the loop itself

The loop is Markdown + an agent + a bash launcher — no compilable C#, so the NUnit requirement
(AGENTS.md rule #3) does not apply. Verify with:

1. **`bash .claude/scripts/loop-contract-check.sh`** — assert the invariants across SKILL.md /
   loop-engineering.md / runner JSON schema / beutl-loop.sh allowlist / .gitignore hold (G-13). Run
   this first; it catches drift mechanically.
2. **`bash .claude/scripts/loop-calibrate.sh`** — re-classify the last ~10 merged PRs through the
   loop's risk classifier and report drift vs the human merge decisions (G-14). Use the drift to
   tune the thresholds in SKILL.md step 4.
3. **`/beutl-loop dry-run 1`** — selects an item and prints its predicted risk tier, whether it is
   design-sensitive (would take the pre-PR review round), whether a production change would require
   a test, the merge decision, and the stop math — **without** claiming, dispatching, PRing,
   resolving, merging, or touching the board.
4. **`bash -n .claude/scripts/beutl-loop.sh`** and confirm it refuses to run on `main`.
5. **One live smoke test** (opens a real PR — run intentionally): `/beutl-loop 1` on a low-risk item
   should open a PR, resolve its **bot** reviews, post the code-owner approval, and **auto-squash-merge**
   the PR, then move the board item to Done and delete the branch. A **high-risk** or **feature** item
   is left open for a human. Stagnation check: against a slice of known false-positives the run stops
   after three consecutive false-positives (`stop_reason: stagnation`); against `systemic`-blocked /
   red items (with no PR opened in between), after three consecutive no-progress ticks. A single
   `item-specific`-blocked item is skipped without tripping the breaker.
