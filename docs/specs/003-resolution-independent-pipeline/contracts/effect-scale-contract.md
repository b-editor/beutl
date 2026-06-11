# Contract: Effect / drawable / brush / pen scale contract

**Feature**: 003 | FR-008/FR-009/FR-010/FR-011/FR-012/FR-015. Audience: authors of `FilterEffect`, `CustomEffect`, `Drawable`, brushes, and C# script effects (in-tree and plugin).

## The rule (FR-008) — what matters is the COORDINATE SPACE, not the parameter type

> **Reframed 2026-06-09 (Codex review #3).** The original framing — "multiply every *spatial-length* parameter
> by `w`" — is the wrong mental model for this CTM-based renderer and it caused real double-scaling bugs (Shake,
> Perlin, strokes, audio visualizers were all "spatial-length" yet must NOT be multiplied). The true rule is:
> **what scale you apply depends on the coordinate space the value lives in / the API consumes.**

This renderer pushes one root `Matrix.CreateScale(w)` (or `s_out`) at the device boundary, so almost all
geometry is authored and drawn in **logical space** and the CTM scales it to device for free. Classify a value
by its coordinate space:

| Coordinate space | Rule | Examples |
|---|---|---|
| **Logical-space geometry drawn under the CTM** | **Leave unchanged.** The CTM already scales it. | shape/bar geometry, pen thickness/offset/dash (pre-outlined logical, D3), Shake displacement (translates logical bounds), `DrawRectangle`/`DrawPath` coords, gradient stops/points, drop-shadow offset & sigma and dilate/erode radius **when passed to a Skia `SKImageFilter`** (they ride the `CreateScale(w)` CTM in `FilterEffectActivator.Flush`) |
| **Device-buffer dimensions / device-space shader uniforms / device pixel indexing** | **Convert once (`× w`).** These bypass the CTM. | a `CustomEffect` buffer's `ceil(bounds × w)` size; SKSL `iScale`/`width`/`height`/`fragCoord`; a CustomEffect point-blit's absolute-px literal (`MyPixelRadius * c.WorkingScale`); the tile/drawable intermediate raster size (A-1) |
| **Readback-derived geometry (device → logical)** | **Convert device back to logical (`÷ w`).** | `ContourTracer` / `PartsSplit` vertices traced from the device alpha mask (`/w` so `CreateTarget` re-densifies) |
| **Magnitude-invariant** | **Leave unchanged.** Not a length. | color; angle; percentage; ratio; 0..1 value; `RelativePoint`/`RelativeRect`; blend mode; count; enum; `MiterLimit`; caps/joins/alignment; `Trim*` |

**Non-obvious cases (empirically settled on this branch):**
- `PerlinNoiseBrush.BaseFrequency` is **left unchanged** — `SkPerlinNoiseShader` already follows the CTM, so its period is logical-invariant; dividing by `w` was tried and made the reduced-scale result *worse* (the dossier's "÷w" recommendation was wrong for this CTM pipeline). Its reduced-scale softness is accepted best-effort (FR-013).
- Text is **re-shaped** at `Size × w` (it reads the device font size, not a CTM-scaled outline — `Hinting=Full` bakes resolution-specific grid-fitting); never matrix- or bitmap-scaled (FR-012).
- **A Skia `SKImageFilter` primitive (Blur/DropShadow/Dilate/Erode) takes its length args RAW** — do NOT `× w`; it rides the `CreateScale(w)` CTM. Only **device-buffer / device-shader** code multiplies.
- **Anisotropic transforms** (FR-019): a scalar `EffectiveScale` projects onto the most-detailed axis, which can over-allocate; the buffer is now bounded by `ClampWorkingScaleToBufferBudget` (FR-037 backstop).

## How an author reads the active scale (FR-015)

Two accessors, both default `1.0`. **They expose the `WorkingScale` `w`** (what the effect runs at), not the output scale. An effect that needs the eventual delivery target reads `FilterEffectContext.OutputScale`.

1. **`FilterEffectContext.WorkingScale`** — for `CSharpScriptEffect` and out-of-tree `FilterEffect`s built from the context primitives. The Skia `SKImageFilter` primitives (`Blur`/`DropShadow`/`Dilate`/`Erode`/`Transform`/`MatrixConvolution`) take their spatial-length args **raw (logical)** — they are **NOT** multiplied by `WorkingScale`; they ride the `CreateScale(w)` CTM that `FilterEffectActivator.Flush` pushes, so Skia scales them for free. An effect that forwards through them inherits scale-correctness **without multiplying anything** (multiplying would double-scale). Only **CustomEffect point-blit** code (Mosaic/InnerShadow/ColorShift/…) multiplies its absolute-length args by `WorkingScale` (those blit into a `ceil(bounds × w)` device buffer instead of riding the CTM).
2. **`CustomFilterEffectContext.WorkingScale`** — for `CustomEffect` / SKSL / GLSL. `CreateTarget(bounds)` allocates `ceil(bounds × WorkingScale)`; `Open` returns that buffer's canvas tagged with the working density but with an **identity CTM** — nothing is pre-scaled. To draw logical content the effect pushes `Matrix.CreateScale(WorkingScale)` itself and draws through the `ImmediateCanvas` APIs (the `StrokeEffect` pattern — this also routes brush fills through the canvas's density so tile/image/drawable brushes rasterize at `w`); code that works directly in device space instead multiplies absolute pixel literals by `WorkingScale`.

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
        // so to use a custom w you reproduce the Process body with your own value. Today this is
        // a full copy of the base flow — only the `workingScale =` line differs (a separate PR
        // improves FilterEffectRenderNode's general customizability, shrinking this copy surface):
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

> **Note (as shipped):** the `CreateRenderNode()` override **is now honoured on every push path** — `FilterEffect.Resource.Push` routes through `CreateRenderNode()` (2026-06-10), so an effect on a normal `Drawable` (not just the node-graph path) gets its custom `FilterEffectRenderNode`. What is **still deferred** is the ergonomics: `FilterEffectRenderNode.Process` computes the supply-driven `w` inline, so a subclass that wants a *different* `w` must copy the whole `Process` body (calling `base.Process(context)` runs supply-driven and silently ignores any `w` the subclass computed). Overriding `FilterEffectRenderNode` is a general customization point — the working scale is only one of the reasons to do it — so the follow-up planned as a **separate PR improves the node's overall customizability** (reducing how much of `Process` a subclass must reproduce), rather than adding a working-scale-specific hook: a narrow `protected virtual float ResolveWorkingScale(RenderNodeContext)` seam was considered and **will not be added**. So today: overriding the *whole* `Process` works end-to-end; there is just no shortcut for the "only change `w`" case yet.

## Working scale — what scale an effect runs at

Every effect runs at the **supply-driven working scale `w`**, computed from its inputs' effective scales (there is **no per-effect policy knob**):

- `w` = the densest **concrete** (bitmap) input density. A 0.5 proxy stays 0.5 (no upsample), a 2.0 source stays 2.0 (no downsample). `s_out` is **not** a ceiling.
- vector-only inputs (`Unbounded`) impose no supply → `w` falls back to `s_out`; a mixed bitmap+vector boundary floors `w` at `s_out` so crisp vector siblings are not dragged down to a low-density bitmap.
- `w` is finally capped by the global ceiling `MaxWorkingScale` (FR-037; **preview `2 × s_out`, export `max(8, 4 × s_out)`** — a generous finite bound). This is the sole **global** ceiling (`FilterEffectRenderNode` passes `context.MaxWorkingScale`); additionally the per-buffer **dimension** clamp (FR-037(b), `RenderNodeContext.ClampWorkingScaleToBufferBudget`, 16384 px per axis) may further reduce `w` at the effect boundary — at the node level and again per target at `Flush` against the post-effect-inflated bounds — to keep the buffer allocatable. Two distinct bounds; do not conflate them.

**Every built-in runs supply-driven** — including the FR-013 resolution-sensitive set (`PixelSort`, contour `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, `Dilate`, `Erode`, `Mosaic`, custom SKSL/GLSL, image-map `Displacement`), since running at the supply density already keeps a high source's density through them. The working scale MUST NOT change the `s_out = 1.0` output.

**Need a different working scale?** An effect that genuinely needs clamp-to-output (perf) or oversampling (SSAA) returns a `FilterEffectRenderNode` subclass from `FilterEffect.Resource.CreateRenderNode()` and overrides `Process` to compute its own `w` (see the example above). There is intentionally no declarative `ResolutionPolicy` — no built-in needed one, and a custom render node is strictly more flexible than a closed enum. *(Earlier drafts had an `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy; it was removed.)*

## Resolution-sensitive effects (FR-013)

`PixelSort`, contour-based `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, integer `Dilate`/`Erode`, `Mosaic`, Perlin-driven, and custom per-texel shaders **still follow the coordinate-space rule above** (their Skia-`SKImageFilter` args — e.g. the Dilate/Erode radius — ride the CTM unchanged; `PerlinNoiseBrush.BaseFrequency` is left unchanged; only their device-buffer / point-blit code converts `× w` once and contour readback converts `÷ w`), but their reduced-scale preview is a **best-effort approximation** (not bit-identical) and is full-fidelity only at export `s_out=1.0`. Running supply-driven already keeps a higher-resolution source through them (downsampled only at the final stage). No force-full-scale subtree mechanism and no warning UI in v1. Their tests assert (a) byte-equality at `s_out=1.0` and (b) a documented structural invariant at a reduced scale (e.g. `mosaic tile == ceil(tileSize × w)` device px), not SSIM.

## Mechanism summary

Centralized scaling lives in the `FilterEffectContext` primitives (covers built-ins and their forwarders for free); the per-effect read accessor (`WorkingScale`) is the escape hatch for pixel-reading custom/shader/script effects, and a custom `FilterEffectRenderNode` is the escape hatch for *what scale* the effect runs at. A plugin effect that touches neither still renders correctly at `s_out=1.0` (supply-driven + `Unbounded` inputs → `w=1.0`); it simply runs supply-driven and won't drive oversampling until it adopts this contract.
