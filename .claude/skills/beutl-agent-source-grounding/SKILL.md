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
| GeometryShape geometry positioning | `src/Beutl.Engine/Graphics/Shapes/Shape.cs` (`OnDraw`, `MeasureCore`) | The `-shapeBounds.Position` normalization is commented out and `MeasureCore` returns only `geometry.Bounds.Size`, so a path is drawn offset by `geometry.Bounds.Position`. Author paths around `(0,0)` or `measure_object_bounds` + compensate. Closed Pen-only paths render when the `Pen` brush/thickness and path bounds are valid. |
| Transform numeric meaning | `src/Beutl.Engine/Graphics/Transformation/TranslateTransform.cs`, `ScaleTransform.cs`, `TransformGroup.cs`, `CanonicalTransformLayout.cs` | Whether values are absolute positions, offsets, percentages, or ordered transform children. `ScaleTransform` values are percentages (`100` = 1x), not normalized multipliers. |
| Render-node transform and bounds behavior | `src/Beutl.Engine/Graphics/Rendering/TransformRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeRenderer.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/` | Recorded-fragment bounds aggregation, bounds transformation, hit-test inversion, and density rescale. |
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

## Verified Runtime Behaviors And The Stale-Editor Caveat

**Suspect a stale running editor before turning a rendered anomaly into a rule.** When a LiveEditor MCP edit renders wrong, the running app may lag repo HEAD. A whole class of apparent toolkit "gotchas" was traced to a **stale build**, not current code — for example:

- `UseGlobalClock=false` keyframes on a non-zero-`Start` element rendering the final value / invisible: **fixed at commit `21db38d08`** (`KeyFrameAnimation{T}.GetAnimatedValue` now resolves the logical parent at evaluation time). Do not adopt "always use `UseGlobalClock=true` + absolute KeyTimes" as a rule; local KeyTimes are correct in current code.
- `TransformGroup` appearing to drop a `Scale` for `[Scale, Translate]` order: `TransformGroup.CreateMatrix` composes both orders correctly.
- `TransformEffect(ApplyToTarget=false)` before `LayerEffect` producing blur/mosaic when scaling a group up: `TransformEffect.ApplyTo` uses `context.Transform(...)` (resolution-independent) for `ApplyToTarget=false`, and `LayerEffect.ApplyTo` bakes the CTM scale from the target density in `ctx.Open` — so scaling before the LayerEffect is the intended crisp path.

Before authoring a workaround for an animation/transform/effect anomaly, rebuild the editor and confirm against `KeyFrameAnimation{T}.GetAnimatedValue`, `TransformGroup.CreateMatrix`, `TransformEffect.ApplyTo`, and `LayerEffect.ApplyTo`.

**Genuine current-code behaviors (source-verified):**

- **`GeometryShape` is not normalized to its geometry origin.** `Shape.OnDraw` leaves `//-shapeBounds.Position` commented out, so the drawn center lands at **the alignment-resolved center PLUS `geometry.Bounds.Position`** (verified by `GeometryShapePlacementTests`). A path authored from `(0,0)` to `(w,h)` (bounds origin `(0,0)`) centers **correctly**; a path **centered on `(0,0)`** has bounds origin `(-w/2,-h/2)` and renders **up-left by half its size** — this is the classic "GeometryShape appears toward the top-left" failure; scene-absolute coordinates shift by their full offset. `RectShape`/`EllipseShape` are unaffected. Rule: **author `GeometryShape` paths with the artwork's top-left at `(0,0)` (all coordinates non-negative)**. If coordinates cannot be normalized, add a static `TranslateTransform(-geometry.Bounds.X, -geometry.Bounds.Y)`; `measure_object_bounds` reports `geometryBoundsOrigin` plus the exact compensation, and `preview_quality_risks` raises a `geometryPathOffset` advisory for uncompensated offsets. For a multi-part vector mark (e.g. a two-color logo), build all parts in one shared `(0,0)`-top-left coordinate frame and verify the composite center with `measure_object_bounds`.
- **Closed Pen-only `GeometryShape` paths are valid.** A closed `PathFigure` with `Fill=null` and a visible `Pen` still renders its stroke in current code when the path has non-zero bounds. If the result is empty, inspect the path points/segments, pen brush, pen thickness, and rendered bounds before assuming an engine defect.
- **`TransformGroup` + `ScaleTransform` + `TransformOrigin` works on `GeometryShape`, but scale units are percentages.** `ScaleTransform.Scale`, `ScaleX`, and `ScaleY` use `100` for 1x; values such as `1.0` or `0.6` mean 1% or 0.6%, which can make a shape effectively invisible. Use `60` for 0.6x and `106` for 1.06x, then verify with `render_still` or `measure_object_bounds`.
- **`measure_object_bounds` measures only direct `Element.Objects`.** A `Drawable` nested inside a `DrawableGroup` cannot be measured ("unsupported improvement area"). Measure the group as a whole, or temporarily lift the child into its own Element to measure it.
- **`LayerEffect` on a `DrawableGroup` flattens the children into one layer** before the group's Opacity applies — use it when overlapping children would otherwise show the back child through the front during a group-opacity fade.

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
