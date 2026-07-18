# EffectGraphBuilder & node-descriptor reference

Effects describe a graph in `FilterEffect.Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)`. This is the reference for the surface `builder` exposes. The authoritative contract is
[`docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md`](../../../../docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md); the removed-surface migration map is
[`contracts/breaking-changes.md`](../../../../docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md).

## Table of contents
1. [Builder properties](#builder-properties)
2. [Convenience methods](#convenience-methods) — blur/shadow, color, morphology, transform
3. [Node descriptors](#node-descriptors) — Shader, ColorFilter, SkiaFilter, Geometry, Compute, Split/Composite, NestedGraph, CustomRenderNode
4. [Child effects and custom execution](#child-effects-and-custom-execution)
5. [Shader children and resource ownership](#shader-children-and-resource-ownership)
6. [GeometrySession & EffectInput](#geometrysession--effectinput)
7. [BoundsContract](#boundscontract)
8. [Structural vs parameter](#structural-vs-parameter)
9. [Shader uniforms (SKSL / GLSL)](#shader-uniforms-sksl--glsl)

---

## Builder properties

```csharp
Rect  builder.OriginalBounds // the initial input rect at the start of this graph
Rect  builder.Bounds         // the current logical rect after all descriptors appended so far
float builder.OutputScale   // 003 s_out (device px per logical unit at the root)
float builder.WorkingScale  // 003 w — the density this boundary's buffers run at
float builder.MaxWorkingScale // quality ceiling forwarded to nested authoring work; +Inf means no ceiling
RenderIntent builder.RenderIntent // preview/delivery failure policy for deferred authoring resources
RenderPullPurpose builder.PullPurpose // frame/auxiliary purpose for deferred resources and nested work
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

Append with `builder.Shader/ColorFilter/SkiaFilter/Geometry/Compute/Split/Composite/NestedGraph/CustomRenderNode(descriptor)`.
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

`UniformBindingBuilder` exposes the complete typed binding vocabulary below. `Raw` is evaluated when the parameter
block is applied; use `Deferred` when the value needs the execution-time pass context.

```csharp
Float(string name, float value)
Float2(string name, float x, float y)
Float3(string name, float x, float y, float z)
Float4(string name, float x, float y, float z, float w)
Int(string name, int value)
FloatArray(string name, float[] values)
Matrix3x3(string name, Matrix value)
DensityScaledFloat2(string name, float logicalX, float logicalY)
Raw(string name, Action<SKRuntimeShaderBuilder, string> writer)
Deferred(string name, Action<SKRuntimeShaderBuilder, string, PassUniformContext> writer)
Add(UniformBinding binding)
```

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

// Script-friendly appender; omitted bounds default to FullFrame.
builder.Geometry(
    Action<GeometrySession> render, BoundsContract? bounds = null,
    object? structuralToken = null, bool requiresReadback = false)
```
The sole descriptor that carries a rendering callback. Never fused, always its own pass. Set `requiresReadback` when the callback calls `EffectInput.Snapshot()`; undeclared readback is rejected.

### Compute (GLSL)
```csharp
ComputeNodeDescriptor.Create(
    Action<IComputeContext> dispatch, int passCount,
    BoundsContract bounds, ComputeFallbackPolicy fallback,
    int colorScratchCount = 0,
    object? structuralToken = null,
    ComputeDispatchFailureBehavior dispatchFailureBehavior = ComputeDispatchFailureBehavior.Throw)
```
`passCount` is the exact number of successful `IComputeContext.Run(...)` calls. An excess call is rejected before dispatch and a shortfall after the callback returns normally. `CopySourceToDestination()` is an exclusive terminal alternative: do not combine it with `Run(...)` or acquire scratch afterward. `bounds` is mandatory: use `BoundsContract.FullFrame` for a full-frame kernel or an exact local contract. The no-Vulkan `fallback` is one closed policy value: `ComputeFallbackPolicy.Identity`, `.Skip`, or `.Cpu(callback, requiresReadback: true)`. Set `requiresReadback: true` on the CPU policy when its callback snapshots an input. The color scratch count is the maximum concurrent acquisition and is enforced at runtime. Fullscreen compute passes are color-only; there is no depth scratch API.

### Split / Composite
```csharp
SplitNodeDescriptor.Static(
    Action<ISplitEmitter> emit, int branchCount, object? structuralToken = null,
    bool requiresReadback = false)

SplitNodeDescriptor.Dynamic(
    Action<ISplitEmitter> emit, object? structuralToken = null,
    bool requiresReadback = false)

CompositeNodeDescriptor.Create(
    BlendMode blendMode, IEnumerable<Point>? inputOffsets = null,
    object? structuralToken = null)
```
`emitter.Emit(tileBounds, session => { ... })` schedules a branch. A static split must call `Emit` exactly `branchCount` times; excess calls fail before allocating and a shortfall fails after the callback returns. Fusion never crosses a split. Set `requiresReadback` when the emitter snapshots `emitter.Input`.

A dynamic split discovers its branch count at execution time and therefore keeps that count out of the structural
key. Use it when the exact count cannot be declared safely at describe time. `CompositeNodeDescriptor` folds the
current branch set into one output; `inputOffsets`, when supplied, are per-branch logical translations.

### NestedGraph

```csharp
NestedGraphNodeDescriptor.Create(
    Action<EffectGraphBuilder, int> describeBranch,
    object? structuralToken = null)

NestedGraphNodeDescriptor.CreateStateful(
    Action<EffectGraphBuilder, int> describeBranch,
    Action<IReadOnlySet<int>> branchesCompleted,
    object? structuralToken = null)
```

The callback receives a builder over one branch and its stable branch index. The executor maintains a persistent
hierarchical plan cache per parent-node ordinal and branch index. Use `nested.Effect(childResource)` inside the
callback when the child may select a custom factory. A stateful author uses `CreateStateful`; after every live branch
in a successful pull finishes, `branchesCompleted` receives the complete stable live-ordinal set so state for
disappeared branches can be retired. It is not invoked when any branch fails.

### CustomRenderNode

```csharp
CustomRenderNodeDescriptor.Create(CustomRenderNodeFilterEffect.Resource resource)
builder.CustomRenderNode(CustomRenderNodeDescriptor descriptor)
```

This is the typed direct-node boundary for fully opaque execution. It is always full-frame, dynamic-output, and
never fused. Prefer `builder.Effect(resource)` when composing an effect: it chooses inline declarative execution or
a custom descriptor without changing the child's top-level policy. Use `CustomRenderNode` directly only when code
already holds a constructed descriptor or a `CustomRenderNodeFilterEffect.Resource`.

---

## Child effects and custom execution

### Placement-preserving composition

```csharp
EffectGraphBuilder builder.Effect(FilterEffect.Resource child)
```

Containers use this method for every enabled child. The default plan factory is inlined into the current graph; an
overridden plan factory or a fully opaque factory becomes one `CustomRenderNodeDescriptor`. Do not call the child's
`Describe` method directly, because doing so discards its selected render-node policy.

### Narrow plan-policy customization

An ordinary descriptor-authored effect may extend its generated `Resource` partial and return a retained plan
factory. Its node MUST derive from `PlanFilterEffectRenderNode`; graph compilation, ROI, pooling, and caches remain
engine-owned.

```csharp
public new partial class Resource
{
    private static readonly PlanFilterEffectRenderNodeFactory s_factory =
        PlanFilterEffectRenderNodeFactory.Of<Resource, MyPlanRenderNode>(
            static resource => new MyPlanRenderNode(resource));

    public override PlanFilterEffectRenderNodeFactory PlanRenderNodeFactory => s_factory;
}

PlanFilterEffectRenderNodeFactory.Of<TResource, TNode>(Func<TResource, TNode> create)
    where TResource : FilterEffect.Resource
    where TNode : PlanFilterEffectRenderNode
```

### Fully opaque execution

Derive the effect from `CustomRenderNodeFilterEffect`. Its sealed `Describe` routes the generated custom resource
through `builder.Effect`, keeping top-level and nested placement equivalent. Extend the generated `Resource`
partial to provide the mandatory retained factory, and implement `Process` in a `FilterEffectRenderNode` subclass.

```csharp
public new partial class Resource
{
    private static readonly FilterEffectRenderNodeFactory s_factory =
        FilterEffectRenderNodeFactory.Of<Resource, MyRenderNode>(
            static resource => new MyRenderNode(resource));

    public override FilterEffectRenderNodeFactory RenderNodeFactory => s_factory;
}

FilterEffectRenderNodeFactory.Of<TResource, TNode>(Func<TResource, TNode> create)
    where TResource : FilterEffect.Resource
    where TNode : FilterEffectRenderNode
```

Both factory properties MUST return a stable retained instance. Factory reference identity participates in render
node reuse; constructing a new factory on each getter call disables reuse. The opaque child render node remains
alive until all operations it returned are disposed. Its `RenderNodeContext` preserves render intent, pull purpose,
scales, diagnostics, cache policy, and child-processor allocation policy, but receives `RequestedBounds =
Rect.Invalid` because an opaque pass has no compiler-visible backward-bounds contract.

---

## Shader children and resource ownership

```csharp
ChildBinding builder.Sampler(string name, SKShader shader)
ChildBinding builder.Child(string name, SKShader shader)
T builder.Track<T>(T disposable) where T : IDisposable
new ChildBinding(string name, SKShader shader)
ChildBinding.Deferred(string name, Func<PassUniformContext, SKShader> factory)
```

| Construction | Owner and lifetime | Use |
|---|---|---|
| `builder.Sampler` / `builder.Child` | Graph-owned; disposed after the frame's graph executes, including a skipped pass | A fresh eager shader created in `Describe` |
| `builder.Track` | Graph-owned; each registered instance is disposed once with the graph | Any other fresh per-frame disposable used by descriptors |
| `new ChildBinding(...)` | Caller-owned; the graph and executor never dispose it | A cached eager shader whose owner manages eviction/disposal |
| `ChildBinding.Deferred(...)` | Executor-owned; factory returns a fresh shader per pass and the executor disposes it after that pass | A density-, bounds-, or coordinate-dependent child built from `PassUniformContext` |

Only an eager sampler that is invariant with respect to pixel position may be attached to a fusable snippet.
Whole-source shaders accept eager and deferred children. Names are structural; shader instances and contents are
parameters. Never dispose a graph-owned or executor-owned product yourself, and never return a cached/shared shader
from a deferred factory.

---

## GeometrySession & EffectInput

Inside a `GeometryNode`/`Split` branch callback the executor owns the target — you only draw:

```csharp
// GeometrySession
Rect  session.Bounds                       // output buffer rect
float session.OutputScale                  // 003 s_out
float session.WorkingScale                 // 003 w
float session.MaxWorkingScale
RenderIntent session.RenderIntent
RenderPullPurpose session.PullPurpose
PipelineDiagnostics? session.Diagnostics
IReadOnlyList<EffectInput> session.Inputs
ImmediateCanvas session.OpenCanvas()       // canvas over the pooled output target
void session.DiscardOutput()                // drop an empty result
void session.SetOutputBounds(Rect bounds)   // shrink the emitted result within session.Bounds

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
