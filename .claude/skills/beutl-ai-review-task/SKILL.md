---
description: |
  Pick up a non-feature task (bug / quality / design-improvement) from the GitHub
  Project #9 "AI Review" board, verify it is not a false positive, then plan, implement,
  test, and open a PR. Use when the user says "AI Review からタスクを選んで作業して",
  "projects/9 のタスクをやって", "pick a task from the AI Review board", "consume an
  AI-review backlog item", or similar. If the chosen item turns out to be a false positive,
  set its Status to "False positive" and move to the next candidate.
allowed-tools: Bash(gh:*) Bash(dotnet:*) Bash(git:*) Bash(./build.sh:*)
argument-hint: "[item-number | title-keyword | bug|diff|design]"
---

# Work an AI Review board task

The `scheduled-code-review.yml` and `claude-code-review.yml` workflows file findings into the
GitHub Project **#9 "AI Review"** (`b-editor/projects/9`). This skill turns one of those
findings into a merged-ready PR, end to end. It deliberately targets **non-feature** work
(bugs, perf/quality, design improvements) — those are concrete, verifiable, and low-risk.

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

```bash
# Full board with status + type + title
gh project item-list 9 --owner b-editor --limit 100 --format json \
  | python3 -c "
import json,sys
d=json.load(sys.stdin)
for i,it in enumerate(d['items']):
    c=it.get('content',{})
    print(i,'|',it.get('status','?'),'|',c.get('type','?'),'|',c.get('title','')[:90])
"
```

**Non-feature filter.** Prefer titles prefixed with `[Bug]`, `[YYYY-MM-DD][diff]`, or
`設計改善:`. Skip plain feature titles (e.g. "リップル削除", "マルチカム編集", "ショートカット: ...").
Skip items already `Done` / `False positive`. Among open ones (`Backlog` / `Todo` / `In Progress`),
bias toward **engine/core, low-risk, high-confidence, testable** items over UI-layer items that
are hard to unit-test.

If `$ARGUMENTS` is an index/number or a title keyword, jump straight to that item. Otherwise,
present the top non-feature candidates to the user with AskUserQuestion before committing to one
(unless the user already told you to just pick one).

Read the chosen item's full body — it carries the exact file:line and a suggested fix:

```bash
gh project item-list 9 --owner b-editor --limit 100 --format json \
  | python3 -c "
import json,sys
i=$INDEX
it=json.load(sys.stdin)['items'][i]; print(it['content']['title']); print(); print(it['content'].get('body',''))
"
```

## Step 2 — Verify it is NOT a false positive (mandatory)

Before any planning, confirm the finding against the **current** code:

- Open the cited `file:line` and confirm the described code still exists and the reasoning holds.
- Trace the claim. Scheduled-review findings are heuristic and sometimes wrong. Common false
  positives seen on this board:
  - A "use-after-dispose / NRE" where an eager token / guard already aborts the path.
  - A "race" on state that is in practice single-threaded, or already guarded elsewhere.
  - A perf claim on a path that is not actually hot.
- If the surrounding code makes the finding **invalid**, mark it and move on:

```bash
# Set Status -> "False positive"
gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 \
  --id <ITEM_ID> \
  --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk \
  --single-select-option-id e6ff360e
```

Get `<ITEM_ID>` from the `id` field of the chosen item in the JSON above. Then return to Step 1
for another candidate. **Do not** silently keep the smaller diff or fabricate a fix for a wrong
finding.

## Step 3 — Plan

Enter plan mode (`EnterPlanMode`) and produce a focused plan. Include:

- **Context**: why the change is needed (the real problem, quoted from the finding + your verification).
- A note that the item was confirmed **not** a false positive, with the evidence.
- The concrete edit(s): `file:line` and the before/after shape.
- A **test** plan (see Step 5) — this repo requires new logic to ship with an NUnit test
  (`.claude/rules/csharp.md`, AGENTS.md rule #3).
- Verification commands and the commit/PR/board updates.

Surface non-obvious design trade-offs to the user (AGENTS.md "adopt better designs eagerly").
For public-surface / extensibility changes, auto-delegate `@beutl-design-reviewer`.

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

Tests are long-running — prefer launching them with `run_in_background: true` and polling the
output file with an `until grep -qE "成功!|失敗!|error " "$f"; do sleep 3; done` loop.

## Step 6 — Commit, push, PR

Use Conventional Commits matching the change kind (`perf:` / `fix:` / `refactor:` / `refactor!:`).
Reference the board item in the body. Example:

```bash
git add <changed files>
git commit -F - <<'EOF'
perf(engine): <imperative summary>

<what was wrong + why the fix is safe + behavior-preserving note>

Refs: Project #9 "AI Review" item "<exact finding title>"
EOF
git push -u origin <feature-branch>     # never push to main/master
gh pr create --base main --head <feature-branch> \
  --title "<conventional-commit title>" \
  --body "<filled-in .github/PULL_REQUEST_TEMPLATE.md>"
```

Fill the PR template's **Affected areas**, **Breaking changes** (`None` for behavior-preserving),
**Test plan** (mention the baseline-first verification + pass counts), and **References**
(`Project board: b-editor/projects/9 ("<title>")`). Opening the PR triggers the automatic
`claude-code-review.yml` review.

## Step 7 — Update the board

Move the item `Backlog` → `In Progress` once the PR is open (and to `Done` after merge):

```bash
gh project item-edit --project-id PVT_kwDOBLw8Fs4BW4g5 \
  --id <ITEM_ID> \
  --field-id PVTSSF_lADOBLw8Fs4BW4g5zhSJTXk \
  --single-select-option-id 47fc9ee4   # In Progress  (98236657 = Done)
```

## Guardrails

- **Confirm outward-facing actions.** Push / PR / board edits change shared state — confirm with
  the user (AskUserQuestion) before doing them unless they already authorized it this turn.
- One task per invocation by default. After finishing, offer the next candidate rather than
  batching many.
- Never force-push to `main`/`master` (hook-enforced). Work on a feature branch.
- Re-discover IDs if the project schema changed:
  ```bash
  gh project field-list 9 --owner b-editor --format json   # field + option ids
  gh project list --owner b-editor                          # project ids
  ```
