#!/usr/bin/env bash
#
# loop-contract-check.sh — assert invariants across the loop's contract artifacts.
#
# The loop is Markdown + agents + helper scripts — no compilable C#, so the NUnit
# requirement (AGENTS.md rule #3) does not apply. This script is the mechanical
# substitute: it checks that the loop's contract artifacts stay in sync. Run it after
# any edit to:
#   .claude/skills/beutl-loop/SKILL.md
#   docs/ai-workflow/loop-engineering.md
#   .claude/agents/beutl-board-task-runner.md
#   .claude/skills/beutl-resolve-reviews/SKILL.md
#   .claude/agents/beutl-reviewer.md
#   .claude/agents/beutl-xaml-binder.md
#   .gitignore
#
# Usage: bash .claude/scripts/loop-contract-check.sh
# Exit: 0 = all invariants hold; 1 = one or more drifted.
#
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "$(cd "$(dirname "$0")/../.." && pwd)")"
cd "$REPO_ROOT"

SKILL=".claude/skills/beutl-loop/SKILL.md"
DOC="docs/ai-workflow/loop-engineering.md"
RUNNER=".claude/agents/beutl-board-task-runner.md"
RESOLVER=".claude/skills/beutl-resolve-reviews/SKILL.md"
REVIEWER=".claude/agents/beutl-reviewer.md"
XAML_BINDER=".claude/agents/beutl-xaml-binder.md"
DESIGN_REVIEWER=".claude/agents/beutl-design-reviewer.md"
GITIGNORE=".gitignore"

fails=0
pass() { printf '  ok  %s\n' "$1"; }
fail() { printf '  FAIL %s\n' "$1" >&2; fails=$((fails + 1)); }
# Pure predicate: callers decide pass/fail, so it must not increment `fails`
# itself (otherwise `have && pass || fail` double-counts a missing file).
have() { [ -f "$1" ]; }

echo "loop-contract-check — verifying invariants across the loop artifacts"

# --- 1. All artifacts exist -----------------------------------------------
for f in "$SKILL" "$DOC" "$RUNNER" "$RESOLVER" "$REVIEWER" "$XAML_BINDER" "$GITIGNORE"; do
  have "$f" && pass "exists: $f" || fail "exists: $f"
done

# --- 2. Stagnation threshold "3" is consistent ----------------------------
# Both SKILL.md and loop-engineering.md must say "3" for the no-progress breaker.
if grep -q 'consecutive_no_progress ≥ 3' "$SKILL" 2>/dev/null && \
   grep -q 'consecutive no-progress' "$DOC" 2>/dev/null; then
  pass "stagnation threshold = 3 (SKILL + doc)"
else
  fail "stagnation threshold drift: SKILL.md / loop-engineering.md disagree on '3'"
fi

# --- 3. Runner JSON schema has the new mandatory fields -------------------
for field in baseline_test_green speckit_required; do
  if grep -q "\"$field\"" "$RUNNER" 2>/dev/null; then
    pass "runner JSON has '$field'"
  else
    fail "runner JSON missing '$field'"
  fi
done

# --- 4. Runner declares "always hands back a draft" -----------------------
if grep -q 'always hands back a draft' "$SKILL" 2>/dev/null && \
   grep -qi 'hand back a draft.*always\|always.*hand back a draft' "$RUNNER" 2>/dev/null; then
  pass "runner + skill agree: always draft (never opens PR itself)"
else
  fail "draft-always contract drift between SKILL.md and runner"
fi

# --- 5. Code-owner approval posture is consistent -------------------------
# Both must say the loop posts its own approval (runs as code owner).
if grep -q 'posts its own approval\|posts the code-owner approval\|post the code-owner approval' "$SKILL" 2>/dev/null && \
   grep -q 'posts its own approval\|post the code-owner approval' "$DOC" 2>/dev/null; then
  pass "code-owner approval posture (SKILL + doc)"
else
  fail "code-owner approval posture drift between SKILL.md and loop-engineering.md"
fi

# --- 6. SKILL.md references steps 0–5 (renumbered) ------------------------
# After the step renumber, the journal update is step 5 (was step 6).
if grep -q '### 5\. Update the journal' "$SKILL" 2>/dev/null && \
   ! grep -q '### 6\. Update the journal' "$SKILL" 2>/dev/null; then
  pass "step numbering: journal update is step 5"
else
  fail "step numbering drift: expected '### 5. Update the journal', no '### 6.'"
fi

# --- 7. In-session execution model (no headless claude -p launcher) -------
# The loop runs in-session on opus with acceptEdits; the headless launcher was removed
# because `claude -p` bills as metered API usage. Guard against its reintroduction.
if [ -f ".claude/scripts/beutl-loop.sh" ]; then
  fail "headless launcher .claude/scripts/beutl-loop.sh is back — the loop is in-session only (no claude -p)"
else
  pass "no headless launcher (in-session only)"
fi
# Catch an actual reintroduced invocation (e.g. `claude -p "/beutl-loop ...`), not the prose that
# explains the removal (which legitimately mentions the words "claude -p" in backticks).
if grep -rqE 'claude +-p +"' "$SKILL" "$DOC" 2>/dev/null; then
  fail "SKILL/doc contain a real 'claude -p \"...\"' invocation (headless model must stay removed)"
else
  pass "no headless claude -p invocation in SKILL + doc"
fi
if grep -q 'in-session' "$SKILL" 2>/dev/null; then
  pass "SKILL documents the in-session execution model"
else
  fail "SKILL no longer documents the in-session execution model"
fi
if grep -q '^model: opus' "$RUNNER" 2>/dev/null; then
  pass "runner runs on opus"
else
  fail "runner model drift: beutl-board-task-runner must be 'model: opus'"
fi
if grep -q 'permissionMode: acceptEdits' "$RUNNER" 2>/dev/null; then
  pass "runner uses acceptEdits (no per-edit stalls in a long run)"
else
  fail "runner permissionMode drift: expected acceptEdits"
fi

# --- 8. resolve-reviews writes bot-false-positive patterns to loop-memory -
if grep -q 'bot-false-positive-patterns.md' "$RESOLVER" 2>/dev/null; then
  pass "resolver writes bot-false-positive patterns (D-8)"
else
  fail "resolver does not reference bot-false-positive-patterns.md (D-8 drift)"
fi

# --- 9. reviewers read loop-memory as advisory ----------------------------
if grep -q 'bot-false-positive-patterns.md' "$REVIEWER" 2>/dev/null && \
   grep -q 'bot-false-positive-patterns.md' "$XAML_BINDER" 2>/dev/null; then
  pass "reviewers read loop-memory (D-8)"
else
  fail "a reviewer is missing the loop-memory advisory read (D-8 drift)"
fi

# --- 9b. All three reviewers parameterize the diff range (BASE_REF/HEAD_REF) -
# In the loop orchestrator checkout HEAD is the loop branch, not the draft
# branch, so a reviewer that hardcodes HEAD/main inspects the wrong diff. All
# three must accept $BASE_REF/$HEAD_REF and fall back to origin/main...HEAD.
reviewer_diff_ok=true
for r in "$REVIEWER" "$XAML_BINDER" "$DESIGN_REVIEWER"; do
  if ! grep -q 'BASE_REF.*HEAD_REF' "$r" 2>/dev/null; then
    fail "reviewer missing diff-range parameterization: $r"
    reviewer_diff_ok=false
  fi
done
if [ "$reviewer_diff_ok" = "true" ]; then
  pass "all reviewers parameterize diff range (BASE_REF/HEAD_REF)"
fi

# --- 10. .gitignore has .claude/loop-memory/ ------------------------------
if grep -q '^\.claude/loop-memory/' "$GITIGNORE" 2>/dev/null; then
  pass ".gitignore has .claude/loop-memory/ (D-7)"
else
  fail ".gitignore missing .claude/loop-memory/ (D-7)"
fi

# --- 11. Pre-PR review round (step 2.5) is present in SKILL ---------------
if grep -q 'Pre-PR review round' "$SKILL" 2>/dev/null; then
  pass "pre-PR review round (step 2.5 / C-6)"
else
  fail "pre-PR review round missing from SKILL.md (C-6 drift)"
fi

# --- 12. Spec-Kit flow (step 2.6) is present in SKILL ---------------------
if grep -q 'Spec-Kit flow' "$SKILL" 2>/dev/null && \
   grep -q 'speckit_required' "$RUNNER" 2>/dev/null; then
  pass "Spec-Kit flow (step 2.6 / F-11)"
else
  fail "Spec-Kit flow missing from SKILL.md or runner (F-11 drift)"
fi

# --- 13. Coverage probe (B-4) is present in SKILL -------------------------
if grep -q 'coverage probe' "$SKILL" 2>/dev/null; then
  pass "coverage probe (B-4)"
else
  fail "coverage probe missing from SKILL.md (B-4 drift)"
fi

# --- 14. Parallel dispatch (C-5) is present in SKILL ----------------------
if grep -q 'bounded parallel' "$SKILL" 2>/dev/null; then
  pass "bounded parallel dispatch (C-5)"
else
  fail "bounded parallel dispatch missing from SKILL.md (C-5 drift)"
fi

# --- 15. F-12 unresolved-findings PR comment is present -------------------
if grep -q 'Unresolved review/design findings' "$SKILL" 2>/dev/null; then
  pass "unresolved-findings PR comment (F-12)"
else
  fail "unresolved-findings PR comment missing from SKILL.md (F-12 drift)"
fi

# --- 16. GPL/MIT diff-scan uses the script, not the (no-op) hook ----------
GPL_SCAN=".claude/scripts/check-gpl-mit-boundary-diff.sh"
have "$GPL_SCAN" && pass "exists: $GPL_SCAN" || fail "missing: $GPL_SCAN"
if grep -q 'check-gpl-mit-boundary-diff.sh' "$SKILL" 2>/dev/null && \
   ! grep -q 'hooks/check-gpl-mit-boundary.sh.*against the diff' "$SKILL" 2>/dev/null; then
  pass "GPL/MIT diff-scan uses the script, not the hook (finding 1)"
else
  fail "GPL/MIT scan drift: SKILL.md still invokes the PreToolUse hook on a diff"
fi

# --- 17. Coverage probe uses the real script, not a no-op snippet ---------
COV_PROBE=".claude/scripts/changed-line-coverage.py"
have "$COV_PROBE" && pass "exists: $COV_PROBE" || fail "missing: $COV_PROBE"
if grep -q 'changed-line-coverage.py' "$SKILL" 2>/dev/null && \
   ! grep -q 'cov = 0; n = 0' "$SKILL" 2>/dev/null; then
  pass "coverage probe uses the script (finding 2)"
else
  fail "coverage probe drift: SKILL.md still has the no-op snippet"
fi

# --- 18. Runner contract: description + intro never claim to open a PR -----
# The runner always hands back a draft; the orchestrator opens the PR. The
# rework mode section legitimately mentions opening a PR (when the orchestrator
# sets OPEN_PR=true), so the check scopes to the frontmatter description + the
# intro paragraph (first 20 lines), which is where the self-contradiction lived.
# Use python regex with negative lookbehind so "opens a PR" on a line that also
# contains "draft" or "never" is NOT falsely cleared — grep -v filters whole
# lines and cannot express "the phrase 'open a PR' must not appear unless
# immediately preceded by 'never'/'not'/'Does NOT'".
offending=$(python3 -c '
import re, sys
lines = open(sys.argv[1]).readlines()[:20]
pat = re.compile(r"(?<!never )(?:open a PR|opens a PR|open a pull request|opens a pull request|open.*PR for it)", re.I)
neg = re.compile(r"never open|does NOT open|do not open|cannot open|not open PRs", re.I)
for i, line in enumerate(lines, 1):
    for m in pat.finditer(line):
        start = max(0, m.start() - 20)
        ctx = line[start:m.end()]
        if not neg.search(ctx):
            print(f"{i}:{line.rstrip()}")
            break
' "$RUNNER" 2>/dev/null || true)
if [ -n "$offending" ]; then
  fail "runner contract drift: description/intro still says 'open a PR' (finding 3)"
  printf '%s\n' "$offending" >&2
else
  pass "runner contract: never claims to open a PR (finding 3)"
fi

# --- 19. beutl-pre-pr can invoke the GPL/MIT script under its tool envelope -
PRE_PR=".claude/skills/beutl-pre-pr/SKILL.md"
have "$PRE_PR" && pass "exists: $PRE_PR" || fail "missing: $PRE_PR"
if grep -q 'check-gpl-mit-boundary-diff.sh' "$PRE_PR" 2>/dev/null; then
  if grep -q 'Bash(bash .claude/scripts/\*' "$PRE_PR" 2>/dev/null; then
    pass "beutl-pre-pr allows the GPL/MIT script invocation"
  else
    fail "beutl-pre-pr invokes check-gpl-mit-boundary-diff.sh but lacks Bash(bash .claude/scripts/*:*) in allowed-tools"
  fi
else
  pass "beutl-pre-pr does not invoke the GPL/MIT script (no allowlist needed)"
fi

# --- Summary ---------------------------------------------------------------
if [ "$fails" -eq 0 ]; then
  echo "loop-contract-check: all invariants hold."
  exit 0
else
  echo "loop-contract-check: $fails invariant(s) drifted — fix before relying on the loop." >&2
  exit 1
fi
