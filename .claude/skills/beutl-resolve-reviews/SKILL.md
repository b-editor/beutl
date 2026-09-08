---
description: |
  Fetch every review and review comment on a pull request (CodeRabbit, GitHub Copilot, Codex,
  Claude code-review, and humans), decide which genuinely need a code change, address the clearly
  actionable ones, then resolve the handled threads without posting review replies. Two modes: the default is human-in-the-loop
  (confirm each comment via AskUserQuestion, like the global handle-pr-reviews skill); pass `--auto`
  for autonomous resolution — used by /beutl-loop after it opens a PR. Use when the user says
  "PRレビューに対応", "レビュー指摘を反映してResolve", "address and resolve the PR reviews".
argument-hint: "[--auto --scope-state-file <absolute-path>] [no-resolve] [PR#]"
---

# Resolve PR reviews

Inspect all feedback on a PR, judge what needs a code change, address the clearly actionable items,
and resolve the handled threads. This is the committed Beutl counterpart of the global
`handle-pr-reviews` skill: it reuses the same fetch/thread/resolve mechanics but adds an **`--auto`**
mode so `/beutl-loop` can clear bot reviews without a human in the loop.

**This skill never merges.** Merging is a separate decision (the loop's risk policy, or a human).

## Modes

- **Default (interactive)** — for standalone human use. Classify each comment and confirm per-item
  via `AskUserQuestion` before changing code, exactly like `handle-pr-reviews`. The human has final
  authority; reviewers can be wrong or out of scope.
- **`--auto` (autonomous)** — for `/beutl-loop`. No per-comment confirmation (the loop run is the
  authorization). Apply the conservative auto-decision policy below, re-verify, resolve without replying, and
  return a structured result. **Escalate anything that needs judgment to the human instead of
  guessing.**

Other flags (both modes): `no-resolve` leaves threads open. This workflow never posts GitHub review
replies; its audit trail is the pushed commit, resolved thread state, and structured result.

## Step 1 — Identify the PR

```bash
# `author` is required: Step 3 drops the PR author's own comments, and in `--auto` they must not be
# misread as human feedback (which would force needs_human and disable the auto-resolve/merge path).
gh pr view ${PR:-} --json number,url,headRefName,headRefOid,baseRefName,title,state,mergeable,author
PR_NUMBER=$(gh pr view ${PR:-} --json number -q .number)
PR_AUTHOR=$(gh pr view ${PR:-} --json author -q .author.login)   # carried into Step 3 filtering
PR_HEAD_OID=$(gh pr view ${PR:-} --json headRefOid -q .headRefOid)
OWNER_REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)   # b-editor/beutl
git fetch origin "pull/$PR_NUMBER/head"
test "$(git rev-parse FETCH_HEAD)" = "$PR_HEAD_OID" || { echo "PR head changed while freezing scope" >&2; exit 1; }
```
If no PR number was given, use the PR for the current branch. If none exists, stop (interactive) or
return `{ "ok": false, "error": "no PR" }` (`--auto`) — the same `ok` flag the success object carries,
so the orchestrator can branch on it.

## Step 2 — Collect every form of feedback (run in parallel)

```bash
# Review summaries: APPROVED / CHANGES_REQUESTED / COMMENTED, with reviewer + body
# --paginate is required: on a busy PR an un-paginated fetch drops older reviews, and missing a human
# review summary would let --auto wrongly treat the PR as bot-only.
gh api "repos/$OWNER_REPO/pulls/<PR>/reviews" --paginate \
  --jq '[.[] | {id, user: .user.login, state, body, commit_id, submitted_at}]'

# Inline (line-anchored, threaded) comments
gh api "repos/$OWNER_REPO/pulls/<PR>/comments" --paginate \
  --jq '[.[] | {id, in_reply_to_id, user: .user.login, path, line, body, html_url, diff_hunk, commit_id, original_commit_id, created_at, updated_at}]'

# General PR (issue) comments
gh api "repos/$OWNER_REPO/issues/<PR>/comments" --paginate \
  --jq '[.[] | {id, user: .user.login, body, html_url, created_at, updated_at}]'

# Commit activity — the latest push timestamp is part of last_activity_at (Step 7), so fetch it
# explicitly. gh pr view --json commits includes each commit's committedDate.
gh pr view <PR> --json commits --jq '[.commits[].committedDate] | max'

# Thread ids + resolved state (to resolve later); map databaseId -> thread id.
# first:100 covers all but pathological PRs; if `pageInfo.hasNextPage` is true, follow the cursor.
gh api graphql -f query='query{repository(owner:"b-editor",name:"beutl"){pullRequest(number:<PR>){reviewThreads(first:100){pageInfo{hasNextPage endCursor} nodes{id isResolved comments(first:50){nodes{databaseId author{login} path line body}}}}}}}'
```

Reviewers include bots — `coderabbitai[bot]`, `github-copilot[bot]` /
`copilot-pull-request-reviewer[bot]`, the Codex reviewer, and the repo's `claude-code-review` output —
and **humans**. In **interactive** mode you weigh all non-author feedback the same way. In **`--auto`**
mode the distinction is load-bearing: **only the known bots above may be auto-addressed or
auto-resolved**; any **human** (non-author, non-bot) review or comment is **never** auto-handled — set
`needs_human` and leave its thread open (Step 5). Identify bots by the `[bot]` suffix / the known
logins; when unsure whether a login is a bot, treat it as **human** (fail safe).

## Step 3 — Reconstruct threads and drop noise

Group inline comments by **`in_reply_to_id ?? id`** — i.e. use the comment's own `id` as the thread
key when `in_reply_to_id` is null, so each top-level comment is its own thread root (grouping by bare
`in_reply_to_id` would collapse every top-level comment into one shared `null` group). The GraphQL
`reviewThreads` query (Step 2) already groups correctly server-side; this REST-side reconstruction
mirrors it. The **latest** comment in a thread is what matters. Skip: **comments whose author is
`$PR_AUTHOR`** (the PR author's / implementing agent's own notes, captured in Step 1 — never treat
these as human review feedback), pure approvals / praise / 👍, and threads already `isResolved: true`
or whose latest reply says done/fixed/resolved. Keep each GraphQL thread ID for Step 6 resolution.

## Step 4 — Classify (same taxonomy as handle-pr-reviews)

Before classifying severity, freeze the remediation boundary:

1. Work only from the PR head fetched and verified in Step 1. Never derive the boundary from the
   resolver's incoming checkout or from a branch name without matching it to `PR_HEAD_OID`.
2. Load the complete scope record from `--scope-state-file`. It contains `initial_head`,
   `previous_remediation_head`, `intended_behavior`, `affected_modules`, and `acceptance_tests`.
   In `--auto`, the loop must create this record in its own journal immediately after opening the PR,
   before the first review poll, and pass an absolute path to a copy outside the PR worktree. Load it
   before running any PR-controlled build or test and retain that in-memory value for the whole turn;
   never re-read a copy that PR code could have changed. The resolver returns the full record in Step
   7, and the orchestrator must reject any change to `initial_head` or the three frozen-scope arrays.
   Only `previous_remediation_head` advances after a remediation commit is successfully pushed.
3. If `--auto` has no exact full record, set `needs_human` and do not edit. The verified current head
   must equal `previous_remediation_head ?? initial_head`; ancestry alone is insufficient because an
   unverified actor can push an arbitrary descendant. Also require
   `git merge-base --is-ancestor <initial_head> "$PR_HEAD_OID"` as a history-integrity check. Commit
   IDs retained in Step 2 are evidence for
   classification, not enough to reconstruct the missing behavior/modules/tests. Interactive mode may
   create a new record only after showing all five derived fields to the user and receiving explicit
   confirmation; an unanswered prompt never creates or replaces it.
4. Classify every finding into exactly one scope class: **original-scope defect**,
   **remediation-cycle regression**, **pre-existing/adjacent issue**, **optional improvement**, or
   **acceptance gap**.

Only original-scope defects and regressions introduced by any remediation commit after `initial_head`
in the current review cycle are candidates for changes on the PR. `previous_remediation_head` remains
the integrity boundary; it does not narrow the set of remediation commits whose regressions are in scope.
The other three classes require an explicit user decision to reopen scope or create independent work.
The repository's "Do not defer work" rule applies only after a finding is inside this frozen boundary;
it never widens the boundary. A finding that adds a public-API change, project dependency, or newly
touched subsystem is scope expansion unless the frozen scope already names it.
An **acceptance gap** means a newly requested criterion that is absent from the frozen acceptance
tests. Failure of a criterion already recorded in `acceptance_tests` is an original-scope defect,
not an acceptance gap.

| Category | Default action |
|---|---|
| **Bug / correctness** ("throws on empty", "race here") | Address |
| **Change request** ("rename this", "extract helper") | Address (if mechanical/clear) |
| **Style / nit** | Address (cheap) |
| **Question** | Resolve without code change when the answer is verified; otherwise escalate |
| **Clear false positive** (bot misread the code; the concern does not hold) | Resolve with no reply and **no code change** |
| **Out of scope / opinion / architecture call** | Do NOT auto-address — escalate |
| **Praise / discussion** | Ignore |

Always **read the cited code** (`path`,`line`) before classifying — a "nit" can hide a real bug.
**Read it from the PR head, not a stale checkout.** Fetch the PR head ref so classification (and any
later fix) reads the same head. **Fetch by the PR head ref** (`refs/pull/<PR>/head`) so this is
correct for **fork / cross-repository** PRs too — `headRefName` is only the source branch name and
`origin/<headRefName>` would resolve to a same-named branch in the base repo (or fail):
```bash
HEAD_REF=$(gh pr view ${PR:-} --json headRefName -q .headRefName)   # same-repo push target (Step 5)
IS_FORK=$(gh pr view ${PR:-} --json isCrossRepository -q .isCrossRepository)
git fetch origin "pull/$PR_NUMBER/head"
test "$(git rev-parse FETCH_HEAD)" = "$PR_HEAD_OID" || { echo "PR head changed during review" >&2; exit 1; }

# Stay on the named branch when possible so the working tree is not left detached after the skill.
# For fork PRs, do NOT use `git checkout "$HEAD_REF"` — headRefName is not globally unique and can
# resolve to a same-named base-repo branch. For fork PRs, always use detached HEAD from FETCH_HEAD.
# For same-repo PRs, try to stay on or switch to the PR branch, falling back to --detach only when
# another worktree owns the branch.
CURRENT_BRANCH=$(git branch --show-current)
if [ "$IS_FORK" = "true" ]; then
    # Fork PR: always detached from FETCH_HEAD (cannot push anyway — see below)
    git checkout --detach FETCH_HEAD
elif [ "$CURRENT_BRANCH" = "$HEAD_REF" ]; then
    # Already on the PR branch — fast-forward to fetched head; if the local branch
    # diverged and cannot fast-forward, fall back to detached HEAD from FETCH_HEAD
    if ! git merge --ff-only FETCH_HEAD; then
        git checkout --detach FETCH_HEAD
    fi
else
    # Try to switch to the PR branch and fast-forward; if either step fails
    # (e.g. another worktree owns the branch, or the local branch diverged),
    # fall back to detached HEAD. Do not suppress stderr.
    if git checkout "$HEAD_REF" && git merge --ff-only FETCH_HEAD; then
        :
    else
        git checkout --detach FETCH_HEAD
    fi
fi
# A local branch that is ahead of the fetched PR head must not carry its extra commits into the
# remediation. Work from the verified object even when `merge --ff-only FETCH_HEAD` reported
# "Already up to date" for that local descendant.
if [ "$(git rev-parse HEAD)" != "$PR_HEAD_OID" ]; then
    git checkout --detach FETCH_HEAD
fi
test "$(git rev-parse HEAD)" = "$PR_HEAD_OID" || {
    echo "Checked-out head does not match the verified PR head" >&2
    exit 1
}
# Track whether we ended up detached for Step 5 push logic
IS_DETACHED=$(git symbolic-ref -q --short HEAD >/dev/null 2>&1 && echo false || echo true)
```
**Pushing back is only valid for a SAME-REPO PR.** Check `gh pr view $PR --json isCrossRepository`: if
`isCrossRepository` is true (a **fork** PR), do **not** push a fix — `origin` is the base repo, so
`git push origin HEAD:<headRefName>` would create/update a same-named *base* branch, not the PR's fork
head. Escalate (`needs_human`) for any cross-repo edit, even when `maintainerCanModify` is true
(pushing to the fork head needs its own remote/repo and is out of scope here). You may still resolve
no-code threads on a fork PR; you just don't push code. /beutl-loop only ever resolves its own
same-repo PRs, so this matters mainly for standalone/interactive use on fork PRs.

## Step 5 — Act

**You are on the PR head branch** (checked out in Step 4 — either you were already on it, or you
switched to it). Commit your fixes directly on the branch and push normally:

```bash
# (HEAD_REF + checkout were done in Step 4; IS_DETACHED tracks the resulting state)
# SAME-REPO ONLY — for a cross-repo (fork) PR, do not push; escalate (see Step 4).
# … apply review-finding fixes, re-verify, commit -S …
if [ "$IS_DETACHED" = "true" ]; then
    git push origin "HEAD:$HEAD_REF"   # detached: push to the PR branch by refspec
else
    git push origin "$HEAD_REF"        # on the branch: normal push
fi
# After the push succeeds, record `pushed_commit_sha=$(git rev-parse HEAD)` in the result. The
# orchestrator independently fetches the PR head and requires exact equality before accepting this
# SHA as a scope advancement. Never derive it from the moving remote head after the push.
```

### Interactive mode
Ask `AskUserQuestion` per candidate (reviewer, file:line, verbatim body, your read, `html_url`). For an
in-scope finding, offer Address / Resolve without code / Skip / Address differently. For a pre-existing,
adjacent, optional, or newly requested acceptance finding, create independent work automatically;
never widen the reviewed PR by default. One decision per in-scope comment. If the question is
dismissed or unanswered, follow the recommended action: Address for an actionable in-scope defect,
Resolve without code for a verified false positive, and Create independent work for anything outside
the frozen scope.

When the user selects **Create independent work**, create a GitHub Issue that records the source PR
and review URL, the separate scope, and its acceptance criteria. Do not edit or commit anything on
the current PR branch; after the Issue is created successfully, resolve the source review thread
without replying so it no longer blocks the current PR. A later implementation must fetch
`origin/main`, create a new feature branch directly from that commit, and open a PR whose base is
`main`; never base it on the reviewed PR or make it a stacked PR. Creating the Issue records the
separate work but does not authorize starting that implementation.

### `--auto` mode — conservative auto-decision
- **Bots only.** Auto-address / auto-resolve only feedback from the known bot reviewers. **Any human
  review or comment → set `needs_human` and leave the thread open**; do not edit code for it and do
  not resolve it. (A human's unresolved thread keeps the PR off the auto-merge path, which is the
  point — a person decides on human feedback.)
- **Auto-address only the clearly actionable + low-judgment code changes:** bug/correctness, straightforward
  mechanical change-requests, and nits. Make the **smallest** change that resolves the comment; do
  **not** expand scope because a reviewer mused about a broader refactor. The finding must also be an
  original-scope defect or a remediation-cycle regression. Pre-existing/adjacent issues, optional
  improvements, and acceptance gaps always set `needs_human` and stay open.
- This scope-class restriction gates edits only. It does not block the no-code handling below for a
  question or a demonstrable bot false positive, even when the cited code is adjacent.
- **Questions:** resolve without replying if the answer is verified from the code; otherwise escalate.
- **Clear bot false positives:** when a **known bot's** comment is demonstrably wrong (you can point
  to the exact `path:line` that already handles the concern), **resolve** the thread without replying
  and with **no code change**. Record the concrete refutation in the structured result. If you are not certain it is a false
  positive, **escalate** (`needs_human`) instead of resolving. Count each one in
  `false_positives_resolved`. (Never do this for human comments — those always escalate.) **Append
  the pattern to `.claude/loop-memory/bot-false-positive-patterns.md`** (D-8): one line per pattern —
  `<bot> | <path:line> | <what the bot misread> | <refutation cite path:line>`. This lets future runs
  and the reviewers (`beutl-reviewer`, `beutl-xaml-binder`) avoid re-tripping the same bot blind spot.
  `mkdir -p .claude/loop-memory` first if it does not exist.
- **Out-of-scope / opinion / anything needing product or architecture judgment, or that would
  enlarge the diff materially:** do **not** touch. Leave the thread open and mark `needs_human`.
- **After any edit, re-verify before pushing — this is mandatory:**
  ```bash
  dotnet build Beutl.slnx                       # all projects at their declared TFMs — never force -f net10.0:
                                                # Beutl.Engine.SourceGenerators / the Extensibility SDK are
                                                # netstandard2.0-only and other projects also build net10.0-windows;
                                                # forcing one TFM falsely reds the build or skips the Windows target. Must be clean.
  dotnet test <affected Tests.csproj> -f net10.0 --filter "FullyQualifiedName~<area>"   # gate on $?
  dotnet format Beutl.slnx --include <changed files> --verify-no-changes
  ```
  Decide pass/fail from the **exit code**, never a console string. If the change goes red and you
  cannot fix it minimally, **revert that edit**, leave the thread open, and set `needs_human`. Never
  push red.
- **Re-run the production-change test gate on the review-fix diff (`--auto`).** A bot-requested fix can
  add new `src/` logic after the runner's gate already passed. If the review-fix diff
  (`git diff origin/main...HEAD`) touches production code under `src/` but adds/modifies no test under
  `tests/` (and is not a documented manual-verification), it would slip untested logic into a PR the
  loop later auto-merges. Add the test in the same fix, or — if you cannot — **revert the edit and
  escalate (`needs_human`)** rather than pushing untested production code.
- Commit addressed changes **signed** (`git commit -S -m "fix(review): …"` — the `main` ruleset
  requires signed commits, and the repo config signs by default; never `--no-gpg-sign`, or the pushed
  fix leaves the PR unmergeable) and push to the PR head (see Step 5 preamble for the branch/detached
  push commands). Never force-push; never push `main`.

## Step 6 — Recheck the head and resolve handled threads

Immediately before changing each thread state, verify that the live PR head is still the object that
was classified and handled. Use the locally created pushed SHA after a code change, otherwise the
head captured in Step 1. If it differs, stop resolving, set `needs_human`, and report the race.

```bash
EXPECTED_HEAD=${pushed_commit_sha:-$PR_HEAD_OID}
for THREAD_ID in "${HANDLED_THREAD_IDS[@]}"; do
    LIVE_HEAD=$(gh pr view "$PR_NUMBER" --json headRefOid -q .headRefOid)
    test "$LIVE_HEAD" = "$EXPECTED_HEAD" || {
        echo "PR head changed before thread resolution" >&2
        exit 1
    }
    gh api graphql -f query="mutation{resolveReviewThread(input:{threadId:\"$THREAD_ID\"}){thread{isResolved}}}"
done
```

For each thread you addressed (or verified as a no-code resolution), resolve it without posting a
reply (skip resolution when `no-resolve`):

**Review-thread comments** (inline, from `pulls/<PR>/comments`) use the guarded loop above.

**General PR (issue) comments** (from `issues/<PR>/comments`, Step 2) have **no review thread** — they
are not line-anchored and have no `THREAD_ID`. Do not reply and do not attempt
`resolveReviewThread`; report them as still open when they need follow-up.

Resolve a review thread once even if it had several handled comments. **Never resolve threads you escalated
(`needs_human`) or skipped.**

## Step 7 — Report

**Interactive:** summarize Addressed / Skipped / Resolved / Still-open (like
handle-pr-reviews). Note that no merge was performed.

**`--auto`:** return EXACTLY this JSON and nothing after it:

```json
{
  "ok": true,
  "pr_number": 0,
  "threads_total": 0,
  "threads_resolved": 0,
  "false_positives_resolved": 0,
  "false_positive_refutations": [
    {
      "thread_id": "<graphql-thread-id>",
      "path": "<repository-relative-path>",
      "line": 0,
      "reason": "<verified factual refutation>"
    }
  ],
  "question_resolutions": [
    {
      "thread_id": "<graphql-thread-id>",
      "path": "<repository-relative-path-or-null>",
      "line": 0,
      "answer": "<verified factual answer>"
    }
  ],
  "unresolved": 0,
  "changes_requested_outstanding": false,
  "human_feedback_present": false,
  "needs_human": false,
  "needs_human_reasons": [],
  "new_commits_pushed": 0,
  "pushed_commit_sha": "<locally-created-pushed-sha-or-null>",
  "post_fix_test_status": "green | none | red",
  "ci_status": "green | red | pending | unknown",
  "scope_state": {
    "initial_head": "<sha>",
    "previous_remediation_head": "<sha-or-null>",
    "intended_behavior": ["<frozen item>"],
    "affected_modules": ["<frozen module>"],
    "acceptance_tests": ["<frozen test or criterion>"]
  },
  "last_activity_at": "<ISO-8601 of the most recent review / comment / commit on the PR>"
}
```
- `false_positives_resolved`: how many **bot** threads you resolved as clear false positives (with a
  factual `path:line` refutation recorded in the result, no reply and no code change). A subset of
  `threads_resolved`.
- `false_positive_refutations`: one entry per silently resolved false positive. Record the GraphQL
  thread ID, exact repository-relative path and line checked at the verified head, and the factual
  reason the finding does not apply. Use an empty array when none were resolved.
- `question_resolutions`: one entry per silently resolved question. Record the thread ID, source
  location when present, and the verified factual answer. Use an empty array when none were resolved.
- `pushed_commit_sha`: the exact local commit created and successfully pushed by this resolver, or
  `null` when `new_commits_pushed == 0`. The orchestrator verifies it against a fresh PR-head fetch;
  never reconstruct it from `scope_state` or a moving branch ref.
- `human_feedback_present`: true if any non-author **human** review or comment exists (always forces
  `needs_human`, since `--auto` never handles human feedback).
- `last_activity_at`: lets the orchestrator compute the "quiet period" (no new activity for ~10 min)
  without re-deriving it; report the **latest** of: the newest review `submitted_at`, the newest inline
  comment `updated_at`, the newest issue comment `updated_at`, and the latest commit `committedDate`
  (all fetched in Step 2). Because every source now carries a real timestamp, this value is reliable —
  the orchestrator still re-derives it itself (see the loop skill's step 3) as the authoritative
  quiet-period clock, treating this field as advisory (consistent with `ci_status` / counts).
- `changes_requested_outstanding`: derive from the **final** PR state, not thread resolution alone —
  resolving threads does not flip a `CHANGES_REQUESTED` review to approved (GitHub needs a re-review).
  Set it true if `gh pr view <PR> --json reviewDecision` is `CHANGES_REQUESTED`, or the latest review
  from any reviewer is `CHANGES_REQUESTED`. `ci_status` likewise reports the freshest `gh pr checks`
  state. (The orchestrator re-reads both itself; these fields are advisory.)
- `needs_human`: true if anything was escalated, went red, or a CHANGES_REQUESTED could not be
  cleanly resolved. The orchestrator treats `needs_human: true` as **high-risk → human merge**.

## Rules

- **Never merge** (no `gh pr merge`). **Never force-push** / push `main`.
- **`--auto` handles bot feedback only.** Human reviews/comments are never auto-addressed or
  auto-resolved — they set `needs_human` and stay open for a person. Unsure if a login is a bot →
  treat as human.
- **`--auto` re-verifies after every edit and never pushes red.** Build/test/format are the binary
  gates.
- **Escalate over guess.** In `--auto`, when a comment needs judgment, mark `needs_human` rather than
  applying a speculative fix — fail safe to the human.
- **Smallest change that resolves the comment.** Do not enlarge scope from a review remark.
- **Frozen scope wins.** "Do not defer" never authorizes a new public API, project dependency, or
  subsystem during review cleanup. Missing user input means no scope expansion.
- **Resolve only what you actually handled.** Escalated/skipped threads stay open.
