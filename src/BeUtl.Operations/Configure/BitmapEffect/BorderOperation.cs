using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Graphics;
using BeUtl.Graphics.Effects;
using BeUtl.Media;

namespace BeUtl.Operations.Configure.BitmapEffect;

public class BorderOperation : BitmapEffectOperation<Border>
{
    public static readonly CoreProperty<Point> OffsetProperty;
    public static readonly CoreProperty<int> ThicknessProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<Border.MaskTypes> MaskTypeProperty;
    public static readonly CoreProperty<Border.BorderStyles> StyleProperty;

    static BorderOperation()
    {
        OffsetProperty = ConfigureProperty<Point, BorderOperation>(nameof(Offset))
            .Accessor(o => o.Offset, (o, v) => o.Offset = v)
            .OverrideMetadata(DefaultMetadatas.Offset)
            .DefaultValue(new Point(0, 0))
            .Register();

        ThicknessProperty = ConfigureProperty<int, BorderOperation>(nameof(Thickness))
            .Accessor(o => o.Thickness, (o, v) => o.Thickness = v)
            .OverrideMetadata(DefaultMetadatas.Thickness)
            .DefaultValue(8)
            .Register();

        ColorProperty = ConfigureProperty<Color, BorderOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .OverrideMetadata(DefaultMetadatas.Color)
            .Register();

        MaskTypeProperty = ConfigureProperty<Border.MaskTypes, BorderOperation>(nameof(MaskType))
            .Accessor(o => o.MaskType, (o, v) => o.MaskType = v)
            .OverrideMetadata(DefaultMetadatas.BorderMaskType)
            .Register();

        StyleProperty = ConfigureProperty<Border.BorderStyles, BorderOperation>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .OverrideMetadata(DefaultMetadatas.BorderStyle)
            .Register();
    }

    public Point Offset
    {
        get => Effect.Offset;
        set => Effect.Offset = value;
    }

    public int Thickness
    {
        get => Effect.Thickness;
        set => Effect.Thickness = value;
    }

    public Color Color
    {
        get => Effect.Color;
        set => Effect.Color = value;
    }

    public Border.MaskTypes MaskType
    {
        get => Effect.MaskType;
        set => Effect.MaskType = value;
    }

    public Border.BorderStyles Style
    {
        get => Effect.Style;
        set => Effect.Style = value;
    }

    public override Border Effect { get; } = new();
}
