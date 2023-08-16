using System.ComponentModel.DataAnnotations;

using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed class HueRotate : FilterEffect
{
    public static readonly CoreProperty<float> AngleProperty;
    private float _angle;

    static HueRotate()
    {
        AngleProperty = ConfigureProperty<float, HueRotate>(nameof(Angle))
            .Accessor(o => o.Angle, (o, v) => o.Angle = v)
            .Register();

        AffectsRender<HueRotate>(AngleProperty);
    }

    [Display(Name = nameof(Strings.Angle), ResourceType = typeof(Strings))]
    public float Angle
    {
        get => _angle;
        set => SetAndRaise(AngleProperty, ref _angle, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.HueRotate(Angle);
    }
}
