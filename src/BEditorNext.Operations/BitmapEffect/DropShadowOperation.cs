using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class DropShadowOperation : BitmapEffectOperation<DropShadow>
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<Point> SigmaProperty;
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

        SigmaProperty = ConfigureProperty<Point, DropShadowOperation>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(new Point(10, 10))
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
        get => new(Effect.X, Effect.Y);
        set
        {
            Effect.X = value.X;
            Effect.Y = value.Y;
        }
    }

    public Point Sigma
    {
        get => new(Effect.SigmaX, Effect.SigmaY);
        set
        {
            Effect.SigmaX = value.X;
            Effect.SigmaY = value.Y;
        }
    }

    public Color Color
    {
        get => Effect.Color;
        set => Effect.Color = value;
    }

    public bool ShadowOnly
    {
        get => Effect.ShadowOnly;
        set => Effect.ShadowOnly = value;
    }

    public override DropShadow Effect { get; } = new();
}
