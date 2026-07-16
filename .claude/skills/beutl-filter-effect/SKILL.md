---
name: beutl-filter-effect
description: |
  Implementation guide for Beutl's FilterEffect. Use when authoring a new filter effect (blur, color
  correction, drop shadow, etc.).
  Triggers: "create a FilterEffect", "implement a new effect", "add a filter", "Beutl effect dev",
  "SKSL", "GLSL effect".
---

# Beutl FilterEffect implementation guide

## Overview

`FilterEffect` is the base class for applying filter processing to an image. Effects are **declarative** (feature 004): an effect no longer *executes* rendering — it *describes* a graph of node descriptors, and the engine compiles, caches, fuses, and executes that graph.

Override the abstract `FilterEffect.Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)` and append node descriptors via `builder`:

- **Shader** (SKSL snippet or whole-source), **ColorFilter**, **SkiaFilter** (`SKImageFilter`), **Geometry** (imperative canvas draw), **Compute** (GLSL passes), **Split** / **Composite**, **NestedGraph**, and the typed **CustomRenderNode** boundary for fully opaque execution.
- Convenience methods (`builder.Blur`, `builder.DropShadow`, `builder.Saturate`, `builder.Dilate`, `builder.Transform`, color matrices, …) keep the names they had on the old `FilterEffectContext` and expand to the right descriptor for you.

Consequences you author against: adjacent coordinate-invariant color nodes **fuse into one GPU draw**; intermediate buffers come from a **pool**; compiled plans are **cached on a structural key**, so animated parameter values update uniforms without recompiling. This is why the two hard rules below matter — never bake a parameter value into shader source, and always declare bounds for non-invariant work.

> The authoritative contract is [`docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md`](../../../docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md); the removed-surface migration map (`ApplyTo`, `FilterEffectContext`, `CustomFilterEffectContext`, `EffectTarget`, `SKImageFilterBuilder`, …) is [`contracts/breaking-changes.md`](../../../docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md).

## Implementation steps

### 1. Class definition

```csharp
using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.YourEffectName), ResourceType = typeof(Strings))]
public sealed partial class YourEffect : FilterEffect
{
    public YourEffect()
    {
        ScanProperties<YourEffect>();  // Required: scan properties
        // Initialize EngineObject-typed properties here (see below)
    }

    // Property definitions

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // Append node descriptors via builder.XXX() — never render here
    }
}
```

**Important:** the `partial` keyword and the `Beutl.Engine.SourceGenerators` analyzer are both required: the
generator emits the concrete `Resource` class and `ToResource` implementation. The recommended
`Beutl.Extensibility.Sdk` project setup below references the analyzer automatically.

### 2. Property definition patterns

#### Animatable properties

Use `Property.CreateAnimatable()` for properties that support keyframe animation:

```csharp
// Numeric property with a range
[Display(Name = nameof(Strings.Amount), ResourceType = typeof(Strings))]
[Range(0, float.MaxValue)]
public IProperty<float> Amount { get; } = Property.CreateAnimatable(100f);

// Size property
[Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
[Range(typeof(Size), "0,0", "max,max")]
public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

// Color property
[Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.Transparent);

// Point property
[Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
public IProperty<Point> Position { get; } = Property.CreateAnimatable(new Point());

// Boolean / enum property
[Display(Name = nameof(Strings.ShadowOnly), ResourceType = typeof(Strings))]
public IProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);
```

#### Non-animatable properties

Use `Property.Create()` for static configuration values:

```csharp
// String property (multi-line)
[Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
[DataType(DataType.MultilineText)]
public IProperty<string> Script { get; } = Property.Create("default value");

// EngineObject-typed property (Pen, Brush, Transform, Geometry, etc.)
[Display(Name = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();
```

#### Initializing EngineObject-typed properties

When `Property.Create<T>()` is used with a `T` that derives from `EngineObject`, **do not pass a default value to the factory**. Instead, set `CurrentValue` in the constructor after `ScanProperties`:

```csharp
public YourEffect()
{
    ScanProperties<YourEffect>();
    // Initialize EngineObject-typed properties after ScanProperties
    Pen.CurrentValue = new Pen();
    Brush.CurrentValue = new SolidColorBrush(Colors.White);
    Transform.CurrentValue = new TranslateTransform();
}

[Display(Name = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

[Display(Name = nameof(Strings.Fill), ResourceType = typeof(Strings))]
public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

[Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();
```

#### List properties (`IListProperty`)

Use `Property.CreateList<T>()` to hold an `EngineObject` collection:

```csharp
// List of FilterEffects (group effect)
public IListProperty<FilterEffect> Children { get; } = Property.CreateList<FilterEffect>();

// List of gradient stops (gradient brush)
public IListProperty<GradientStop> GradientStops { get; } = Property.CreateList<GradientStop>();
```

**List operations:**
```csharp
// Add
Children.Add(new Blur());

// Insert
Children.Insert(0, new DropShadow());

// Remove
Children.RemoveAt(0);
Children.Clear();
```

**Using lists in `Describe`:** describe every enabled child into the *same* builder so their nodes can fuse with each other (a group must not hide its children behind one opaque pass):
```csharp
public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
{
    var r = (Resource)resource;
    // r.Children is auto-generated as an immutable IReadOnlyList<FilterEffect.Resource> snapshot.
    foreach (FilterEffect.Resource child in r.Children)
    {
        if (!child.IsEnabled)
            continue;

        builder.Effect(child);
    }
}
```

**Implementation example (`FilterEffectGroup.cs`):**
```csharp
[Display(Name = nameof(Strings.Group), ResourceType = typeof(Strings))]
public sealed partial class FilterEffectGroup : FilterEffect
{
    public FilterEffectGroup()
    {
        ScanProperties<FilterEffectGroup>();
    }

    public IListProperty<FilterEffect> Children { get; } = Property.CreateList<FilterEffect>();

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        foreach (FilterEffect.Resource item in r.Children)
        {
            if (!item.IsEnabled)
                continue;

            builder.Effect(item);
        }
    }
}
```

### 3. Implementing `Describe`

`Describe` MUST be side-effect-free apart from appending descriptors: **no rendering, no target allocation, no GPU calls.** It may be called every frame and must be cheap. Read all animated values from the passed `Resource`, never from live properties.

**Choosing a node kind:**

`EffectNodeDescriptor` is a closed union. Compose the public sealed descriptor types below; plugins cannot add an
unknown descriptor kind because the compiler has no registration seam for one.

| Want to… | Use |
|---|---|
| Per-pixel color math (gamma, curves, LUT) | `builder.Shader(ShaderNodeDescriptor.Snippet(...))` — fusable, identity bounds |
| A ready-made filter (blur, shadow, morphology, color) | the convenience method (`builder.Blur`, …) |
| A raw `SKColorFilter` / `SKImageFilter` | `builder.ColorFilter(...)` / `builder.SkiaFilter(...)` |
| A shader that samples other pixels (mosaic, displacement) | `builder.Shader(ShaderNodeDescriptor.WholeSource(source, bounds, ...))` |
| Imperative canvas drawing (stroke, flat shadow, clip, C# script) | `builder.Geometry(GeometryNodeDescriptor.Create(...))` |
| GLSL / compute passes | `builder.Compute(ComputeNodeDescriptor.Create(...))` |
| Fan out into tiles/branches | `builder.Split(...)` / `builder.Composite(...)` |
| Describe a child graph independently for every branch | `builder.NestedGraph(NestedGraphNodeDescriptor.Create(...))` |
| Compose another effect without changing its top-level execution policy | `builder.Effect(childResource)` |
| Embed a fully opaque child render node | normally `builder.Effect(customChildResource)`; use `builder.CustomRenderNode(CustomRenderNodeDescriptor.Create(...))` only when you already hold the descriptor/resource boundary |

**Built-in convenience methods** (same names as before; full list in [references/context_methods.md](references/context_methods.md)):

```csharp
builder.Blur(sigma);                     // Gaussian blur
builder.DropShadow(position, sigma, color);
builder.Brightness(amount);              // color correction
builder.Saturate(amount);
builder.HueRotate(degrees);
builder.Dilate(radiusX, radiusY);        // morphology
builder.Erode(radiusX, radiusY);
builder.Transform(matrix, interpolationMode);
```

**Child effects and custom execution:**

- Containers MUST call `builder.Effect(childResource)`, not `childResource.GetOriginal().Describe(...)`. The
  builder inlines an ordinary effect only when it uses the default plan factory; otherwise it emits a typed
  `CustomRenderNodeDescriptor`, so nested placement preserves the child's top-level policy.
- For a narrow execution-policy hook while retaining graph compilation, ROI, pooling, and caching, extend the
  generated `Resource` partial and override `PlanRenderNodeFactory` with one retained
  `PlanFilterEffectRenderNodeFactory.Of<TResource,TNode>(...)`. `TNode` MUST derive from
  `PlanFilterEffectRenderNode`.
- For fully opaque execution, derive the effect from `CustomRenderNodeFilterEffect`, extend its generated
  `Resource` partial with one retained `FilterEffectRenderNodeFactory.Of<TResource,TNode>(...)`, and implement a
  `FilterEffectRenderNode`. The base seals `Describe` and routes the resource through `builder.Effect`, so the
  effect remains group-safe. Do not create a new factory on each property access: factory identity controls node
  reuse.
- `builder.Sampler` and `builder.Child` transfer a fresh eager shader to graph ownership;
  `builder.Track` transfers any other per-frame disposable. `new ChildBinding(...)` remains caller-owned, while
  `ChildBinding.Deferred(...)` returns a fresh executor-owned shader for each pass. See the complete signatures and
  ownership table in [references/context_methods.md](references/context_methods.md).

**Two hard rules (why the cache/fusion works):**

- **Structure vs parameters.** Shader source identity, pass/branch counts, invariance flags, and the bounds-contract identity are *structural* — changing one recompiles the plan (once). Everything else (uniform values, colors, matrices, LUT contents) is a *parameter* and must NOT change the compiled plan. **Never interpolate a parameter value into shader source** — pass it as a uniform, or you defeat the program cache. Bounds MAY depend on parameters (an animated blur radius inflating bounds is fine); they are re-resolved every frame and are not structural.
- **Bounds & ROI.** Every non-invariant node MUST declare a `BoundsContract` (forward `TransformBounds` + backward `GetRequiredInputBounds`). A coordinate-invariant snippet gets identity bounds by construction and MUST only read the current pixel. See [contracts/effect-authoring.md §A3–A4](../../../docs/specs/004-gpu-pass-fusion/contracts/effect-authoring.md).

### 4. Localization

**For the Beutl core:**
Add entries to `src/Beutl.Language/Strings.resx` and `Strings.ja.resx`.

**For extension packages:**
Create your own resource files inside the extension project, or pass a literal string via the `Display` attribute:

```csharp
// Use your own resource
[Display(Name = nameof(MyExtensionStrings.EffectName), ResourceType = typeof(MyExtensionStrings))]

// Or pass a literal string (when localization is not needed)
[Display(Name = "My Effect")]
```

## Shader-based implementations

### SKSL snippet (fusable per-pixel color) — preferred

A coordinate-invariant snippet defines `half4 apply(half4 c)` where `c` is the **premultiplied-alpha, linear-light** source pixel (and your return value must be too). It participates in fusion and gets identity bounds for free — no bounds contract, no target management. Pass parameters as uniforms; never inline them into the source string.

Declare each uniform in its own statement. Precision qualifiers (`lowp`/`mediump`/`highp`) are supported, but do not declare a `struct` at any scope in a snippet: top-level structs are rejected by the descriptor and the pinned Skia compiler rejects function-local struct statements.

```csharp
// src/Beutl.Engine/Graphics/FilterEffects/Gamma.cs
private static readonly string s_snippet =
    """
    uniform float gamma;
    uniform float strength;

    half4 apply(half4 c) {
        float alpha = c.a;
        if (alpha <= 0.0001) return half4(0.0);
        float3 rgb = c.rgb / alpha;                       // unpremultiply
        float3 corrected = pow(max(rgb, float3(0.0)), float3(1.0 / gamma));
        float3 result = mix(rgb, corrected, strength);
        return half4(half3(result * alpha), half(alpha)); // re-premultiply
    }
    """;

public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
{
    var r = (Resource)resource;
    float gamma = Math.Clamp(r.Amount / 100f, 0.01f, 3f);
    builder.Shader(ShaderNodeDescriptor.Snippet(
        s_snippet, u => u.Float("gamma", gamma).Float("strength", r.Strength / 100f)));
}
```

### SKSL whole-source (samples other pixels)

When the shader samples coordinates other than the current pixel (mosaic, displacement), use `WholeSource`: an SKSL `half4 main(float2 coord)` with an implicit `src` child, plus a mandatory `BoundsContract`. Device-pixel uniforms must be late-bound from the pass's execution-time `PassUniformContext`: the executor can re-clamp the pass below `builder.WorkingScale`. Use `DensityScaledFloat2` for logical lengths and `Deferred` for target dimensions or other values that need `TargetWidth`, `TargetHeight`, or the actual `WorkingScale`.

```csharp
// src/Beutl.Engine/Graphics/FilterEffects/MosaicEffect.cs (abridged)
public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
{
    var r = (Resource)resource;
    Size tileSize = r.TileSize;

    // The non-local tile-centre samples use full-frame coordinates, so an ROI crop is unsound.
    builder.Shader(ShaderNodeDescriptor.WholeSource(
        ShaderSource,
        BoundsContract.FullFrame,
        u => u.DensityScaledFloat2(
                  "tileSize", (float)tileSize.Width, (float)tileSize.Height)
              .Deferred("resolution", (b, name, ctx) =>
                  b.Uniforms[name] = new[] { (float)ctx.TargetWidth, (float)ctx.TargetHeight })));
}
```

### Imperative canvas drawing (Geometry node)

Stroke, flat shadow, clipping, and C# scripts that must draw with a canvas use a `GeometryNode`. The engine hands your callback a `GeometrySession` with a canvas over a pooled output target and read-only `Inputs`; you never allocate or dispose targets. The bounds contract is **mandatory**.

```csharp
// src/Beutl.Engine/Graphics/FilterEffects/FlatShadow.cs (abridged)
public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
{
    var r = (Resource)resource;
    var data = (r.Angle, r.Length, r.Brush, r.ShadowOnly);
    Rect inputBounds = builder.Bounds;
    builder.Geometry(GeometryNodeDescriptor.Create(
        session => ApplyGeometry(session, data),
        // Contour tracing snapshots the whole input. A requested sub-ROI is not sufficient.
        BoundsContract.Create(rect => TransformBounds(data, rect), _ => inputBounds),
        structuralToken: nameof(FlatShadow),
        requiresReadback: true));
}

private static void ApplyGeometry(GeometrySession session, (...) data)
{
    EffectInput input = session.Inputs[0];       // read-only upstream result
    ImmediateCanvas canvas = session.OpenCanvas(); // canvas over the pooled output
    using Bitmap srcBitmap = input.Snapshot();
    float inputDensity = input.Density.IsUnbounded ? 1f : input.Density.Value;
    float outputDensity = session.WorkingScale;
    // ... draw into canvas; the executor owns the target, ROI, and sync ...
}
```

### Split into tiles / branches

```csharp
// src/Beutl.Engine/Graphics/FilterEffects/SplitEffect.cs (abridged)
public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
{
    var r = (Resource)resource;
    int hDiv = r.HorizontalDivisions, vDiv = r.VerticalDivisions;
    long branchCount = (long)hDiv * vDiv;
    bool useStaticPlan = !builder.HasBranchedInput // engine-only condition in the built-in implementation
        && hDiv > 0 && vDiv > 0
        && builder.Bounds.Width / hDiv >= 1f
        && builder.Bounds.Height / vDiv >= 1f
        && branchCount <= 4096;
    Action<ISplitEmitter> emit =
        emitter => EmitTiles(emitter, hDiv, vDiv, r.HorizontalSpacing, r.VerticalSpacing);

    // Only a safely declarable grid is structural. Sub-pixel grids, a split after fan-out, and grids above
    // 4096 branches use dynamic outputs so resource-plan construction stays bounded.
    builder.Split(useStaticPlan
        ? SplitNodeDescriptor.Static(emit, (int)branchCount, nameof(SplitEffect))
        : SplitNodeDescriptor.Dynamic(emit, nameof(SplitEffect)));
}
```

`EffectGraphBuilder.HasBranchedInput` is engine-internal; plugin authors should choose `Static` only when their
branch count is small, exact, and independent of per-branch runtime bounds. Otherwise use `Dynamic`. A static
descriptor's `branchCount` is structural and must match exactly; a dynamic descriptor keeps its runtime count out
of the plan key.

### Dynamic SKSL pattern

When the user can edit the script, compile it inside the `Resource` class (see `SKSLScriptEffect.cs` for the full
pattern). A script that declares no `src` child runs as a generator geometry pass. `CoordinateInvariant` asserts
identity bounds and single-pixel sampling for ROI/sizing, but a whole-source script still runs as its own pass;
only `ShaderNodeDescriptor.Snippet(...)` participates in shader-source fusion:

```csharp
public new partial class Resource
{
    internal SKRuntimeEffect? _runtimeEffect;
    internal string? _compiledScript;

    partial void PostUpdate(YourEffect obj, CompositionContext context)
    {
        CompileScript(Script);
    }

    private void CompileScript(string script)
    {
        if (_compiledScript == script) return;

        SKRuntimeEffect? replacement = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(script))
            {
                replacement = SKRuntimeEffect.CreateShader(script, out string? errorText);
                // validate replacement and handle errors before publishing it
            }
        }
        catch
        {
            replacement?.Dispose();
            throw;
        }

        SKRuntimeEffect? previous = _runtimeEffect;
        _runtimeEffect = replacement;
        _compiledScript = script;
        previous?.Dispose();
    }

    partial void PostDispose(bool disposing)
    {
        if (!disposing) return;

        SKRuntimeEffect? runtimeEffect = _runtimeEffect;
        _runtimeEffect = null;
        _compiledScript = null;
        Exception? failure = null;
        DisposeOwnedResources(ref failure, runtimeEffect);
        ThrowIfCleanupFailed(failure);
    }
}
```

### GLSL (Vulkan) pattern

GLSL fragment shaders run as a `ComputeNode` (`builder.Compute(ComputeNodeDescriptor.Create(...))`); the executor schedules color-only fullscreen passes, provides ping-pong color textures from the pool, and applies push constants. `passCount` is the exact number of successful `IComputeContext.Run(...)` calls: over-dispatch is rejected before execution and under-dispatch after a normal callback return. `CopySourceToDestination()` is an exclusive terminal alternative that cannot be combined with a dispatch or followed by scratch acquisition. Declare `BoundsContract.FullFrame` for a full-frame kernel or an exact local bounds contract. Declare the maximum concurrently acquired color scratch count so the compiled resource plan is exact; an acquire beyond that declaration throws. Depth scratch is not exposed because the fullscreen pipeline disables depth testing and writes. A compute node MUST declare one no-Vulkan `ComputeFallbackPolicy` (`Identity`, `Skip`, or `Cpu(callback, requiresReadback)`) so GPU-less CI still passes. If that CPU callback calls `EffectInput.Snapshot()`, construct it as `ComputeFallbackPolicy.Cpu(callback, requiresReadback: true)`. `dispatchFailureBehavior` defaults to `Throw`; opt into `IdentityInPreview` only when an ordinary callback/dispatch exception may preserve the input during interactive preview. It never converts cancellation, dispatch/resource-contract violations, or backend preparation failures to identity, and delivery still propagates the exception; allocation failures retain the executor's preview-drop/delivery-throw behavior. See `GLSLScriptEffect.cs` for the full pattern. Declare the shader source as a property:

```csharp
[Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
[DataType(DataType.MultilineText)]
public IProperty<string> FragmentShader { get; } = Property.Create("""
    #version 450

    layout(location = 0) in vec2 fragCoord;
    layout(location = 0) out vec4 outColor;

    layout(set = 0, binding = 0) uniform sampler2D srcTexture;

    layout(push_constant) uniform PushConstants {
        float progress;   // 0.0 - 1.0
        float duration;   // seconds
        float time;       // seconds
        float width;      // render target width (device px)
        float height;     // render target height (device px)
        float scale;      // working scale w; multiply absolute-px literals by this
    } pc;

    void main() {
        vec4 c = texture(srcTexture, fragCoord);
        outColor = c;
    }
    """);
```

## Implementing as an extension

### Project layout

```
MyExtension/
├── MyExtension.csproj
├── Effects/
│   └── MyCustomEffect.cs
└── Resources/
    ├── Strings.resx
    └── Strings.ja.resx
```

### Namespace

```csharp
namespace MyExtension.Effects;  // the extension's own namespace

[Display(Name = nameof(Strings.MyEffect), ResourceType = typeof(Strings))]
public sealed partial class MyCustomEffect : FilterEffect
{
    // ...
}
```

### csproj reference

Use the extension SDK. It references the Beutl packages and `Beutl.Engine.SourceGenerators` analyzer at the same
version, so the concrete partial effect shown above compiles with its generated `Resource`:

```xml
<Project Sdk="Beutl.Extensibility.Sdk/x.x.x">
  <PropertyGroup>
    <PackageId>Beutl.Extensions.MyExtension</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

If the project must keep `Microsoft.NET.Sdk`, reference the analyzer explicitly; a
`Beutl.Extensibility` package reference alone does not run resource generation:

```xml
<ItemGroup>
  <PackageReference Include="Beutl.Extensibility" Version="x.x.x" />
  <PackageReference Include="Beutl.Engine.SourceGenerators" Version="x.x.x"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
</ItemGroup>
```

## Implementation example

**Effect with an EngineObject-typed property (`StrokeEffect.cs`):**

```csharp
public partial class StrokeEffect : FilterEffect
{
    public StrokeEffect()
    {
        ScanProperties<StrokeEffect>();
        Pen.CurrentValue = new Pen();  // initialize after ScanProperties
    }

    [Display(Name = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public IProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Pen is null)
            return;

        var data = (r.Offset, r.Pen);
        Rect inputBounds = builder.Bounds;
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => Apply(session, data),
            // Contour tracing snapshots the whole input. A requested sub-ROI is not sufficient.
            BoundsContract.Create(rect => TransformBounds(data, rect), _ => inputBounds),
            structuralToken: nameof(StrokeEffect),
            requiresReadback: true));
    }
}
```

## File placement

**Beutl core:**
```
src/Beutl.Engine/Graphics/FilterEffects/YourEffect.cs
```

**Extension package:**
```
MyExtension/Effects/YourEffect.cs
```

## About the Resource class

The source generator (`EngineObjectResourceGenerator`) emits a `partial class Resource`. Property values declared as
`IProperty<T>` are captured onto this `Resource` class for rendering. Generated list properties are get-only
`IReadOnlyList<T.Resource>` immutable snapshots; mutate the owning model property and run `Resource.Update`, never
the resource snapshot itself. An owned object-resource property may only be initialized once or assigned the same
identity, so replacing it directly throws instead of leaking the previous owner.

Generated resources reserve the complete generated ownership graph before cleanup and dispose every generated
child/list resource even when one cleanup fails. A manual partial that owns an `EngineObject.Resource` must reserve
it without mutation in `PrepareResourceDispose`, then detach it in `PostDispose`; the graph performs the disposal.
For other manually owned disposables, only release managed objects when `disposing` is true, detach every owned field
before invoking fallible cleanup, sweep every owner, and rethrow the exact first failure with the protected
`DisposeOwnedResources` / `ThrowIfCleanupFailed` helpers.

All resource update/bind operations are non-reentrant. A handwritten or generation-suppressed `Resource.Update`
must hold `BeginExclusiveResourceOperation(obj)` across the complete override. Handwritten ownership getters return
`ReadGeneratedResourceState(ref field)` so validation and the read are atomic. Disposal during an operation or
another thread's cleanup throws `InvalidOperationException` before cleanup starts and is retryable once that
operation finishes; getter access after cleanup throws `ObjectDisposedException`. Recompile every
extension containing generated `Resource` classes so it emits the new lifecycle protocol.
Cleanup hooks also run for a directly constructed `Resource`; such a resource has no `GetOriginal()` value until its
first `Update` begins, so cleanup must not assume that value exists.

The generated `ToResource` method also treats construction as a transaction: it creates a fresh resource for each
call and disposes a partially initialized instance if `Update` fails. A custom `ToResource` override must preserve
both rules. In particular, never return one resource instance from multiple calls or share it between Frame and
Auxiliary contexts; each context may update and dispose its resource independently.

### Extending the Resource class

When you need extra fields or hook logic, declare your own `partial class Resource`:

```csharp
public new partial class Resource
{
    internal SKRuntimeEffect? _effect;

    partial void PostUpdate(YourEffect obj, CompositionContext context)
    {
        // Additional update logic
    }

    partial void PostDispose(bool disposing)
    {
        if (!disposing) return;

        SKRuntimeEffect? effect = _effect;
        _effect = null;
        Exception? failure = null;
        DisposeOwnedResources(ref failure, effect);
        ThrowIfCleanupFailed(failure);
    }
}
```
