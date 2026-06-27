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
4. Call `plan_edit`.
5. Resolve all `validation_rejected`, `unknown_type`, and stale-handle errors before applying.
6. Call `apply_edit` with `expectedChangeSet`.
7. Render stills before and after the most visible transition points.

## Effect Chain Rules

- Use PascalCase property keys exactly as exposed by `get_schema`.
- Treat effect arrays as id-keyed arrays when entries have `Id`.
- Reorder effects with `$index`, `$after`, or `$before`; never delete and reinsert just to move an existing effect.
- Use in-range values from the schema. A coerced value is a signal to re-plan with the exact accepted value.
- Keep effect types installed and discoverable; `unknown_type` means the effect cannot be used in this runtime.

## Consistency Rules

- For a shared look, use the same property values across matching shots unless the brief names exceptions.
- Preserve source media and audio bindings unless the user asks to replace them.
- Verify with `render_still`; do not judge a look only from the JSON document.
