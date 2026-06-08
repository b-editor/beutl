# Contract: Effect / drawable / brush / pen scale contract

**Feature**: 003 | FR-008/FR-009/FR-010/FR-011/FR-012/FR-015. Audience: authors of `FilterEffect`, `CustomEffect`, `Drawable`, brushes, and C# script effects (in-tree and plugin).

## The one rule (FR-008)

When an effect's **working scale** is `w` (the supply-driven scale it actually runs at — FR-036, **NOT** the output scale `s_out`):
- **Multiply by `w`** every **spatial-length / pixel-magnitude** parameter.
- **Leave unchanged** every **magnitude-invariant** parameter.

| Multiply by `w` (spatial length) | Leave unchanged (magnitude-invariant) |
|---|---|
| blur sigma; drop/inner-shadow offset & sigma; flat-shadow length; dilate/erode radius; mosaic tile size; color-shift offset; displacement translate; pen thickness & offset & dash; stroke offset; particle pixel sizes; audio-visualizer bar width / block gap / pixel minimums; tile/drawable intermediate raster resolution | color; angle/degrees/radians; percentage; ratio; 0..1 value; `RelativePoint`/`RelativeRect`; blend mode; division count; enum; `MiterLimit`; caps/joins/alignment; `Trim*` |

**Non-obvious cases**:
- `PerlinNoiseBrush.BaseFrequency` is **divided** by `w` (period invariant in logical units), centralized in `BrushConstructor.CreatePerlinNoiseShader`.
- Text is **re-shaped** at `Size × w` (font size, spacing, stroke; `Hinting=Full`) — never matrix- or bitmap-scaled (FR-012).
- Strokes are pre-outlined in logical space and scaled by the root CTM — pen authors do **nothing** (D3).

## How an author reads the active scale (FR-015)

Two accessors, both default `1.0`. **They expose the `WorkingScale` `w`** (what the effect runs at), not the output scale. An effect that needs the eventual delivery target reads `FilterEffectContext.OutputScale`.

1. **`FilterEffectContext.WorkingScale`** — for `CSharpScriptEffect` and out-of-tree `FilterEffect`s built from the context primitives. The Skia `SKImageFilter` primitives (`Blur`/`DropShadow`/`Dilate`/`Erode`/`Transform`/`MatrixConvolution`) take their spatial-length args **raw (logical)** — they are **NOT** multiplied by `WorkingScale`; they ride the `CreateScale(w)` CTM that `FilterEffectActivator.Flush` pushes, so Skia scales them for free. An effect that forwards through them inherits scale-correctness **without multiplying anything** (multiplying would double-scale). Only **CustomEffect point-blit** code (Mosaic/InnerShadow/ColorShift/…) multiplies its absolute-length args by `WorkingScale` (those blit into a `ceil(bounds × w)` device buffer instead of riding the CTM).
2. **`CustomFilterEffectContext.WorkingScale`** — for `CustomEffect` / SKSL / GLSL. `CreateTarget(bounds)` allocates `ceil(bounds × WorkingScale)` and `Open` returns a pre-scaled canvas, so a custom effect drawing into its target gets the right resolution automatically; absolute pixel literals in the author's draw code are multiplied by `WorkingScale`.

```csharp
// out-of-tree FilterEffect example
public override void ApplyTo(FilterEffectContext context)
{
    // sigma is a logical length -> pass it RAW; the Skia primitive rides the root CTM, so do NOT multiply by w
    context.Blur(new Size(BlurRadius, BlurRadius));

    // a custom step that hand-picks a pixel literal:
    context.CustomEffect(state, (d, c) =>
    {
        float devRadius = MyPixelRadius * c.WorkingScale; // explicit
        // draw into c.CreateTarget(bounds) which is already ceil(bounds*WorkingScale)
    });
}

// An effect that needs a working scale OTHER than the supply density (clamp-to-output for perf,
// oversample for SSAA) overrides the render node instead of declaring a policy:
public sealed partial class Resource
{
    public override FilterEffectRenderNode CreateRenderNode() => new OversampleRenderNode(this);
}

private sealed class OversampleRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        // base.Process recomputes the supply-driven w itself and ignores any w you compute here,
        // so to use a custom w you reproduce the Process body with your own value. Until the
        // separate-PR `protected virtual ResolveWorkingScale(context)` seam lands, this is a full
        // copy of the base flow — only the `workingScale =` line differs:
        //
        //   var inputScales = ...; // = context.Input[i].EffectiveScale
        //   float supplyW = RenderNodeContext.ResolveWorkingScale(inputScales, context.OutputScale, context.MaxWorkingScale);
        //   float workingScale = MathF.Min(MathF.Max(supplyW, 2f * context.OutputScale), context.MaxWorkingScale); // SSAA-on-demand
        //   using var feContext = new FilterEffectContext(context.CalculateBounds(), context.OutputScale, workingScale);
        //   ... (the rest of FilterEffectRenderNode.Process verbatim) ...
        return base.Process(context); // placeholder — supply-driven; see the copy above for a custom w
    }
}
```

> **Note (as shipped):** there is currently **no lightweight hook** to inject a custom working scale — `FilterEffectRenderNode.Process` computes the supply-driven `w` inline, so a custom-`w` effect must copy the whole `Process` body. A one-method `protected virtual ResolveWorkingScale(RenderNodeContext)` seam is **deferred to a separate PR**; until then, calling `base.Process(context)` runs supply-driven and silently ignores any `w` the subclass computes.

## Working scale — what scale an effect runs at

Every effect runs at the **supply-driven working scale `w`**, computed from its inputs' effective scales (there is **no per-effect policy knob**):

- `w` = the densest **concrete** (bitmap) input density. A 0.5 proxy stays 0.5 (no upsample), a 2.0 source stays 2.0 (no downsample). `s_out` is **not** a ceiling.
- vector-only inputs (`Unbounded`) impose no supply → `w` falls back to `s_out`; a mixed bitmap+vector boundary floors `w` at `s_out` so crisp vector siblings are not dragged down to a low-density bitmap.
- `w` is finally capped by the global ceiling `MaxWorkingScale` (FR-037; **preview `2 × s_out`, export `max(8, 4 × s_out)`** — a generous finite bound). This is the **sole** upper bound; `FilterEffectRenderNode` passes `context.MaxWorkingScale`.

**Every built-in runs supply-driven** — including the FR-013 resolution-sensitive set (`PixelSort`, contour `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, `Dilate`, `Erode`, `Mosaic`, custom SKSL/GLSL, image-map `Displacement`), since running at the supply density already keeps a high source's density through them. The working scale MUST NOT change the `s_out = 1.0` output.

**Need a different working scale?** An effect that genuinely needs clamp-to-output (perf) or oversampling (SSAA) returns a `FilterEffectRenderNode` subclass from `FilterEffect.Resource.CreateRenderNode()` and overrides `Process` to compute its own `w` (see the example above). There is intentionally no declarative `ResolutionPolicy` — no built-in needed one, and a custom render node is strictly more flexible than a closed enum. *(Earlier drafts had an `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy; it was removed.)*

## Resolution-sensitive effects (FR-013)

`PixelSort`, contour-based `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, integer `Dilate`/`Erode`, `Mosaic`, Perlin-driven, and custom per-texel shaders **still apply the multiply rule**, but their reduced-scale preview is a **best-effort approximation** (not bit-identical) and is full-fidelity only at export `s_out=1.0`. Running supply-driven already keeps a higher-resolution source through them (downsampled only at the final stage). No force-full-scale subtree mechanism and no warning UI in v1. Their tests assert (a) byte-equality at `s_out=1.0` and (b) a documented structural invariant at a reduced scale (e.g. `mosaic tile == ceil(tileSize × w)` device px), not SSIM.

## Mechanism summary

Centralized scaling lives in the `FilterEffectContext` primitives (covers built-ins and their forwarders for free); the per-effect read accessor (`WorkingScale`) is the escape hatch for pixel-reading custom/shader/script effects, and a custom `FilterEffectRenderNode` is the escape hatch for *what scale* the effect runs at. A plugin effect that touches neither still renders correctly at `s_out=1.0` (supply-driven + `Unbounded` inputs → `w=1.0`); it simply runs supply-driven and won't drive oversampling until it adopts this contract.
