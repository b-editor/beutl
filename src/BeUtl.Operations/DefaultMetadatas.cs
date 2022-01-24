
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
        Header = "XString",
        SerializeName = "x",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<float> Y => new()
    {
        IsAnimatable = true,
        Header = "YString",
        SerializeName = "y",
        PropertyFlags = PropertyFlags.Designable,
    };
    
    public static OperationPropertyMetadata<float> Scale => new()
    {
        IsAnimatable = true,
        //Todo: String resource
        //Header = "ScaleString",
        SerializeName = "scale",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };
    
    public static OperationPropertyMetadata<float> ScaleX => new()
    {
        IsAnimatable = true,
        //Todo: String resource
        //Header = "ScaleXString",
        SerializeName = "scaleX",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };

    public static OperationPropertyMetadata<float> ScaleY => new()
    {
        IsAnimatable = true,
        //Todo: String resource
        //Header = "ScaleYString",
        SerializeName = "scaleY",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };
    
    public static OperationPropertyMetadata<float> SkewX => new()
    {
        IsAnimatable = true,
        //Todo: String resource
        //Header = "SkewXString",
        SerializeName = "skewX",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<float> SkewY => new()
    {
        IsAnimatable = true,
        //Todo: String resource
        //Header = "SkewYString",
        SerializeName = "skewY",
        PropertyFlags = PropertyFlags.Designable,
    };
    
    public static OperationPropertyMetadata<float> Rotation => new()
    {
        IsAnimatable = true,
        //Todo: String resource
        //Header = "RotationString",
        SerializeName = "rotation",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<float> Width => new()
    {
        IsAnimatable = true,
        Header = "WidthString",
        Minimum = 0,
        SerializeName = "width",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };

    public static OperationPropertyMetadata<float> Height => new()
    {
        IsAnimatable = true,
        Header = "HeightString",
        Minimum = 0,
        SerializeName = "height",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100
    };

    public static OperationPropertyMetadata<float> StrokeWidth => new()
    {
        IsAnimatable = true,
        Header = "StrokeWidthString",
        Minimum = 0,
        SerializeName = "strokeWidth",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 4000
    };

    public static OperationPropertyMetadata<Color> Color => new()
    {
        IsAnimatable = true,
        Header = "ColorString",
        SerializeName = "color",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = Colors.White
    };

    public static OperationPropertyMetadata<CornerRadius> CornerRadius => new()
    {
        IsAnimatable = true,
        Header = "CornerRadiusString",
        SerializeName = "cornerRadius",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = new CornerRadius(25),
        Minimum = new CornerRadius(0)
    };

    public static OperationPropertyMetadata<float> FontSize => new()
    {
        IsAnimatable = true,
        Header = "SizeString",
        Minimum = 0,
        SerializeName = "size",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Size
    };

    public static OperationPropertyMetadata<FontFamily> FontFamily => new()
    {
        Header = "FontFamilyString",
        SerializeName = "font",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Typeface.FontFamily
    };

    public static OperationPropertyMetadata<FontStyle> FontStyle => new()
    {
        Header = "FontStyleString",
        SerializeName = "style",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Typeface.Style
    };

    public static OperationPropertyMetadata<FontWeight> FontWeight => new()
    {
        Header = "FontWeightString",
        SerializeName = "weight",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = FormattedTextInfo.Default.Typeface.Weight
    };

    public static OperationPropertyMetadata<float> FontSpace => new()
    {
        IsAnimatable = true,
        Header = "CharactorSpacingString",
        SerializeName = "space",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<Thickness> Margin => new()
    {
        IsAnimatable = true,
        Header = "MarginString",
        SerializeName = "margin",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<string> Text => new()
    {
        Header = "TextString",
        SerializeName = "text",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = string.Empty
    };

    public static OperationPropertyMetadata<float> Opacity => new()
    {
        IsAnimatable = true,
        Header = "OpacityString",
        SerializeName = "opacity",
        PropertyFlags = PropertyFlags.Designable,
        DefaultValue = 100,
        Maximum = 100,
        Minimum = 0
    };

    public static OperationPropertyMetadata<Point> Position => new()
    {
        IsAnimatable = true,
        Header = "PositionString",
        SerializeName = "position",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<Vector> Sigma => new()
    {
        IsAnimatable = true,
        Header = "SigmaString",
        SerializeName = "sigma",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<bool> ShadowOnly => new()
    {
        IsAnimatable = true,
        Header = "ShadowOnlyString",
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
        Header = "CanvasAlignmentXString",
        SerializeName = "canvasAlignX",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<AlignmentY> CanvasAlignmentY => new()
    {
        Header = "CanvasAlignmentYString",
        SerializeName = "canvasAlignY",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<AlignmentX> AlignmentX => new()
    {
        Header = "AlignmentXString",
        SerializeName = "alignX",
        PropertyFlags = PropertyFlags.Designable,
    };

    public static OperationPropertyMetadata<AlignmentY> AlignmentY => new()
    {
        Header = "AlignmentYString",
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
