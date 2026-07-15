# EffectGraphBuilder & node-descriptor reference

Effects describe a graph in `FilterEffect.Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)`. This is the reference for the surface `builder` exposes. The authoritative contract is
[`docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md`](../../../../docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md); the removed-surface migration map is
[`contracts/breaking-changes.md`](../../../../docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md).

## Table of contents
1. [Builder properties](#builder-properties)
2. [Convenience methods](#convenience-methods) — blur/shadow, color, morphology, transform
3. [Node descriptors](#node-descriptors) — Shader, ColorFilter, SkiaFilter, Geometry, Compute, Split/Composite
4. [GeometrySession & EffectInput](#geometrysession--effectinput)
5. [BoundsContract](#boundscontract)
6. [Structural vs parameter](#structural-vs-parameter)
7. [Shader uniforms (SKSL / GLSL)](#shader-uniforms-sksl--glsl)

---

## Builder properties

```csharp
Rect  builder.OriginalBounds // the initial input rect at the start of this graph
Rect  builder.Bounds         // the current logical rect after all descriptors appended so far
float builder.OutputScale   // 003 s_out (device px per logical unit at the root)
float builder.WorkingScale  // 003 w — the density this boundary's buffers run at
```

`RenderNodeContext.DeviceBufferSize(builder.Bounds, builder.WorkingScale)` is only a describe-time boundary estimate. Do not use it to freeze resolution uniforms: bind those with `UniformBindingBuilder.Deferred` and read `PassUniformContext.TargetWidth` / `TargetHeight` after the executor has applied the per-pass density clamp.

---

## Convenience methods

Each returns the builder (chainable) and expands to the right descriptor. Same names/semantics as the old `FilterEffectContext`.

### Blur and shadow
```csharp
EffectGraphBuilder Blur(Size sigma)                                    // bounds expand by sigma * 3
EffectGraphBuilder DropShadow(Point position, Size sigma, Color color)
EffectGraphBuilder DropShadowOnly(Point position, Size sigma, Color color)
EffectGraphBuilder InnerShadow(Point position, Size sigma, Color color)
EffectGraphBuilder InnerShadowOnly(Point position, Size sigma, Color color)
```

### Color correction
```csharp
EffectGraphBuilder ColorMatrix(ColorMatrix matrix)         // 5x4 color matrix
EffectGraphBuilder ColorMatrix<T>(T data, Func<T, ColorMatrix> factory) where T : IEquatable<T>
EffectGraphBuilder Saturate(float amount)                  // 1.0 = unchanged, 0.0 = grayscale
EffectGraphBuilder HueRotate(float degrees)
EffectGraphBuilder Brightness(float amount)
EffectGraphBuilder HighContrast(bool grayscale, HighContrastInvertStyle invertStyle, float contrast)
EffectGraphBuilder Lighting(Color multiply, Color add)
EffectGraphBuilder LumaColor()
EffectGraphBuilder LuminanceToAlpha()
EffectGraphBuilder BlendMode(Color color, BlendMode blendMode)
EffectGraphBuilder BlendMode(Brush.Resource? brush, BlendMode blendMode)
```
The color overload and solid-brush blend emit coordinate-invariant `ColorFilterNode`s, so adjacent color methods fuse. A non-solid brush blend emits a render-time `GeometryNode` to preserve brush coordinates.

### Morphology
```csharp
EffectGraphBuilder Dilate(float radiusX, float radiusY)    // bounds expand by radius
EffectGraphBuilder Erode(float radiusX, float radiusY)     // bounds unchanged
```

### Transform
```csharp
EffectGraphBuilder Transform(Matrix matrix, BitmapInterpolationMode interpolation)
EffectGraphBuilder MatrixConvolution(
    PixelSize kernelSize, float[] kernel, float gain, float bias,
    PixelPoint kernelOffset, GradientSpreadMethod spreadMethod, bool convolveAlpha)
```

---

## Node descriptors

Append with `builder.Shader/ColorFilter/SkiaFilter/Geometry/Compute/Split/Composite/NestedGraph(descriptor)`.
`EffectNodeDescriptor` is a closed union: use these public sealed descriptor types rather than deriving a new kind.

### Shader (SKSL)
```csharp
// Fusable per-pixel color: `half4 apply(half4 c)`, c is premultiplied linear-light.
ShaderNodeDescriptor.Snippet(
    string source, Action<UniformBindingBuilder>? uniforms = null,
    IEnumerable<ChildBinding>? samplers = null)

// Non-invariant `half4 main(float2 coord)` with an implicit `src` child; bounds contract mandatory.
ShaderNodeDescriptor.WholeSource(
    string source, BoundsContract bounds, Action<UniformBindingBuilder>? uniforms = null,
    IEnumerable<ChildBinding>? children = null,
    SKShaderTileMode srcTileMode = SKShaderTileMode.Decal)

// Whole-source that provably samples only the current pixel: identity bounds, but still its own pass.
ShaderNodeDescriptor.WholeSourceInvariant(
    string source, Action<UniformBindingBuilder>? uniforms = null,
    IEnumerable<ChildBinding>? children = null,
    SKShaderTileMode srcTileMode = SKShaderTileMode.Decal)
```
There is no separate `SamplerBinding`: eager samplers and extra child shaders are both `ChildBinding`s. Uniforms are bound via the `UniformBindingBuilder`. Device-space values use `DensityScaledFloat2` or `Deferred`, not a describe-time `builder.WorkingScale` multiplier.

### ColorFilter / SkiaFilter (raw Skia)
```csharp
ColorFilterNodeDescriptor.Create(Func<SKColorFilter?> factory, object? structuralToken = null)
SkiaFilterNodeDescriptor.Create(
    Func<SKImageFilter?, SKImageFilter?> factory, BoundsContract bounds, object? structuralToken = null)
```
The `SKImageFilter?` argument is the upstream filter (chain input). Adjacent Skia-filter nodes group into one filtered draw.

### Geometry (imperative canvas)
```csharp
GeometryNodeDescriptor.Create(
    Action<GeometrySession> render, BoundsContract bounds,
    object? structuralToken = null,
    bool requiresReadback = false)                        // bounds MANDATORY
```
The sole descriptor that carries a rendering callback. Never fused, always its own pass. Set `requiresReadback` when the callback calls `EffectInput.Snapshot()`; undeclared readback is rejected.

### Compute (GLSL)
```csharp
ComputeNodeDescriptor.Create(
    Action<IComputeContext> dispatch, int passCount, ComputeFallback fallback,
    int colorScratchCount = 0,
    Action<GeometrySession>? cpuCallback = null,
    object? structuralToken = null,
    bool cpuFallbackRequiresReadback = false)              // fallback MANDATORY
```
`passCount` is the exact number of successful `IComputeContext.Run(...)` calls. An excess call is rejected before dispatch and a shortfall after the callback returns normally. `CopySourceToDestination()` is an exclusive terminal alternative: do not combine it with `Run(...)` or acquire scratch afterward. `fallback` (`Identity` / `Skip` / a CPU callback) is applied when Vulkan is unavailable so GPU-less CI passes. The color scratch count is the maximum concurrent acquisition and is enforced at runtime. Fullscreen compute passes are color-only; there is no depth scratch API. Set `cpuFallbackRequiresReadback` when the CPU callback snapshots its input.

### Split / Composite
```csharp
SplitNodeDescriptor.Static(
    Action<ISplitEmitter> emit, int branchCount, object? structuralToken = null,
    bool requiresReadback = false)
```
`emitter.Emit(tileBounds, session => { ... })` schedules a branch. A static split must call `Emit` exactly `branchCount` times; excess calls fail before allocating and a shortfall fails after the callback returns. Fusion never crosses a split. Set `requiresReadback` when the emitter snapshots `emitter.Input`.

---

## GeometrySession & EffectInput

Inside a `GeometryNode`/`Split` branch callback the executor owns the target — you only draw:

```csharp
// GeometrySession
Rect  session.Bounds                       // output buffer rect
float session.OutputScale                  // 003 s_out
float session.WorkingScale                 // 003 w
float session.MaxWorkingScale
IReadOnlyList<EffectInput> session.Inputs
ImmediateCanvas session.OpenCanvas()       // canvas over the pooled output target

// EffectInput (read-only upstream result)
Rect          input.Bounds
EffectiveScale input.Density
PixelSize     input.DeviceSize
SKShader       input.AsShader()
Bitmap         input.Snapshot()            // dispose it; descriptor must declare readback
void           input.Draw(ImmediateCanvas canvas, Point devicePoint)
void           input.Draw(ImmediateCanvas canvas)
```
Do NOT allocate/dispose targets, catch-and-continue, or apply Skia image filters through the session — pass/target scheduling and lifetimes are executor-owned (compose a dedicated `Blur`/`DropShadow` effect in the chain instead).

---

## BoundsContract

```csharp
BoundsContract.Identity                                     // output == input rect
BoundsContract.FullFrame                                    // allocate at complete input bounds and disable ROI cropping
BoundsContract.Create(Func<Rect,Rect> transformBounds,     // forward: how output bounds grow
                      Func<Rect,Rect> getRequiredInputBounds) // backward: input texels a region samples
```
Every non-invariant node MUST declare one. Backward MUST cover every input texel the node samples for a given output region; the engine may render inputs cropped to exactly that region. `FullFrame` uses identity forward/backward maps but requires the complete input; a geometry callback may shrink or discard its allocation at execution, but cannot grow or move it outside the allocated input frame. Common forward shapes: expand `bounds.Inflate(thickness)`, unchanged `bounds`.

---

## Structural vs parameter

- **Structural** (changing it recompiles the plan once): shader source identity, pass/branch counts, invariance flags, bounds-contract identity, the `structuralToken`.
- **Parameter** (must NOT change the compiled plan): uniform values, colors, matrices, LUT texture contents. **Never encode a parameter into shader source** — it defeats the program cache and forces recompiles.
- Bounds/ROIs/buffer sizes MAY depend on parameters and are re-resolved every frame; only the graph's *shape* is structural. A parameter that changes the *number/kind* of nodes/passes/branches is structural and must be declared via `structuralToken`.

---

## Shader uniforms (SKSL / GLSL)

Compile the holder shader once (usually a static field), then reference it in `Describe`.

**SKSL** — compile with `SKSLShader.TryCreate(source, out shader, out errorText)`. Built-in whole-source uniforms the script executors bind when present:
```glsl
uniform shader src;          // input image (implicit child)
uniform float progress;      // 0.0 - 1.0
uniform float duration;      // seconds
uniform float time;          // seconds
uniform float width;         // device-px target width
uniform float height;        // device-px target height
uniform float2 iResolution;  // (width, height) device px
uniform float iScale;        // device px per logical px (003 working scale w)
uniform float iTime;         // alias for time
```

**GLSL** — runs as a `ComputeNode`. Push-constant layout:
```glsl
#version 450
layout(location = 0) in vec2 fragCoord;       // normalized 0.0 - 1.0
layout(location = 0) out vec4 outColor;
layout(set = 0, binding = 0) uniform sampler2D srcTexture;
layout(push_constant) uniform PushConstants {
    float progress; float duration; float time;
    float width; float height; float scale;   // scale mirrors SKSL iScale (= w)
} pc;
```

Absolute-length pixel literals (tile size, displacement, `iResolution`-style constants) multiply by the working scale `w` to stay logically constant; normalized/content-relative math needs nothing. Full uniform contract:
[`docs/specs/003-resolution-independent-pipeline/contracts/shader-uniforms.md`](../../../../docs/specs/003-resolution-independent-pipeline/contracts/shader-uniforms.md).
