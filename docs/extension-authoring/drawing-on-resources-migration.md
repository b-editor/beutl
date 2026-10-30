# Drawing on resources migration

Drawables and filter effects now draw and apply from their evaluated `Resource`, the way `Geometry.Resource`
already builds a path. The object classes keep only properties; the methods that used to receive a resource
argument moved onto the resource and read its values directly. There are no compatibility shims, so
extensions must update their sources and rebuild.

| Before | After |
| --- | --- |
| `override void FilterEffect.ApplyTo(FilterEffectContext context, Resource resource)` | `protected override void ApplyTo(FilterEffectContext context)` in the effect's `Resource` |
| `override Size Drawable.MeasureCore(Size availableSize, Resource resource)` | `protected override Size MeasureCore(Size availableSize)` in the drawable's `Resource` |
| `override void Drawable.OnDraw(GraphicsContext2D context, Resource resource)` | `protected override void OnDraw(GraphicsContext2D context)` in the drawable's `Resource` |
| `override void Drawable.Render(GraphicsContext2D context, Resource resource)` | `protected override void RenderCore(GraphicsContext2D context)` in the drawable's `Resource` |
| `drawable.Render(context, resource)` | `resource.Render(context)` |
| `effect.ApplyTo(context, resource)` | `context.Apply(resource)` |

Move the body into the generated nested `Resource` (declare `public new partial class Resource` in the same
file) and replace `r.Property` with `Property`:

```csharp
public sealed partial class MyBlur : FilterEffect
{
    public IExpressionProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Blur(Sigma);
        }
    }
}
```

Inside `Resource`, a property name such as `Sigma` is the evaluated value at the rendered time, not the
object's `IExpressionProperty`. `RequireOriginal()` still returns the object when a method needs state that
is not a property.

`Resource.ApplyTo` is protected: applying another effect, such as a composite's child, goes through
`FilterEffectContext.Apply(resource)`, which keeps continuation boundaries and the enabled state. Likewise,
`Drawable.Resource.Render` is the only way to draw a drawable; it skips a disabled resource and lets an effect
replace the time-sampled input before falling back to `RenderCore`. `RenderCore` does not need to check
`IsEnabled` itself.
