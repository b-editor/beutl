using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Media;

namespace BeUtl.Operations.Configure.BitmapEffect;

public sealed class InnerShadowOperation : BitmapEffectOperation<InnerShadow>
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;
    public static readonly CoreProperty<Color> ColorProperty;

    static InnerShadowOperation()
    {
        PositionProperty = ConfigureProperty<Point, InnerShadowOperation>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .OverrideMetadata(DefaultMetadatas.Position)
            .DefaultValue(new Point(10, 10))
            .Register();

        KernelSizeProperty = ConfigureProperty<PixelSize, InnerShadowOperation>(nameof(KernelSize))
            .OverrideMetadata(DefaultMetadatas.KernelSize)
            .DefaultValue(new PixelSize(25, 25))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .Register();

        ColorProperty = ConfigureProperty<Color, InnerShadowOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .OverrideMetadata(DefaultMetadatas.Color)
            .DefaultValue(Colors.Black)
            .Register();
    }

    public Point Position
    {
        get => Effect.Position;
        set => Effect.Position = value;
    }

    public PixelSize KernelSize
    {
        get => Effect.KernelSize;
        set => Effect.KernelSize = value;
    }

    public Color Color
    {
        get => Effect.Color;
        set => Effect.Color = value;
    }

    public override InnerShadow Effect { get; } = new();
}
