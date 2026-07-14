# Contract: Effect / drawable / brush / pen scale contract

**Feature**: 003 | FR-008/FR-009/FR-010/FR-011/FR-012/FR-015. Audience: authors of `FilterEffect`, effect-graph descriptors, `Drawable`, brushes, and C# script effects (in-tree and plugin). The authoring surface was replaced by feature 004; the scale semantics below remain binding on the declarative API.

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
| **Logical-space geometry drawn under the CTM** | **Leave unchanged.** The CTM already scales it. | shape/bar geometry, pen thickness/offset/dash (pre-outlined logical, D3), Shake displacement (translates logical bounds), `DrawRectangle`/`DrawPath` coords, gradient stops/points, drop-shadow offset & sigma and dilate/erode radius passed to a declarative Skia-filter node |
| **Device-buffer dimensions / device-space shader uniforms / device pixel indexing** | **Convert once (`× w`).** These bypass the CTM. | `PassUniformContext.TargetWidth`/`TargetHeight`; SKSL `iScale`/`width`/`height`/`fragCoord`; a geometry callback's absolute-px literal (`MyPixelRadius * session.WorkingScale`); the tile/drawable intermediate raster size (A-1) |
| **Readback-derived geometry (device → logical)** | **Convert device back to logical (`÷ w`).** | `ContourTracer` / `PartsSplit` vertices traced from `EffectInput.Snapshot()` (`/ EffectInput.Density` before drawing through the output CTM) |
| **Magnitude-invariant** | **Leave unchanged.** Not a length. | color; angle; percentage; ratio; 0..1 value; `RelativePoint`/`RelativeRect`; blend mode; count; enum; `MiterLimit`; caps/joins/alignment; `Trim*` |

**Non-obvious cases (empirically settled on this branch):**
- `PerlinNoiseBrush.BaseFrequency` is **left unchanged** — `SkPerlinNoiseShader` follows the CTM, so its period is logical-invariant; dividing by `w` made the reduced-scale result *worse* (the dossier's "÷w" was wrong for this CTM pipeline). Reduced-scale softness is accepted best-effort (FR-013).
- Text is **re-shaped** at `Size × w` (it reads the device font size, not a CTM-scaled outline — `Hinting=Full` bakes resolution-specific grid-fitting); never matrix- or bitmap-scaled (FR-012).
- **A Skia `SKImageFilter` primitive (Blur/DropShadow/Dilate/Erode) takes its length args RAW** — do NOT `× w`; it rides the `CreateScale(w)` CTM. Only **device-buffer / device-shader** code multiplies.
- **Anisotropic transforms** (FR-019): a scalar `EffectiveScale` projects onto the most-detailed axis, which can over-allocate; the buffer is bounded by `ClampWorkingScaleToBufferBudget` (FR-037 backstop).

## How an author reads the active scale (FR-015)

The declarative surface exposes scale at the point where each value can be resolved correctly:

1. **`EffectGraphBuilder.OutputScale` / `WorkingScale`** describe the effect boundary. Pass Skia-filter lengths (`Blur`/`DropShadow`/`Dilate`/`Erode`/`Transform`/`MatrixConvolution`) in raw logical units. Do **not** freeze a device-space shader uniform from `builder.WorkingScale`: per-pass bounds resolution and the 16 384-px clamp can execute that pass at a lower density.
2. **`PassUniformContext.WorkingScale` / `TargetWidth` / `TargetHeight`** are the execution-time values for shader uniforms. Bind logical device lengths through `UniformBindingBuilder.DensityScaledFloat2`; use `Deferred` for target size, resolution, or other derived values.
3. **`GeometrySession.WorkingScale`** is the output canvas density. `OpenCanvas()` already carries the `CreateScale(w)` base CTM, so logical geometry is not pre-scaled; use `PushDeviceSpace()` and `session.WorkingScale` only for device-pixel work. Each `EffectInput` has its own `Density`; use that density for snapshot/readback coordinates and bridge input-device pixels to the output density when required.
4. **`IComputeContext.WorkingScale` / `Width` / `Height`** are the corresponding execution-time values for compute dispatch and push constants.

```csharp
// Out-of-tree FilterEffect example after feature 004.
public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
{
    // Sigma is logical: pass it raw. The pass canvas applies the working-density CTM.
    builder.Blur(new Size(BlurRadius, BlurRadius));

    builder.Geometry(GeometryNodeDescriptor.Create(
        session =>
        {
            ImmediateCanvas canvas = session.OpenCanvas(); // executor-owned; do not dispose
            session.Inputs[0].Draw(canvas);                 // preserve the upstream result
            canvas.DrawRectangle(logicalRect, brush, null); // logical; no manual pre-scale

            using (canvas.PushDeviceSpace())
            {
                float devRadius = MyPixelRadius * session.WorkingScale;
                // ... device-pixel output work ...
            }
        },
        BoundsContract.RenderTime,
        structuralToken: nameof(MyEffect)));
}

// An effect that needs a working scale OTHER than the supply density (clamp-to-output for perf,
// oversample for SSAA) overrides the render node instead of declaring a policy.
// (As shipped since 004: the seam is `RenderNodeFactory`, not `CreateRenderNode()`.)
public new partial class Resource
{
    private static readonly FilterEffectRenderNodeFactory s_factory =
        FilterEffectRenderNodeFactory.Of<Resource, MyCompleteCustomNode>(
            static resource => new MyCompleteCustomNode(resource));

    public override FilterEffectRenderNodeFactory RenderNodeFactory
        => s_factory;
}
```

> **Note (as shipped):** the custom-render-node override is honoured on every push path. The seam was reshaped in 004: `CreateRenderNode()` + `RenderNodeType` became one `FilterEffect.Resource.RenderNodeFactory`, and `FilterEffectRenderNode` / `Process` became abstract. `MyCompleteCustomNode` above therefore represents a plugin-owned subclass that implements the complete `Process(RenderNodeContext)` operation construction; there is no `base.Process(context)` fallback. The base supplies only captured-resource/update plumbing, while the engine's declarative plan execution and prefix cache live in its internal default node. A plugin should override the factory only when it genuinely owns that complete execution path. See 004 [`breaking-changes.md`](../../004-gpu-pass-fusion/contracts/breaking-changes.md).

## Working scale — what scale an effect runs at

Every effect runs at the **supply-driven working scale `w`**, computed from its inputs' effective scales (there is **no per-effect policy knob**). The rule is `w = min( max(s_out, densest concrete supply), MaxWorkingScale )` *(amended 2026-06-15 — `s_out` is the FLOOR; the earlier "a 0.5 proxy stays 0.5" wording is superseded)*:

- `w` is **floored at `s_out`** (the deliverable density) and **raised by the densest concrete (bitmap) input above it**. A 2.0 source runs at 2.0 (no downsample — `s_out` is **not** a ceiling). A **sub-output** concrete supply — an enlarged / low-density bitmap, `At(0.5)` — feeding an effect at a `1.0` export is **floored to `w = 1.0`** (rendering at the deliverable density, matching the pre-feature renderer), **not** held at 0.5. Why: an effect's own working resolution (its blur kernel / shadow / shader grid) is distinct from the source's available *detail* — running it below `s_out` only discards resolution the delivery target can use, without fabricating source detail. A genuine reduced-scale proxy is still cheap in **preview**: at a `0.5` preview a `0.5` proxy gives `max(0.5, 0.5) = 0.5`.
- vector-only inputs (`Unbounded`) impose no supply → `w` stays at the `s_out` floor; a mixed bitmap+vector boundary likewise lands at `s_out` when no concrete input exceeds it, so crisp vector siblings are not dragged down to a low-density bitmap — now an instance of the universal floor, no longer a special case.
- `w` is finally capped by the global ceiling `MaxWorkingScale` (FR-037; **preview `2 × s_out`, export `+∞`** — export imposes no working-scale quality ceiling, *amended 2026-06-15*). This is the sole **global** ceiling (`FilterEffectRenderNode` passes `context.MaxWorkingScale`); additionally the per-buffer **dimension** clamp (FR-037(b), `RenderNodeContext.ClampWorkingScaleToBufferBudget`, 16384 px per axis) is the sole **allocatability** bound and may further reduce `w` at the effect boundary — at the node level and again per target at `Flush` against the post-effect-inflated bounds. Two distinct bounds; do not conflate them.

**Every built-in runs supply-driven** — including the FR-013 resolution-sensitive set (`PixelSort`, contour `Stroke`/`FlatShadow`/`PartsSplit`, `AutoClip`, `Dilate`, `Erode`, `Mosaic`, custom SKSL/GLSL, image-map `Displacement`), since running at the supply density already keeps a high source's density through them. The working scale MUST NOT change the `s_out = 1.0` output.

**Need a different working scale?** An effect that genuinely needs clamp-to-output (perf) or oversampling (SSAA) returns a `FilterEffectRenderNode` subclass from `FilterEffect.Resource.RenderNodeFactory` (as shipped since 004; formerly `CreateRenderNode()`) and overrides `Process` to compute its own `w` (see the example above). There is intentionally no declarative `ResolutionPolicy` — no built-in needed one, and a custom render node is more flexible than a closed enum. *(Earlier drafts had an `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy; it was removed.)*

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

Centralized scaling lives in the compiled effect plan and executor. Logical convenience/Skia-filter parameters need no manual scaling; execution-time shader values come from `PassUniformContext`, geometry values from `GeometrySession`, and compute values from `IComputeContext`. A custom `FilterEffectRenderNode` remains the escape hatch for a plugin that owns a complete non-default execution path. A plugin effect that uses only logical descriptor values still renders correctly at `s_out=1.0` (supply-driven + `Unbounded` inputs → `w=1.0`).
