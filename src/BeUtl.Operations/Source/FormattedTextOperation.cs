using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Media.TextFormatting.Compat;
using BeUtl.ProjectSystem;

using FormattedTextInfo = BeUtl.Media.TextFormatting.FormattedTextInfo;

namespace BeUtl.Operations.Source;

[Obsolete("Use TextBlock API.")]
public sealed class FormattedTextOperation : DrawableOperation
{
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<FontStyle> StyleProperty;
    public static readonly CoreProperty<FontWeight> WeightProperty;
    public static readonly CoreProperty<float> SpaceProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<string> TextProperty;
    private readonly FormattedText _formattedText = new();
    private string _text = string.Empty;
    private Thickness _margin;
    private float _space;
    private FontWeight _weight = FormattedTextInfo.Default.Typeface.Weight;
    private FontStyle _style = FormattedTextInfo.Default.Typeface.Style;
    private FontFamily _fontFamily = FormattedTextInfo.Default.Typeface.FontFamily;
    private Color _color = Colors.White;
    private float _size = FormattedTextInfo.Default.Size;
    private bool _isDirty;

    static FormattedTextOperation()
    {
        SizeProperty = ConfigureProperty<float, FormattedTextOperation>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .OverrideMetadata(DefaultMetadatas.FontSize)
            .Register();

        ColorProperty = ConfigureProperty<Color, FormattedTextOperation>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .OverrideMetadata(DefaultMetadatas.Color)
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily, FormattedTextOperation>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .OverrideMetadata(DefaultMetadatas.FontFamily)
            .Register();

        StyleProperty = ConfigureProperty<FontStyle, FormattedTextOperation>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .OverrideMetadata(DefaultMetadatas.FontStyle)
            .Register();

        WeightProperty = ConfigureProperty<FontWeight, FormattedTextOperation>(nameof(Weight))
            .Accessor(o => o.Weight, (o, v) => o.Weight = v)
            .OverrideMetadata(DefaultMetadatas.FontWeight)
            .Register();

        SpaceProperty = ConfigureProperty<float, FormattedTextOperation>(nameof(Space))
            .Accessor(o => o.Space, (o, v) => o.Space = v)
            .OverrideMetadata(DefaultMetadatas.FontSpace)
            .Register();

        MarginProperty = ConfigureProperty<Thickness, FormattedTextOperation>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .OverrideMetadata(DefaultMetadatas.Margin)
            .Register();

        TextProperty = ConfigureProperty<string, FormattedTextOperation>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .OverrideMetadata(DefaultMetadatas.Text)
            .Register();
    }

    public float Size
    {
        get => _size;
        set => Set(ref _size, value);
    }

    public Color Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    public FontFamily FontFamily
    {
        get => _fontFamily;
        set => Set(ref _fontFamily, value);
    }

    public FontStyle Style
    {
        get => _style;
        set => Set(ref _style, value);
    }

    public FontWeight Weight
    {
        get => _weight;
        set => Set(ref _weight, value);
    }

    public float Space
    {
        get => _space;
        set => Set(ref _space, value);
    }

    public Thickness Margin
    {
        get => _margin;
        set => Set(ref _margin, value);
    }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public override Drawable Drawable => _formattedText;

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        base.RenderCore(ref args);
        try
        {
            if (_isDirty)
            {
                _formattedText.Load(Text, new FormattedTextInfo(new Typeface(FontFamily, Style, Weight), Size, Color, Space, Margin));
            }
            _isDirty = false;
        }
        catch
        {

        }
    }

    private void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _isDirty = true;
        }
    }
}
