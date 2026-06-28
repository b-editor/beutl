---
name: beutl-agent-timeline-from-shotlist
description: Build a Beutl timeline from a shot list through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Timeline From Shot List

Use this skill when an agent needs to turn a shot list, storyboard, or timed brief into a Beutl project through the Agent Editing Toolkit.

## Workflow

1. For creative briefs with little or no direction, call `list_creative_directions`, compare at least two `conceptPlan` entries, avoid any concept close to the last output, then map the chosen plan's listed elements into named Beutl elements/objects before authoring. Do not pick a concept only because it appears first.
2. Call `get_schema` before authoring if the required drawable, media, or audio type is not already known.
3. Create or attach a session:
   - Stdio/headless: `create_project` or `open_project` with a `.bep` project path. Paths without an extension are normalized to `.bep`; `.beutl` is reserved for exported project packages.
   - Live editor: `attach_active_editor`.
4. If live attach fails and the task allows headless output, switch to the stdio/headless `create_project` route rather than creating a custom generator.
5. When an output directory is requested, create/update `notes.md` there before the first edit and after every `plan_edit`, `apply_edit`, `save_project`, `render_still`, `evaluate_motion_variation`, and `export_video` result. Record success/failure, change count or verdict/path, and the next action.
6. Call `read_document` and keep the returned `schemaVersion`.
7. Build the timeline as a declarative document:
   - Use PascalCase property names exactly as returned by `get_schema`.
   - Use stable `Id` handles when modifying existing elements.
   - Omit `Id` only for genuinely new elements/objects so the toolkit can mint one.
   - When adding new `Objects` to an existing `Element`, keep the parent `Element.Id` and omit `Id` on each new Object.
   - Keep element `Start`, `Length`, and layer/Z values consistent with the shot list.
8. Call `plan_edit`, inspect the change set and validation outcomes, and keep the returned `expectedChangeSet` array unchanged for application.
9. Call `apply_edit` with the same `schemaVersion` and the exact `expectedChangeSet` array from the accepted plan. Do not replace it with a count, label, or shorthand.
10. For file sessions, call `save_project` after every successful major `apply_edit` before continuing to the next stage.
11. Verify with `read_document`, then `render_still` at representative shot boundaries.
12. Run `evaluate_motion_variation` across 4-6 samples. If it reports `low-motion-variation` or `poor-frame-coverage`, revise the edit before exporting.
13. Export a short preview with `export_video` when an encoder is available; if export is unavailable, record the reason in notes.
14. Save with `save_project` for file sessions after final revisions.

## Motion Graphics Quality Bar

- Use at least three timing phases: reveal, development, and resolution. Avoid a single continuous drift.
- Animate multiple property families across the piece, such as transform, opacity, brush/gradient, effect parameters, and text spacing. Do not rely only on X movement plus opacity.
- Maintain visual density: use layered background, foreground motion, accents, and typography/labels. A lone title over one moving shape is too sparse unless the brief asks for minimalism.
- Give each major visual part a clear name in the patch so `read_document_summary` exposes the intended structure.
- After still renders, use `evaluate_motion_variation`; treat low adjacent-frame variation or persistent one-quadrant/sparse frame coverage as a failed self-check for motion graphics.

## Originality Rules

- For creative briefs, build an original timeline with `plan_edit` / `apply_edit`; do not use `list_compositions`, `plan_composition`, or empty-scene examples as the default output path.
- Use composition templates only when the user explicitly asks for a template, starter, quick draft, or named template style.
- When a template is explicitly requested, pick a specific returned template name from `list_compositions`; do not rely on an implicit first template selection.
- Treat examples as schema snippets or fallbacks. Adapt their structure to the brief instead of copying a full starter scene unchanged.
- Avoid overused no-context motifs such as orbit rings, radar sweeps, map/atlas labels, signal nodes, dashboard bars, and dark teal cyan/magenta neon unless the user asks for them.

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
- Existing parent with new child example: `{ "Elements": [{ "Id": "<existing-element-id>", "Objects": [{ "$type": "<discriminator-from-get_schema>", "Name": "new-title" }] }] }`. The existing `Element` keeps its `Id`; the new Object omits `Id`.

## Progress Watchdog

- Keep `notes.md` granular enough for another observer to reconstruct the route: every plan, apply, save, render, evaluate, export, validation failure, and route change gets an entry.
- If no tool success, saved project artifact, render/export artifact, or notes update happens for about three minutes while editing, stop and report the blocker/status instead of silently continuing.

## Safety Rules

- Keep values in documented ranges. If `plan_edit` reports coercion or rejection, adjust the request and re-plan.
- Confirm destructive output overwrites only when the user explicitly asked for overwrite.
- Do not write outside `BEUTL_WORKSPACE`.
