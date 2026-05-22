# Contract: Transformer RenderNode Scale Handling

**Surface**: `FilterEffectRenderNode`, `TransformRenderNode`, `ContainerRenderNode`, push-state nodes (`ClipRenderNode`, `LayerRenderNode`, `OpacityMaskRenderNode`-style), and any future transformer node.

**Audience**: authors of transformer `RenderNode` subclasses. Drawable / FilterEffect / Shape authors do not touch this.

## The contract in one paragraph

A transformer RenderNode reads its upstream operation(s)' `CorrectionScale`, converts its own length-typed internal parameters from authoring space to raster space (`p_raster = scale.ToRasterX(p_authoring) = p_authoring / scale.ScaleX`) before invoking Skia, computes its output `Bounds` in **authoring space** using the **authored** (un-converted) parameters, and propagates `CorrectionScale` on its output operation. The transformer **does not** re-rasterize; it operates in place on the upstream raster. The numeric convention is fixed in `render-node-operation-scale.md` — `CorrectionScale ≥ 1` is the bounds-over-raster upscale ratio.

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

**Phase 3 implementation split (2026-05-22)**: the actual injection point depends on whether the effect uses `FilterEffectContext`'s primitive helpers or builds a custom filter via `CustomEffect`:

- **Primitive-helper effects** (e.g. `Blur` calls `context.Blur(sigma)`, `DropShadow` calls `context.DropShadow(...)`): the division happens **inside `FilterEffectContext`**. `FilterEffectRenderNode` sets `feContext.CorrectionScale = unifiedUpstreamScale` before invoking `effect.ApplyTo(feContext, resource)`. The primitive method body divides at the call site (e.g. `Blur(sigma)` builds its data tuple with `rasterSigma = DivideLength(sigma)`; the Skia factory consumes the raster sigma; `transformBounds` consumes the authored sigma). The effect's `.cs` file is **unmodified**.
- **CustomEffect-based effects** (e.g. `StrokeEffect`, `ColorShift`, `DisplacementMapTransform`, `FlatShadow`, `Clipping`, `SplitEffect`, `ShakeEffect`, `MosaicEffect` — 8 of the 13 in-scope): `FilterEffectActivator` exposes the active `CorrectionScale` on `CustomFilterEffectContext.CorrectionScale`. The effect's custom action reads it and divides its own length-typed parameters before invoking Skia. **The effect's `.cs` file is modified.** This is the carve-out from FR-008 documented in `spec.md`.

Worked example — `DropShadow(Position = (10, 10), Sigma = (15, 15))` on input `CorrectionScale = (4, 4)`:

- `FilterEffectRenderNode` sets `feContext.CorrectionScale = (4, 4)` before `effect.ApplyTo`.
- `effect.ApplyTo` calls `feContext.DropShadow(position, sigma, color)` — unmodified.
- `FilterEffectContext.DropShadow` divides: `rasterPosition = (10/4, 10/4) = (2.5, 2.5)`, `rasterSigma = (15/4, 15/4) = (3.75, 3.75)`. Embeds `(rasterPosition, rasterSigma, authoredPosition, authoredSigma, color)` into the data tuple.
- Skia factory uses raster values: `SKImageFilter.CreateDropShadow(dx: 2.5, dy: 2.5, sigmaX: 3.75, sigmaY: 3.75, color)`.
- `transformBounds` uses authored values: `bounds.Union(bounds.Translate(10, 10).Inflate(15, 15))` — in authoring space.
- Output Bounds extends by the authored sigma; output `CorrectionScale = (4, 4)` — propagated unchanged.

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

## Multiple upstream — authoritative policy

When a transformer has more than one upstream operation, the policy is **fixed and authoritative** (no per-node case-by-case discretion). Two patterns are recognized:

### Pattern X — Independent-children container (forward each upstream verbatim)

`ContainerRenderNode` is the canonical example. It aggregates child operations that **do not interact** at the container level (they are placed side-by-side / overlapped, but the container itself does not blend or composite them into a single new raster). Behavior:

- Each child operation flows through unchanged. Its `Bounds` and `CorrectionScale` are forwarded verbatim.
- The container's "output" is the *set* of forwarded operations.
- Different children CAN report different `CorrectionScale` values; that is by design (the whole point of per-clip proxy).
- The final compositor handles each child operation independently per `compositor-blit.md`.

### Pattern Y — Multi-input compositing (unify at MAX upstream `CorrectionScale`)

For transformers that actually composite multiple upstream rasters into a single output raster — blend modes (`BlendEffect`), displacement maps (`DisplacementMapEffect`), composite filter chains, opacity-mask nodes that consume both a content raster and a mask raster — the policy is:

1. Compute `unifiedCorrectionScale = ComponentWiseMax(upstream.Select(u => u.CorrectionScale))` — the largest `ScaleX` and largest `ScaleY` across all upstream. Concretely: if upstream A is `(4, 4)` and upstream B is `(2, 2)`, the unified scale is `(4, 4)`.
2. For each upstream whose `CorrectionScale ≠ unifiedCorrectionScale`, **downsample** that upstream's raster to match the unified scale before compositing. Concretely: B's raster (which was at 1/2 of authoring) is downsampled to 1/4 of authoring to align with A. This loses some quality from B, but the trade-off is intentional — proxy is opt-in for performance, and the lowest-resolution upstream sets the effective ceiling for the composite.
3. Composite the now-aligned rasters using Skia at the unified scale; the transformer's own length-typed parameters use `unifiedCorrectionScale.ToRaster*` for adjustment.
4. The output operation reports `CorrectionScale = unifiedCorrectionScale`.

**Why max, not min**: choosing the max (lowest-resolution upstream) means downsampling other upstream rasters, which is cheap (Skia bilinear filter). Choosing the min would require upsampling at least one upstream — wasteful, since the upstream already chose to live at proxy resolution for performance.

**Why a fixed policy, not per-node discretion**: prior drafts of this contract left the policy "TBD during audit", which Codex review flagged as a critical risk (implementations would diverge). The fixed policy here is the design contract; nodes that genuinely cannot follow it (e.g. a hypothetical filter whose output quality is intolerable at the unified scale) must document the deviation in their own contract.

## Hybrid nodes — authoritative policy

A "hybrid" node is one that:
- Reads one or more upstream operations (consumes their CorrectionScale).
- Materializes a **new** raster at a chosen resolution via `SKSurface.Create` / `SaveLayer` etc.
- Produces an output operation whose raster is that new raster (its own CorrectionScale, potentially different from any upstream).

Examples: `PushLayer` paths that allocate a backing surface, group filtering that pre-rasterizes a sub-graph, screen-space distortion filters that need an explicit intermediate.

Policy:

1. Compute the unified upstream scale per Pattern Y above (or take the single upstream's scale if there is only one).
2. Choose the hybrid's **own** raster size and `CorrectionScale` for output. By default, match the unified upstream — i.e. the hybrid does not re-rasterize at a finer scale than upstream supports. A hybrid that needs to materialize at a different scale (e.g. always at Identity for a quality-critical layer) must document the choice in its own contract.
3. Apply the unified-scale parameter conversion (`ToRaster*`) when invoking Skia on the materialized raster.
4. Report the hybrid's chosen `CorrectionScale` on the output operation. Downstream transformers see this value, not the upstream's.

## Other special cases

- **No upstream** (transformer with zero inputs): treat as a source-producing node and declare CorrectionScale directly per `source-node-proxy.md` Type B.
- **Identity-only upstream**: the conversion math is a no-op (`p / 1 = p`), so transformers that branch on `CorrectionScale == Identity` to skip work for backward compatibility are encouraged but not required.

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
