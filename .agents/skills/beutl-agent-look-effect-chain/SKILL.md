---
name: beutl-agent-look-effect-chain
description: Apply a consistent look or effect chain to Beutl elements through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Look Effect Chain

Use this skill when an agent needs to apply color, blur, shadow, stylization, or other effect chains consistently across Beutl elements.

## Workflow

1. Call `get_schema` for the target effect/drawable category and read parameter ranges, defaults, animatable flags, and expression support.
2. Call `read_document` and identify the element/object handles to modify.
3. If source-code reading is allowed, use `beutl-agent-source-grounding` before changing effect-unit, transform, bounds, text measurement, backing-plate alignment, render-scale, or live-session behavior.
   - Read `.agents/skills/beutl-agent-source-grounding/SKILL.md`, then use narrow `rg`/read passes over the source and tests it identifies.
   - Record a `sourceGrounding` note with `assumption`, `evidence`, `rule`, and `uncertainty` before the first relevant `apply_edit`.
   - If the user explicitly forbids source reading, skip this step and record that limitation.
4. Before changing the look, record the look brief in notes or the response:
   - `paletteRoles`: background, text, accent, support, and shadow.
   - `contrastPlan`: how text, backing plates, and focal objects stay readable.
   - `hierarchyPlan`: what remains the primary focal point after the look change.
   - `effectIntentPlan`: the job of each effect chain, such as material texture, hierarchy separation, transition energy, color grade, or text legibility.
   - `roleTagPlan`: preserve or add intent tags such as `[role:background]`, `[role:text-backing]`, and `[role:decorative]` when the look change touches plates, decorative rectangles, or text readability.
   - `structurePreservationPlan`: keep ordinary Elements to one EngineObject; do not add extra Objects to an Element unless it is an intentional `IFlowOperator` chain such as `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, or `Scene3D`.
   - `shapeMotionIntentPlan`: if the look change adds or animates foreground shapes, name their role, purpose, and motion intent before patching. Do not add abstract glint/glow/aperture/lens ellipses as foreground decoration; use parseable strokes, particles, letter fragments, editor/timeline marks, masks, media, or procedural texture instead.
   - `gradientFalloffPlan`: when changing ambient/aperture/glow backgrounds, use at least three falloff stops, wider alpha/color transitions, a real Blur/SKSL texture, or procedural texture so color boundaries do not read as hard bands.
   - `transformPreservationPlan`: if a target object has animated transform children plus static rotation/skew/scale, state whether the motion is screen-space or local/rotated-space and preserve the existing `TransformGroup.Children` order unless the change explicitly fixes it.
5. Prefer a merge-patch for look changes:
   - Preserve existing element timing and unrelated properties.
   - Patch only the target `Objects`, effect collections, and property values.
6. Call `apply_edit` in the smallest useful look/effect stage and inspect `valid`, `changes`, `validation`, and `createdIds` before continuing.
7. Resolve all `validation_rejected`, `unknown_type`, fallback-object, and stale-handle errors from `apply_edit` by reading `get_schema`/`read_document` and retrying only that small stage.
8. For file sessions, call `save_project` after a successful major look stage. For LiveEditor sessions, record the `save_project`/`read_operation_status` message that saving is not required or supported by the toolkit.
9. Run `preview_quality_risks` when the look change adds/removes plates, changes text/background contrast, introduces foreground `RectShape` objects, adds abstract decorative light shapes, changes ambient/glow gradients, or changes short-lived typography.
10. Render stills before and after the most visible transition points. Confirm the primary focal point, text contrast, backing plate fit, and whether each visible effect still serves its named job.
11. Run `evaluate_edit_quality`; resolve all critical/major issues introduced by the look change before export. Prefer `final_preflight` before export when available.

## Effect Chain Rules

- Use PascalCase property keys exactly as exposed by `get_schema`.
- Treat effect arrays as id-keyed arrays when entries have `Id`.
- Reorder effects with `$index`, `$after`, or `$before`; never delete and reinsert just to move an existing effect.
- Use in-range values from the schema. A coerced value is a signal to retry the same small `apply_edit` stage with the exact accepted value.
- Use concrete serialized color values such as `#ffffb34d`; do not use palette names such as `Amber`.
- For `Pen`, brush, transform, animation, and effect values, copy the schema/read-document object shape with a concrete `$type` discriminator instead of inventing shorthand fields.
- When editing a moving rotated object, treat `TransformGroup.Children` order as behavior, not formatting. For screen-space drift with a tilted object, static orientation transforms should precede the animated `TranslateTransform`; for local-axis motion, record that intent and verify the result with still/motion samples.
- Keep effect types installed and discoverable; `unknown_type` means the effect cannot be used in this runtime.
- Do not stack effects decoratively. Every effect must serve material texture, hierarchy separation, transition energy, color grade, or text legibility.
- Avoid three or more foreground objects with dense three-effect stacks unless the brief explicitly asks for a maximal or degraded look and the reason is recorded.
- Do not create foreground `RectShape` accents for glints, slashes, or rhythm marks unless the plain rectangular shape is intentional. Prefer non-rectangular accents, strokes, procedural texture, or tag them `[role:decorative]` and keep them shot-limited.
- Do not introduce unclear decorative shapes as a look fix. Any large or animated foreground shape must be named with a role and motion job such as beat sweep, scan texture, pulse reveal, transition wipe, or text backing.
- Do not introduce abstract foreground light blobs named only as glint, glow, aperture, lens, glass, reflection, or refraction. If viewers cannot parse what the shape represents without reading the layer name, replace it with a concrete visual system or move the light into the background with soft falloff.
- For ambient/aperture/glow gradients, avoid hard two-stop falloff. Use at least three gradient stops, wider offsets, Blur/SKSL texture, or a procedural surface treatment.
- Do not add a second Object to an ordinary Element while applying a look. Split the visual into its own Element unless the target Element is an intentional `IFlowOperator` chain.
- Prefer restrained color grading, texture, and subtle depth before heavy glow, blur, or card-like shadows.
- Avoid creating the dark teal plus cyan/magenta palette unless the user explicitly asks for that look.

## Consistency Rules

- For a shared look, use the same property values across matching shots unless the brief names exceptions.
- Preserve source media and audio bindings unless the user asks to replace them.
- Verify with `render_still`; do not judge a look only from the JSON document.
- Preserve the designed visual hierarchy. A look change should not make supporting effects, panels, or labels compete with the primary focal point.
- Preserve Element/Object structure by default. Of the quality categories, only `typographyReadTime`, `elementStructure`, and `motionContinuity` can fail the gate; `shapeIntent`, `motionIntent`, `decorativeShapeClarity`, `gradientFalloff`, `tempoRhythm`, and `paletteHarmony` are advisory guidance. A deliberate, brief-justified deviation (stillness, negative space, monochrome / low-contrast, hard cuts, glow / atmospheric shapes) is allowed: record the intent and set the matching intent flag (`allowStillness`, `allowDenseText`, `allowMultiObjectElements`, `allowMonochrome`) or `[role:...]` tag so the check downgrades to advisory. Block only genuine accidents: unreadable text, structural errors with no recorded intent, or a gate failure with no documented justification.
- If text uses a backing plate, keep text and `[role:text-backing]` plate timing, center, and padding aligned after the look change.
- For default-aligned text and shape backing plates, use the source-grounded center-offset coordinate rule: `TranslateTransform(0, 0)` means centered, and `(x, y)` offsets the object center from the scene center unless `AlignmentX=Left`/`AlignmentY=Top` is deliberately set.
- Use `measure_object_bounds` for text/backing-plate or shape alignment changes before judging the result from still renders.
- Before exporting or finishing a look change, run a transform-order audit on any object whose summary shows nested transform animation and a static rotation/skew/scale. If the order does not match the recorded motion intent, patch only the transform child ordering and rerun the representative still/motion check.
- Do not leave `preview_quality_risks`, `evaluate_edit_quality`, or `final_preflight` critical/major blockers unresolved.
