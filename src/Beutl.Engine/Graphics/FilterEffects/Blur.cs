using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

using Beutl.Language;
using Beutl.Serialization;
using Beutl.Serialization.Migration;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class Blur : FilterEffect
{
    public static readonly CoreProperty<Size> SigmaProperty;
    private Size _sigma;

    static Blur()
    {
        SigmaProperty = ConfigureProperty<Size, Blur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Size.Empty)
            .Register();

        AffectsRender<Blur>(SigmaProperty);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public Size Sigma
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
        return bounds.Inflate(new Thickness(_sigma.Width * 3, _sigma.Height * 3));
    }
}
