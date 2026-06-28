---
name: beutl-agent-timeline-from-shotlist
description: Build a Beutl timeline from a shot list through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Timeline From Shot List

Use this skill when an agent needs to turn a shot list, storyboard, or timed brief into a Beutl project through the Agent Editing Toolkit.

## Workflow

1. Call `get_schema` before authoring if the required drawable, media, or audio type is not already known.
2. Create or attach a session:
   - Stdio/headless: `create_project` or `open_project`.
   - Live editor: `attach_active_editor`.
3. Call `read_document` and keep the returned `schemaVersion`.
4. Build the timeline as a declarative document:
   - Use PascalCase property names exactly as returned by `get_schema`.
   - Use stable `Id` handles when modifying existing elements.
   - Omit `Id` only for genuinely new elements/objects so the toolkit can mint one.
   - Keep element `Start`, `Length`, and layer/Z values consistent with the shot list.
5. Call `plan_edit` and inspect the change set and validation outcomes.
6. Call `apply_edit` with the same `schemaVersion` and `expectedChangeSet` from the accepted plan.
7. Verify with `read_document`, then `render_still` at representative shot boundaries.
8. Save with `save_project` for file sessions.

## Originality Rules

- For creative briefs, build an original timeline with `plan_edit` / `apply_edit`; do not use `list_compositions`, `plan_composition`, or empty-scene examples as the default output path.
- Use composition templates only when the user explicitly asks for a template, starter, quick draft, or named template style.
- When a template is explicitly requested, pick a specific returned template name from `list_compositions`; do not rely on an implicit first template selection.
- Treat examples as schema snippets or fallbacks. Adapt their structure to the brief instead of copying a full starter scene unchanged.

## Shot List Mapping

- One shot normally maps to one `Element` with `Start`, `Length`, `ZIndex`, and one or more drawable/audio objects.
- Background plates should be lower `ZIndex`; titles, logos, and overlays should be higher.
- Prefer explicit durations over relying on media original duration unless the brief explicitly asks to preserve source timing.
- For repeated visual treatments, duplicate structure deliberately; do not rely on implied defaults when the brief gives concrete values.

## Merge-Patch Rules

- Arrays of objects with `Id` are id-keyed.
- Use `{ "Id": "...", "$delete": true }` for removals.
- Use `$index`, `$after`, or `$before` for ordering; do not combine ordering directives.
- Unknown `Id` means stale handle; call `read_document` again instead of guessing.

## Safety Rules

- Keep values in documented ranges. If `plan_edit` reports coercion or rejection, adjust the request and re-plan.
- Confirm destructive output overwrites only when the user explicitly asked for overwrite.
- Do not write outside `BEUTL_WORKSPACE`.
