namespace Beutl.Graphics.Effects;

public sealed class Dilate : FilterEffect
{
    public static readonly CoreProperty<float> RadiusXProperty;
    public static readonly CoreProperty<float> RadiusYProperty;
    private float _radiusX;
    private float _radiusY;

    static Dilate()
    {
        RadiusXProperty = ConfigureProperty<float, Dilate>(nameof(RadiusX))
            .Accessor(o => o.RadiusX, (o, v) => o.RadiusX = v)
            .Register();

        RadiusYProperty = ConfigureProperty<float, Dilate>(nameof(RadiusY))
            .Accessor(o => o.RadiusY, (o, v) => o.RadiusY = v)
            .Register();

        AffectsRender<Dilate>(RadiusXProperty, RadiusYProperty);
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
        context.Dilate(RadiusX, RadiusY);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return bounds.Inflate(new Thickness(RadiusX, RadiusY));
    }
}
