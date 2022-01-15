using BeUtl.Graphics;

using SkiaSharp;

namespace BeUtl.Media.TextFormatting;

public class TextElement : IDisposable
{
    private readonly SKPaint _paint = new();
    private Typeface _typeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Regular);
    private float _size;
    private bool _isDirty = true;
    private FontMetrics _fontMetrics;

    ~TextElement()
    {
        Dispose();
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
            if (_typeface != value)
            {
                _typeface = value;
                _isDirty = true;
            }
        }
    }

    public float Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                _isDirty = true;
            }
        }
    }

    public IBrush Foreground { get; set; } = Brushes.White;

    public float Spacing { get; set; }

    public string Text { get; set; } = string.Empty;

    public Thickness Margin { get; set; }

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

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _paint.Dispose();
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // Marginを考慮しない
    public Size Measure()
    {
        _ = FontMetrics;
        float w = _paint.MeasureText(Text);

        return new Size(
            w + (Text.Length - 1) * Spacing,
            FontMetrics.Descent - FontMetrics.Ascent);
    }
}
