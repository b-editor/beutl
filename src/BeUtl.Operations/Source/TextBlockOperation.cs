using BeUtl.Graphics;
using BeUtl.Graphics.Shapes;
using BeUtl.Media;

namespace BeUtl.Operations.Source;

public sealed class TextBlockOperation : DrawableOperation
{
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<FontStyle> StyleProperty;
    public static readonly CoreProperty<FontWeight> WeightProperty;
    public static readonly CoreProperty<float> SpaceProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<string> TextProperty;
    private TextBlock _textBlock = new();

    static TextBlockOperation()
    {
        SizeProperty = ConfigureProperty<float, TextBlockOperation>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .OverrideMetadata(DefaultMetadatas.FontSize)
            .Register();

        ColorProperty = ConfigureProperty<Color, TextBlockOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .OverrideMetadata(DefaultMetadatas.Color)
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily, TextBlockOperation>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .OverrideMetadata(DefaultMetadatas.FontFamily)
            .Register();

        StyleProperty = ConfigureProperty<FontStyle, TextBlockOperation>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .OverrideMetadata(DefaultMetadatas.FontStyle)
            .Register();

        WeightProperty = ConfigureProperty<FontWeight, TextBlockOperation>(nameof(Weight))
            .Accessor(o => o.Weight, (o, v) => o.Weight = v)
            .OverrideMetadata(DefaultMetadatas.FontWeight)
            .Register();

        SpaceProperty = ConfigureProperty<float, TextBlockOperation>(nameof(Space))
            .Accessor(o => o.Space, (o, v) => o.Space = v)
            .OverrideMetadata(DefaultMetadatas.FontSpace)
            .Register();

        MarginProperty = ConfigureProperty<Thickness, TextBlockOperation>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .OverrideMetadata(DefaultMetadatas.Margin)
            .Register();

        TextProperty = ConfigureProperty<string, TextBlockOperation>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .OverrideMetadata(DefaultMetadatas.Text)
            .Register();
    }

    public float Size
    {
        get => _textBlock.Size;
        set => _textBlock.Size = value;
    }

    public Color Color
    {
        get => _textBlock.Foreground?.TryGetColorOrDefault(Colors.White) ?? Colors.White;
        set
        {
            if (_textBlock.Foreground?.TrySetColor(value) != true)
            {
                _textBlock.Foreground = value.ToImmutableBrush();
            }
        }
    }

    public FontFamily FontFamily
    {
        get => _textBlock.FontFamily;
        set => _textBlock.FontFamily = value;
    }

    public FontStyle Style
    {
        get => _textBlock.FontStyle;
        set => _textBlock.FontStyle = value;
    }

    public FontWeight Weight
    {
        get => _textBlock.FontWeight;
        set => _textBlock.FontWeight = value;
    }

    public float Space
    {
        get => _textBlock.Spacing;
        set => _textBlock.Spacing = value;
    }

    public Thickness Margin
    {
        get => _textBlock.Margin;
        set => _textBlock.Margin = value;
    }

    public string Text
    {
        get => _textBlock.Text;
        set => _textBlock.Text = value;
    }

    public override Drawable Drawable => _textBlock;
}
