---
name: beutl-agent-look-effect-chain
description: Apply a consistent look or effect chain to Beutl elements through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Look Effect Chain

Use this skill when an agent needs to apply color, blur, shadow, stylization, or other effect chains consistently across Beutl elements.

## Workflow

1. Call `get_schema` for the target effect/drawable category and read parameter ranges, defaults, animatable flags, and expression support.
2. Call `read_document` and identify the element/object handles to modify.
3. Prefer a merge-patch for look changes:
   - Preserve existing element timing and unrelated properties.
   - Patch only the target `Objects`, effect collections, and property values.
4. Call `apply_edit` in the smallest useful look/effect stage and inspect `valid`, `changes`, `validation`, and `createdIds` before continuing.
5. Resolve all `validation_rejected`, `unknown_type`, fallback-object, and stale-handle errors from `apply_edit` by reading `get_schema`/`read_document` and retrying only that small stage.
6. For file sessions, call `save_project` after a successful major look stage. For LiveEditor sessions, record the `save_project`/`read_operation_status` message that saving is not required or supported by the toolkit.
7. Render stills before and after the most visible transition points.
8. Run `evaluate_edit_quality`; resolve all critical/major issues introduced by the look change before export.

## Effect Chain Rules

- Use PascalCase property keys exactly as exposed by `get_schema`.
- Treat effect arrays as id-keyed arrays when entries have `Id`.
- Reorder effects with `$index`, `$after`, or `$before`; never delete and reinsert just to move an existing effect.
- Use in-range values from the schema. A coerced value is a signal to retry the same small `apply_edit` stage with the exact accepted value.
- Use concrete serialized color values such as `#ffffb34d`; do not use palette names such as `Amber`.
- For `Pen`, brush, transform, animation, and effect values, copy the schema/read-document object shape with a concrete `$type` discriminator instead of inventing shorthand fields.
- Keep effect types installed and discoverable; `unknown_type` means the effect cannot be used in this runtime.
- Prefer restrained color grading, texture, and subtle depth before heavy glow, blur, or card-like shadows.
- Avoid creating the dark teal plus cyan/magenta palette unless the user explicitly asks for that look.

## Consistency Rules

- For a shared look, use the same property values across matching shots unless the brief names exceptions.
- Preserve source media and audio bindings unless the user asks to replace them.
- Verify with `render_still`; do not judge a look only from the JSON document.
- If text uses a backing plate, keep text and plate timing, center, and padding aligned after the look change.
- Do not leave `evaluate_edit_quality` critical/major issues unresolved.
