using System.ComponentModel.DataAnnotations;

using Beutl.Language;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

public sealed class Blur : ImageFilter
{
    public static readonly CoreProperty<Vector> SigmaProperty;
    private Vector _sigma;

    static Blur()
    {
        SigmaProperty = ConfigureProperty<Vector, Blur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Vector.Zero)
            .Register();

        AffectsRender<Blur>(SigmaProperty);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Vector), "0,0", "max,max")]
    public Vector Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        return rect.Inflate(new Thickness(Sigma.X, Sigma.Y));
    }

    protected internal override SKImageFilter ToSKImageFilter(Rect bounds)
    {
        return SKImageFilter.CreateBlur(Sigma.X, Sigma.Y);
    }
}
