using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.Media.TextFormatting;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public sealed class FormattedTextOperation : RenderOperation
{
    public static readonly PropertyDefine<float> SizeProperty;
    public static readonly PropertyDefine<Color> ColorProperty;
    public static readonly PropertyDefine<FontFamily> FontFamilyProperty;
    public static readonly PropertyDefine<FontStyle> StyleProperty;
    public static readonly PropertyDefine<FontWeight> WeightProperty;
    public static readonly PropertyDefine<float> SpaceProperty;
    public static readonly PropertyDefine<Thickness> MarginProperty;
    public static readonly PropertyDefine<string> TextProperty;
    private FormattedText? _formattedText;

    static FormattedTextOperation()
    {
        SizeProperty = RegisterProperty<float, FormattedTextOperation>(nameof(Size), (owner, obj) => owner.Size = obj, owner => owner.Size)
            .EnableEditor()
            .Minimum(0)
            .DefaultValue(FormattedTextInfo.Default.Size)
            .Animatable()
            .JsonName("size");

        ColorProperty = RegisterProperty<Color, FormattedTextOperation>(nameof(Color), (owner, obj) => owner.Color = obj, owner => owner.Color)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Color)
            .Animatable()
            .JsonName("color");

        FontFamilyProperty = RegisterProperty<FontFamily, FormattedTextOperation>(nameof(FontFamily), (owner, obj) => owner.FontFamily = obj, owner => owner.FontFamily)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Typeface.FontFamily)
            .JsonName("font");
        
        StyleProperty = RegisterProperty<FontStyle, FormattedTextOperation>(nameof(Style), (owner, obj) => owner.Style = obj, owner => owner.Style)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Typeface.Style)
            .JsonName("style");
        
        WeightProperty = RegisterProperty<FontWeight, FormattedTextOperation>(nameof(Weight), (owner, obj) => owner.Weight = obj, owner => owner.Weight)
            .EnableEditor()
            .DefaultValue(FormattedTextInfo.Default.Typeface.Weight)
            .JsonName("weight");

        SpaceProperty = RegisterProperty<float, FormattedTextOperation>(nameof(Space), (owner, obj) => owner.Space = obj, owner => owner.Space)
            .EnableEditor()
            .DefaultValue(0)
            .Animatable()
            .JsonName("space");

        MarginProperty = RegisterProperty<Thickness, FormattedTextOperation>(nameof(Margin), (owner, obj) => owner.Margin = obj, owner => owner.Margin)
            .EnableEditor()
            .DefaultValue(new Thickness())
            .Animatable()
            .JsonName("margin");

        TextProperty = RegisterProperty<string, FormattedTextOperation>(nameof(Text), (owner, obj) => owner.Text = obj, owner => owner.Text)
            .EnableEditor()
            .DefaultValue(string.Empty)
            .JsonName("text");
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
        _formattedText = FormattedText.Parse(Text, new FormattedTextInfo(new Typeface(FontFamily, Style, Weight), Size, Color, Space, Margin));
        args.List.Add(_formattedText);
    }
}
