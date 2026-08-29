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

`FilterEffect` is the base class for applying filter processing to an image. Implementations rely on SkiaSharp's `SKImageFilter` / `SKColorFilter` or on SKSL/GLSL shaders.

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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // Apply effects via context.XXX()
    }
}
```

**Important:** the `partial` keyword is required (a source generator emits the `Resource` class).

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

**Using lists in `ApplyTo`:**
```csharp
public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
{
    var r = (Resource)resource;
    // r.Children is auto-generated as List<FilterEffect.Resource>
    foreach (FilterEffect.Resource child in r.Children)
    {
        child.GetOriginal().ApplyTo(context, child);
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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        foreach (FilterEffect.Resource item in r.Children)
        {
            item.GetOriginal().ApplyTo(context, item);
        }
    }
}
```

### 3. Implementing `ApplyTo`

Use the methods provided by `FilterEffectContext` to apply effects:

**Built-in effects** (see [references/context_methods.md](references/context_methods.md) for the full list):

```csharp
// Blur
context.Blur(sigma);

// Drop shadow
context.DropShadow(position, sigma, color);

// Color correction
context.Brightness(amount);
context.Saturate(amount);
context.HueRotate(degrees);

// Morphology
context.Dilate(radiusX, radiusY);
context.Erode(radiusX, radiusY);

// Transform
context.Transform(matrix, interpolationMode);
```

**Custom effects:**

```csharp
context.CustomEffect(
    data: (param1, param2),
    action: (data, customContext) => {
        // customContext.Targets — access the render targets
        // customContext.CreateTarget() — create a new target
        // customContext.Open() — obtain a canvas
    },
    transformBounds: (data, bounds) => bounds
);
```

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

### SKSL (SkiaShaderLanguage) pattern

> **Prefer `context.Shader(...)` for per-pixel work.** A `ShaderDefinition<TState>` recorded through
> `FilterEffectContext.Shader` is a typed fragment the planner can fuse with neighbouring shader stages
> into one GPU pass. `SKSLScriptEffect` also records supported scripts declaratively and exposes them to
> the fusion planner: `half4 main(float2 fragCoord)` becomes `WholeSource`, which can head a fusion run
> and absorb later per-pixel work but not upstream operations, while `half4 apply(half4 color)` becomes
> fully fusable `CurrentPixel` work. Scripts
> that cannot be represented declaratively—including reserved `__beutl*`/`fe*_*` names, multi-declarator
> uniforms, non-literal array lengths, or uniform types without a canonical zero value—automatically fall
> back to the legacy `CustomEffect` path, so existing scripts do not break. Using `CustomEffect`
> directly remains the right tool for raw-target allocation, sampling, or drawing, but it is opaque to the
> planner and forms a fusion boundary.

| Authoring construct | Fusion behavior | Limit or boundary |
|---|---|---|
| `CurrentPixel` shaders; immutable opacity | Fusable | May join a compatible fusion run. |
| `WholeSource` shaders | Can be the head of a fusion run | May absorb later per-pixel work; upstream work cannot fold into it. |
| Skia image filters (`Blur`, `DropShadow`, `Dilate`, `Erode`); `CustomEffect`; Geometry; 3D; raw canvas access | Not fusable | Forms a fusion boundary. |
| Sampler/child budget | Portable: 12; Vulkan/Metal: 12 | The implicit `src` sampler consumes one slot; exceeding the cap falls back to a standalone pass. |

Declare the shader once as a `static readonly ShaderDefinition<TState>` and record a call of it per frame.
The definition holds the shape — source, uniform and resource bindings — and `.Call(state)` supplies this
frame's values, so the planner sees a typed fragment it can fuse:

```csharp
public partial class MosaicEffect : FilterEffect
{
    private static readonly ShaderDefinition<Size> s_definition =
        ShaderDefinition<Size>.WholeSource(
            """
            uniform shader src;
            uniform float2 tileSize;

            half4 main(float2 fragCoord) {
                float2 blockIndex = floor(fragCoord / tileSize);
                float2 sampleCoord = blockIndex * tileSize + tileSize * 0.5;
                return src.eval(sampleCoord);
            }
            """,
            RenderBoundsContract.Identity,
            static bindings => bindings.Uniform(
                "tileSize",
                static tileSize => new Vector2(tileSize.Width, tileSize.Height),
                static (writer, tileSize, context) => writer.Set(tileSize * context.WorkingScale)));

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Shader(s_definition.Call(r.TileSize));
    }
}
```

The definition callback must be pure and non-capturing: its `MethodInfo` is the shader's structural
identity, so two frames that differ only in `tileSize` reuse one compiled program. If several effects share
one source, parse it once with `SkslSource.WholeSource(...)` or `SkslSource.CurrentPixel(...)` and pass the
result to the matching factory instead of the raw string.

Note the third argument to `Uniform`. A `WholeSource` shader reads `fragCoord` in **output-device pixels**,
while every property an author types — a tile size, a radius, an offset — is a **logical** length. Binding
one straight through makes the effect's appearance track the render density: the same mosaic shows coarser
tiles in a 2x preview than in the exported frame. Any uniform the shader compares against `fragCoord` has to
be multiplied by `ShaderExecutionContext.WorkingScale` in a binder, which is why the built-in `MosaicEffect`
scales both its tile size and its origin. A uniform that never meets `fragCoord` — a colour, a blend
amount — needs no binder and takes the two-argument overload. Getting this wrong is invisible at scale 1,
so it survives to export.

### The `CustomEffect` fallback

Reach for this only when the work cannot be expressed declaratively — raw target allocation, sampling
outside the declared input, or drawing onto the target. It is opaque to the planner and forms a fusion
boundary. The same mosaic, written the imperative way:

```csharp
public partial class MosaicEffect : FilterEffect
{
    private static readonly SKRuntimeEffect? s_runtimeEffect;

    static MosaicEffect()
    {
        string sksl = """
            uniform shader src;
            uniform float2 tileSize;

            half4 main(float2 fragCoord) {
                float2 blockIndex = floor(fragCoord / tileSize);
                float2 sampleCoord = blockIndex * tileSize + tileSize * 0.5;
                return src.eval(sampleCoord);
            }
            """;

        s_runtimeEffect = SKRuntimeEffect.CreateShader(sksl, out string? errorText);
        if (errorText is not null)
        {
            // log it
        }
    }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(r.TileSize, OnApplyTo, static (_, bounds) => bounds);
    }

    private static void OnApplyTo(Size tileSize, CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget target = c.Targets[i];
            using var image = target.RenderTarget!.Value.Snapshot();
            using var baseShader = SKShader.CreateImage(image);

            var builder = new SKRuntimeShaderBuilder(s_runtimeEffect);
            builder.Children["src"] = baseShader;
            builder.Uniforms["tileSize"] = tileSize.ToSKSize();

            var newTarget = c.CreateTarget(target.Bounds);
            using (SKShader shader = builder.Build())
            using (var paint = new SKPaint { Shader = shader })
            using (var canvas = c.Open(newTarget))
            {
                canvas.Clear();
                canvas.Canvas.DrawRect(
                    new SKRect(0, 0, target.Bounds.Width, target.Bounds.Height), paint);
            }
            target.Dispose();
            c.Targets[i] = newTarget;
        }
    }
}
```

### Dynamic SKSL pattern

When the user can edit the script, compile it inside the `Resource` class:

```csharp
public new partial class Resource
{
    internal SKRuntimeEffect? _runtimeEffect;
    internal string? _compiledScript;

    partial void PostUpdate(YourEffect obj, RenderContext context)
    {
        CompileScript(Script);
    }

    private void CompileScript(string script)
    {
        if (_compiledScript == script) return;

        _runtimeEffect?.Dispose();
        _compiledScript = script;

        if (string.IsNullOrWhiteSpace(script)) return;

        _runtimeEffect = SKRuntimeEffect.CreateShader(script, out string? errorText);
        // handle errors
    }

    partial void PostDispose(bool disposing)
    {
        _runtimeEffect?.Dispose();
    }
}
```

### GLSL (Vulkan) pattern

Use the fragment shader on Vulkan-capable environments:

```csharp
[Display(Name = nameof(Strings.Script), ResourceType = typeof(Strings))]
[DataType(DataType.MultilineText)]
public IProperty<string> FragmentShader { get; } = Property.Create("""
    #version 450

    layout(location = 0) in vec2 fragCoord;
    layout(location = 0) out vec4 outColor;

    layout(set = 0, binding = 0) uniform sampler2D srcTexture;

    layout(push_constant) uniform PushConstants {
        float progress;
        float time;
        float width;
        float height;
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

```xml
<ItemGroup>
  <PackageReference Include="Beutl.Extensibility" Version="x.x.x" />
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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect((r.Offset, r.Pen), Apply, TransformBounds);
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

The source generator (`EngineObjectResourceGenerator`) emits a `partial class Resource`. Property values declared as `IProperty<T>` are copied onto this `Resource` class so they can be accessed thread-safely during rendering.

### Extending the Resource class

When you need extra fields or hook logic, declare your own `partial class Resource`:

```csharp
public new partial class Resource
{
    internal SKRuntimeEffect? _effect;

    partial void PostUpdate(YourEffect obj, RenderContext context)
    {
        // Additional update logic
    }

    partial void PostDispose(bool disposing)
    {
        _effect?.Dispose();
    }
}
```
