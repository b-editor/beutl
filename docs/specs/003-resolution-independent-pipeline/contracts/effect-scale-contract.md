# Contract: Effect / drawable / brush / pen scale contract

**Feature**: 003 | FR-008/FR-009/FR-010/FR-011/FR-012/FR-015. Audience: authors of `FilterEffect`, `CustomEffect`, `Drawable`, brushes, and C# script effects (in-tree and plugin).

## The rule (FR-008) — what matters is the COORDINATE SPACE, not the parameter type

> **Reframed 2026-06-09 (Codex review #3).** The original "multiply every *spatial-length* parameter
> by `w`" framing is wrong for this CTM-based renderer and caused real double-scaling bugs (Shake,
> Perlin, strokes, audio visualizers are all "spatial-length" yet must NOT be multiplied). The true rule:
> **the scale you apply depends on the coordinate space the value lives in / the API consumes.**

Every `ImmediateCanvas` **bakes** a base CTM `Matrix.CreateScale(w)` (or `s_out` at the root) at construction
(feature 003 — a no-op at density 1), so almost all geometry is authored in **logical space**
and the base CTM scales it to device for free. Device-space code opts out with `canvas.PushDeviceSpace()`
(CTM → identity, density → 1). Classify a value by its coordinate space:

| Coordinate space | Rule | Examples |
|---|---|---|
| **Logical-space geometry drawn under the CTM** | **Leave unchanged.** The CTM already scales it. | shape/bar geometry, pen thickness/offset/dash (pre-outlined logical, D3), Shake displacement (translates logical bounds), `DrawRectangle`/`DrawPath` coords, gradient stops/points, drop-shadow offset & sigma and dilate/erode radius **when passed to a Skia `SKImageFilter`** (they ride the `CreateScale(w)` CTM in `FilterEffectActivator.Flush`) |
| **Device-buffer dimensions / device-space shader uniforms / device pixel indexing** | **Convert once (`× w`).** These bypass the CTM. | a `CustomEffect` buffer's `ceil(bounds × w)` size; SKSL `iScale`/`width`/`height`/`fragCoord`; a CustomEffect point-blit's absolute-px literal (`MyPixelRadius * c.WorkingScale`); the tile/drawable intermediate raster size (A-1) |
| **Readback-derived geometry (device → logical)** | **Convert device back to logical (`÷ w`).** | `ContourTracer` / `PartsSplit` vertices traced from the device alpha mask (`/w` so `CreateTarget` re-densifies) |
| **Magnitude-invariant** | **Leave unchanged.** Not a length. | color; angle; percentage; ratio; 0..1 value; `RelativePoint`/`RelativeRect`; blend mode; count; enum; `MiterLimit`; caps/joins/alignment; `Trim*` |

**Non-obvious cases (empirically settled on this branch):**
- `PerlinNoiseBrush.BaseFrequency` is **left unchanged** — `SkPerlinNoiseShader` follows the CTM, so its period is logical-invariant; dividing by `w` made the reduced-scale result *worse* (the dossier's "÷w" was wrong for this CTM pipeline). Reduced-scale softness is accepted best-effort (FR-013).
- Text is **re-shaped** at `Size × w` (it reads the device font size, not a CTM-scaled outline — `Hinting=Full` bakes resolution-specific grid-fitting); never matrix- or bitmap-scaled (FR-012).
- **A Skia `SKImageFilter` primitive (Blur/DropShadow/Dilate/Erode) takes its length args RAW** — do NOT `× w`; it rides the `CreateScale(w)` CTM. Only **device-buffer / device-shader** code multiplies.
- **Anisotropic transforms** (FR-019): a scalar `EffectiveScale` projects onto the most-detailed axis, which can over-allocate; the buffer is bounded by `ClampWorkingScaleToBufferBudget` (FR-037 backstop).

## How an author reads the active scale (FR-015)

Two accessors, both default `1.0`. **They expose the `WorkingScale` `w`** (what the effect runs at), not the output scale. An effect that needs the eventual delivery target reads `FilterEffectContext.OutputScale`.

1. **`FilterEffectContext.WorkingScale`** — for `CSharpScriptEffect` and out-of-tree `FilterEffect`s built from the context primitives. The Skia `SKImageFilter` primitives (`Blur`/`DropShadow`/`Dilate`/`Erode`/`Transform`/`MatrixConvolution`) take their spatial-length args **raw (logical)** — they are **NOT** multiplied by `WorkingScale`; they ride the `CreateScale(w)` CTM that `FilterEffectActivator.Flush` pushes, so Skia scales them for free. An effect that forwards through them inherits scale-correctness **without multiplying anything** (multiplying would double-scale). Only **CustomEffect point-blit** code (Mosaic/InnerShadow/ColorShift/…) multiplies its absolute-length args by `WorkingScale` (those blit into a `ceil(bounds × w)` device buffer instead of riding the CTM).
2. **`CustomFilterEffectContext.WorkingScale`** — for `CustomEffect` / SKSL / GLSL. `CreateTarget(bounds)` allocates `ceil(bounds × WorkingScale)`; `Open` returns that buffer's canvas **already carrying the baked base CTM `CreateScale(density)`** (the buffer's real, post-clamp density — read it from the returned target's `Scale.Value` for clamp-correct device math). So the effect draws **logical** content directly through the `ImmediateCanvas` APIs with **no manual prescale** (the `StrokeEffect` pattern — this also routes brush fills through the canvas's density so tile/image/drawable brushes rasterize at `w`). Code that must work in **device pixels** — point-blitting another device buffer, a contour traced from the device alpha mask, a full-buffer shader rect — wraps that draw in **`canvas.PushDeviceSpace()`** (CTM → identity, density → 1) and uses device-px literals (`× WorkingScale`). **Clamp caveat (FR-037(b)) — `WorkingScale` is the REQUESTED density; the allocated buffer can be CLAMPED below it.** On a **large-bounds** frame the allocated target is reduced by `ClampWorkingScaleToBufferBudget` (FR-037(b)) to stay within the 16384-px GPU axis limit, and the created target carries that **lower** density in its `Scale.Value`. Device-pixel author math (point-blit offsets, shader resolution uniforms, absolute-px literals) multiplying by the **bare `WorkingScale`** then computes coordinates for a *denser* buffer than was allocated and **mis-registers** (the draw lands at the wrong pixels). So such code MUST read the **created target's `Scale.Value`**, not `WorkingScale`. An effect that builds device-px values **before** it holds a target to read `Scale.Value` from — the in-tree shader effects compute uniforms up front, and `GLSLShader.Apply` hands its callback the *source* target — must recompute the density `CreateTarget` will resolve: `RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, WorkingScale)` on the **same `bounds`** passed to `CreateTarget` (the one canonical clamp `CreateTarget` itself calls, so the result is identical by construction — see `Mosaic`/`ColorShift`/`Displacement`/`SKSLScriptEffect`/`GLSLScriptEffect`). When you *do* hold the created target, read its `Scale.Value` directly (the InnerShadow/BlendMode pattern).

```csharp
// out-of-tree FilterEffect example
public override void ApplyTo(FilterEffectContext context)
{
    // sigma is a logical length -> pass it RAW; the Skia primitive rides the root CTM, so do NOT multiply by w
    context.Blur(new Size(BlurRadius, BlurRadius));

    // a custom step: logical content draws directly (the canvas bakes CreateScale(density));
    // device-pixel work (point-blit / contour / shader rect) wraps in PushDeviceSpace.
    context.CustomEffect(state, (d, c) =>
    {
        EffectTarget t = c.CreateTarget(bounds);          // ceil(bounds * WorkingScale) device buffer
        using ImmediateCanvas canvas = c.Open(t);         // base CTM = CreateScale(t.Scale.Value), already baked
        canvas.DrawRectangle(logicalRect, brush, null);   // LOGICAL — no manual prescale
        using (canvas.PushDeviceSpace())                  // absolute device px
        {
            float devRadius = MyPixelRadius * t.Scale.Value; // post-clamp density
            // ... device-px draw (e.g. canvas.DrawRenderTarget(other, devicePoint)) ...
        }
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
        // base.Process recomputes the supply-driven w and ignores any w you compute here,
        // so a custom w means reproducing the Process body with your own value. Today this is
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

> **Note (as shipped):** the `CreateRenderNode()` override **is now honoured on every push path** — `FilterEffect.Resource.Push` routes through `CreateRenderNode()` (2026-06-10), so an effect on a normal `Drawable` (not just the node-graph path) gets its custom `FilterEffectRenderNode`. **Still deferred** is the ergonomics: `FilterEffectRenderNode.Process` computes the supply-driven `w` inline, so a subclass wanting a *different* `w` must copy the whole `Process` body (`base.Process(context)` runs supply-driven and silently ignores any `w` the subclass computed). Overriding `FilterEffectRenderNode` is a general customization point — working scale is only one reason to do it — so the follow-up is a **separate PR improving the node's overall customizability** (reducing how much of `Process` a subclass must reproduce), not a working-scale-specific hook: a narrow `protected virtual float ResolveWorkingScale(RenderNodeContext)` seam was considered and **will not be added**. So today: overriding the *whole* `Process` works end-to-end, but there is no shortcut for the "only change `w`" case yet.

## Working scale — what scale an effect runs at

Every effect runs at the **supply-driven working scale `w`**, computed from its inputs' effective scales (there is **no per-effect policy knob**). The rule is `w = min( max(s_out, densest concrete supply), MaxWorkingScale )` *(amended 2026-06-15 — `s_out` is the FLOOR; the earlier "a 0.5 proxy stays 0.5" wording is superseded)*:

- `w` is **floored at `s_out`** (the deliverable density) and **raised by the densest concrete (bitmap) input above it**. A 2.0 source runs at 2.0 (no downsample — `s_out` is **not** a ceiling). A **sub-output** concrete supply — an enlarged / low-density bitmap, `At(0.5)` — feeding an effect at a `1.0` export is **floored to `w = 1.0`** (rendering at the deliverable density, matching the pre-feature renderer), **not** held at 0.5. Why: an effect's own working resolution (its blur kernel / shadow / shader grid) is distinct from the source's available *detail* — running it below `s_out` only discards resolution the delivery target can use, without fabricating source detail. A genuine reduced-scale proxy is still cheap in **preview**: at a `0.5` preview a `0.5` proxy gives `max(0.5, 0.5) = 0.5`.
- vector-only inputs (`Unbounded`) impose no supply → `w` stays at the `s_out` floor; a mixed bitmap+vector boundary likewise lands at `s_out` when no concrete input exceeds it, so crisp vector siblings are not dragged down to a low-density bitmap — now an instance of the universal floor, no longer a special case.
- `w` is finally capped by the global ceiling `MaxWorkingScale` (FR-037; **preview `2 × s_out`, export `+∞`** — export imposes no working-scale quality ceiling, *amended 2026-06-15*). This is the sole **global** ceiling (`FilterEffectRenderNode` passes `context.MaxWorkingScale`); additionally the per-buffer **dimension** clamp (FR-037(b), `RenderNodeContext.ClampWorkingScaleToBufferBudget`, 16384 px per axis) is the sole **allocatability** bound and may further reduce `w` at the effect boundary — at the node level and again per target at `Flush` against the post-effect-inflated bounds. Two distinct bounds; do not conflate them.

**Every built-in runs supply-driven** — including the FR-013 resolution-sensitive set (`PixelSort`, contour `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, `Dilate`, `Erode`, `Mosaic`, custom SKSL/GLSL, image-map `Displacement`), since running at the supply density already keeps a high source's density through them. The working scale MUST NOT change the `s_out = 1.0` output.

**Need a different working scale?** An effect that genuinely needs clamp-to-output (perf) or oversampling (SSAA) returns a `FilterEffectRenderNode` subclass from `FilterEffect.Resource.CreateRenderNode()` and overrides `Process` to compute its own `w` (see the example above). There is intentionally no declarative `ResolutionPolicy` — no built-in needed one, and a custom render node is more flexible than a closed enum. *(Earlier drafts had an `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy; it was removed.)*

> **Footgun — `w` is per-boundary, not per-op; and shrinking a source makes it *more* expensive (2026-06-15).** `w` = the **densest concrete input** applies to the **whole buffer-allocating boundary**, so a single small high-density sibling raises the working scale — and thus the buffer **area** (`∝ w²`) — of the *entire* boundary, not just its own region. Example: a 4K logo shrunk into a corner carries `At(16)` density; under a shared effect (or any container allocating one buffer for the group) it lifts the whole boundary to `w = 16`, allocating a `16×`-denser buffer for mostly-low-density content. Because density is *backing pixels per logical unit*, scaling a high-resolution source **down** **raises** its density — so the source gets **more** expensive the smaller you draw it (inverting the usual "smaller = cheaper" intuition). The per-buffer **dimension** clamp (`ClampWorkingScaleToBufferBudget`) keeps such a buffer *allocatable* but does not stop it from dominating the boundary's cost. **Follow-up (deferred):** per-target (per-region) `w` scoping and a request-scoped area/byte budget — so a small dense sibling raises only its own region's density — are the proper fix.

## Resolution-sensitive effects (FR-013)

`PixelSort`, contour-based `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, integer `Dilate`/`Erode`, `Mosaic`, Perlin-driven, and custom per-texel shaders **still follow the coordinate-space rule above** (their Skia-`SKImageFilter` args — e.g. the Dilate/Erode radius — ride the CTM unchanged; `PerlinNoiseBrush.BaseFrequency` is left unchanged; only their device-buffer / point-blit code converts `× w` once and contour readback converts `÷ w`), but their reduced-scale preview is a **best-effort approximation** (not bit-identical), full-fidelity only at export `s_out=1.0`. Running supply-driven already keeps a higher-resolution source through them (downsampled only at the final stage). No force-full-scale subtree mechanism and no warning UI in v1. Their tests assert (a) byte-equality at `s_out=1.0` and (b) a documented structural invariant at a reduced scale (e.g. `mosaic tile == ceil(tileSize × w)` device px), not SSIM.

## Reporting a density: `EffectiveScale.At` throws — use `AtOrUnbounded` on the pull path (2026-06-15)

A custom `RenderNodeOperation` / `EffectiveScale` override that *reports* a supply density (e.g. a plugin op whose density is `sourcePixels / logicalWidth`) must respect the two factories:

- **`EffectiveScale.At(scale)` THROWS** (`ArgumentOutOfRangeException`) on a non-finite / non-positive density. A density derived from **animatable geometry** can momentarily go degenerate — `0/0 = NaN` on a collapsed bound, `x/0 = ∞` on an off-screen clip. **The render pull path has no try/catch, so a throw from `At` aborts the whole render** (the export frame, not just that op).
- **`EffectiveScale.AtOrUnbounded(scale)`** is the **non-throwing pull-path factory**: it returns `At(scale)` for a positive-finite density and **degrades a bad density to `Unbounded`** (the safe re-rasterizable default — the op then rasterizes at the consumer's working scale).

So a plugin override that derives a density from animatable geometry MUST either **pre-guard the quotient** (as `TransformRenderNode.RescaleDensity` does — clamp the factor, then re-check the quotient is finite-positive) **or use `AtOrUnbounded`**. Reserve `At` for densities already proven finite-positive.

Relatedly, `RenderNodeContext` **sanitizes degenerate inputs once at construction** so downstream consumers (effects, particles, 3D) inherit a safe density without re-validating: a degenerate `OutputScale` (`0` / `NaN` / `∞`) becomes `1`, and a degenerate `MaxWorkingScale` (`NaN` / `≤ 0`) becomes `+∞` (no ceiling — it can never NaN-propagate into `w` or pull it to zero). `ResolveWorkingScale` and `ClampWorkingScaleToBufferBudget` harden the same way (a non-finite/non-positive `outputScale` is treated as `1`; a non-finite `w`/bounds passes through unchanged).

## Mechanism summary

Centralized scaling lives in the `FilterEffectContext` primitives (covers built-ins and their forwarders for free); the per-effect read accessor (`WorkingScale`) is the escape hatch for pixel-reading custom/shader/script effects, and a custom `FilterEffectRenderNode` is the escape hatch for *what scale* the effect runs at. A plugin effect that touches neither still renders correctly at `s_out=1.0` (supply-driven + `Unbounded` inputs → `w=1.0`); it simply runs supply-driven and won't drive oversampling until it adopts this contract.
