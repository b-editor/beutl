---
name: beutl-agent-timeline-from-shotlist
description: Build a Beutl timeline from a shot list through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Timeline From Shot List

Use this skill when an agent needs to turn a shot list, storyboard, or timed brief into a Beutl project through the Agent Editing Toolkit.

## Workflow

1. For creative briefs with little or no direction, call `list_creative_directions`, then synthesize an original pitch from the returned inspiration seeds:
   - If the user prompt does not specify a concrete motif, style, palette, message, audience, or subject, choose at least two `inspirationSeeds` from different categories by random index before judging quality. Do not implement the seed names as a finished concept.
   - If the user prompt does specify concrete creative constraints, keep those constraints literal and choose seeds only as ways to make the result less generic.
   - In notes, record `selectionTrace.requestIndex`, `selectionTrace.appliedOffset`, `selectionTrace.seedMaterial`, `selectionTrace.returnedSeedOrder`, the selected seed names/categories, the combination rule or variation prompt used, and a new one-sentence pitch with a new title.
   - Only reroll or reject a random-selected seed when it conflicts with an explicit user constraint or a listed overused motif.
   - Before authoring, map the synthesized pitch into your own named Beutl elements/objects. Do not reuse returned seed names as Element/Object names.
   - For unconstrained briefs, keep project, still, and video basenames neutral, such as `project.bep`, `preview.mp4`, and `still-*.png`, or use the requested output directory slug. Record the synthesized pitch in notes instead of filenames.
2. Call `get_schema` before authoring if the required drawable, media, or audio type is not already known.
   - For organic heat, ink, glass, smoke, grain, caustic, or other procedural fields, call `list_effect_recipes` with a shader/organic intent and consider `SKSLScriptEffect` instead of stacking only blurred gradient shapes. Prefer SKSL over GLSL for low-context file sessions because it is CPU-safe in still renders.
3. Before authoring, record a quality preflight plan in notes:
   - `textCasePlan`: use Title Case or sentence case by default; avoid long all-caps unless explicitly requested.
   - `shapeBudget`: reserve `RectShape` for full-frame/background plates or deliberately plain geometry; use rounded rectangles, ellipses, paths, media, strokes, or procedural texture for foreground structure.
   - `paletteRoles`: name background, text, accent, support, and shadow colors; avoid dark teal plus cyan/magenta unless requested.
   - `textPlatePlan`: if text needs a backing plate, plan matching Start/Length, centered transforms, and padding for the named text/plate pair.
   - `motionContinuityPlan`: define reveal, development, and resolution phases plus how boundaries are bridged.
   - `verificationSamples`: choose at least three still times plus the motion/quality review sample set.
4. Create or attach a session:
   - Stdio/headless: `create_project` or `open_project` with a `.bep` project path. Paths without an extension are normalized to `.bep`; `.beutl` is reserved for exported project packages.
   - Live editor: `attach_active_editor`.
5. If live attach fails and the task allows headless output, switch to the stdio/headless `create_project` route rather than creating a custom generator.
6. When an output directory is requested, create/update `notes.md` there before the first edit and after every `apply_edit`, `save_project`, `render_still`, `evaluate_motion_variation`, `evaluate_edit_quality`, and `export_video` result. Record success/failure, change count or verdict/path, and the next action. While drafting a large patch before the next tool call, append a short heartbeat note every few minutes with the current stage and blocker risk.
7. Call `read_document` and keep the returned `schemaVersion`.
8. Build the timeline as a declarative document:
   - Use PascalCase property names exactly as returned by `get_schema`.
   - New timeline `Elements` require `$type: "[Beutl.ProjectSystem]:Element"`.
   - Use stable `Id` handles when modifying existing elements.
   - Omit `Id` only for genuinely new elements/objects so the toolkit can mint one.
   - When adding new `Objects` to an existing `Element`, keep the parent `Element.Id` and omit `Id` on each new Object.
   - Keep element `Start`, `Length`, and layer/Z values consistent with the shot list.
   - Decide the animation clock mode deliberately. With `UseGlobalClock=false`, `KeyFrame.KeyTime` is local to the owning timeline Element and should normally stay within `00:00:00`..`Element.Length`. With `UseGlobalClock=true`, `KeyFrame.KeyTime` is a scene timeline time and should intersect the visible Element range.
   - If `apply_edit.validation` contains a `Warning` for relative keyframes outside the Element local range, treat it as a timing bug unless the user explicitly asked for that state. Fix by either converting the keyframes to local times or setting `UseGlobalClock=true` when scene timeline times were intended.
   - For rotated moving shapes, do not animate only `TranslateTransform.X` and assume it will travel along the rotated visual axis. If the intended screen-space path is diagonal, animate both X and Y as a vector; if the intended local-axis path depends on transform order, verify the order with a rendered still/motion sample before export.
   - If you only need the required container shape, fetch the targeted `insert-new-element-skeleton` example; do not inspect a full-scene starter just to learn `$type` placement.
9. Apply edits in small `apply_edit` stages that map to your synthesized scene plan, such as surface/background, primary motion, detail/accent, and typography. Inspect `valid`, `changes`, `validation`, and `createdIds` after each stage before continuing.
10. If `apply_edit` returns `validation_rejected`, `unknown_type`, stale handles, or fallback-object guidance, fix the patch from `get_schema`/`read_document` and retry only that stage. Do not invent shorthand values for colors, pens, animations, brushes, transforms, or effects.
11. For file sessions, call `save_project` after every successful major `apply_edit` before continuing to the next stage. For LiveEditor sessions, `save_project` should report that saving is not required/supported; record that message instead of treating it as a blocker.
12. After each major stage, verify with `read_document_summary`. Compare every expected element name/role from your synthesized scene plan against the actual elements and revise before rendering unless the omission is recorded with a concrete reason. If any object has `isFallback: true`, stop rendering and fix the patch from schema because fallback objects are placeholders, not usable visuals.
13. Verify with `render_still` at representative shot boundaries. Treat any returned `warnings` as a blocker for export until you have either revised the scene or recorded why the warning is acceptable. For each still, record `visibilityAnalysis.visiblePixelRatio`, `foregroundPixelRatio`, `occupiedBoundsRatio`, and `maxQuadrantForegroundRatio`; compare `activeElements` against the planned visible elements; note whether text/title elements are readable and whether foreground/background/accent density is present. Development and resolution stills should show at least three visible layer types, such as background/surface, primary motion, accent/detail, and typography; if text is present, it must have clear contrast against the background.
14. Run `evaluate_motion_variation` across 4-6 samples. If it reports `low-motion-variation` or `poor-frame-coverage`, or if the still review shows planned elements are never visible/readable, revise the edit.
15. Run `evaluate_edit_quality` with the same sample set. Treat `critical` or `major` issues as blockers for export; revise and re-run until `passesQualityGate` is true, or record the explicit user reason for allowing an issue.
16. Export a short preview with `export_video` when an encoder is available; if export is unavailable, record the reason in notes.
17. Save with `save_project` for file sessions after final revisions. For LiveEditor sessions, call `read_operation_status` or `save_project` once near the end if you need to report that the live edit is already applied but not file-saved by the toolkit.

## Motion Graphics Quality Bar

- Use at least three timing phases: reveal, development, and resolution. Avoid a single continuous drift.
- Animate multiple property families across the piece, such as transform, opacity, brush/gradient, effect parameters, and text spacing. Do not rely only on X movement plus opacity.
- Maintain visual density: use layered background, foreground motion, accents, and typography/labels. A lone title over one moving shape is too sparse unless the brief asks for minimalism.
- Use procedural texture when the concept is organic or atmospheric. A short `SKSLScriptEffect` on a broad shape is often better than many low-contrast blurred ellipses for heat, ink, glass, smoke, caustics, grain, or shimmer.
- Give each major visual part a clear name in the patch so `read_document_summary` exposes the intended structure.
- Treat your synthesized scene plan as a completion checklist. A final scene that omits planned accent/density elements without a recorded reason is incomplete.
- After still renders, use `evaluate_motion_variation`; treat low adjacent-frame variation or persistent one-quadrant/sparse frame coverage as a failed self-check for motion graphics.
- Numerical motion variation is necessary but not sufficient: planned elements must also be visibly present across representative stills, and text/title elements must be readable before export.
- A still that is mostly a smooth background after the reveal phase is not dense enough even if `evaluate_motion_variation` passes.
- Long all-caps text, foreground RectShape dominance, misaligned text backing plates, dark teal/cyan/magenta palettes, repeated card shadows, low temporal variation, and unmotivated hard cuts are quality failures unless explicitly requested by the user.
- `evaluate_edit_quality.passesQualityGate` must be true before final export for normal deliverables.

## Originality Rules

- For creative briefs, build an original timeline with small staged `apply_edit` calls; do not use `list_compositions`, `plan_composition`, or empty-scene examples as the default output path.
- Treat `list_creative_directions` output as raw inspiration only. Do not copy returned seed names as the final concept title, Element/Object names, layer order, or file basename.
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

- Keep `notes.md` granular enough for another observer to reconstruct the route: every apply, save, render, evaluate, export, validation failure, and route change gets an entry.
- During long patch authoring between tool calls, update `notes.md` before the three-minute mark with a heartbeat such as `drafting stage N patch; next tool: apply_edit`; if you cannot do that, stop and report a blocker.
- If no tool success, saved project artifact, render/export artifact, or notes update happens for about three minutes while editing, stop and report the blocker/status instead of silently continuing.
- If the user or coordinator asks for status, call `read_operation_status` when available and respond immediately with the current session/source, last successful stage, and blocker before continuing.

## Safety Rules

- Keep values in documented ranges. If `apply_edit` reports coercion or rejection, adjust the request and retry the same small stage.
- Confirm destructive output overwrites only when the user explicitly asked for overwrite.
- Do not write outside `BEUTL_WORKSPACE`.
