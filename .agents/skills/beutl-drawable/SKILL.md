---
name: beutl-drawable
description: Implementation guide for Beutl's Drawable class. Use when adding a new Drawable (drawable object) to a Beutl project. Triggers on "create a Drawable", "implement a drawable object", "add an element like SourceImage/Shape/TextBlock". Covers both the Beutl core and extension packages.
---

# Beutl Drawable implementation guide

Guide for implementing a custom Drawable (drawable object) in Beutl.

## Architecture overview

```
EngineObject (base)
  └── Drawable (abstract)
        ├── Shape (abstract) — shape family
        │     ├── RectShape
        │     ├── EllipseShape
        │     └── GeometryShape
        ├── SourceImage — image
        ├── SourceVideo — video
        ├── SourceBackdrop — backdrop
        ├── TextBlock — text
        └── DrawableGroup — group
```

## Where you implement it matters

### In the Beutl core

```csharp
using Beutl.Language;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.MyDrawable), ResourceType = typeof(Strings))]
public partial class MyDrawable : Drawable
{
    // Add the string to Strings.resx
}
```

### In an extension package

```csharp
using MyExtension.Strings; // extension's own string resources

namespace MyExtension.Graphics;

// Option 1: use your own string resource
[Display(Name = nameof(ExtensionStrings.MyDrawable), ResourceType = typeof(ExtensionStrings))]
public partial class MyDrawable : Drawable
{
}

// Option 2: pass a literal string (skip localization)
[Display(Name = "My Drawable")]
public partial class MyDrawable : Drawable
{
}
```

**Notes for extension packages:**
- Use your extension's own namespace.
- Create string resources inside the extension project.
- `Beutl.Language.Strings` is not available (internal).

## Basic pattern

### 1. Minimal Drawable

```csharp
using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Graphics; // or your extension's namespace

[Display(Name = "My Drawable")]
public partial class MyDrawable : Drawable
{
    public MyDrawable()
    {
        ScanProperties<MyDrawable>();
    }

    // Property definitions (below)

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        // Compute and return the size
        return new Size(100, 100);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        // Drawing logic
    }
}
```

### 2. Required elements

| Element | Why |
|------|------|
| `partial class` | Required — a source generator emits the `Resource` class |
| `[Display]` attribute | Sets the display name shown in the editor |
| `ScanProperties<T>()` in the constructor | Registers properties with the system |
| `MeasureCore` | Returns the drawable's size |
| `OnDraw` | The actual drawing logic |

## Property definitions

### Value properties (primitives)

```csharp
// Animatable value property
[Display(Name = "Width")]
[Range(0, float.MaxValue)]
public IProperty<float> Width { get; } = Property.CreateAnimatable<float>(100);

// Non-animatable value property
[Display(Name = "Mode")]
public IProperty<MyMode> Mode { get; } = Property.Create(MyMode.Default);

// Boolean
public IProperty<bool> IsVisible { get; } = Property.CreateAnimatable(true);
```

### Object properties (EngineObject-derived types)

```csharp
// Brush (fill) — already on the Drawable base class
[Display(Name = "Fill")]
public IProperty<Brush?> Fill { get; } = Property.Create<Brush?>();

// Pen (stroke)
[Display(Name = "Stroke")]
public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

// Transform
[Display(Name = "Transform")]
public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();

// FilterEffect
[Display(Name = "Filter")]
public IProperty<FilterEffect?> FilterEffect { get; } = Property.Create<FilterEffect?>();

// ImageSource
[Display(Name = "Source")]
public IProperty<ImageSource?> Source { get; } = Property.Create<ImageSource?>();
```

### List properties (IListProperty)

Use when the drawable owns a collection of child elements:

```csharp
// List of child Drawables
public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

// List of custom elements
public IListProperty<GradientStop> GradientStops { get; } = Property.CreateList<GradientStop>();
```

**Behavior of `IListProperty`:**
- The source generator emits a `List<T.Resource>` field.
- Adds, removes, and updates are tracked automatically.
- Inside the `Resource` class the collection is accessible as `List<T.Resource>`.

**Example (`DrawableGroup`):**
```csharp
public sealed partial class DrawableGroup : Drawable
{
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        // Resource.Children is List<Drawable.Resource>
        foreach (Drawable.Resource item in r.Children)
        {
            context.DrawDrawable(item);
        }
    }
}
```

### SuppressResourceClassGeneration

When you want to suppress auto-generation of the `Resource` class and manage values manually:

```csharp
[SuppressResourceClassGeneration]
[Display(Name = "FontFamily")]
public IProperty<FontFamily?> FontFamily { get; } = Property.Create<FontFamily?>();
```

## The Resource class

To extend the auto-generated `Resource` class:

```csharp
public partial class MyDrawable : Drawable
{
    // ... property definitions ...

    public partial class Resource
    {
        // Extra fields
        private MyInternalData? _cachedData;

        // Hook called before Update
        partial void PreUpdate(MyDrawable obj, RenderContext context)
        {
            // Refresh custom values
        }

        // Hook called after Update
        partial void PostUpdate(MyDrawable obj, RenderContext context)
        {
            // Refresh cache; bump Version
            if (_needsUpdate)
            {
                Version++;
                _cachedData = null;
            }
        }

        // Hook called on disposal
        partial void PostDispose(bool disposing)
        {
            _cachedData?.Dispose();
        }
    }
}
```

### Version management

`Resource.Version` drives cache invalidation. Bump `Version++` whenever the value changes.

```csharp
partial void PostUpdate(MyDrawable obj, RenderContext context)
{
    if (_geometryResource is null)
    {
        _geometryResource = _geometry.ToResource(context);
        Version++;
    }
    else
    {
        var oldVersion = _geometryResource.Version;
        _geometryResource.Update(_geometry, context, ref _);
        if (oldVersion != _geometryResource.Version)
        {
            Version++;
        }
    }
}
```

## Drawing

### Main methods on `GraphicsContext2D`

```csharp
protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
{
    var r = (Resource)resource;

    // Geometry
    context.DrawGeometry(geometry, r.Fill, r.Pen);

    // Image
    context.DrawImageSource(imageSource, Brushes.Resource.White, null);

    // Text
    context.DrawText(formattedText, r.Fill, r.Pen);

    // Backdrop
    context.DrawBackdrop(backdrop);

    // Child Drawable
    context.DrawDrawable(childResource);
}
```

### Push/Pop pattern

```csharp
protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
{
    var r = (Resource)resource;

    using (context.PushTransform(Matrix.CreateTranslation(10, 10)))
    using (context.PushOpacity(0.5f))
    {
        // Transform and opacity apply inside this scope
        context.DrawGeometry(geometry, r.Fill, r.Pen);
    }
}
```

## Overriding `Render`

Overriding `Render` lets you **completely replace the base class's draw logic**. You can customize the order in which BlendMode, Transform, Opacity, and FilterEffect are applied.

```csharp
public override void Render(GraphicsContext2D context, Drawable.Resource resource)
{
    // Do not call base.Render — fully overridden
    if (resource.IsEnabled)
    {
        var r = (Resource)resource;

        // Custom rendering logic
        Size availableSize = context.Size.ToSize(1);
        Size size = MeasureCore(availableSize, resource);

        Matrix transform = GetTransformMatrix(availableSize, size, resource);

        // Example: custom order of effect application
        using (context.PushBlendMode(r.BlendMode))
        using (context.PushTransform(transform))
        using (context.PushOpacity(r.Opacity / 100f))
        using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
        {
            OnDraw(context, resource);
        }
    }
}
```

**Use cases for overriding Render:**
- You need to grab a backdrop before drawing (`SourceBackdrop`).
- You want to customize the order in which children are drawn (`DrawableGroup`).
- You use a custom RenderNode.
- Transform calculation depends on the children's bounds.

**`DrawableGroup` example (custom Transform application):**
```csharp
public override void Render(GraphicsContext2D context, Drawable.Resource resource)
{
    if (resource.IsEnabled)
    {
        var r = (Resource)resource;
        Size availableSize = context.Size.ToSize(1);
        var boundsMemory = context.UseMemory<Rect>();

        using (context.PushBlendMode(r.BlendMode))
        using (context.PushNode(...)) // custom Transform node
        using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
        using (context.PushNode(...)) // bounds calculation node
        {
            OnDraw(context, r);
        }
    }
}
```

## Shape-derived classes

For subclasses of `Shape`, implement `GetGeometry`:

```csharp
public sealed partial class MyShape : Shape
{
    public partial class Resource
    {
        private readonly MyGeometry _geometry = new();
        private MyGeometry.Resource? _geometryResource;

        partial void PostUpdate(MyShape obj, RenderContext context)
        {
            // Update the geometry
            _geometry.Width.CurrentValue = Math.Max(Width, 0);
            _geometry.Height.CurrentValue = Math.Max(Height, 0);

            // Update the resource
            if (_geometryResource is null)
            {
                _geometryResource = _geometry.ToResource(context);
                Version++;
            }
            else
            {
                if (_geometryResource.GetOriginal() != _geometry)
                {
                    var oldGeometry = _geometryResource;
                    _geometryResource = _geometry.ToResource(context);
                    oldGeometry.Dispose();
                    Version++;
                }
                else
                {
                    var oldVersion = _geometryResource.Version;
                    var _ = false;
                    _geometryResource.Update(_geometry, context, ref _);
                    if (oldVersion != _geometryResource.Version)
                    {
                        Version++;
                    }
                }
            }
        }

        partial void PostDispose(bool disposing)
        {
            _geometryResource?.Dispose();
        }

        public override Geometry.Resource? GetGeometry() => _geometryResource;
    }
}
```

## Checklist

When you add a new Drawable, confirm:

- [ ] Class is marked `partial`
- [ ] `[Display]` attribute sets the display name
- [ ] Constructor calls `ScanProperties<T>()`
- [ ] `MeasureCore` returns the size
- [ ] `OnDraw` implements drawing
- [ ] Resources for object properties are disposed in the appropriate partial method
- [ ] `Version++` is called whenever a value changes
- [ ] Strings added (core: `Strings.resx` / extension: your own resource)
- [ ] Correct namespace for the extension case

## Related files

- `src/Beutl.Engine/Graphics/Drawable.cs` — base class
- `src/Beutl.Engine/Graphics/Shapes/Shape.cs` — Shape base
- `src/Beutl.Engine/Graphics/DrawableGroup.cs` — Group implementation example
- `src/Beutl.Engine/Engine/EngineObject.cs` — EngineObject base
- `src/Beutl.Engine/Engine/Property.cs` — property factories
- `src/Beutl.Engine.SourceGenerators/EngineObjectResourceGenerator.cs` — source generator
