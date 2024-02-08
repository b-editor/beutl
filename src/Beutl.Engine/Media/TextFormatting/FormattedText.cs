using System.Diagnostics;

using Beutl.Graphics;
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

    private void Measure()
    {
        using SKFont font = this.ToSKFont();

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
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];

            SKPoint point = result.Points[i];
            point.X += i * Spacing;
            positions[i] = point;

            using SKPath tmp = font.GetGlyphPath(glyphs[i]);
            fillPath.AddPath(tmp, point.X, point.Y);
        }

        SKPath? strokePath = null;
        Rect bounds = fillPath.TightBounds.ToGraphicsRect();
        Rect actualBounds = bounds;
        SKTextBlob textBlob = builder.Build();

        if (result.Codepoints.Length > 0)
        {
            if (Pen != null && Pen.Thickness > 0)
            {
                ConfigureStrokePaint(paint, actualBounds.Size);
                float maxAspect = Math.Max(actualBounds.Width, actualBounds.Height);
                float thickness = Pen.Thickness;
                if (Pen.StrokeAlignment == StrokeAlignment.Outside)
                {
                    thickness /= 2;
                }
                else if (Pen.StrokeAlignment == StrokeAlignment.Inside)
                {
                    thickness = paint.StrokeWidth;
                }

                if (maxAspect < thickness)
                {
                    strokePath = new SKPath();
                    paint.StrokeWidth = maxAspect;
                    SKPath? prev = null;
                    try
                    {
                        while (maxAspect < thickness)
                        {
                            SKPath tmp = paint.GetFillPath(prev ?? fillPath);
                            strokePath.AddPath(tmp);

                            prev?.Dispose();
                            prev = tmp;
                            thickness -= maxAspect;
                        }

                        if (prev != null)
                        {
                            paint.StrokeWidth = thickness;
                            using SKPath tmp2 = paint.GetFillPath(prev);
                            strokePath.AddPath(tmp2);
                        }

                        paint.IsStroke = false;
                        actualBounds = strokePath.TightBounds.ToGraphicsRect();
                    }
                    finally
                    {
                        prev?.Dispose();
                    }
                }
                else
                {
                    strokePath = paint.GetFillPath(fillPath);
                    if (Pen.StrokeAlignment != StrokeAlignment.Inside)
                    {
                        actualBounds = strokePath.TightBounds.ToGraphicsRect();
                    }
                }
            }
        }

        (_metrics, _bounds, _actualBounds) = (font.Metrics.ToFontMetrics(), bounds, actualBounds);

        (_textBlob, _fillPath, _strokePath).DisposeAll();
        (_textBlob, _fillPath, _strokePath) = (textBlob, fillPath, strokePath);
    }

    private void ConfigureStrokePaint(SKPaint paint, Size size)
    {
        paint.Reset();

        if (Pen != null && Pen.Thickness != 0)
        {
            float thickness = Pen.Thickness;

            switch (Pen.StrokeAlignment)
            {
                case StrokeAlignment.Outside:
                    thickness *= 2;
                    break;

                case StrokeAlignment.Inside:
                    thickness *= 2;
                    float maxAspect = Math.Max(size.Width, size.Height);
                    thickness = Math.Min(thickness, maxAspect);
                    break;

                default:
                    break;
            }

            paint.IsStroke = true;
            paint.StrokeWidth = thickness;
            paint.StrokeCap = (SKStrokeCap)Pen.StrokeCap;
            paint.StrokeJoin = (SKStrokeJoin)Pen.StrokeJoin;
            paint.StrokeMiter = Pen.MiterLimit;
            if (Pen.DashArray != null && Pen.DashArray.Count > 0)
            {
                IReadOnlyList<float> srcDashes = Pen.DashArray;

                int count = srcDashes.Count % 2 == 0 ? srcDashes.Count : srcDashes.Count * 2;

                float[] dashesArray = new float[count];

                for (int i = 0; i < count; ++i)
                {
                    dashesArray[i] = (float)srcDashes[i % srcDashes.Count] * thickness;
                }

                float offset = (float)(Pen.DashOffset * thickness);

                var pe = SKPathEffect.CreateDash(dashesArray, offset);

                paint.PathEffect = pe;
            }
        }
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
