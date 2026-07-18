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

### Extension project setup

Use the extension SDK. It references the Beutl packages and the `Beutl.Engine.SourceGenerators` analyzer at the
same version, so a partial drawable receives its generated `Resource` and `ToResource` implementation:

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

## Basic pattern

### 1. Minimal Drawable

```csharp
using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
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
| `partial class` | Required — the `Beutl.Engine.SourceGenerators` analyzer emits the `Resource` class |
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
- The source generator owns a private `List<T.Resource>` backing field.
- Adds, removes, and updates on the `EngineObject` property are reconciled automatically by `Resource.Update`.
- Inside the `Resource` class the collection is exposed as a get-only `IReadOnlyList<T.Resource>` immutable snapshot.
  Do not cast or mutate it; update the owning `IListProperty<T>` and run `Resource.Update`.

**Example (`DrawableGroup`):**
```csharp
public sealed partial class DrawableGroup : Drawable
{
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        // Resource.Children is an immutable IReadOnlyList<Drawable.Resource> snapshot.
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

The source generator creates a fresh `Resource` for every `ToResource` call and treats initialization as a
transaction: if `Update` fails, it disposes the partially initialized resource while preserving the acquisition
failure. A custom `ToResource` override must keep both guarantees. Never return a cached resource or the same
instance from multiple calls; Frame and Auxiliary contexts own, update, and dispose their resources independently,
and composition rejects cross-purpose aliases.

Generated ownership is reserved as one graph before cleanup starts and swept completely even when one cleanup fails.
For a manually owned `EngineObject.Resource`, implement `PrepareResourceDispose` and call `context.Reserve(...)`
without mutating state, then detach the field in `PostDispose`; the reserved graph performs the disposal. For other
managed disposables, release only when `disposing` is true, detach every field before fallible cleanup, dispose every
owner via `DisposeOwnedResources`, and rethrow the exact first failure with `ThrowIfCleanupFailed`.

All resource operations are non-reentrant. A handwritten or generation-suppressed `Resource.Update` must hold
`BeginExclusiveResourceOperation(obj)` across the complete override. A handwritten ownership getter must return
`ReadGeneratedResourceState(ref field)` so validation and the read share the lifecycle gate. Disposal during an
update/bind operation or another thread's cleanup throws `InvalidOperationException` before cleanup starts and can
be retried after that operation ends; getter access after cleanup throws `ObjectDisposedException`. Recompile every
extension that contains generated `Resource` classes; an older generated binary cannot participate in this protocol.
Cleanup of a directly constructed `Resource` also runs these hooks, before any original object necessarily exists;
do not call `GetOriginal()` from cleanup unless the resource is known to have completed or entered `Update`.

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
        partial void PreUpdate(MyDrawable obj, CompositionContext context)
        {
            // Refresh custom values
        }

        // Hook called after Update
        partial void PostUpdate(MyDrawable obj, CompositionContext context)
        {
            // Refresh cache; bump Version
            if (_needsUpdate)
            {
                MyInternalData? cachedData = _cachedData;
                _cachedData = null;
                Version++;

                Exception? failure = null;
                DisposeOwnedResources(ref failure, cachedData);
                ThrowIfCleanupFailed(failure);
            }
        }

        // Hook called on disposal
        partial void PostDispose(bool disposing)
        {
            if (!disposing) return;

            MyInternalData? cachedData = _cachedData;
            _cachedData = null;
            Exception? failure = null;
            DisposeOwnedResources(ref failure, cachedData);
            ThrowIfCleanupFailed(failure);
        }
    }
}
```

### Version management

`Resource.Version` drives cache invalidation. Bump `Version++` whenever the value changes.

```csharp
partial void PostUpdate(MyDrawable obj, CompositionContext context)
{
    if (_geometryResource is null)
    {
        _geometryResource = _geometry.ToResource(context);
        Version++;
    }
    else
    {
        var oldVersion = _geometryResource.Version;
        bool updateOnly = false;
        _geometryResource.Update(_geometry, context, ref updateOnly);
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
        Size availableSize = context.Size;
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
        Size availableSize = context.Size;
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

        partial void PostUpdate(MyShape obj, CompositionContext context)
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
                    MyGeometry.Resource replacement = _geometry.ToResource(context);
                    var cleanupFailure = ReplaceOwnedResource(ref _geometryResource, replacement);
                    Version++;
                    cleanupFailure?.Throw();
                }
                else
                {
                    var oldVersion = _geometryResource.Version;
                    bool updateOnly = false;
                    _geometryResource.Update(_geometry, context, ref updateOnly);
                    if (oldVersion != _geometryResource.Version)
                    {
                        Version++;
                    }
                }
            }
        }

        partial void PrepareResourceDispose(
            bool disposing,
            EngineObject.Resource.GeneratedResourceCleanupContext context)
        {
            if (disposing)
                context.Reserve(_geometryResource);
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing) return;

            _geometryResource = null;
        }

        public override Geometry.Resource? GetGeometry()
            => ReadGeneratedResourceState(ref _geometryResource);
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
- [ ] Manual resources are detached and fully swept under a `disposing` guard
- [ ] A custom `ToResource` returns a fresh instance per call and rolls back failed initialization
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
