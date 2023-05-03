using Beutl.Animation;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

public sealed class ComposedImageFilter : ImageFilter
{
    public static readonly CoreProperty<IImageFilter?> OuterProperty;
    public static readonly CoreProperty<IImageFilter?> InnerProperty;
    private IImageFilter? _outer;
    private IImageFilter? _inner;

    static ComposedImageFilter()
    {
        OuterProperty = ConfigureProperty<IImageFilter?, ComposedImageFilter>(nameof(Outer))
            .Accessor(o => o.Outer, (o, v) => o.Outer = v)
            .Register();

        InnerProperty = ConfigureProperty<IImageFilter?, ComposedImageFilter>(nameof(Inner))
            .Accessor(o => o.Inner, (o, v) => o.Inner = v)
            .Register();

        AffectsRender<ComposedImageFilter>(OuterProperty, InnerProperty);
    }

    public ComposedImageFilter()
    {
    }

    public ComposedImageFilter(IImageFilter? outer, IImageFilter? inner)
    {
        Outer = outer;
        Inner = inner;
    }

    public IImageFilter? Outer
    {
        get => _outer;
        set => SetAndRaise(OuterProperty, ref _outer, value);
    }

    public IImageFilter? Inner
    {
        get => _inner;
        set => SetAndRaise(InnerProperty, ref _inner, value);
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        switch ((Outer, Inner))
        {
            case (null or { IsEnabled: false }, { IsEnabled: true }):
                return Inner.ToSKImageFilter();

            case ({ IsEnabled: true }, null or { IsEnabled: false }):
                return Outer.ToSKImageFilter();

            case ({ IsEnabled: true }, { IsEnabled: true }):
                return SKImageFilter.CreateCompose(Outer.ToSKImageFilter(), Inner.ToSKImageFilter());

            default:
                return SKImageFilter.CreateOffset(0, 0);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Outer as IAnimatable)?.ApplyAnimations(clock);
        (Inner as IAnimatable)?.ApplyAnimations(clock);
    }
}
