---
name: beutl-agent-timeline-from-shotlist
description: Build a Beutl timeline from a shot list through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Timeline From Shot List

Use this skill when an agent needs to turn a shot list, storyboard, or timed brief into a Beutl project through the Agent Editing Toolkit.

## Workflow

Author **storyboard-first**: plan a fine shot breakdown, build the **static** layout of every shot (絵コンテ) with no keyframes and no effects, lock it, then add effects, then motion. Do not animate or add effects until the static storyboard reads correctly. The phases below map to that order.

### Phase 0 — Direction and shot breakdown

1. Author the creative direction yourself. `list_creative_directions` is optional divergence stimulus, not a menu to pick from.
   - Decide the concept, palette roles, type system, motion vocabulary, and shot structure from the brief (or from scratch when the brief is vague) before leaning on any tool. Do not anchor on returned seed names.
   - Call `list_creative_directions` (pass a fresh `seed` each run to vary the stimulus) and read its `recentToAvoid` list. Your direction MUST differ structurally from those recent fingerprints: change the dominant motion verbs, layout, palette family, and type treatment, not just the words. Do not default to the same look every run (e.g. hero-glow-on-dark + dashed selection-marquee + magnetic letter-spacing) — that repetition is the monotony this step exists to prevent.
   - If the user prompt specifies concrete constraints (motif, style, palette, message, audience, subject), keep them literal; use the stimulus only to make the result less generic.
   - Once the concept is locked, call `record_creative_direction` with its fingerprint (concept label, palette roles, motion verbs, structural signature) so future sessions steer away from it.
   - In notes, record the authored concept label, palette roles, motion verbs, structural signature, any stimulus names you used, and how you diverged from `recentToAvoid`.
   - Map the concept into your own named Beutl elements/objects. Do not reuse returned seed names as Element/Object names.
   - For unconstrained briefs, keep project, still, and video basenames neutral, such as `project.bep`, `preview.mp4`, and `still-*.png`, or use the requested output directory slug. Record the concept name in notes instead of naming files after it.
2. Call `get_schema` before authoring if the required drawable, media, or audio type is not already known.
   - For organic heat, ink, glass, smoke, grain, caustic, or other procedural fields, call `list_effect_recipes` with a shader/organic intent and consider `SKSLScriptEffect` instead of stacking only blurred gradient shapes. Prefer SKSL over GLSL for low-context file sessions because it is CPU-safe in still renders.
   - GPU/stylize effects (GLSL, `PixelSortEffect`, `ColorShift` on split-character text) are render-guarded to skip degenerate targets rather than crash the renderer, so use them for richer looks when wanted — but GPU-only effects no-op without a GPU, so always confirm the result with `render_still` before relying on them. Do not over-restrict to a single safe effect; varying the effect vocabulary is part of avoiding monotone output.
3. If source-code reading is allowed, use `beutl-agent-source-grounding` before authoring layout, transform, bounds, text measurement, render scale, effect-unit, reconciliation, or live-session semantics.
   - This is mandatory when the task mentions centered placement, coordinates/origin, `TranslateTransform`, `TransformOrigin`, backing plates, object bounds, render/export range, or when a rendered/user-observed result contradicts the plan.
   - Read `.claude/skills/beutl-agent-source-grounding/SKILL.md`, then use narrow `rg`/read passes over the source and tests it identifies.
   - Record a `sourceGrounding` note with `assumption`, `evidence`, `rule`, and `uncertainty` before the first relevant `apply_edit`.
   - If the user explicitly forbids source reading, skip this step and record that limitation.
4. Before authoring, record a quality preflight plan in notes:
   - `directionContract`: state the objective, audience, emotional temperature, brand posture, delivery surface, and one-sentence promise.
   - `messageHierarchy`: name the primary message, secondary emphasis, and supporting/caption information for each shot.
   - `textCasePlan`: use Title Case or sentence case by default; do not use long all-caps text unless the user explicitly asked for it.
   - `typographyRolePlan`: assign type roles such as hero, secondary, caption, label, and texture text before choosing sizes.
   - `readTimePlan`: keep fast-beat copy to a word, phrase, or symbol; hold or split longer text.
   - `beatGridPlan`: for explicit BPM or fast-tempo briefs, convert BPM into beat length before authoring. For 120-140 BPM, default to 130 BPM when unspecified: 1 beat is about 462 ms, 2 beats about 923 ms, and 4 beats about 1.85 s. Plan visible foreground changes every 1-2 beats, normal foreground holds around 2-4 beats, no foreground event gaps longer than 4 beats, and only named final resolves/background textures may hold longer.
   - `highTempoDensityPlan`: for 120-140 BPM or roughly 1.5s shots, keep hero text to 1-3 words and supporting labels to 2-4 word tokens. Add density through nodes, particles, strokes, texture, accent motion, and secondary shapes rather than long copy. Do not count background-only drift as foreground tempo.
   - `shapeBudget`: reserve `RectShape` for full-frame/background plates or deliberately plain geometry; use rounded rectangles, ellipses, paths, media, strokes, or procedural texture for foreground structure. Do not leave a large persistent foreground `RectShape` behind multiple text beats unless it is an intentional named text backing plate with matching timing and measured padding. Do not use abstract glint/glow/aperture/lens ellipses as foreground decoration; replace them with parseable systems such as strokes, particles, letter fragments, editor/timeline marks, masks, media, or procedural texture.
   - `elementStructurePlan`: one ordinary timeline `Element` owns exactly one `EngineObject`. Multiple objects inside one `Element` are allowed only when that `Element` contains an `IFlowOperator` such as `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, or `Scene3D`; otherwise split content into separate Elements.
   - `roleTagPlan`: name important objects/elements with role tags such as `[role:background]`, `[role:text-backing]`, or `[role:decorative]` so MCP quality tools can distinguish real text plates from decorative accents.
   - `shapeIntentPlan`: every large or animated foreground shape must have a clear role, purpose, and motion intent in the Element/Object name, such as `[role:decorative] beat sweep`, `[role:text-backing] title plate`, or `[role:background] surface`. Do not create anonymous blobs, panels, abstract light ellipses, or shapes whose job cannot be named in viewer-visible terms.
   - `paletteRoles`: name background, text, accent, support, and shadow colors; avoid dark teal plus cyan/magenta unless requested. For ambient/aperture/glow backgrounds, plan at least three gradient falloff stops or a real Blur/SKSL/procedural texture so color boundaries do not read as hard bands.
   - `textPlatePlan`: if text needs a backing plate, plan matching Start/Length, centered transforms, and padding for the named `[role:text-backing]` text/plate pair. Decorative light slashes, glass bands, and texture plates should be tagged `[role:decorative]`, shot-limited, lower-Z background/surface elements, or non-rectangular/stroke/procedural treatments so quality review does not misclassify them as text backing plates.
   - `effectIntentPlan`: name the job of each effect chain: material texture, hierarchy separation, transition energy, color grade, or text legibility.
   - `shotBreakdownPlan`: derive the shot/beat count from duration × tempo before authoring (for 120-140 BPM, default 130 BPM: a 30s piece is about 65 beats). Plan a visible foreground event roughly every 1-2 beats, so a typical 30s high-tempo piece is **tens of fine shots, not 6-8 coarse ones**. Enumerate every shot with an index, `Start`, `Length`, primary focal point, primary message, and role. Subdivide long holds into distinct beats instead of letting one shot span many beats; only named final resolves and background textures may span multiple beats. This enumerated breakdown is the source of `Element` boundaries in Phase 1.
   - `compositionPlan`: define one primary focal point per enumerated shot and how grouping, alignment, scale, color, and repetition support it.
   - `motionContinuityPlan`: define reveal, development, and resolution phases plus how boundaries are bridged.
   - `verificationSamples`: choose at least three still times plus the motion/quality review sample set.

### Phase 1 — Static storyboard (絵コンテ)

Build the static layout of **every enumerated shot** with NO keyframes and NO effects. The deliverable is a readable 絵コンテ: correct composition, hierarchy, typography, color, and layering at each shot's representative frame.

5. Create or attach a session:
   - Stdio/headless: `create_project` or `open_project` with a `.bep` project path. Paths without an extension are normalized to `.bep`; `.beutl` is reserved for exported project packages.
   - Live editor: `attach_active_editor`.
6. If live attach fails and the task allows headless output, switch to the stdio/headless `create_project` route rather than creating a custom generator.
7. When an output directory is requested, create/update `notes.md` there before the first edit and after every `apply_edit`, `save_project`, `render_storyboard`, `preview_quality_risks`, `suggest_quality_fixes`, `render_still`, `evaluate_motion_variation`, `evaluate_edit_quality`, `final_preflight`, and `export_video` result. Record success/failure, change count or verdict/path, and the next action. While drafting a large patch before the next tool call, append a short heartbeat note every few minutes with the current stage and blocker risk.
8. Call `read_document` and keep the returned `schemaVersion`.
9. Build the **static layout** as a declarative document (Phase 1 — no motion, no effects):
   - Use PascalCase property names exactly as returned by `get_schema`.
   - New timeline `Elements` require `$type: "[Beutl.ProjectSystem]:Element"`.
   - Use stable `Id` handles when modifying existing elements.
   - Omit `Id` only for genuinely new elements/objects so the toolkit can mint one.
   - Do not add a second `Object` to an ordinary existing `Element`. To place another visible item, create another `Element` with its own single `EngineObject`. Add multiple `Objects` to one `Element` only for an intentional `IFlowOperator` flow chain such as `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, or `Scene3D`; keep the parent `Element.Id` and omit `Id` only for genuinely new child objects in that flow chain.
   - Keep element `Start`, `Length`, and layer/Z values consistent with the enumerated shot breakdown from `shotBreakdownPlan`.
   - Author only static property values in this phase — no `KeyFrameAnimation`/`KeyFrame` (that is Phase 3) and no `FilterEffect` (that is Phase 2).
   - For default-aligned `TextBlock` and shape objects, treat `TranslateTransform(0, 0)` as centered in the scene; `TranslateTransform(x, y)` is an offset from the scene center. Do not use half-frame coordinates such as `(960, 540)` to center content in a 1920x1080 scene unless `AlignmentX=Left`/`AlignmentY=Top` was deliberately selected and source-grounded.
   - Use `measure_object_bounds` after creating or modifying layout-sensitive text, shape, and backing-plate pairs to confirm render-node size, scene-space center, transformed bounds, and padding before relying on still renders.
   - If you only need the required container shape, fetch the targeted `insert-new-element-skeleton` example; do not inspect a full-scene starter just to learn `$type` placement.
10. Apply the static layout in small `apply_edit` stages that map to your enumerated shot breakdown — background/surface, then primary structure/shapes, then typography, then text backing plates — using static property values only (no motion, no effects yet). Inspect `valid`, `changes`, `validation`, and `createdIds` after each stage before continuing. Pass `quiet: true` for large staged patches.
11. If `apply_edit` returns `validation_rejected`, `unknown_type`, stale handles, invalid animation discriminator tokens, or fallback-object guidance, fix the patch from `get_schema`/`get_examples`/`read_document` and retry only that stage. Do not invent shorthand values for colors, pens, animations, brushes, transforms, or effects. Do not silently fall back to cut-only timing after a keyframe failure unless the user explicitly accepts that reduced motion model.
12. For file sessions, call `save_project` after every successful major `apply_edit` before continuing to the next stage. For LiveEditor sessions, `save_project` should report that saving is not required/supported; record that message instead of treating it as a blocker.
13. After each major stage, verify with `read_document_summary`. Compare every expected element name/role from your synthesized scene plan against the actual elements and revise before rendering unless the omission is recorded with a concrete reason. If any object has `isFallback: true`, stop rendering and fix the patch from schema because fallback objects are placeholders, not usable visuals. Also audit object counts: any ordinary Element with multiple objects must be split unless it contains a named `IFlowOperator`.
14. Verify the static storyboard before adding any effects or motion:
   - Call `render_storyboard` to render one still per enumerated shot plus a contact-sheet PNG of the whole storyboard. Review it as a 絵コンテ: every planned shot present, one clear focal point per shot, readable typography, the intended layering and color, and correctly aligned text/backing-plate pairs. For a scene with many Elements the synchronous call can exceed the MCP client request timeout; pass `background: true` to get `{ status: "running", jobId }` immediately, then poll `read_render_job(jobId)` until `state` is `completed` (its `result` holds the storyboard payload) — do not issue `apply_edit` while a background render is running.
   - Run `preview_quality_risks` (it does not evaluate motion). Treat `elementStructure`, `shapeIntent`, `decorativeShapeClarity`, and `gradientFalloff` major issues as blockers; call `suggest_quality_fixes` for multiple related failures and apply the smallest repair. For high-tempo promos, set a `styleProfile` such as `high-tempo-promo`, `kinetic-type`, or `high-tempo-promo 130bpm`.
   - Run `evaluate_edit_quality(staticLayout:true)` — the storyboard-phase gate. It skips motion checks, so the motionless storyboard is judged on composition, typography, readability, and structure only. Resolve `critical`/`major` issues before leaving this phase.
   - Use `measure_object_bounds` for any text/backing-plate pair that looks misaligned in the contact sheet.
   - Do NOT run `evaluate_motion_variation`, `evaluate_edit_quality` without `staticLayout`, or `final_preflight` in this phase — those are motion-phase gates and will false-block a static storyboard (identical frames read as zero motion).
   Iterate Phase 1 until the storyboard reads correctly; only then proceed.

### Phase 2 — Effects

15. Add effect chains onto the locked storyboard, one named job at a time. Do not start motion until effects read correctly on stills.
   - Every effect chain needs a named job: material texture, hierarchy separation, transition energy, color grade, or text legibility. Remove decorative stacks that do not serve one.
   - For organic heat, ink, glass, smoke, grain, caustic, or atmospheric fields, prefer `SKSLScriptEffect` (from `list_effect_recipes`) over stacking only blurred gradient shapes; SKSL is CPU-safe in still renders. Prefer SKSL over GLSL for low-context file sessions. The `fine-film-grain-field` recipe ships a monochrome film-grain shader; `organic-shader-field` ships a colored field shader — pick by intent, and `validate_shader` any custom SKSL before `apply_edit`.
   - GPU/stylize effects (GLSL, `PixelSortEffect`, `ColorShift` on split-character text) are render-guarded to skip degenerate targets rather than crash, but no-op without a GPU; confirm every effect with `render_still`/`render_storyboard`. Vary the effect vocabulary instead of over-restricting to one safe effect.
   - For a true emissive glow/bloom (light that adds over the original, not a `DropShadow` fake), duplicate the drawable with `duplicate_object`, then `apply_edit` the `additive-bloom` recipe (blur + `BlendMode` `Plus` + reduced `Opacity`) onto the returned `objectId` so the copy glows over the untouched original. The copy lands as a second Object in the same Element — wrap both in a `DrawableGroup` (or move the copy to a separate Element at a higher `ZIndex`) to keep `evaluate_edit_quality`'s `elementStructure` check clean. Lower `Opacity` or switch `BlendMode` to `Screen` for bright footage that blows out.
   - Re-verify with `render_storyboard`/`render_still` and `evaluate_edit_quality(staticLayout:true)` that effects serve their named jobs and did not break readability. Motion-phase gates still do not apply yet.

### Phase 3 — Motion

16. Add keyframes and animation on top of the locked storyboard and effects. Build the reveal, development, and resolution phases from `motionContinuityPlan`, and animate multiple property families (transform, opacity, brush/gradient, effect parameters, text spacing) — not just X plus opacity. Apply motion in small `apply_edit` stages per beat and inspect `validation` after each; pass `quiet: true` for large patches.
   - Decide the animation clock mode deliberately. With `UseGlobalClock=false`, `KeyFrame.KeyTime` is local to the owning timeline Element and should normally stay within `00:00:00`..`Element.Length`. With `UseGlobalClock=true`, `KeyFrame.KeyTime` is a scene timeline time and should intersect the visible Element range.
   - For explicit keyframes, fetch `get_examples` for `animate-float-property-keyframes` when animating an existing object, or `insert-new-animated-text-keyframes` when creating a new animated text object. Copy the concrete `KeyFrameAnimation<T>` and `KeyFrame<T>` discriminators from the example instead of inventing animation type names.
   - Before authoring multiple keyframed properties, make a small local keyframe helper snippet in your draft from the MCP example and reuse that exact JSON shape for every animated `Single`, `Boolean`, `Color`, `Size`, or transform property. Do not hand-type or manually Unicode-escape the generic discriminator strings; invalid tokens around `KeyFrameAnimation` or `KeyFrame` mean the helper is wrong and must be rebuilt from `get_examples` before continuing.
   - If `apply_edit.validation` contains a `Warning` for relative keyframes outside the Element local range, treat it as a timing bug unless the user explicitly asked for that state. Fix by either converting the keyframes to local times or setting `UseGlobalClock=true` when scene timeline times were intended.
   - For rotated moving shapes, do not animate only `TranslateTransform.X` and assume it will travel along the rotated visual axis. If the intended screen-space path is diagonal, animate both X and Y as a vector; if the intended local-axis path depends on transform order, verify the order with a rendered still/motion sample before export.

### Phase 4 — Motion verification and export

17. Verify with `render_still` at representative shot boundaries. Treat any returned `warnings` as a blocker for export until you have either revised the scene or recorded why the warning is acceptable. For each still, record `visibilityAnalysis.visiblePixelRatio`, `foregroundPixelRatio`, `occupiedBoundsRatio`, and `maxQuadrantForegroundRatio`; compare `activeElements` against the planned visible elements; note the primary focal point, whether text/title elements are readable for their duration, whether effect chains still serve their named jobs, and whether foreground/background/accent density is present. Development and resolution stills should show at least three visible layer types, such as background/surface, primary motion, accent/detail, and typography; if text is present, it must have clear contrast against the background.
   - If a large decorative `RectShape` remains active behind several unrelated text shots, treat it as a likely text-background-fit problem before quality review. Limit it to the shot where it belongs, move it clearly into the background/surface role, or replace it with stroke/ellipse/path/procedural texture.
18. Run `evaluate_motion_variation` across 4-6 samples. If it reports `low-motion-variation` or `poor-frame-coverage`, or if the still review shows planned elements are never visible/readable, revise the edit.
19. Run `evaluate_edit_quality` with the same sample set (the full motion gate; leave `staticLayout` off here). Treat `critical` or `major` issues as blockers for export; revise and re-run until `passesQualityGate` is true, or record the explicit user reason for allowing an issue.
   - For motion-graphics deliverables, `animatedPropertyCount: 0` is a blocker even when `evaluate_edit_quality.passesQualityGate` is true. Add explicit transform, opacity, spacing, brush, or effect animation before export.
   - For `textBackgroundFit` issues involving decorative glass/light/texture rectangles, prefer a real design fix over suppressing the issue: constrain the rectangle's Start/Length to the intended beat, align it as a named backing plate with `measure_object_bounds`, lower it into the background, or replace it with a non-plate visual treatment.
   - For high-tempo/BPM briefs, inspect `metrics.tempo.RequiredTimelineEventsPerSecond`, `TimelineEventsPerSecond`, `RequiredTotalEventsPerSecond`, `LongForegroundGapCount`, and `LongestForegroundEventGapSeconds`. A scene is too slow if background motion hides sparse foreground changes or long foreground gaps.
20. Prefer `final_preflight` before export when the tool is available. For motion graphics, pass `requireAnimatedProperties=true`; export only when `readyForExport` is true. If `final_preflight` is unavailable, use the separate `render_still` + `evaluate_motion_variation` + `evaluate_edit_quality` sequence above.
21. Export a short preview with `export_video` when an encoder is available; if export is unavailable, record the reason in notes. Control output size with `crf` (0-51, higher = smaller; raise it to ~28-30 for full-frame grain or other hard-to-compress content) or `bitrate` (bits/s, ABR) — the two are mutually exclusive. For a long export, pass `background: true` and poll `read_render_job(jobId)` the same way as `render_storyboard`.
22. Save with `save_project` for file sessions after final revisions. For LiveEditor sessions, call `read_operation_status` or `save_project` once near the end if you need to report that the live edit is already applied but not file-saved by the toolkit.

## Motion Graphics Quality Bar

- Author storyboard-first: build and verify the static layout of every shot (Phase 1, via `render_storyboard` + `evaluate_edit_quality(staticLayout:true)`) before adding effects (Phase 2) or motion (Phase 3). A storyboard that does not read as a clear 絵コンテ will not improve by animating it.
- Use at least three timing phases: reveal, development, and resolution. Avoid a single continuous drift.
- Build fast tempo through contrast between quick accents and held readability beats. Do not make every layer move at the same speed.
- For 120-140 BPM briefs, work from a beat grid instead of the vague word "fast". At 130 BPM, 1 beat is about 462 ms, 2 beats about 923 ms, and 4 beats about 1.85 s. Plan enough foreground element boundaries and keyframes for the piece to read as fast in `tempoRhythm` metrics.
- Keep normal foreground beats near 2-4 beats. Longer holds are acceptable only for named background texture, ambient support, or a deliberate final resolve; add visible foreground events when readability requires a longer text hold. Background-only drift does not satisfy a fast-tempo brief.
- Animate multiple property families across the piece, such as transform, opacity, brush/gradient, effect parameters, and text spacing. Do not rely only on X movement plus opacity.
- Keep ordinary timeline Elements to one EngineObject. Multiple objects in one Element are reserved for `IFlowOperator` chains such as `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, or `Scene3D`; otherwise split each visual object into its own Element.
- Every shot needs one primary focal point. Supporting text, marks, panels, and effects should sit lower in scale, contrast, timing, or density.
- Keep hero-scale typography to one primary message per beat; make captions, labels, and texture text visibly quieter before running `evaluate_edit_quality`.
- Readability is timed: short-lived text must be short, split across beats, or held longer. At roughly 1.5s per shot, use 1-3 words for hero text and 2-4 words or compact symbols for supporting labels.
- Maintain visual density: use layered background, foreground motion, accents, and typography/labels. A lone title over one moving shape is too sparse unless the brief asks for minimalism.
- For fast promos, add perceived information through short typography, repeated non-rectangular nodes, particles, strokes, texture, and accent motion rather than adding long text.
- Use role tags consistently: `[role:background]` for full-frame surfaces, `[role:text-backing]` only for real measured text plates, and `[role:decorative]` for glass bands, slashes, glints, or rhythm marks that should not be interpreted as backing plates.
- Large or animated foreground shapes must expose role, purpose, and motion intent in names. If a shape's job cannot be stated as beat sweep, scan texture, pulse reveal, transition wipe, text backing, or another concrete intent, remove it before export.
- Do not use abstract foreground glint/glow/aperture/lens/glass ellipses as a quality shortcut. If viewers cannot parse what the shape represents without reading its layer name, replace it with strokes, particles, letter fragments, editor/timeline marks, masks, media, or procedural texture.
- For ambient/aperture/glow backgrounds, avoid hard two-stop falloff. Use at least three gradient stops, widen alpha/color transitions, add a real Blur/SKSL texture, or replace the shape with procedural surface texture.
- Use procedural texture when the concept is organic or atmospheric. A short `SKSLScriptEffect` on a broad shape is often better than many low-contrast blurred ellipses for heat, ink, glass, smoke, caustics, grain, or shimmer.
- Every effect chain needs a named job: material texture, hierarchy separation, transition energy, color grade, or text legibility. Remove decorative stacks that do not serve one.
- Give each major visual part a clear name in the patch so `read_document_summary` exposes the intended structure.
- Treat your synthesized scene plan as a completion checklist. A final scene that omits planned accent/density elements without a recorded reason is incomplete.
- After still renders, use `evaluate_motion_variation`; treat low adjacent-frame variation or persistent one-quadrant/sparse frame coverage as a failed self-check for motion graphics.
- For motion graphics, a passing rendered-difference check is not enough when the document has no explicit animated properties. If `evaluate_edit_quality` reports `animatedPropertyCount: 0`, revise the edit to add deliberate animation on transform, opacity, typography spacing, brush, or effect parameters before export.
- Numerical motion variation is necessary but not sufficient: planned elements must also be visibly present across representative stills, and text/title elements must be readable before export.
- A still that is mostly a smooth background after the reveal phase is not dense enough even if `evaluate_motion_variation` passes.
- Long all-caps text, overloaded visual hierarchy, unreadable short-lived copy, foreground RectShape dominance, abstract decorative light ellipses, hard ambient gradient falloff, unclear or arbitrary animated shapes, ordinary Elements with multiple EngineObjects, sparse high-tempo event density, long foreground event gaps, overlong high-tempo foreground holds, misaligned text backing plates, dark teal/cyan/magenta palettes, dense effect stacks without a named job, repeated card shadows, low temporal variation, and unmotivated hard cuts are quality failures unless explicitly requested by the user.
- `evaluate_edit_quality.passesQualityGate` must be true before final export for normal deliverables.

## Originality Rules

- For creative briefs, build an original timeline with small staged `apply_edit` calls; do not use `list_compositions`, `plan_composition`, or empty-scene examples as the default output path.
- Use composition templates only when the user explicitly asks for a template, starter, quick draft, or named template style.
- When a template is explicitly requested, pick a specific returned template name from `list_compositions`; do not rely on an implicit first template selection.
- Treat examples as schema snippets or fallbacks. Adapt their structure to the brief instead of copying a full starter scene unchanged.
- Avoid overused no-context motifs such as orbit rings, radar sweeps, map/atlas labels, signal nodes, dashboard bars, and dark teal cyan/magenta neon unless the user asks for them.
- Cross-session variety is a hard requirement: the same brief should NOT keep producing the same video. Before locking a direction, compare it against `recentToAvoid` and deliberately change the structural language (motion verbs, layout grid, palette family, type treatment, transition style) from recent runs; then `record_creative_direction` so the next run can diverge too.
- Pass `quiet: true` to `apply_edit` for large staged patches; the full echoed change set can exceed the response size limit, so keep individual patches small and use the compact summary while authoring.

## Shot List Mapping

- Drive `Element` boundaries from the enumerated `shotBreakdownPlan`. One shot normally maps to one or more `Element` entries with explicit `Start`, `Length`, and `ZIndex`; each ordinary `Element` contains exactly one drawable/audio `EngineObject`.
- Use multiple `Objects` inside one `Element` only for explicit `IFlowOperator` flow chains such as `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, or `Scene3D`; otherwise split compound visuals into separate Elements.
- Background plates should be lower `ZIndex`; titles, logos, and overlays should be higher.
- Prefer explicit durations over relying on media original duration unless the brief explicitly asks to preserve source timing.
- For repeated visual treatments, duplicate structure deliberately; do not rely on implied defaults when the brief gives concrete values.

## Merge-Patch Rules

- Arrays of objects with `Id` are id-keyed. A bare id-less array merges/appends into the existing members; it does NOT replace them.
- Use `{ "Id": "...", "$delete": true }` for removals.
- To wholesale-replace an id-keyed array in one patch (e.g. swap a `FilterEffectGroup.Children` chain instead of appending to it), make the FIRST element the sentinel `{ "$replace": true }`; the following elements rebuild the array in order (omit `Id` to mint fresh, or reuse an `Id` to keep that child), and `[{ "$replace": true }]` alone clears it. Replacement elements cannot also carry `$delete`/`$index`/`$after`/`$before`. Keep the group's own `Id` so only its children change.
- Use `$index`, `$after`, or `$before` for ordering; do not combine ordering directives.
- Unknown `Id` means stale handle; call `read_document` again instead of guessing.
- Existing parent with new flow child example: `{ "Elements": [{ "Id": "<existing-flow-element-id>", "Objects": [{ "$type": "<discriminator-from-get_schema>", "Name": "new-flow-child" }] }] }`. Use this only when the existing Element is an intentional `IFlowOperator` flow chain. Ordinary Elements should not receive a second Object.
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
