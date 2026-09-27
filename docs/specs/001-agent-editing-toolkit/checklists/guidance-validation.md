# Guidance Validation Checklist

Use this checklist to validate the packaged Skills and Subagents without reading Beutl source.

## Scenario

Create a 10-second 1920x1080 project from this brief:

- 0.0-3.0 s: full-frame background plate and centered mixed-case title text.
- 3.0-7.0 s: title moves to the top-left; add a second text caption.
- 7.0-10.0 s: keep the background, fade the caption out, and show a simple logo shape bottom-right.
- Apply a consistent look: subtle blur or shadow on text and a warm color adjustment where supported by the runtime schema.

## Required Entry Points

- Timeline: `src/Beutl.AgentToolkit/Installation/Assets/skills/beutl-agent-timeline-from-shotlist/SKILL.md`
- Look/effects: `src/Beutl.AgentToolkit/Installation/Assets/skills/beutl-agent-look-effect-chain/SKILL.md`
- Optional specialist: `src/Beutl.AgentToolkit/Installation/Assets/agents/beutl-agent-timeline-builder.md`
- Optional specialist: `src/Beutl.AgentToolkit/Installation/Assets/agents/beutl-agent-look-applier.md`

## Editing-contract checks

- The agent can complete the specified edits without Beutl source code, using targeted schemas only where needed.
- Existing Id-based patches preserve unrelated elements and properties, and undo restores the previous state.
- Timing and transforms match the requested scene when rendered and played back.
- Names describe the visible content, and layer indices remain compact with correct Portal capture ranges.
- Output uses the requested paths under `BEUTL_WORKSPACE`; the saved project reopens with the same edit.
- An intentionally static, sparse, or dark variant can render and export without an aesthetic pass/fail result.
- The agent assesses the requested appearance and motion from the actual result, not from operation success or numerical thresholds.

Record relevant evidence in the test report or conversation; no per-project planning or review files are required by Beutl.
