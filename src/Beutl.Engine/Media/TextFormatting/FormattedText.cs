using System.Diagnostics;

using Beutl.Graphics;

using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beutl.Media.TextFormatting;

[DebuggerDisplay("{Text}")]
public struct FormattedText : IEquatable<FormattedText>
{
    private FontWeight _weight = FontWeight.Regular;
    private FontStyle _style = FontStyle.Normal;
    private FontFamily _font = FontFamily.Default;
    private float _size = 11;
    private float _spacing = 0;
    private StringSpan _text = StringSpan.Empty;
    private FontMetrics _metrics = default;
    private Size _bounds = default;
    private bool _isDirty = false;

    public FormattedText()
    {
    }

    public FontWeight Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, value);
    }

    public FontStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value);
    }

    public FontFamily Font
    {
        get => _font;
        set => SetProperty(ref _font, value);
    }

    // > 0
    public float Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    // >= 0
    public float Spacing
    {
        get => _spacing;
        set => SetProperty(ref _spacing, value);
    }

    // 改行コードは含まない
    public StringSpan Text
    {
        get => _text;
        set
        {
            ReadOnlySpan<char> span = value.AsSpan();
            if (span.Contains('\n') || span.Contains('\r'))
            {
                throw new Exception("Cannot contain newline codes.");
            }
            SetProperty(ref _text, value);
        }
    }

    public bool BeginOnNewLine { get; set; } = false;

    public IBrush? Brush { get; set; }

    public IPen? Pen { get; set; }

    public FontMetrics Metrics => MeasureAndSetField().Metrics;

    public Size Bounds => MeasureAndSetField().Bounds;

    internal Point AddToSKPath(SKPath path, Point point)
    {
        SKTypeface typeface = new Typeface(Font, Style, Weight).ToSkia();
        using SKPaint paint = new()
        {
            TextSize = Size,
            Typeface = typeface
        };

        using var shaper = new SKShaper(typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();

        SKShaper.Result result = shaper.Shape(buffer, paint);

        // create the text blob
        using var builder = new SKTextBlobBuilder();
        using SKFont font = paint.ToFont();
        SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, result.Codepoints.Length);

        // copy the glyphs
        Span<ushort> glyphs = run.GetGlyphSpan();
        Span<SKPoint> positions = run.GetPositionSpan();
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];
            SKPoint p = result.Points[i];
            p.X += i * Spacing;
            positions[i] = p;
        }

        // build
        using SKTextBlob textBlob = builder.Build();

        for (int i = 0; i < glyphs.Length; i++)
        {
            ushort glyph = glyphs[i];
            SKPoint p = positions[i] + point.ToSKPoint();

            using SKPath glyphPath = font.GetGlyphPath(glyph);
            path.AddPath(glyphPath, p.X, p.Y);
        }

        return point;
    }

    private (FontMetrics, Size) Measure()
    {
        SKTypeface typeface = new Typeface(Font, Style, Weight).ToSkia();
        using SKPaint paint = new()
        {
            TextSize = Size,
            Typeface = typeface
        };

        using var shaper = new SKShaper(typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();

        SKShaper.Result result = shaper.Shape(buffer, paint);

        FontMetrics fontMetrics = paint.FontMetrics.ToFontMetrics();
        float w = result.Width;
        var size = new Size(
            w + (buffer.Length - 1) * Spacing,
            fontMetrics.Descent - fontMetrics.Ascent);

        return (fontMetrics, size);
    }

    private void SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _isDirty = true;
        }
    }

    private (FontMetrics Metrics, Size Bounds) MeasureAndSetField()
    {
        if (_isDirty)
        {
            (_metrics, _bounds) = Measure();
            _isDirty = false;
        }

        return (_metrics, _bounds);
    }

    public override bool Equals(object? obj)
    {
        return obj is FormattedText text && Equals(text);
    }

    public bool Equals(FormattedText other)
    {
        return Weight == other.Weight && Style == other.Style && Font.Equals(other.Font) && Size == other.Size && Spacing == other.Spacing && Text.Equals(other.Text) && BeginOnNewLine == other.BeginOnNewLine && EqualityComparer<IBrush>.Default.Equals(Brush, other.Brush);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Weight);
        hash.Add(Style);
        hash.Add(Font);
        hash.Add(Size);
        hash.Add(Spacing);
        hash.Add(Text);
        hash.Add(BeginOnNewLine);
        hash.Add(Brush);
        return hash.ToHashCode();
    }

    public static bool operator ==(FormattedText left, FormattedText right) => left.Equals(right);

    public static bool operator !=(FormattedText left, FormattedText right) => !(left == right);
}
