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
   - Read `.claude/skills/beutl-agent-source-grounding/SKILL.md`, then use narrow `rg`/read passes over the source and tests it identifies.
   - Record a `sourceGrounding` note with `assumption`, `evidence`, `rule`, and `uncertainty` before the first relevant `apply_edit`.
   - If the user explicitly forbids source reading, skip this step and record that limitation.
4. Before changing the look, record the look brief in notes or the response:
   - `paletteRoles`: background, text, accent, support, and shadow.
   - `contrastPlan`: how text, backing plates, and focal objects stay readable.
   - `hierarchyPlan`: what remains the primary focal point after the look change.
   - `effectIntentPlan`: the job of each effect chain, such as material texture, hierarchy separation, transition energy, color grade, or text legibility.
   - `roleTagPlan`: preserve or add intent tags such as `[role:background]`, `[role:text-backing]`, and `[role:decorative]` when the look change touches plates, decorative rectangles, or text readability.
5. Prefer a merge-patch for look changes:
   - Preserve existing element timing and unrelated properties.
   - Patch only the target `Objects`, effect collections, and property values.
6. Call `apply_edit` in the smallest useful look/effect stage and inspect `valid`, `changes`, `validation`, and `createdIds` before continuing.
7. Resolve all `validation_rejected`, `unknown_type`, fallback-object, and stale-handle errors from `apply_edit` by reading `get_schema`/`read_document` and retrying only that small stage.
8. For file sessions, call `save_project` after a successful major look stage. For LiveEditor sessions, record the `save_project`/`read_operation_status` message that saving is not required or supported by the toolkit.
9. Run `preview_quality_risks` when the look change adds/removes plates, changes text/background contrast, introduces foreground `RectShape` objects, or changes short-lived typography.
10. Render stills before and after the most visible transition points. Confirm the primary focal point, text contrast, backing plate fit, and whether each visible effect still serves its named job.
11. Run `evaluate_edit_quality`; resolve all critical/major issues introduced by the look change before export. Prefer `final_preflight` before export when available.

## Effect Chain Rules

- Use PascalCase property keys exactly as exposed by `get_schema`.
- Treat effect arrays as id-keyed arrays when entries have `Id`.
- Reorder effects with `$index`, `$after`, or `$before`; never delete and reinsert just to move an existing effect.
- Use in-range values from the schema. A coerced value is a signal to retry the same small `apply_edit` stage with the exact accepted value.
- Use concrete serialized color values such as `#ffffb34d`; do not use palette names such as `Amber`.
- For `Pen`, brush, transform, animation, and effect values, copy the schema/read-document object shape with a concrete `$type` discriminator instead of inventing shorthand fields.
- Keep effect types installed and discoverable; `unknown_type` means the effect cannot be used in this runtime.
- Do not stack effects decoratively. Every effect must serve material texture, hierarchy separation, transition energy, color grade, or text legibility.
- Avoid three or more foreground objects with dense three-effect stacks unless the brief explicitly asks for a maximal or degraded look and the reason is recorded.
- Do not create foreground `RectShape` accents for glints, slashes, or rhythm marks unless the plain rectangular shape is intentional. Prefer non-rectangular accents, strokes, procedural texture, or tag them `[role:decorative]` and keep them shot-limited.
- Prefer restrained color grading, texture, and subtle depth before heavy glow, blur, or card-like shadows.
- Avoid creating the dark teal plus cyan/magenta palette unless the user explicitly asks for that look.

## Consistency Rules

- For a shared look, use the same property values across matching shots unless the brief names exceptions.
- Preserve source media and audio bindings unless the user asks to replace them.
- Verify with `render_still`; do not judge a look only from the JSON document.
- Preserve the designed visual hierarchy. A look change should not make supporting effects, panels, or labels compete with the primary focal point.
- If text uses a backing plate, keep text and `[role:text-backing]` plate timing, center, and padding aligned after the look change.
- For default-aligned text and shape backing plates, use the source-grounded center-offset coordinate rule: `TranslateTransform(0, 0)` means centered, and `(x, y)` offsets the object center from the scene center unless `AlignmentX=Left`/`AlignmentY=Top` is deliberately set.
- Use `measure_object_bounds` for text/backing-plate or shape alignment changes before judging the result from still renders.
- Do not leave `preview_quality_risks`, `evaluate_edit_quality`, or `final_preflight` critical/major blockers unresolved.
