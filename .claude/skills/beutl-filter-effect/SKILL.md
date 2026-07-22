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

`FilterEffect` is the base class for applying filter processing to an image. Authors keep the existing
`ApplyTo(FilterEffectContext, Resource)` lifecycle and append deferred operations to the context. Prefer
built-in operations, `ShaderDescription`, or `GeometryDescription`; use `CustomEffect` only for work that
cannot be described declaratively because it is an opaque GPU-pass-fusion boundary.

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
        // Append built-in, Shader, Geometry, or opaque fallback work via context.XXX().
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

**Deferred Shader effects:**

```csharp
private const string InvertSource = """
    half4 apply(half4 color) {
        return half4(color.a - color.rgb, color.a);
    }
    """;

public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
{
    context.Shader(ShaderDescription.CurrentPixel(InvertSource));
}
```

Use `CurrentPixel` only for the restricted `half4 apply(half4 color)` form. It is validated by the
engine and can join adjacent compatible Shader and invariant-opacity stages after upstream analytic
coverage has been resolved. It cannot sample the implicit source or use device/screen coordinates.

Use `WholeSource` for coordinate-dependent sampling. It declares an implicit `uniform shader src`, a
`half4 main(float2 coord)` entry point, and an explicit `RenderBoundsContract`. It remains a normal
unfused pass:

```csharp
private static readonly ShaderDescription s_offset = ShaderDescription.WholeSource(
    """
    uniform shader src;
    uniform float2 offset;
    half4 main(float2 coord) { return src.eval(coord - offset); }
    """,
    RenderBoundsContract.Create(
        bounds => bounds.Inflate(8),
        required => required.Inflate(8),
        structuralKey: typeof(YourEffect)),
    bindings => bindings.Uniform("offset", new System.Numerics.Vector2(8, 0)));
```

**Deferred Geometry effects:**

```csharp
context.Geometry(GeometryDescription.Create(
    render: session => session.Canvas.Use(canvas =>
    {
        // The output starts transparent. Read session.Input only through its declared APIs.
        canvas.DrawRectangle(session.OutputBounds, Brushes.Resource.White, null);
    }),
    bounds: RenderBoundsContract.Identity,
    hitTest: RenderHitTestContract.Input(0),
    structuralKey: typeof(YourEffect)));
```

`GeometrySession` is valid only during its callback. Its output allocation is initialized transparent;
`SetOutputBounds` may shrink within the declared conservative bounds, and `DiscardOutput` publishes no
value. Set `requiresReadback: true` only when CPU readback is genuinely required.

**Opaque fallback effects:**

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

`CustomEffect` preserves compatibility for backend-specific or otherwise uninspectable work. Its callback
runs only during execution, but the renderer treats it as opaque external work: it cannot be fused across,
and its internal pass/synchronization count is not claimed by request diagnostics. Do not use it merely to
compile an ordinary SkSL effect that can be expressed with `ShaderDescription`. Supply a conservative finite
`transformBounds` whenever possible. Omitting it declares genuinely unknown output bounds; the renderer then
resolves the complete finite local domain of the owning destination or target scope after enclosing transforms
and clips are known. A target-less root without an explicit `TargetDomain` fails before the callback runs. Later
Skia/custom/Shader/Geometry items remain in the same opaque runtime sequence and use actual target bounds; only
the sequence's final semantic output is cropped to the resolved domain. The two-argument overload is never an
identity-bounds shortcut.

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

Describe the source and bindings during `ApplyTo`; compilation and native child creation are deferred until
execution and cached by the renderer:

```csharp
public partial class TintEffect : FilterEffect
{
    private const string Source = """
        uniform float amount;
        half4 apply(half4 color) {
            half luminance = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
            return half4(mix(color.rgb, half3(luminance), amount), color.a);
        }
        """;

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.Shader(ShaderDescription.CurrentPixel(
            Source,
            bindings => bindings.Uniform("amount", r.Amount)));
    }
}
```

### Dynamic SKSL pattern

When the user can edit the script, keep the last validated `ShaderDescription` in the generated `Resource`
state. Do not create `SKRuntimeEffect` during recording:

```csharp
public new partial class Resource
{
    internal ShaderDescription? _description;
    internal string? _validatedScript;

    partial void PostUpdate(YourEffect obj, RenderContext context)
    {
        ValidateScript(Script);
    }

    private void ValidateScript(string script)
    {
        if (_validatedScript == script) return;
        _description = string.IsNullOrWhiteSpace(script)
            ? null
            : ShaderDescription.CurrentPixel(script);
        _validatedScript = script;
    }
}
```

### GLSL (Vulkan) opaque fallback

The public declarative Shader API accepts SkSL. A Vulkan-only GLSL implementation therefore remains an
opaque `CustomEffect` fallback and must also provide a supported ordinary-2D path if the effect is expected
to work without that backend:

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
