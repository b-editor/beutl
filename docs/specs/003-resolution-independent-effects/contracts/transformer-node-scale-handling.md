# Contract: Transformer RenderNode Scale Handling

**Surface**: `FilterEffectRenderNode`, `TransformRenderNode`, `ContainerRenderNode`, push-state nodes (`ClipRenderNode`, `LayerRenderNode`, `OpacityMaskRenderNode`-style), and any future transformer node.

**Audience**: authors of transformer `RenderNode` subclasses. Drawable / FilterEffect / Shape authors do not touch this.

## The contract in one paragraph

A transformer RenderNode reads its upstream operation(s)' `CorrectionScale`, divides its own length-typed internal parameters by that scale before invoking Skia (which operates on the raster), computes its output `Bounds` in authoring space using the **authored** (un-divided) parameters, and propagates `CorrectionScale` on its output operation. The transformer **does not** re-rasterize; it operates in place on the upstream raster.

## Pattern

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    var upstreamOps = base.GetUpstreamOperations(context);   // existing helper
    var output = new List<RenderNodeOperation>();
    foreach (var upstream in upstreamOps)
    {
        var scale = upstream.CorrectionScale;
        var authoredParams = ReadAuthoredParameters();        // existing — from the IProperty<…> snapshot
        var rasterParams = AdjustForRaster(authoredParams, scale);  // divide

        // Compute output bounds in authoring space using AUTHORED parameters.
        var outputBounds = ComputeBounds(upstream.Bounds, authoredParams);

        output.Add(RenderNodeOperation.CreateLambda(
            bounds: outputBounds,
            render: canvas => {
                // canvas's matrix is the parent's (Identity in the outermost compositor).
                // The upstream raster is at scaled resolution; apply Skia ImageFilter / draw
                // using rasterParams (already divided).
                ApplyToRaster(canvas, upstream, rasterParams);
            },
            hitTest: upstream.HitTest));   // propagate hit-test from upstream by default

        // CorrectionScale: propagated by default. The factory needs an overload that takes it.
    }
    return output.ToArray();
}

// The factory overload accepts CorrectionScale; transformers pass upstream's value.
RenderNodeOperation.CreateLambda(bounds, render, hitTest, onDispose, correctionScale: upstream.CorrectionScale)
```

## Per-transformer recipe

### `FilterEffectRenderNode` (and the 13 in-scope effects' filter operations)

The largest beneficiary of this pattern. Per-effect adjustment math is enumerated in `research.md` § R3. The key rule: any length-typed parameter is divided by upstream `CorrectionScale` before the Skia call; output bounds are extended by the authored parameter (not the divided one).

Worked example — `DropShadow(Position = (10, 10), Sigma = (15, 15))` on input `CorrectionScale = (4, 4)`:

- Authored Position = (10, 10), authored Sigma = (15, 15).
- Output Bounds = `Rect.Union(input.Bounds, input.Bounds.Translate(10, 10).Inflate(15, 15))` — extended in authoring space using authored values.
- Skia call: `SKImageFilter.CreateDropShadow(dx: 10/4, dy: 10/4, sigmaX: 15/4, sigmaY: 15/4, color)` — divided by upstream CorrectionScale.
- Output CorrectionScale = (4, 4) — same as input.

### `TransformRenderNode`

Transform's Matrix is in authoring space (translation column in authoring pixels). The transformer adjusts the bounds (multiplies by the matrix in authoring space). The CorrectionScale propagates unchanged because the transformer does not re-rasterize.

If the Transform includes a SCALE component (e.g. `Matrix.CreateScale(2, 2)`), the operation's bounds are scaled in authoring space; the underlying raster stays the same size; the CorrectionScale stays the same. The compositor's blit will fit the same raster into the larger bounds — which is correct, because the user's intent was "render this content at the original raster, then scale it up by 2× in the scene".

Note: this differs from prior design drafts where Transform was scaled internally. Under the new design Transform translation is **not** divided by CorrectionScale at the transformer node — instead, the compositor handles both the Transform's bounds adjustment and the CorrectionScale upscale at blit time.

### `ContainerRenderNode`

Aggregates child operations. The container does NOT produce its own raster. Each child operation flows through to the container's output independently, retaining its own CorrectionScale. (Different children can have different CorrectionScale values — that's the per-clip proxy model.)

### `ClipRenderNode` / `LayerRenderNode` / `OpacityMaskRenderNode` (push-state derivatives)

These wrap child operations with a clip rect / layer bounds / mask brush. The clip rect / bounds are in authoring space; intersect with upstream bounds in authoring space; do **not** divide by CorrectionScale (the clip describes a region in authoring coordinates).

When the clip / layer is applied to the upstream raster (which is at smaller resolution), the SKCanvas matrix transforms the clip rect to raster coordinates automatically — as long as the compositor's canvas has the appropriate matrix applied. The transformer does not divide.

The exception: if a `PushLayer` materializes a new raster (via SKCanvas.SaveLayer), then it's effectively source-producing for downstream and can choose its own raster size and CorrectionScale. This is rare and treated case-by-case during the audit (see `tasks.md`).

## Special cases

- **No upstream** (transformer with zero inputs — shouldn't happen for transformer nodes, but if a node hybrid behaves as both transformer and source): treat as a source-producing node and declare CorrectionScale directly.
- **Multiple upstream with different `CorrectionScale`**: the transformer should handle each independently or composite them at a unified scale. The unified scale is chosen by the transformer (often = max or = Identity if compositing in authoring space). Composition transformers (e.g. blend modes) should generally upscale all inputs to a common authoring space before compositing. The audit task identifies any such case.
- **Operations that need to materialize a new raster (saveLayer, group filtering, complex compositing)**: the node becomes hybrid — read upstream CorrectionScale (to know the input scale), produce a new raster at a chosen resolution, declare its own CorrectionScale for downstream. This is described in `source-node-proxy.md` Type B.

## Sub-pixel / zero handling

After dividing a length-typed parameter by `CorrectionScale`, the result may be sub-pixel positive or zero:

- **Zero**: pass through exactly. `0 / s = 0`.
- **Sub-pixel positive** (`0 < x_raster < 1`): pass through; Skia handles it (typically renders as ~1 pixel with anti-aliasing).
- **NaN** input: rejected at the transformer's parameter validation (`ArgumentException`), per FR-009.
- **Negative-where-nonsensical** (sigma, radius — anything where negative is meaningless): rejected (`ArgumentOutOfRangeException`).

## Tests required

`tests/Beutl.UnitTests/Engine/Graphics/Rendering/TransformerNodeCorrectionScaleTests.cs`:

- Each transformer subclass: given an upstream operation with `CorrectionScale = (4, 4)` and known length parameters, verify the produced Skia call receives parameters divided by 4 and output Bounds is in authoring space using authored values.
- CorrectionScale propagation: a chain of three transformers above one source produces operations all reporting the source's CorrectionScale.
- Mixed-scale composition: two sources with different CorrectionScale values pass through a container; each child operation retains its own scale.
- Sub-pixel / zero / NaN: cover the FR-009 guards.
