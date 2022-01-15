using BEditorNext.Graphics;
using BEditorNext.Graphics.Filters;
using BEditorNext.Media;

namespace BEditorNext.Operations.Filters;

public sealed class DropShadowOperation : ImageFilterOperation<DropShadow>
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<Vector> SigmaProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<bool> ShadowOnlyProperty;

    static DropShadowOperation()
    {
        PositionProperty = ConfigureProperty<Point, DropShadowOperation>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(new Point(10, 10))
            .EnableEditor()
            .Animatable()
            .Header("PositionString")
            .JsonName("position")
            .Register();

        SigmaProperty = ConfigureProperty<Vector, DropShadowOperation>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(new Vector(10, 10))
            .EnableEditor()
            .Animatable()
            .Header("SigmaString")
            .JsonName("sigma")
            .Register();

        ColorProperty = ConfigureProperty<Color, DropShadowOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Black)
            .EnableEditor()
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .Register();

        ShadowOnlyProperty = ConfigureProperty<bool, DropShadowOperation>(nameof(ShadowOnly))
            .Accessor(o => o.ShadowOnly, (o, v) => o.ShadowOnly = v)
            .DefaultValue(false)
            .EnableEditor()
            .Header("ShadowOnlyString")
            .JsonName("shadowOnly")
            .Register();
    }

    public Point Position
    {
        get => Filter.Position;
        set => Filter.Position = value;
    }

    public Vector Sigma
    {
        get => Filter.Sigma;
        set => Filter.Sigma = value;
    }

    public Color Color
    {
        get => Filter.Color;
        set => Filter.Color = value;
    }

    public bool ShadowOnly
    {
        get => Filter.ShadowOnly;
        set => Filter.ShadowOnly = value;
    }

    public override DropShadow Filter { get; } = new();
}
