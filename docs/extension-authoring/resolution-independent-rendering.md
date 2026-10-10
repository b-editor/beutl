# Resolution-independent rendering

Beutl renders in logical coordinates and scales to device pixels at the root, so the editor can preview at
a reduced scale and export at a supersampled one. This guide is for authors of drawables, filter effects,
brushes, shaders, and custom render nodes.

## The three scales

| Scale | Where to read it | Meaning |
|---|---|---|
| **Output scale `s_out`** | `Renderer.OutputScale`, `RenderNodeContext.OutputScale` | Device pixels per logical unit at the final target. `1.0` means logical equals device. |
| **Effective scale** | `RenderFragmentHandle.TryGetMetadata(out RenderFragmentMetadata)`, then `EffectiveScale` | The density a recorded fragment's pixels exist at. Vector fragments are `Unbounded`; bitmap fragments report `At(scale)`. |
| **Working scale `w`** | `FilterEffectContext.WorkingScale`, `CustomFilterEffectContext.WorkingScale` | The density a buffer-allocating effect runs at, resolved from its inputs as described below. |

## What most authors need to do: nothing

The root `Matrix.CreateScale(s_out)` transform scales vector geometry, text, strokes, and the Skia-backed
`FilterEffectContext` primitives (`Blur`, `DropShadow`, `Dilate`, `Erode`, `Transform`, color matrices, …).
If an effect is built from those primitives, or a drawable draws plain geometry and text, **do not multiply
anything by a scale**: the transform already handles it, and a manual `× w` scales the result twice.

## Which values to convert

The coordinate space a value lives in decides the rule, not the value's type:

| Coordinate space | Rule | Examples |
|---|---|---|
| Logical geometry drawn under the canvas transform | Leave unchanged | shape and path coordinates, pen thickness and dashes, gradient points, blur sigma, drop-shadow offset, a dilate radius passed to a Skia primitive |
| Device-buffer sizes, device-space shader values, device pixel indexing | Multiply by `w` once | the size of a buffer you allocate, an absolute pixel literal in a pixel loop or shader (tile size, displacement amount) |
| Geometry read back from device pixels | Divide by `w` | contour vertices traced from a device alpha mask |
| Values that are not lengths | Leave unchanged | color, angle, percentage, ratio, `RelativePoint`, `RelativeRect`, blend mode, count, enum |

`PerlinNoiseBrush.BaseFrequencyX` and `BaseFrequencyY` follow the canvas transform and are left unchanged.
Text is re-shaped at the device font size rather than scaled as a bitmap.

## Working scale

An effect runs at `w = min(max(s_out, densest bitmap input), MaxWorkingScale)`:

- Vector-only inputs (`Unbounded`) leave `w` at `s_out`. A denser bitmap raises it, so a high-resolution
  source keeps its detail through the effect: `s_out` is a floor, not a ceiling.
- `MaxWorkingScale` is the global ceiling: `2 × s_out` in the editor preview and unbounded in export.
- A per-buffer dimension limit (16384 px per axis, or the device's smaller attachment limit) can lower the
  density of an individual buffer further. `BufferDimensionBudget.ClampWorkingScale` applies it, and
  `CustomFilterEffectContext.ResolveTargetDensity` returns the density of a buffer a custom effect is about to
  allocate.

Reading the scale while authoring:

- `FilterEffectContext.TryGetWorkingScale(out float)` returns the nominal working scale when the effect has one
  concrete input. It returns `false` while the input is unresolved or when several input branches may run at
  different densities, and the `WorkingScale` getter throws in that case. Record scale-independent structure
  then, and do device-pixel math in the execution-time shader, geometry, or custom-effect callback.
- In a `CustomEffect` callback, `CustomFilterEffectContext.CreateTarget(bounds)` allocates a `ceil(bounds × w)`
  device buffer, and `Open` returns a canvas that already applies `CreateScale(density)`, so logical content is
  drawn directly. For device-pixel work, wrap the drawing in `canvas.PushDeviceSpace()` and scale literals by
  the target's actual density, `target.Scale.Value`, which can be lower than `WorkingScale` after the
  per-buffer limit.

The built-in effects follow these rules; for example, Mosaic multiplies its tile size by `w`, and the SKSL
and GLSL script effects pass `w` to shaders.

### Choosing a different working scale

There is no per-effect policy setting. An effect that needs a different density, such as clamping to the
output for performance or oversampling, returns a `FilterEffectRenderNode` subclass from `CreateRenderNode()`
and overrides `GetWorkingScaleContract()`:

```csharp
public partial class Resource
{
    public override FilterEffectRenderNode CreateRenderNode() => new OutputScaleRenderNode(this);
}

private sealed class OutputScaleRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    // Runs at the output density even when a denser bitmap feeds the effect.
    private static readonly RenderScaleContract s_scale =
        RenderScaleContract.Custom(static context => context.OutputScale);

    protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
}
```

The callback must be pure and return a finite positive density. It receives the input supplies, the output
bounds, `s_out`, and `MaxWorkingScale` through `RenderScaleContext`. Its result is capped by `MaxWorkingScale`
and the per-buffer limit, but it is not raised to `s_out`. Override `Process` only when the effect needs a
different topology, not merely a different density.

## Sources, brushes, and custom render nodes

- **Bitmap sources.** A decoded image or video reports the density its pixels exist at, separately from its
  logical footprint. A proxy reports its reduced density (the proxy's long edge divided by the original's).
  Mixed-density compositing resamples bitmaps through `ImmediateCanvas.DrawRenderTargetScaled` and
  `DrawSurfaceScaled`.
- **Tile and drawable brushes.** `BrushConstructor` rasterizes `TileBrush` and `DrawableBrush` content at the
  canvas density (`ImmediateCanvas.Density`), so fills stay sharp at `s_out > 1` without author action. Solid,
  gradient, and Perlin brushes are resolution-independent shaders.
- **3D scenes.** A 3D scene renders at the output scale, reduced only by the per-buffer limit, and reports that
  density, so it stays sharp in supersampled export.
- **Reporting a density.** `EffectiveScale.At(scale)` throws on a non-finite or non-positive value. A density
  derived from animatable geometry can become `NaN` or infinite for a collapsed or off-screen bound, so record
  it with `EffectiveScale.AtOrUnbounded(scale)`, which falls back to `Unbounded`, or guard the value first.
  When the density derives from an input, declare it with `RenderScaleContract.MapInputSupply` so the output
  demand is carried back to that input.

## Custom shaders (SKSL / GLSL)

Shader resolution values are device pixels of the scaled target, and a separate value carries the scale:

| Effect | Device-pixel values | Scale |
|---|---|---|
| SKSL | `width`, `height`, `iResolution` (declare it as `uniform float2`), and `fragCoord` | `uniform float iScale` |
| GLSL | push constants `width` and `height` | push constant `scale` |

The scale is the density the buffer was actually allocated at, so it agrees with the pixels the shader
iterates. A shader that does not read it behaves as if the scale were `1.0`.

- Normalized coordinates (`fragCoord / iResolution` in SKSL, the default normalized `fragCoord` in GLSL) need
  no change.
- Multiply an authored logical length by the scale before using it as a device-pixel value: `10.0 * iScale`
  in SKSL, or `10.0 * pc.scale` in GLSL.
- A script written before resolution-independent rendering that treated these values as logical pixels must
  convert them back with the scale:

```skia
uniform float2 iResolution;  // device px
uniform float iScale;        // device px per logical px

float2 shaderGridLogicalSize = iResolution / iScale;
float2 shaderGridLogicalCoord = fragCoord / iScale;
```

```glsl
vec2 deviceSize = vec2(pc.width, pc.height);
vec2 shaderGridLogicalSize = deviceSize / pc.scale;
vec2 shaderGridLogicalCoord = (fragCoord * deviceSize) / pc.scale;
```

These values describe the rounded shader grid, not the exact authored bounds. Buffers are allocated as
`ceil(bounds × scale)`, so a 101 px target at scale `0.5` reports 51 device px, which converts back to 102
logical px. Keep center and edge math normalized when it must follow the exact bounds; the script values do
not expose them.

## Scale 1.0 and testing

Every scale-aware path keeps an exact `scale == 1` short-circuit, so at `s_out = 1.0` vector content, text,
Skia filters, and unscaled bitmaps are drawn without resampling. Keep that short-circuit in new scale-aware
code. A scaled bitmap that feeds an effect is still rasterized at its supply density, so do not rely on
byte-identical output in that case.

The golden suite under `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/` covers this behavior. It runs
when a Vulkan implementation (MoltenVK or SwiftShader) is available and calls `Assert.Ignore` otherwise.
