using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics.Shapes;

public sealed class GeometryShape : Shape
{
    public static readonly CoreProperty<Geometry?> DataProperty;
    private Geometry? _data;

    static GeometryShape()
    {
        DataProperty = ConfigureProperty<Geometry?, GeometryShape>(nameof(Data))
            .Accessor(o => o.Data, (o, v) => o.Data = v)
            .Register();

        AffectsGeometry<GeometryShape>(DataProperty);
    }

    public Geometry? Data
    {
        get => _data;
        set => SetAndRaise(DataProperty, ref _data, value);
    }

    protected override Geometry? CreateGeometry()
    {
        return _data;
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Data as IAnimatable)?.ApplyAnimations(clock);
    }
}
