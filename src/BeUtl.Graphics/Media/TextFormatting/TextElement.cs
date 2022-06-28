using BeUtl.Graphics;

using SkiaSharp;

namespace BeUtl.Media.TextFormatting;

public class TextElement : Drawable
{
    public static readonly CoreProperty<Typeface> TypefaceProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string> TextProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    internal readonly SKPaint _paint = new();
    private Typeface _typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Regular);
    private float _size;
    private bool _isDirty = true;
    private FontMetrics _fontMetrics;
    private float _spacing;
    private string _text = string.Empty;
    private Thickness _margin;

    static TextElement()
    {
        TypefaceProperty = ConfigureProperty<Typeface, TextElement>(nameof(Typeface))
            .Accessor(o => o.Typeface, (o, v) => o.Typeface = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(new(FontFamily.Default, FontStyle.Normal, FontWeight.Regular))
            .Register();

        SizeProperty = ConfigureProperty<float, TextElement>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(0)
            .Register();

        SpacingProperty = ConfigureProperty<float, TextElement>(nameof(Spacing))
            .Accessor(o => o.Spacing, (o, v) => o.Spacing = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(0)
            .Register();

        TextProperty = ConfigureProperty<string, TextElement>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(string.Empty)
            .Register();

        MarginProperty = ConfigureProperty<Thickness, TextElement>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(new Thickness())
            .Register();

        AffectsRender<TextElement>(TypefaceProperty, SizeProperty, SpacingProperty, TextProperty, MarginProperty);
    }

    ~TextElement()
    {
        _paint.Dispose();
    }

    public FontWeight Weight
    {
        get => _typeface.Weight;
        set => Typeface = new Typeface(_typeface.FontFamily, _typeface.Style, value);
    }

    public FontStyle Style
    {
        get => _typeface.Style;
        set => Typeface = new Typeface(_typeface.FontFamily, value, _typeface.Weight);
    }

    public FontFamily Font
    {
        get => _typeface.FontFamily;
        set => Typeface = new Typeface(value, _typeface.Style, _typeface.Weight);
    }

    public Typeface Typeface
    {
        get => _typeface;
        set
        {
            if (SetAndRaise(TypefaceProperty, ref _typeface, value))
            {
                _isDirty = true;
            }
        }
    }

    public float Size
    {
        get => _size;
        set
        {
            if (SetAndRaise(SizeProperty, ref _size, value))
            {
                _isDirty = true;
            }
        }
    }

    public float Spacing
    {
        get => _spacing;
        set => SetAndRaise(SpacingProperty, ref _spacing, value);
    }

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    public Thickness Margin
    {
        get => _margin;
        set => SetAndRaise(MarginProperty, ref _margin, value);
    }

    public FontMetrics FontMetrics
    {
        get
        {
            if (_isDirty)
            {
                _paint.TextSize = Size;
                _paint.Typeface = Typeface.ToSkia();
                _fontMetrics = _paint.FontMetrics.ToFontMetrics();
                _isDirty = false;
            }

            return _fontMetrics;
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        _ = FontMetrics;
        float w = _paint.MeasureText(Text);

        return new Size(
            w + (Text.Length - 1) * Spacing,
            FontMetrics.Descent - FontMetrics.Ascent);
    }

    protected override void OnDraw(ICanvas canvas)
    {
        DrawInternal(canvas);
    }

    internal void DrawInternal(ICanvas canvas)
    {
        using (canvas.PushTransform(Matrix.CreateTranslation(Margin.Left, Margin.Top)))
        {
            canvas.DrawText(this, MeasureCore(Graphics.Size.Infinity));
        }
    }
}

