# Resolution-independent rendering (feature 003) — author guide

Beutl renders at a logical coordinate system and scales to device pixels at the root. This lets the
editor preview at a reduced scale (faster) and export at a supersampled scale (smoother). At scale 1.0,
unscaled content stays **byte-identical** to pre-feature output — but this is no longer a *universal*
guarantee (a scaled bitmap feeding an effect is rendered at its coherent supply density instead; see
[*The scale-1.0 guarantee*](#the-scale-10-guarantee) below). This guide is for authors of drawables,
filter effects, brushes, and shaders.

## The three scales

| Scale | Type | Meaning |
|---|---|---|
| **Output scale `s_out`** | `Renderer.OutputScale` / `RenderNodeContext.OutputScale` | the final target only: device pixels per logical unit at the root. `1.0` = logical == device. |
| **Effective scale** | `RenderNodeOperation.EffectiveScale` | the supply density an op's pixels actually exist at. Vector ops are `Unbounded`; bitmap ops report `At(scale)`. |
| **Working scale `w`** | `FilterEffectContext.WorkingScale` (+ `RenderNodeContext.ResolveWorkingScale`) | the density a buffer-allocating boundary runs at, negotiated from the inputs' supply densities (falling back to `s_out` for vector-only inputs), capped by `MaxWorkingScale`. There is no per-effect policy knob. |

## What most authors need to do: nothing

The root `Matrix.CreateScale(s_out)` CTM scales **vector geometry, text, strokes, and inline Skia
image filters (blur, drop shadow, color, gradients) for free**. This is empirically verified:

- a 0.5× vector render upscales to the 1.0× render at **SSIM 0.9971**;
- a blurred shape scales at **SSIM 1.0000**;
- every built-in effect category (incl. the buffer-allocating InnerShadow, 0.9983) scales faithfully
  at reduced scale with **no per-effect code**.

So if your effect is built from the `FilterEffectContext` primitives (`Blur`, `DropShadow`, `Dilate`,
`Transform`, color matrices, …) or draws plain geometry/text, **do not multiply anything by a scale** —
the CTM handles it, and adding a manual `× w` would double-scale and regress the result.

## When scale matters

- **Reading the working scale.** A `CustomEffect` / SKSL / GLSL author who hand-allocates an
  intermediate or hard-codes a pixel literal reads `CustomFilterEffectContext.WorkingScale`
  (or `FilterEffectContext.WorkingScale`); both default to `1.0`. `CreateTarget(bounds)` **already**
  allocates a `ceil(bounds × w)` device buffer and tags it `EffectiveScale.At(w)`; the runtime shader
  therefore evaluates in DEVICE pixels, so you must multiply any **absolute-length** pixel literal
  (tile size, displacement amount, split offset, a hard-coded `iResolution`-style constant) by `w` to
  stay logically constant. Content-relative logic (a luminance pixel-sort, a normalized-uv shader)
  needs nothing. The built-ins already do this — Mosaic (`tileSize × w`), DisplacementMap (translate /
  pivot `× w`, and the displacement-map shader gets a `CreateScale(w)` local matrix so it shares the base
  texture's device-px coord space), PartsSplit (contour bounds `/ w`), SKSL (`iResolution`/`width`/`height`
  `× w` + `iScale = w`), GLSL (`Width`/`Height` push constants `× w`, plus a `scale` push constant `= w`
  mirroring SKSL's `iScale`) — verified by `CustomEffectSupersampleTests` (Mosaic + DisplacementMap 2×-delivered vs 1:1 SSIM
  1.0000; Mosaic strictly closer to ground truth than 1:1).
- **Working scale (supply-driven).** Every effect runs at its **input supply density** — the densest
  concrete (bitmap) input, with `s_out` as the floor for vector-only/mixed boundaries, capped only by the
  global memory ceiling (`MaxWorkingScale`). `s_out` is **not** a ceiling. Resolution-sensitive effects
  (PixelSort, Dilate/Erode, Mosaic, contour Stroke/FlatShadow/PartsSplit, Displacement, custom SKSL/GLSL,
  Clipping) get a high-resolution source's detail through them for free — no per-effect knob. There is
  **no `ResolutionPolicy`** (an earlier `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy
  was removed — no built-in needed a non-default value). An effect that genuinely needs a different working
  scale (clamp-to-output for perf, oversample for SSAA) returns a `FilterEffectRenderNode` subclass from
  `FilterEffect.Resource.CreateRenderNode()` and overrides `Process` to compute its own `w`.
- **Bitmap sources.** A decoded image/video op reports its decoded density as `EffectiveScale.At(...)`,
  distinct from its logical footprint. Mixed-scale compositing resamples off-target bitmaps via
  `ImmediateCanvas.DrawRenderTargetScaled` / `DrawSurfaceScaled` (Mitchell). (The proxy / reduced-decode
  workflow that exploits this is a future feature; 003 ships the seam only — see `MediaOptions`.)
- **3D scenes.** `Scene3DRenderNode` renders at `ceil(size × s_out)` and reports `EffectiveScale.At(s_out)`,
  so 3D content is crisp under supersampled export instead of being upscaled by the root CTM.

## Known limitation

`DrawableBrush` / `TileBrush` FILL content (`BrushConstructor`) is still rasterized at LOGICAL density,
so a tile-/drawable-brush fill is soft-upscaled by the root CTM at `s_out > 1` — the filled shape's
**edges stay crisp** (vector), only the fill texture is soft. Solid/gradient/image-shader brushes are
resolution-independent and unaffected. Full TileBrush density requires threading `s_out` through the
`TileBrushCalculator` + intermediate + shader local-matrix for every `TileMode`/`Transform`, each with
its own golden, and is a scoped follow-up.

## The scale-1.0 guarantee

At `s_out = 1.0` with unit-scale inputs, the **golden content set** — vector geometry, text, Skia-filter
effects, and unscaled bitmaps — stays **byte-identical** to pre-feature output. Every scale-aware path
keeps an exact `w == 1` / `scale == 1` short-circuit to preserve this.

This is **not** a universal guarantee: a *scaled* bitmap feeding an effect is rasterized at its coherent
supply density (FR-019) and can differ bit-for-bit from the old path — that trade was made deliberately
(the former universal byte-identity-at-`s_out = 1` constraint was abolished in commit `32634977c`). So
write to the coherent-density model, not to a universal byte-identity rule.

New rendering code is verified by the golden suite (`tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/`),
which runs for real when a Vulkan implementation (MoltenVK / SwiftShader) is present and `Assert.Ignore`s
otherwise. See `docs/specs/003-resolution-independent-pipeline/contracts/effect-scale-contract.md` for
the full per-parameter classification.
