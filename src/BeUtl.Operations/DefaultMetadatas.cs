using System.Collections.Immutable;

using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Media.TextFormatting;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations;

public static class DefaultMetadatas
{
    public static OperationPropertyMetadata<float> X => new()
    {
        IsAnimatable = true,
        Header = "S.Common.X",
        SerializeName = "x",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<float> Y => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Y",
        SerializeName = "y",
        PropertyFlags = PropertyFlags.Designable,
    };
    
    public static OperationPropertyMetadata<float> Scale => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Scale",
        SerializeName = "scale",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };
    
    public static OperationPropertyMetadata<float> ScaleX => new()
    {
        IsAnimatable = true,
        Header = "S.Common.ScaleX",
        SerializeName = "scaleX",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };

    public static OperationPropertyMetadata<float> ScaleY => new()
    {
        IsAnimatable = true,
        Header = "S.Common.ScaleY",
        SerializeName = "scaleY",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };
    
    public static OperationPropertyMetadata<float> SkewX => new()
    {
        IsAnimatable = true,
        Header = "S.Common.SkewX",
        SerializeName = "skewX",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<float> SkewY => new()
    {
        IsAnimatable = true,
        Header = "S.Common.SkewY",
        SerializeName = "skewY",
        PropertyFlags = PropertyFlags.Designable,
    };
    
    public static OperationPropertyMetadata<float> Rotation => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Rotation",
        SerializeName = "rotation",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<float> Width => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Width",
        Minimum = 0,
        SerializeName = "width",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };

    public static OperationPropertyMetadata<float> Height => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Height",
        Minimum = 0,
        SerializeName = "height",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };

    public static OperationPropertyMetadata<float> StrokeWidth => new()
    {
        IsAnimatable = true,
        Header = "S.Common.StrokeWidth",
        Minimum = 0,
        SerializeName = "strokeWidth",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 4000
    };

    public static OperationPropertyMetadata<Color> Color => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Color",
        SerializeName = "color",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = Colors.White
    };

    public static OperationPropertyMetadata<CornerRadius> CornerRadius => new()
    {
        IsAnimatable = true,
        Header = "S.Common.CornerRadius",
        SerializeName = "cornerRadius",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = new CornerRadius(25),
        Minimum = new CornerRadius(0)
    };

    public static OperationPropertyMetadata<float> FontSize => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Size",
        Minimum = 0,
        SerializeName = "size",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Size
    };

    public static OperationPropertyMetadata<FontFamily> FontFamily => new()
    {
        Header = "S.Common.FontFamily",
        SerializeName = "font",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Typeface.FontFamily
    };

    public static OperationPropertyMetadata<FontStyle> FontStyle => new()
    {
        Header = "S.Common.FontStyle",
        SerializeName = "style",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Typeface.Style
    };

    public static OperationPropertyMetadata<FontWeight> FontWeight => new()
    {
        Header = "S.Common.FontWeight",
        SerializeName = "weight",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Typeface.Weight
    };

    public static OperationPropertyMetadata<float> FontSpace => new()
    {
        IsAnimatable = true,
        Header = "S.Common.CharactorSpacing",
        SerializeName = "space",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<Thickness> Margin => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Margin",
        SerializeName = "margin",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<string> Text => new()
    {
        Header = "S.Common.Text",
        SerializeName = "text",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = string.Empty
    };

    public static OperationPropertyMetadata<float> Opacity => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Opacity",
        SerializeName = "opacity",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100,
        Maximum = 100,
        Minimum = 0
    };

    public static OperationPropertyMetadata<Point> Position => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Position",
        SerializeName = "position",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<PixelSize> KernelSize => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Sigma",
        SerializeName = "kernel",
        PropertyFlags = PropertyFlags.Designable,
        Minimum = new PixelSize(1, 1)
    };
    
    public static OperationPropertyMetadata<Vector> Sigma => new()
    {
        IsAnimatable = true,
        Header = "S.Common.Sigma",
        SerializeName = "sigma",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<bool> ShadowOnly => new()
    {
        IsAnimatable = true,
        Header = "S.Common.ShadowOnly",
        SerializeName = "shadowOnly",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<BlendMode> BlendMode => new()
    {
        Header = "BlendString",
        SerializeName = "blend",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = Graphics.BlendMode.SrcOver
    };

    public static OperationPropertyMetadata<AlignmentX> CanvasAlignmentX => new()
    {
        Header = "S.Common.CanvasAlignmentX",
        SerializeName = "canvasAlignX",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<AlignmentY> CanvasAlignmentY => new()
    {
        Header = "S.Common.CanvasAlignmentY",
        SerializeName = "canvasAlignY",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<AlignmentX> AlignmentX => new()
    {
        Header = "S.Common.AlignmentX",
        SerializeName = "alignX",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<AlignmentY> AlignmentY => new()
    {
        Header = "S.Common.AlignmentY",
        SerializeName = "alignY",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static FilePropertyMetadata ImageFile => new()
    {
        PropertyFlags = PropertyFlags.Designable,
        SerializeName = "file",
        Extensions = ImmutableArray.Create("bmp", "gif", "ico", "jpg", "jpeg", "png", "wbmp", "webp", "pkm", "ktx", "astc", "dng", "heif"),
        Header = "ImageFileString"
    };
}
