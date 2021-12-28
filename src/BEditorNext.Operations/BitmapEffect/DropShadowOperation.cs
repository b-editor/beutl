using BEditorNext.Graphics;
using BEditorNext.Graphics.Effects;
using BEditorNext.Media;

namespace BEditorNext.Operations.BitmapEffect;

public sealed class DropShadowOperation : BitmapEffectOperation<DropShadow>
{
    public static readonly PropertyDefine<Point> PositionProperty;
    public static readonly PropertyDefine<Point> SigmaProperty;
    public static readonly PropertyDefine<Color> ColorProperty;
    public static readonly PropertyDefine<bool> ShadowOnlyProperty;

    static DropShadowOperation()
    {
        PositionProperty = RegisterProperty<Point, DropShadowOperation>(nameof(Position), (owner, obj) => owner.Position = obj, owner => owner.Position)
            .DefaultValue(new Point(10, 10))
            .EnableEditor()
            .Animatable()
            .Header("PositionString")
            .JsonName("position");

        SigmaProperty = RegisterProperty<Point, DropShadowOperation>(nameof(Sigma), (owner, obj) => owner.Sigma = obj, owner => owner.Sigma)
            .DefaultValue(new Point(10, 10))
            .EnableEditor()
            .Animatable()
            .Header("SigmaString")
            .JsonName("sigma");

        ColorProperty = RegisterProperty<Color, DropShadowOperation>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .DefaultValue(Colors.Black)
            .EnableEditor()
            .Animatable()
            .Header("ColorString")
            .JsonName("color");

        ShadowOnlyProperty = RegisterProperty<bool, DropShadowOperation>(nameof(ShadowOnly), (owner, obj) => owner.ShadowOnly = obj, owner => owner.ShadowOnly)
            .DefaultValue(false)
            .EnableEditor()
            .Header("ShadowOnlyString")
            .JsonName("shadowOnly");
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
