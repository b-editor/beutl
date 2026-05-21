# Contract: Pixel-Absolute Parameter Wrappers

**Surface**: `Beutl.Graphics.PixelLength`, `Beutl.Graphics.PixelExtent`, `Beutl.Graphics.PixelOffset` (new public types).

**Audience**: built-in effect authors, third-party plugin authors, scripting users (`CSharpScriptEffect`, `GLSLScriptEffect`).

## Stability

These types are public API on `Beutl.Engine`. Per the repo's "adopt better designs eagerly" priority, the names and shape are open for one round of feedback during the implementation PR; after the first release they follow normal semver discipline. The runtime semantics ("value is measured in pixels at the project's export resolution") are fixed and will not change without a breaking-change Conventional Commit.

## When to use a wrapper

Use a `Pixel*` wrapper for any parameter whose semantic dimension is **a length on the rendered raster**. Examples: blur sigma, shadow offset, stroke offset, dilation radius, mosaic tile size, displacement amplitude.

Do **not** use a `Pixel*` wrapper for:

- Dimensionless amounts (saturation, opacity, brightness, hue rotation degrees, contrast).
- Colors, color matrices, LUTs.
- Time / frames (`DelayAnimationEffect`).
- Path-relative units (`PathFollowEffect`).
- Counts, indices, booleans.

If unsure: ask "if the user re-rendered the same project at a different resolution, should this value scale?" If yes → wrap. If no → leave as a primitive.

## Surface

```csharp
namespace Beutl.Graphics
{
    public readonly struct PixelLength : IEquatable<PixelLength>, IFormattable
    {
        public PixelLength(float referencePixels);
        public float ReferencePixels { get; }
        public static PixelLength Zero { get; }
        public static implicit operator PixelLength(float referencePixels);
        public float ResolveX(RenderScale scale);
        public float ResolveY(RenderScale scale);
        public float ResolveUniform(RenderScale scale);
    }

    public readonly struct PixelExtent : IEquatable<PixelExtent>, IFormattable
    {
        public PixelExtent(float width, float height);
        public PixelExtent(Size referencePixels);
        public float Width { get; }
        public float Height { get; }
        public Size ToSize();
        public static PixelExtent Empty { get; }
        public Size Resolve(RenderScale scale);
    }

    public readonly struct PixelOffset : IEquatable<PixelOffset>, IFormattable
    {
        public PixelOffset(float x, float y);
        public PixelOffset(Point referencePixels);
        public float X { get; }
        public float Y { get; }
        public Point ToPoint();
        public static PixelOffset Zero { get; }
        public Point Resolve(RenderScale scale);
    }
}
```

## Declaring a parameter

For a built-in or plugin `FilterEffect`:

```csharp
public sealed partial class MyBlurEffect : FilterEffect
{
    [Display(...)]
    public IProperty<PixelExtent> Sigma { get; } =
        Property.CreateAnimatable<PixelExtent>();

    public override void ApplyTo(FilterEffectContext context, Resource r)
    {
        // ApplyTo signature is unchanged. The resolved value is computed
        // inside FilterEffectContext's wrapper-aware overloads.
        context.Blur(r.Sigma);
    }
}
```

The change from a previous `IProperty<Size>` declaration is mechanical:

```diff
- public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);
+ public IProperty<PixelExtent> Sigma { get; } = Property.CreateAnimatable(PixelExtent.Empty);
```

…and at the call site:

```diff
- context.Blur(r.Sigma);
+ context.Blur(r.Sigma);  // overload resolution picks the PixelExtent one
```

If the call uses the raw helper directly (e.g. `context.Blur((Size)r.Sigma.ToSize())`), the effect stays raw-pixel — the type system does not silently scale it. Opt-in is **explicit at the call site**.

## Migration discipline for plugin authors

1. Identify each parameter that is "a length in pixels".
2. Change its `IProperty<TPrimitive>` declaration to `IProperty<PixelLength | PixelExtent | PixelOffset>` as appropriate.
3. Pass the property's value to a `FilterEffectContext` wrapper-aware overload.
4. If your effect serializes its parameters to disk: confirm the on-disk shape is unchanged — `PixelLength` is a single `float`, `PixelExtent` is `{ width, height }`, `PixelOffset` is `{ x, y }`. Existing project files keep loading.
5. Add a test that renders your effect at a 1.0 scale and at a 0.5 scale, upscales the smaller render, and asserts SSIM ≥ 0.97. (See `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ResolutionEquivalenceTests.cs` for the pattern.)

## Scripting users

`CSharpScriptEffect` exposes the same `FilterEffectContext`. The wrapper-aware overloads are available as soon as the script targets the new `Beutl.Engine`. Existing scripts continue to compile and run unchanged because the raw overloads remain.

`GLSLScriptEffect` (and other shader-driven effects) need slightly different treatment: the script declares a `uniform` whose unit is reference-pixels, and the host passes `value * RenderScale.ScaleX` (and `ScaleY` for 2-D uniforms) before binding. The implementation surface for that is in `Beutl.Engine`'s shader binding code — not part of this contract document, but mentioned here so script authors know to consult the GLSL/SKSL guide once it lands.

## Backward compatibility guarantees

- All current `FilterEffectContext` raw-primitive overloads (`Blur(Size)`, `DropShadow(Point, Size, Color)`, `Erode(float, float)`, …) remain in the public surface with their existing semantics. They do **not** apply `RenderScale`.
- All current `IProperty<Size> | IProperty<Point> | IProperty<float>` declarations on third-party effects keep their current behavior.
- A plugin that does nothing is **bit-for-bit unchanged** by this feature.
- The opt-in is a type change made by the plugin author. There is no global flag or hidden switch.
