---
name: beutl-agent-timeline-from-shotlist
description: Build a Beutl timeline from a shot list through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Timeline From Shot List

Use this skill when an agent needs to turn a shot list, storyboard, or timed brief into a Beutl project through the Agent Editing Toolkit.

## Workflow

1. For creative briefs with little or no direction, call `list_creative_directions`, then select the `conceptPlan` mechanically:
   - If the user prompt does not specify a concrete motif, style, palette, message, audience, or subject, choose one returned `conceptPlan` by random index before judging quality. Do not override the random choice because another plan looks easier, denser, or more polished.
   - If the user prompt does specify concrete creative constraints, compare the returned `conceptPlan` entries against those constraints and choose the best fit.
   - In notes, record whether the concept was random-selected or constraint-selected, the chosen concept name, and the selection index/method.
   - Only reroll or reject a random-selected concept when it conflicts with an explicit user constraint or a listed overused motif.
   - After selection, map the chosen plan's listed elements into named Beutl elements/objects before authoring.
   - For unconstrained briefs, keep project, still, and video basenames neutral, such as `project.bep`, `preview.mp4`, and `still-*.png`, or use the requested output directory slug. Record the concept name in notes instead of naming files after it.
2. Call `get_schema` before authoring if the required drawable, media, or audio type is not already known.
   - For organic heat, ink, glass, smoke, grain, caustic, or other procedural fields, call `list_effect_recipes` with a shader/organic intent and consider `SKSLScriptEffect` instead of stacking only blurred gradient shapes. Prefer SKSL over GLSL for low-context file sessions because it is CPU-safe in still renders.
3. Create or attach a session:
   - Stdio/headless: `create_project` or `open_project` with a `.bep` project path. Paths without an extension are normalized to `.bep`; `.beutl` is reserved for exported project packages.
   - Live editor: `attach_active_editor`.
4. If live attach fails and the task allows headless output, switch to the stdio/headless `create_project` route rather than creating a custom generator.
5. When an output directory is requested, create/update `notes.md` there before the first edit and after every `plan_edit`, `apply_edit`, `save_project`, `render_still`, `evaluate_motion_variation`, and `export_video` result. Record success/failure, change count or verdict/path, and the next action. While drafting a large patch before the next tool call, append a short heartbeat note every few minutes with the current stage and blocker risk.
6. Call `read_document` and keep the returned `schemaVersion`.
7. Build the timeline as a declarative document:
   - Use PascalCase property names exactly as returned by `get_schema`.
   - New timeline `Elements` require `$type: "[Beutl.ProjectSystem]:Element"`.
   - Use stable `Id` handles when modifying existing elements.
   - Omit `Id` only for genuinely new elements/objects so the toolkit can mint one.
   - When adding new `Objects` to an existing `Element`, keep the parent `Element.Id` and omit `Id` on each new Object.
   - Keep element `Start`, `Length`, and layer/Z values consistent with the shot list.
   - Animation `KeyFrame.KeyTime` values are scene timeline times in toolkit patches, not object-local guesses. For Elements with nonzero `Start`, choose keyframe times that intersect the still/video frames you will render.
   - If you only need the required container shape, fetch the targeted `insert-new-element-skeleton` example; do not inspect a full-scene starter just to learn `$type` placement.
8. Call `plan_edit`, inspect the change count and validation outcomes, and keep either the returned `planId` or the returned `expectedChangeSet` for application. For multi-element motion graphics, plan/apply/save in small stages that map to the selected concept's element plan. Use exactly one `conceptPlan.elementPlan` item per stage unless the user explicitly asks for a combined edit.
9. Call `apply_edit` with the returned `planId` when present, especially when inline `changes` or `expectedChangeSet` are omitted. If using `expectedChangeSet`, pass the exact array from the accepted plan. Do not replace it with a count, label, or shorthand.
10. For file sessions, call `save_project` after every successful major `apply_edit` before continuing to the next stage.
11. Verify with `read_document_summary`. If a selected `conceptPlan` had an `elementPlan`, compare every expected element name/role against the actual elements and revise before rendering unless the omission is recorded with a concrete reason.
12. Verify with `render_still` at representative shot boundaries. For each still, record which planned elements are visible, whether text/title elements are readable, and whether foreground/background/accent density is present. Development and resolution stills should show at least three visible layer types, such as background/surface, primary motion, accent/detail, and typography; if text is present, it must have clear contrast against the background.
13. Run `evaluate_motion_variation` across 4-6 samples. If it reports `low-motion-variation` or `poor-frame-coverage`, or if the still review shows planned elements are never visible/readable, revise the edit before exporting.
14. Export a short preview with `export_video` when an encoder is available; if export is unavailable, record the reason in notes.
15. Save with `save_project` for file sessions after final revisions.

## Motion Graphics Quality Bar

- Use at least three timing phases: reveal, development, and resolution. Avoid a single continuous drift.
- Animate multiple property families across the piece, such as transform, opacity, brush/gradient, effect parameters, and text spacing. Do not rely only on X movement plus opacity.
- Maintain visual density: use layered background, foreground motion, accents, and typography/labels. A lone title over one moving shape is too sparse unless the brief asks for minimalism.
- Use procedural texture when the concept is organic or atmospheric. A short `SKSLScriptEffect` on a broad shape is often better than many low-contrast blurred ellipses for heat, ink, glass, smoke, caustics, grain, or shimmer.
- Give each major visual part a clear name in the patch so `read_document_summary` exposes the intended structure.
- Treat the chosen `conceptPlan.elementPlan` as a completion checklist. A final scene that omits planned accent/density elements without a recorded reason is incomplete.
- After still renders, use `evaluate_motion_variation`; treat low adjacent-frame variation or persistent one-quadrant/sparse frame coverage as a failed self-check for motion graphics.
- Numerical motion variation is necessary but not sufficient: planned elements must also be visibly present across representative stills, and text/title elements must be readable before export.
- A still that is mostly a smooth background after the reveal phase is not dense enough even if `evaluate_motion_variation` passes.

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
- New Element example: `{ "Elements": [{ "$type": "[Beutl.ProjectSystem]:Element", "Name": "new-element", "Start": "00:00:00", "Length": "00:00:02", "Objects": [{ "$type": "<drawable-discriminator-from-get_schema>", "Name": "new-object" }] }] }`. New Elements and Objects omit `Id`.

## Progress Watchdog

- Keep `notes.md` granular enough for another observer to reconstruct the route: every plan, apply, save, render, evaluate, export, validation failure, and route change gets an entry.
- During long patch authoring between tool calls, update `notes.md` before the three-minute mark with a heartbeat such as `drafting stage N patch; next tool: plan_edit`; if you cannot do that, stop and report a blocker.
- If no tool success, saved project artifact, render/export artifact, or notes update happens for about three minutes while editing, stop and report the blocker/status instead of silently continuing.

## Safety Rules

- Keep values in documented ranges. If `plan_edit` reports coercion or rejection, adjust the request and re-plan.
- Confirm destructive output overwrites only when the user explicitly asked for overwrite.
- Do not write outside `BEUTL_WORKSPACE`.
