using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.Media.TextFormatting;
using BEditorNext.ProjectSystem;

using SkiaSharp;

namespace BEditorNext.Operations;

public sealed class FormattedTextOperation : RenderOperation
{
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<FontStyle> StyleProperty;
    public static readonly CoreProperty<FontWeight> WeightProperty;
    public static readonly CoreProperty<float> SpaceProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<string> TextProperty;
    private FormattedText? _formattedText;

    static FormattedTextOperation()
    {
        SizeProperty = ConfigureProperty<float, FormattedTextOperation>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .EnableEditor()
            .Minimum(0)
            .DefaultValue(FormattedTextInfo.Default.Size)
            .Animatable()
            .Header("SizeString")
            .JsonName("size")
            .Register();

        ColorProperty = ConfigureProperty<Color, FormattedTextOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Color)
            .Animatable()
            .Header("ColorString")
            .JsonName("color")
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily, FormattedTextOperation>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Typeface.FontFamily)
            .Header("FontFamilyString")
            .JsonName("font")
            .Register();

        StyleProperty = ConfigureProperty<FontStyle, FormattedTextOperation>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Typeface.Style)
            .Header("FontStyleString")
            .JsonName("style")
            .Register();

        WeightProperty = ConfigureProperty<FontWeight, FormattedTextOperation>(nameof(Weight))
            .Accessor(o => o.Weight, (o, v) => o.Weight = v)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Typeface.Weight)
            .Header("FontWeightString")
            .JsonName("weight")
            .Register();

        SpaceProperty = ConfigureProperty<float, FormattedTextOperation>(nameof(Space))
            .Accessor(o => o.Space, (o, v) => o.Space = v)
            .EnableEditor()
            .DefaultValue(0)
            .Animatable()
            .Header("CharactorSpacingString")
            .JsonName("space")
            .Register();

        MarginProperty = ConfigureProperty<Thickness, FormattedTextOperation>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .EnableEditor()
            .DefaultValue(new Thickness())
            .Animatable()
            .Header("MarginString")
            .JsonName("margin")
            .Register();

        TextProperty = ConfigureProperty<string, FormattedTextOperation>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .EnableEditor()
            .DefaultValue(string.Empty)
            .Header("TextString")
            .JsonName("text")
            .Register();
    }

    public float Size { get; set; } = FormattedTextInfo.Default.Size;

    public Color Color { get; set; } = FormattedTextInfo.Default.Color;

    public FontFamily FontFamily { get; set; } = FormattedTextInfo.Default.Typeface.FontFamily;

    public FontStyle Style { get; set; } = FormattedTextInfo.Default.Typeface.Style;

    public FontWeight Weight { get; set; } = FormattedTextInfo.Default.Typeface.Weight;

    public float Space { get; set; }

    public Thickness Margin { get; set; }

    public string Text { get; set; } = string.Empty;

    public override void Render(in OperationRenderArgs args)
    {
        try
        {
            _formattedText = FormattedText.Parse(Text, new FormattedTextInfo(new Typeface(FontFamily, Style, Weight), Size, Color, Space, Margin));
            args.List.Add(_formattedText);
        }
        catch
        {
        }
    }
}
