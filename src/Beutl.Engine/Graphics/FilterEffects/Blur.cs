using System.ComponentModel.DataAnnotations;

using Beutl.Language;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class Blur : FilterEffect
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

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Blur(_sigma);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return bounds.Inflate(new Thickness(_sigma.X * 3, _sigma.Y * 3));
    }
}
