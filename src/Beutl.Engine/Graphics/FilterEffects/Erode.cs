namespace Beutl.Graphics.Effects;

public sealed class Erode : FilterEffect
{
    public static readonly CoreProperty<float> RadiusXProperty;
    public static readonly CoreProperty<float> RadiusYProperty;
    private float _radiusX;
    private float _radiusY;

    static Erode()
    {
        RadiusXProperty = ConfigureProperty<float, Erode>(nameof(RadiusX))
            .Accessor(o => o.RadiusX, (o, v) => o.RadiusX = v)
            .Register();

        RadiusYProperty = ConfigureProperty<float, Erode>(nameof(RadiusY))
            .Accessor(o => o.RadiusY, (o, v) => o.RadiusY = v)
            .Register();

        AffectsRender<Erode>(RadiusXProperty, RadiusYProperty);
    }

    public float RadiusX
    {
        get => _radiusX;
        set => SetAndRaise(RadiusXProperty, ref _radiusX, value);
    }

    public float RadiusY
    {
        get => _radiusY;
        set => SetAndRaise(RadiusYProperty, ref _radiusY, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Erode(RadiusX, RadiusY);
    }
}
