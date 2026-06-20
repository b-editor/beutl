# Resolution-independent rendering (feature 003) — author guide

Beutl renders in a logical coordinate system and scales to device pixels at the root, so the editor can
preview at a reduced scale and export at a supersampled one. At scale 1.0, unscaled content stays
**byte-identical** to pre-feature output, but this is no longer a *universal* guarantee — a scaled bitmap
feeding an effect is rendered at its coherent supply density instead (see
[*The scale-1.0 guarantee*](#the-scale-10-guarantee)). This guide is for authors of drawables,
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
`Transform`, color matrices, …) or draws plain geometry/text, **do not multiply anything by a scale**:
the CTM handles it, and a manual `× w` would double-scale and regress the result.

## When scale matters

- **Reading the working scale.** A `CustomEffect` / SKSL / GLSL author who hand-allocates an
  intermediate or hard-codes a pixel literal reads `CustomFilterEffectContext.WorkingScale`
  (or `FilterEffectContext.WorkingScale`); both default to `1.0`. `CreateTarget(bounds)` already
  allocates a `ceil(bounds × w)` device buffer tagged `EffectiveScale.At(w)`, so the runtime shader
  evaluates in DEVICE pixels: multiply any **absolute-length** pixel literal
  (tile size, displacement amount, split offset, a hard-coded `iResolution`-style constant) by `w` to
  stay logically constant. Content-relative logic (a luminance pixel-sort, a normalized-uv shader)
  needs nothing. The built-ins already do this — Mosaic (`tileSize × w`), DisplacementMap (translate /
  pivot `× w`, plus a `CreateScale(w)` local matrix on the displacement-map shader so it shares the base
  texture's device-px coord space), PartsSplit (contour bounds `/ w`), SKSL (`iResolution`/`width`/`height`
  `× w` + `iScale = w`), GLSL (`Width`/`Height` push constants `× w`, plus a `scale` push constant `= w`
  mirroring SKSL's `iScale`) — verified by `CustomEffectSupersampleTests` (Mosaic + DisplacementMap 2×-delivered vs 1:1 SSIM
  1.0000; Mosaic strictly closer to ground truth than 1:1).
- **Working scale (supply-driven).** Every effect runs at its **input supply density** — the densest
  concrete (bitmap) input, with `s_out` as the floor for vector-only/mixed boundaries, capped only by the
  global memory ceiling (`MaxWorkingScale`). `s_out` is **not** a ceiling. Resolution-sensitive effects
  (PixelSort, Dilate/Erode, Mosaic, contour Stroke/FlatShadow/PartsSplit, Displacement, custom SKSL/GLSL,
  Clipping) get a high-resolution source's detail through them for free, with no per-effect knob. There is
  **no `ResolutionPolicy`**: the earlier `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` policy
  was removed because no built-in needed a non-default value. An effect that genuinely needs a different
  working scale (clamp-to-output for perf, oversample for SSAA) returns a `FilterEffectRenderNode` subclass from
  `FilterEffect.Resource.CreateRenderNode()` and overrides `Process` to compute its own `w`.
- **Bitmap sources.** A decoded image/video op reports its decoded density as `EffectiveScale.At(...)`,
  distinct from its logical footprint. Mixed-scale compositing resamples off-target bitmaps via
  `ImmediateCanvas.DrawRenderTargetScaled` / `DrawSurfaceScaled` (Mitchell). 003 ships only this seam; the
  proxy / reduced-decode workflow that exploits it is a future feature (see `MediaOptions`).
- **Tile / drawable brush fills.** `BrushConstructor` rasterizes `TileBrush` / `DrawableBrush` fill content at
  the canvas density (`ImmediateCanvas.Density` — `s_out` or the enclosing `w`, passed in as `Scale`): the
  drawable/tile intermediate is allocated at `ceil(size × Scale)` device px and a `Scale(1/Scale)` shader
  local-matrix un-densifies the texture coords back to logical. So tile- and drawable-brush fills stay crisp at
  `s_out > 1` with no author action, across every `TileMode` / `Transform`; only the `Scale == 1` short-circuit
  preserves byte-identity. Solid / gradient / perlin brushes are vector shaders and resolution-independent
  regardless.
- **3D scenes.** `Scene3DRenderNode` renders at `ceil(size × w)` and reports `EffectiveScale.At(w)`, where
  `w = ClampWorkingScaleToBufferBudget(size, s_out)` is the output scale reduced only if the dense surface would
  exceed the per-buffer dimension budget. So 3D content is crisp under supersampled export instead of being
  upscaled by the root CTM.

## Migrating pre-003 SKSL / GLSL shaders

Before feature 003, custom shader target pixels and logical pixels were effectively the same because the
render target was always allocated at scale `1.0`. After 003, SKSL `width` / `height` / `iResolution`
and GLSL `pc.width` / `pc.height` report the scaled target size in device pixels. At `w = 1.0` the values
are unchanged; at reduced preview, supersampled export, or a mixed-density effect boundary they change
with the working scale.

If a shader intentionally works in normalized coordinates, no migration is usually needed:
`fragCoord / iResolution` in SKSL and the default GLSL `fragCoord` input already track the target. If a
shader used those resolution values as logical project pixels, convert device pixels back to logical pixels
with the scale uniform:

```skia
uniform float2 iResolution;  // device px
uniform float iScale;        // device px per logical px

float2 logicalSize = iResolution / iScale;
float2 logicalCoord = fragCoord / iScale;
```

```glsl
vec2 deviceSize = vec2(pc.width, pc.height);
vec2 logicalSize = deviceSize / pc.scale;
vec2 logicalCoord = (fragCoord * deviceSize) / pc.scale;
```

For the opposite direction, keep authored logical pixel literals stable by multiplying them by the working
scale before using them in device-pixel shader math: `10.0 * iScale` in SKSL or `10.0 * pc.scale` in GLSL.
See `docs/specs/003-resolution-independent-pipeline/contracts/shader-uniforms.md` for the full uniform
contract.

## The scale-1.0 guarantee

At `s_out = 1.0` with unit-scale inputs, the **golden content set** — vector geometry, text, Skia-filter
effects, and unscaled bitmaps — stays **byte-identical** to pre-feature output. Every scale-aware path
keeps an exact `w == 1` / `scale == 1` short-circuit to preserve this.

This is **not** a universal guarantee: a *scaled* bitmap feeding an effect is rasterized at its coherent
supply density (FR-019) and can differ bit-for-bit from the old path — a deliberate trade that abolished
the former universal byte-identity-at-`s_out = 1` constraint (commit `32634977c`). Write to the
coherent-density model, not to a universal byte-identity rule.

New rendering code is verified by the golden suite (`tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/`),
which runs for real when a Vulkan implementation (MoltenVK / SwiftShader) is present and `Assert.Ignore`s
otherwise. See `docs/specs/003-resolution-independent-pipeline/contracts/effect-scale-contract.md` for
the full per-parameter classification.
