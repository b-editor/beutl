---
description: |
  Fetch every review and review comment on a pull request (CodeRabbit, GitHub Copilot, Codex,
  Claude code-review, and humans), decide which genuinely need a code change, address the clearly
  actionable ones, then reply and resolve the threads. Two modes: the default is human-in-the-loop
  (confirm each comment via AskUserQuestion, like the global handle-pr-reviews skill); pass `--auto`
  for autonomous resolution — used by /beutl-loop after it opens a PR. Use when the user says
  "PRレビューに対応", "レビュー指摘を反映してResolve", "address and resolve the PR reviews".
argument-hint: "[--auto] [reply | no-resolve] [PR#]"
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
  authorization). Apply the conservative auto-decision policy below, re-verify, reply + resolve, and
  return a structured result. **Escalate anything that needs judgment to the human instead of
  guessing.**

Other flags (both modes): `no-resolve` leaves threads open; in interactive mode `reply` also posts
GitHub replies (in `--auto`, replying on handled threads is always on, for the audit trail).

## Step 1 — Identify the PR

```bash
# `author` is required: Step 3 drops the PR author's own comments, and in `--auto` they must not be
# misread as human feedback (which would force needs_human and disable the auto-resolve/merge path).
gh pr view ${PR:-} --json number,url,headRefName,baseRefName,title,state,mergeable,author
PR_AUTHOR=$(gh pr view ${PR:-} --json author -q .author.login)   # carried into Step 3 filtering
OWNER_REPO=$(gh repo view --json nameWithOwner -q .nameWithOwner)   # b-editor/beutl
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
  --jq '[.[] | {id, user: .user.login, state, body, submitted_at}]'

# Inline (line-anchored, threaded) comments
gh api "repos/$OWNER_REPO/pulls/<PR>/comments" --paginate \
  --jq '[.[] | {id, in_reply_to_id, user: .user.login, path, line, body, html_url, diff_hunk}]'

# General PR (issue) comments
gh api "repos/$OWNER_REPO/issues/<PR>/comments" --paginate \
  --jq '[.[] | {id, user: .user.login, body, html_url}]'

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

Group inline comments by `in_reply_to_id`; the **latest** comment in a thread is what matters. Skip:
**comments whose author is `$PR_AUTHOR`** (the PR author's / implementing agent's own notes, captured
in Step 1 — never treat these as human review feedback), pure approvals / praise / 👍, and threads
already `isResolved: true` or whose latest reply says done/fixed/resolved. Keep the
`{thread id ↔ comment databaseId}` map for Step 6.

## Step 4 — Classify (same taxonomy as handle-pr-reviews)

| Category | Default action |
|---|---|
| **Bug / correctness** ("throws on empty", "race here") | Address |
| **Change request** ("rename this", "extract helper") | Address (if mechanical/clear) |
| **Style / nit** | Address (cheap) |
| **Question** | Reply, no code change |
| **Clear false positive** (bot misread the code; the concern does not hold) | Reply with a factual refutation citing `path:line`, then resolve — **no code change** |
| **Out of scope / opinion / architecture call** | Do NOT auto-address — escalate |
| **Praise / discussion** | Ignore |

Always **read the cited code** (`path`,`line`) before classifying — a "nit" can hide a real bug.

## Step 5 — Act

### Interactive mode
Ask `AskUserQuestion` per candidate (reviewer, file:line, verbatim body, your read, `html_url`).
Offer Address / Reply only / Skip / Address differently. One decision per comment; never change code
without an explicit "Address it".

### `--auto` mode — conservative auto-decision
- **Bots only.** Auto-address / auto-resolve only feedback from the known bot reviewers. **Any human
  review or comment → set `needs_human` and leave the thread open**; do not edit code for it and do
  not resolve it. (A human's unresolved thread keeps the PR off the auto-merge path, which is the
  point — a person decides on human feedback.)
- **Auto-address only the clearly actionable + low-judgment:** bug/correctness, straightforward
  mechanical change-requests, and nits. Make the **smallest** change that resolves the comment; do
  **not** expand scope because a reviewer mused about a broader refactor.
- **Questions:** post a brief factual reply if it is answerable from the code; otherwise escalate.
- **Clear bot false positives:** when a **known bot's** comment is demonstrably wrong (you can point
  to the exact `path:line` that already handles the concern), post a **neutral, factual** reply that
  cites that `path:line` to refute it, then **resolve** the thread — with **no code change**. The
  refutation must quote concrete code, not a general assurance. If you are not certain it is a false
  positive, **escalate** (`needs_human`) instead of resolving. Count each one in
  `false_positives_resolved`. (Never do this for human comments — those always escalate.)
- **Out-of-scope / opinion / anything needing product or architecture judgment, or that would
  enlarge the diff materially:** do **not** touch. Leave the thread open and mark `needs_human`.
- **After any edit, re-verify before pushing — this is mandatory:**
  ```bash
  dotnet build Beutl.slnx -f net10.0            # must be clean
  dotnet test <affected Tests.csproj> -f net10.0 --filter "FullyQualifiedName~<area>"   # gate on $?
  dotnet format Beutl.slnx --include <changed files> --verify-no-changes
  ```
  Decide pass/fail from the **exit code**, never a console string. If the change goes red and you
  cannot fix it minimally, **revert that edit**, leave the thread open, and set `needs_human`. Never
  push red.
- Commit addressed changes (`fix(review): …`) and `git push` the existing PR branch. Never
  force-push; never push `main`.

## Step 6 — Reply + resolve handled threads

For each thread you addressed (or answered), post a short factual reply, then resolve it (skip both
when `no-resolve`):

```bash
gh api "repos/$OWNER_REPO/pulls/<PR>/comments/<comment_id>/replies" -f body="Done — <one-line fix>."
gh api graphql -f query='mutation{resolveReviewThread(input:{threadId:"<THREAD_ID>"}){thread{isResolved}}}'
```
Resolve a thread once even if it had several handled comments. **Never resolve threads you escalated
(`needs_human`) or skipped.** Keep replies neutral and factual; do not argue with reviewers.

## Step 7 — Report

**Interactive:** summarize Addressed / Replied / Skipped / Resolved / Still-open (like
handle-pr-reviews). Note that no merge was performed.

**`--auto`:** return EXACTLY this JSON and nothing after it:

```json
{
  "ok": true,
  "pr_number": 0,
  "threads_total": 0,
  "threads_resolved": 0,
  "false_positives_resolved": 0,
  "unresolved": 0,
  "changes_requested_outstanding": false,
  "human_feedback_present": false,
  "needs_human": false,
  "needs_human_reasons": [],
  "new_commits_pushed": 0,
  "post_fix_test_status": "green | none | red",
  "ci_status": "green | red | pending | unknown",
  "last_activity_at": "<ISO-8601 of the most recent review / comment / commit on the PR>"
}
```
- `false_positives_resolved`: how many **bot** threads you resolved as clear false positives (a factual
  `path:line` refutation reply, no code change). A subset of `threads_resolved`.
- `human_feedback_present`: true if any non-author **human** review or comment exists (always forces
  `needs_human`, since `--auto` never handles human feedback).
- `last_activity_at`: lets the orchestrator compute the "quiet period" (no new activity for ~10 min)
  without re-deriving it; report the latest of the newest review, comment, or pushed commit.
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
- **Resolve only what you actually handled.** Escalated/skipped threads stay open.
