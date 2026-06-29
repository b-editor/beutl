---
name: beutl-agent-source-grounding
description: Ground Beutl Agent Editing Toolkit MCP edits in Beutl source code. Use before or during Beutl Live MCP / Agent Editing Toolkit work when an edit depends on coordinates, centered placement, transforms, bounds, text measurement, shape sizing, render scale, effect parameter units, serialization/reconciliation behavior, undo scope, export range, or live-editor session semantics; also use when rendered output or user feedback contradicts an MCP edit assumption.
---

# Beutl Agent Source Grounding

Use this skill as a source-code check layer for Beutl Agent Editing Toolkit work. MCP schemas describe the serializable document shape; they do not fully define runtime semantics such as alignment, transform order, coordinate origin, measured bounds, render scale, or effect units.

## Workflow

1. Name the behavior assumption before editing, for example `centered TextBlock TranslateTransform coordinates`.
2. Search narrowly with `rg` for the relevant runtime type, test helper, or toolkit analyzer.
3. Read the implementation and at least one nearby test, analyzer, or schema example when available.
4. Record a `sourceGrounding` note before the relevant `apply_edit`:
   - `assumption`: the behavior being relied on.
   - `evidence`: source/test paths and symbols read.
   - `rule`: the editing rule derived from the evidence.
   - `uncertainty`: anything still unverified.
5. Author the smallest MCP patch that applies the rule.
6. When measuring layout-sensitive objects, call `measure_object_bounds` before or after the patch to inspect render-node size, transform translation, scene-space bounds, center, and padding.
7. Verify with `read_document_summary`, representative `render_still`, and the relevant evaluator before export.

If the user explicitly forbids source-code reading, do not use this skill. Record that source grounding was skipped and keep the MCP edit conservative.

## Source Map

| Topic | Start here | What to verify |
|---|---|---|
| Drawable placement and default alignment | `src/Beutl.Engine/Graphics/Drawable.cs` | `AlignmentX`/`AlignmentY` defaults, `TransformOrigin`, `GetTransformMatrix`, and `CalculateTranslate`. |
| Text drawing and render bounds | `src/Beutl.Engine/Graphics/Shapes/TextBlock.cs`, `src/Beutl.Engine/Graphics/Rendering/TextRenderNode.cs` | Line layout, draw origin, and rendered glyph bounds; use `measure_object_bounds` for authoritative scene-space size. |
| Shape sizing and local drawing | `src/Beutl.Engine/Graphics/Shapes/Shape.cs`, `RectShape.cs`, `RoundedRectShape.cs`, `EllipseShape.cs` | Bounds size, stroke inflation, and draw origin. |
| Transform numeric meaning | `src/Beutl.Engine/Graphics/Transformation/TranslateTransform.cs`, `TransformGroup.cs`, `CanonicalTransformLayout.cs` | Whether values are absolute positions, offsets, or ordered transform children. |
| Render-node transform and bounds behavior | `src/Beutl.Engine/Graphics/Rendering/TransformRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs` | Operation bounds aggregation, bounds transformation, hit-test inversion, and density rescale. |
| Toolkit examples and generated snippets | `src/Beutl.AgentToolkit/Schema/SchemaGenerator.cs`, `CompositionTemplates.cs` | How toolkit examples choose translate values, animation discriminators, and reusable object shapes. |
| Quality analyzer assumptions | `src/Beutl.AgentToolkit/Rendering/QualityAnalyzer.cs` | How text/plate bounds, centers, foreground rect dominance, and typography overload are estimated. |
| Still and motion verification | `src/Beutl.AgentToolkit/Rendering/StillRenderer.cs`, `MotionVariationAnalyzer.cs` | Which warnings should block export and how frame coverage is computed. |
| Declarative document and reconciliation | `src/Beutl.AgentToolkit/Documents/DocumentAdapter.cs`, `DeclarativeDocumentApplier.cs` | Identity matching, merge-patch behavior, fallback objects, and schema-version handling. |
| Live/file session tools | `src/Beutl.AgentToolkit/Tools`, `tests/Beutl.AgentToolkit.Tests/Tools` | Tool result semantics, status messages, and save/export limitations. |

## Placement Rule For Text And Shapes

The current source model for normal `Drawable` placement is center-aligned by default:

- `Drawable.AlignmentX` and `Drawable.AlignmentY` default to `Center`.
- `Drawable.TransformOrigin` defaults to `RelativePoint.Center`.
- `Drawable.CalculateTranslate` places local drawable bounds at `canvasSize / 2 - bounds / 2` for center alignment.
- A pure `TranslateTransform(x, y)` then acts as an offset from that alignment-resolved position.
- The toolkit quality analyzer models this as object center = `scene.FrameSize / 2 + translate`.

Practical MCP authoring rule:

- To center a `TextBlock`, `RectShape`, `RoundedRectShape`, or `EllipseShape` in a default 1920x1080 scene, keep `AlignmentX=Center`, `AlignmentY=Center`, and use `TranslateTransform(0, 0)`.
- To place a default-aligned object by desired center coordinate `(cx, cy)`, use `X = cx - frameWidth / 2` and `Y = cy - frameHeight / 2`.
- To place by desired top-left coordinate `(left, top)`, first estimate or know the object size `(w, h)`, then use `X = left + w / 2 - frameWidth / 2` and `Y = top + h / 2 - frameHeight / 2`.
- Do not use `TranslateTransform(frameWidth / 2, frameHeight / 2)` to center an object; that moves the object's center to the lower-right frame corner.
- If true top-left anchoring is intended, set `AlignmentX=Left` and `AlignmentY=Top` deliberately, then verify the transform and backing plates with rendered stills.

Use the same coordinate rule for a text/backing-plate pair: share the same intended center offset, size the plate around the text, then call `measure_object_bounds` to confirm both objects have the intended render-node center and padding before rendering.

## Source Inspector Output

When delegating the source check to a subagent, ask for this compact format:

```text
ASSUMPTION: ...
EVIDENCE:
- path:line symbol - observed behavior
RULE: ...
PATCH IMPLICATION: ...
VERIFY WITH: ...
UNCERTAINTY: ...
```

The inspector should not edit files or call Live MCP tools. It should return source-grounded rules that the timeline or look agent can use in the next MCP patch.
