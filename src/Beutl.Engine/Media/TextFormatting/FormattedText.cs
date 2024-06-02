using System.Diagnostics;
using System.Runtime.InteropServices;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Immutable;
using Beutl.Reactive;

using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beutl.Media.TextFormatting;

[DebuggerDisplay("{Text}")]
public class FormattedText : IEquatable<FormattedText>
{
    private FontWeight _weight = FontWeight.Regular;
    private FontStyle _style = FontStyle.Normal;
    private FontFamily _font = FontFamily.Default;
    private float _size = 11;
    private float _spacing = 0;
    private StringSpan _text = StringSpan.Empty;
    private FontMetrics _metrics = default;
    private Rect _bounds = default;
    private Rect _actualBounds;
    private bool _isDirty = false;
    private IPen? _pen;
    private SKTextBlob? _textBlob;
    private SKPath? _fillPath;
    private SKPath? _strokePath;
    private List<SKPathGeometry> _pathList = [];

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

    public IPen? Pen
    {
        get => _pen;
        set => SetProperty(ref _pen, value);
    }

    public FontMetrics Metrics
    {
        get
        {
            MeasureAndSetField();
            return _metrics;
        }
    }

    public Rect Bounds
    {
        get
        {
            MeasureAndSetField();
            return _bounds;
        }
    }

    // Strokeを含めた境界線
    public Rect ActualBounds
    {
        get
        {
            MeasureAndSetField();
            return _actualBounds;
        }
    }

    // テスト用
    internal Point AddToSKPath(SKPath path, Point point)
    {
        using SKFont font = this.ToSKFont();

        using var shaper = new SKShaper(font.Typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();

        using SKPaint paint = new()
        {
            TextSize = Size,
            Typeface = font.Typeface
        };
        SKShaper.Result result = shaper.Shape(buffer, paint);

        // create the text blob
        using var builder = new SKTextBlobBuilder();
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

    internal SKPath GetFillPath()
    {
        MeasureAndSetField();
        return _fillPath!;
    }

    internal SKPath? GetStrokePath()
    {
        MeasureAndSetField();
        return _strokePath;
    }

    internal SKTextBlob GetTextBlob()
    {
        MeasureAndSetField();
        return _textBlob!;
    }

    internal SKFont ToSKFont()
    {
        var typeface = new Typeface(Font, Style, Weight);
        var font = new SKFont(typeface.ToSkia(), Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.Full
        };

        return font;
    }

    internal IReadOnlyList<Geometry> ToGeometies()
    {
        MeasureAndSetField();
        return _pathList;
    }

    private void Measure()
    {
        using SKFont font = ToSKFont();

        using var shaper = new SKShaper(font.Typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();

        using SKPaint paint = new()
        {
            TextSize = Size,
            Typeface = font.Typeface,
        };
        SKShaper.Result result = shaper.Shape(buffer, paint);

        // create the text blob
        using var builder = new SKTextBlobBuilder();
        SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, result.Codepoints.Length);

        var fillPath = new SKPath();
        Span<ushort> glyphs = run.GetGlyphSpan();
        Span<SKPoint> positions = run.GetPositionSpan();
        CollectionsMarshal.SetCount(_pathList, result.Codepoints.Length);
        Span<SKPathGeometry> pathList = CollectionsMarshal.AsSpan(_pathList);
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];

            SKPoint point = result.Points[i];
            point.X += i * Spacing;
            positions[i] = point;

            SKPath? tmp = font.GetGlyphPath(glyphs[i]);
            if (tmp != null)
            {
                fillPath.AddPath(tmp, point.X, point.Y);

                tmp.Transform(SKMatrix.CreateTranslation(point.X, point.Y));

                ref SKPathGeometry? exist = ref pathList[i]!;
                exist ??= new SKPathGeometry();
                exist.SetSKPath(tmp, false);
            }
            else
            {
                ref SKPathGeometry? exist = ref pathList[i]!;
                exist ??= new SKPathGeometry();
            }
        }

        SKPath? strokePath = null;
        // 空白で開始または、終了した場合
        var bounds = new Rect(0, 0, (glyphs.Length - 1) * Spacing + result.Width, fillPath.TightBounds.Height);
        Rect actualBounds = fillPath.TightBounds.ToGraphicsRect();
        SKTextBlob textBlob = builder.Build();

        if (result.Codepoints.Length > 0)
        {
            if (Pen != null && Pen.Thickness > 0)
            {
                strokePath = PenHelper.CreateStrokePath(fillPath, Pen, actualBounds);
                actualBounds = strokePath.TightBounds.ToGraphicsRect();
            }
        }

        (_metrics, _bounds, _actualBounds) = (font.Metrics.ToFontMetrics(), bounds, actualBounds);

        (_textBlob, _fillPath, _strokePath).DisposeAll();
        (_textBlob, _fillPath, _strokePath) = (textBlob, fillPath, strokePath);
    }

    private void SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _isDirty = true;
        }
    }

    private void MeasureAndSetField()
    {
        if (_isDirty)
        {
            Measure();
            _isDirty = false;
        }
    }

    public override bool Equals(object? obj)
    {
        return obj is FormattedText text && Equals(text);
    }

    public bool Equals(FormattedText? other)
    {
        return Weight == other?.Weight
            && Style == other?.Style
            && Font.Equals(other?.Font)
            && Size == other?.Size
            && Spacing == other?.Spacing
            && Text.Equals(other?.Text)
            && BeginOnNewLine == other?.BeginOnNewLine
            && EqualityComparer<IBrush>.Default.Equals(Brush, other?.Brush)
            && EqualityComparer<IPen>.Default.Equals(Pen, other?.Pen);
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
        hash.Add(Pen);
        return hash.ToHashCode();
    }

    public static bool operator ==(FormattedText left, FormattedText right) => left.Equals(right);

    public static bool operator !=(FormattedText left, FormattedText right) => !(left == right);
}
