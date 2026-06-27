# Guidance Validation Checklist

Use this checklist to validate the packaged Skills and Subagents without reading Beutl source.

## Scenario

Create a 10-second 1920x1080 project from this brief:

- 0.0-3.0 s: full-frame dark rectangle background and centered title text.
- 3.0-7.0 s: title moves to the top-left; add a second text caption.
- 7.0-10.0 s: keep the background, fade the caption out, and show a simple logo shape bottom-right.
- Apply a consistent look: subtle blur or shadow on text and a warm color adjustment where supported by the runtime schema.

## Required Entry Points

- Timeline: `.claude/skills/beutl-agent-timeline-from-shotlist/SKILL.md`
- Look/effects: `.claude/skills/beutl-agent-look-effect-chain/SKILL.md`
- Optional specialist: `.claude/agents/beutl-agent-timeline-builder.md`
- Optional specialist: `.claude/agents/beutl-agent-look-applier.md`

## Pass Criteria

- The agent uses `get_schema` before relying on effect/drawable properties.
- The agent creates or attaches a session, calls `read_document`, then uses `plan_edit` before `apply_edit`.
- The final document uses PascalCase property keys.
- Existing id-keyed arrays are patched by `Id`; new objects omit `Id` only when they are genuinely new.
- The project saves under `BEUTL_WORKSPACE`.
- At least two still frames are rendered for verification.
- The project can be reopened by Beutl.
- The end-to-end task completes within 15 minutes.

## Record

- Date:
- Agent host:
- Session mode: stdio / live editor
- Workspace:
- Render outputs:
- Result: pass / fail
- Notes:
