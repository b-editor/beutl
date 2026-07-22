# Contract: Effect / drawable / brush / pen scale contract

**Feature**: 003 | FR-008/FR-009/FR-010/FR-011/FR-012/FR-015. Audience: authors of `FilterEffect`, `CustomEffect`, `Drawable`, brushes, and C# script effects (in-tree and plugin).

> **Superseded API note (Feature 004):** the scale semantics below remain current, but Feature 004 replaces
> `RenderNodeOperation`/`RenderNodeProcessor` with recorded `RenderNode` fragments. API examples and
> migration rules in this contract use the Feature 004 surface.

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
- **Anisotropic transforms** (FR-019): a scalar `EffectiveScale` projects onto the most-detailed axis, which can over-allocate; general policy uses `RenderScaleUtilities.ClampWorkingScaleToBufferBudget`, while allocation contexts exact-check their canonical device footprint (FR-037 backstop).

## How an author reads the active scale (FR-015)

Two execution surfaces expose the `WorkingScale` `w` (what the effect runs at), not the output scale. An effect that needs the eventual delivery target reads `FilterEffectContext.OutputScale`. Author-time access is availability-checked; it never substitutes `1.0` for symbolic metadata.

1. **`FilterEffectContext.TryGetWorkingScale(out float)` / `WorkingScale`** — for `CSharpScriptEffect` and out-of-tree `FilterEffect`s built from the context primitives. The value is author-readable only when recording has one concrete effect input. A symbolic owning-target domain or multiple concrete branches returns `false`; the `WorkingScale` getter throws instead of exposing a provisional or aggregate value as final. Record scale-independent structure in that case and use the execution-time Shader/Geometry/CustomEffect context for device math. Even when available, this is the nominal effect-input density: a later bounds-expanding operation may apply the per-buffer dimension clamp and run below it. The Skia `SKImageFilter` primitives (`Blur`/`DropShadow`/`Dilate`/`Erode`/`Transform`/`MatrixConvolution`) take their spatial-length args **raw (logical)** — they are **NOT** multiplied by `WorkingScale`; they ride the `CreateScale(w)` CTM that `FilterEffectActivator.Flush` pushes, so Skia scales them for free. An effect that forwards through them inherits scale-correctness **without multiplying anything** (multiplying would double-scale). Only **CustomEffect point-blit** code (Mosaic/InnerShadow/ColorShift/…) multiplies its absolute-length args by its execution-time working scale (those blit into a `ceil(bounds × w)` device buffer instead of riding the CTM).
2. **`CustomFilterEffectContext.WorkingScale`** — for `CustomEffect` / SKSL / GLSL. `CreateTarget(bounds)` allocates the canonical `DeviceBufferBounds(bounds, density)` footprint; `Open` returns that buffer's canvas **already carrying the baked base CTM `CreateScale(density)`** (the buffer's real, post-clamp density — read it from the returned target's `Scale.Value` for clamp-correct device math). So the effect draws **logical** content directly through the `ImmediateCanvas` APIs with **no manual prescale** (the `StrokeEffect` pattern — this also routes brush fills through the canvas's density so tile/image/drawable brushes rasterize at `w`). Code that must work in **device pixels** — point-blitting another device buffer, a contour traced from the device alpha mask, a full-buffer shader rect — wraps that draw in **`canvas.PushDeviceSpace()`** (CTM → identity, density → 1) and uses device-px literals (`× WorkingScale`). **Clamp caveat (FR-037(b)) — `WorkingScale` is the REQUESTED density; the allocated buffer can be CLAMPED below it.** On a **large-bounds** frame the allocated target is reduced by the per-buffer clamp to stay within the 16384-px GPU axis limit, and the created target carries that **lower** density in its `Scale.Value`. Device-pixel author math multiplying by the **bare `WorkingScale`** then computes coordinates for a denser buffer than was allocated and mis-registers. Such code MUST read the created target's `Scale.Value`. Code that must bind values before allocation calls `c.ResolveTargetDensity(bounds)` on the exact `bounds` passed to `CreateTarget`; shader code that receives an existing source target uses that target's concrete `Scale`, `DeviceBounds`, `RasterBounds`, and backing dimensions so AA apron pixels are neither stretched nor discarded.

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
        EffectTarget t = c.CreateTarget(bounds);          // canonical PixelRect device footprint
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
// oversample for SSAA) overrides the render node's declarative scale hook:
public sealed partial class Resource
{
    public override FilterEffectRenderNode CreateRenderNode() => new OversampleRenderNode(this);
}

private sealed class OversampleRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
        static metadata => MathF.Min(
            MathF.Max(
                RenderScaleUtilities.ResolveWorkingScale(
                    metadata.InputSupplies.ToArray(),
                    metadata.OutputScale,
                    metadata.MaxWorkingScale),
                2f * metadata.OutputScale),
            metadata.MaxWorkingScale),
        typeof(OversampleRenderNode));

    protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
}
```

`FilterEffect.Resource.Push` honours `CreateRenderNode()` on every push path. `GetWorkingScaleContract()` keeps the base isolation, transactional `ApplyTo`, typed lowering, and resource transfer. Its policy is folded into the first surviving Shader, Geometry, or legacy operation; it does not create an identity map or an extra pass. A no-item result is a true pass-through and commits neither provisional isolation nor unused owned resources. The hook and resolver remain lazy when `ApplyTo` does not probe `WorkingScale`; an explicit probe can evaluate them even when no item is ultimately authored. The pure contract is reevaluated after a symbolic owning domain resolves. Override `Process` only when the effect needs genuinely different topology or lowering, not merely a different working density.

## Working scale — what scale an effect runs at

Every built-in effect uses the **standard supply-driven working scale `w`** from `RenderScaleContract.MaterializeAtWorkingScale`. There is no closed per-effect policy enum. The standard rule is `w = min( max(s_out, densest concrete supply), MaxWorkingScale )`:

- `w` is **floored at `s_out`** (the deliverable density) and **raised by the densest concrete (bitmap) input above it**. A 2.0 source runs at 2.0 (no downsample — `s_out` is **not** a ceiling). A **sub-output** concrete supply — an enlarged / low-density bitmap, `At(0.5)` — feeding an effect at a `1.0` export is **floored to `w = 1.0`** (rendering at the deliverable density, matching the pre-feature renderer), **not** held at 0.5. Why: an effect's own working resolution (its blur kernel / shadow / shader grid) is distinct from the source's available *detail* — running it below `s_out` only discards resolution the delivery target can use, without fabricating source detail. A genuine reduced-scale proxy is still cheap in **preview**: at a `0.5` preview a `0.5` proxy gives `max(0.5, 0.5) = 0.5`.
- vector-only inputs (`Unbounded`) impose no supply → `w` stays at the `s_out` floor; a mixed bitmap+vector boundary likewise lands at `s_out` when no concrete input exceeds it, so crisp vector siblings are not dragged down to a low-density bitmap — now an instance of the universal floor, no longer a special case.
- `w` is finally capped by the global ceiling `MaxWorkingScale` (FR-037; **preview `2 × s_out`, export `+∞`** — export imposes no working-scale quality ceiling, *amended 2026-06-15*). This is the sole **global** ceiling (`FilterEffectRenderNode` passes `context.MaxWorkingScale`); additionally the per-buffer **dimension** clamp (FR-037(b), 16384 px per axis) is the sole **allocatability** bound and may further reduce `w` at the effect boundary — at the node level and again per target at `Flush` against the post-effect-inflated canonical device footprint. General calculations use `RenderScaleUtilities.ClampWorkingScaleToBufferBudget`; `CustomEffect` authors use `CustomFilterEffectContext.ResolveTargetDensity` for the allocation they are about to make. Two distinct bounds; do not conflate them.

**Every built-in runs supply-driven** — including the FR-013 resolution-sensitive set (`PixelSort`, contour `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, `Dilate`, `Erode`, `Mosaic`, custom SKSL/GLSL, image-map `Displacement`), since running at the supply density already keeps a high source's density through them. The working scale MUST NOT change the `s_out = 1.0` output.

**Need a different working scale?** An effect that genuinely needs intentional sub-output rendering, clamp-to-output (performance), or oversampling (SSAA) returns a `FilterEffectRenderNode` subclass from `FilterEffect.Resource.CreateRenderNode()` and overrides `GetWorkingScaleContract()` (see the example above). An explicit `Custom` result is capped by `MaxWorkingScale` and the relevant buffer bounds but is not raised to the standard `s_out` floor, so returning `0.5` remains `0.5` in a `1.0` delivery. The callback is invoked independently for each surviving branch with exactly one `InputSupplies` item and that branch's isolated effect-input bounds as `OutputBounds`. A legacy multi-input segment takes the densest concrete mapped result; only an all-`Unbounded` result falls back to `s_out`. There is intentionally no closed `ResolutionPolicy` enum. The contract is a narrow declarative hook that preserves the base lowering; override `Process` only for genuinely different topology or lowering. *(Earlier drafts had an `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy; it was removed.)*

> **Footgun — legacy multi-input `w` is shared, while allocation topology can change.** A dense sibling can still raise the nominal working scale used by every surviving branch, so shrinking a high-resolution source can make the effect more expensive. Before an opaque `CustomEffect`, the dimension clamp uses each branch's actual local-origin footprint, preserves transformed fractional origins, and includes every forced/intermediate `Flush`; empty space between distant independent branches is not backing storage. `CustomEffect` exposes no combine/split topology, so at the first such callback the planner unions the already-transformed branch results and conservatively tracks subsequent footprints in that aggregate domain. That can choose a lower safe density when the implementation actually preserves separate targets, but it cannot under-clamp a callback that combines them. Typed Shader/Geometry and pure Skia branch paths remain per-branch. A request-scoped aggregate area/byte budget remains separate from this per-axis safety rule.

## Resolution-sensitive effects (FR-013)

`PixelSort`, contour-based `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, integer `Dilate`/`Erode`, `Mosaic`, Perlin-driven, and custom per-texel shaders **still follow the coordinate-space rule above** (their Skia-`SKImageFilter` args — e.g. the Dilate/Erode radius — ride the CTM unchanged; `PerlinNoiseBrush.BaseFrequency` is left unchanged; only their device-buffer / point-blit code converts `× w` once and contour readback converts `÷ w`), but their reduced-scale preview is a **best-effort approximation** (not bit-identical), full-fidelity only at export `s_out=1.0`. Running supply-driven already keeps a higher-resolution source through them (downsampled only at the final stage). No force-full-scale subtree mechanism and no warning UI in v1. Their tests assert (a) byte-equality at `s_out=1.0` and (b) a documented structural invariant at a reduced scale (e.g. `mosaic tile == ceil(tileSize × w)` device px), not SSIM.

## Reporting a density: `EffectiveScale.At` throws — use `AtOrUnbounded` while recording (2026-06-15)

A custom `RenderNode` that records a materialized source or maps an input supply (for example, a plugin whose density is `sourcePixels / logicalWidth`) must respect the two factories and use `RenderScaleContract.MapInputSupply` when the result derives from an input:

- **`EffectiveScale.At(scale)` THROWS** (`ArgumentOutOfRangeException`) on a non-finite / non-positive density. A density derived from **animatable geometry** can momentarily go degenerate — `0/0 = NaN` on a collapsed bound, `x/0 = ∞` on an off-screen clip. A throw while recording or resolving the request aborts the whole render (the export frame, not just that fragment).
- **`EffectiveScale.AtOrUnbounded(scale)`** is the **non-throwing recording factory**: it returns `At(scale)` for a positive-finite density and **degrades a bad density to `Unbounded`** (the safe re-rasterizable default — the fragment then rasterizes at the consumer's working scale).

So a plugin override that derives a density from animatable geometry MUST either **pre-guard the quotient** (as `TransformRenderNode.RescaleDensity` does — clamp the factor, then re-check the quotient is finite-positive) **or use `AtOrUnbounded`**. Reserve `At` for densities already proven finite-positive.

Relatedly, `RenderNodeContext` **sanitizes degenerate inputs once at construction** so downstream consumers (effects, particles, 3D) inherit a safe density without re-validating: a degenerate `OutputScale` (`0` / `NaN` / `∞`) becomes `1`, and a degenerate `MaxWorkingScale` (`NaN` / `≤ 0`) becomes `+∞` (no ceiling — it can never NaN-propagate into `w` or pull it to zero). `ResolveWorkingScale` and `ClampWorkingScaleToBufferBudget` harden the same way (a non-finite/non-positive `outputScale` is treated as `1`; a non-finite `w`/bounds passes through unchanged).

## Mechanism summary

Centralized scaling lives in the `FilterEffectContext` primitives (covers built-ins and their forwarders for free); `TryGetWorkingScale` is the guarded author-time probe, execution contexts expose operation-specific density for pixel work, and `GetWorkingScaleContract()` is the escape hatch for *what scale* the effect runs at. A plugin effect that touches neither still renders correctly at `s_out=1.0` (supply-driven + `Unbounded` inputs → `w=1.0`); it simply runs supply-driven and won't drive oversampling until it adopts this contract.
