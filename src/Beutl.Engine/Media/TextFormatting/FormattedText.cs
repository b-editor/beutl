using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Reactive;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beutl.Media.TextFormatting;

[DebuggerDisplay("{Text}")]
public class FormattedText : IEquatable<FormattedText>, IDisposable
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
    private Pen.Resource? _pen;
    private SKTextBlob? _colorGlyphBlob;
    private SKPath? _fillPath;
    private SKPath? _strokePath;
    private List<SKPathGeometry.Resource> _pathList = [];
    private readonly ScaledTextCache _scaledCache;

    public FormattedText()
    {
        _scaledCache = new ScaledTextCache(MeasureColorGlyphBlob);
    }

    public bool IsDisposed { get; private set; }

    /// <remarks>
    /// Disposal is idempotent and one-shot; the instance must not be used afterwards. Measuring members
    /// (e.g. <see cref="Bounds"/> or the density-scaled blob/stroke accessors) throw
    /// <see cref="ObjectDisposedException"/> rather than re-allocating Skia handles that a later
    /// <see cref="Dispose"/> call could not release.
    /// </remarks>
    // No finalizer: every owned field (SKTextBlob / SKPath / SKPathGeometry.Resource) is itself
    // finalizable via SkiaSharp, so deterministic Dispose only speeds up release. If a non-SkiaSharp
    // unmanaged field is ever added, add ~FormattedText() here.
    public void Dispose()
    {
        if (IsDisposed) return;

        _scaledCache.Dispose();
        (_colorGlyphBlob, _fillPath, _strokePath).DisposeAll();
        foreach (SKPathGeometry.Resource? resource in _pathList)
        {
            DisposePathListEntry(resource);
        }

        _pathList = [];
        _colorGlyphBlob = null;
        _fillPath = null;
        _strokePath = null;
        IsDisposed = true;
    }

    // Dispose the geometry too: it owns the per-glyph SKPath (set via SetSKPath(..., clone: false)),
    // which the resource's cached render path does not cover.
    private static void DisposePathListEntry(SKPathGeometry.Resource? resource)
    {
        resource?.GetOriginal().Dispose();
        resource?.Dispose();
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

    public Brush.Resource? Brush { get; set; }

    public Pen.Resource? Pen
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

        SKShaper.Result result = shaper.Shape(buffer, font);

        // create the text blob
        using var builder = new SKTextBlobBuilder();
        SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, result.Codepoints.Length);

        // copy the glyphs
        Span<ushort> glyphs = run.Glyphs;
        Span<SKPoint> positions = run.Positions;
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];
            SKPoint p = result.Points[i];
            p.X += i * Spacing;
            positions[i] = p;
        }

        // build
        using SKTextBlob? textBlob = builder.Build();

        for (int i = 0; i < glyphs.Length; i++)
        {
            ushort glyph = glyphs[i];
            SKPoint p = positions[i] + point.ToSKPoint();

            using SKPath? glyphPath = font.GetGlyphPath(glyph);
            if (glyphPath != null)
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

    /// <summary>
    /// The glyphs the font has no outline for — colour and bitmap glyphs such as emoji. They cannot
    /// join <see cref="GetFillPath"/> and so remain on Skia's glyph-rasterizer path, which is why
    /// this one accessor is still density-dependent.
    /// </summary>
    internal SKTextBlob? GetColorGlyphBlob()
    {
        MeasureAndSetField();
        return _colorGlyphBlob;
    }

    /// <inheritdoc cref="GetColorGlyphBlob()"/>
    internal SKTextBlob? GetColorGlyphBlob(float density)
    {
        density = NormalizeDensity(density);
        if (density == 1f)
        {
            return GetColorGlyphBlob();
        }

        MeasureAndSetField();
        return _scaledCache.Get(density);
    }

    internal SKFont ToSKFont(float density = 1f)
    {
        density = NormalizeDensity(density);
        var typeface = new Typeface(Font, Style, Weight);
        var font = new SKFont(typeface.ToSkia(), Size * density)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.Full,
            // Baseline snapping quantizes vertical placement to whole device pixels, which makes an
            // animated transform advance the text in 1 px jumps.
            BaselineSnap = false
        };

        return font;
    }

    internal IReadOnlyList<Geometry.Resource> ToGeometies()
    {
        MeasureAndSetField();
        return _pathList;
    }

    private void Measure()
    {
        (SKTextBlob? colorGlyphBlob, SKPath fillPath, SKPath? strokePath, FontMetrics metrics, Rect bounds, Rect actualBounds)
            = MeasureCore(1f);

        (_metrics, _bounds, _actualBounds) = (metrics, bounds, actualBounds);

        (_colorGlyphBlob, _fillPath, _strokePath).DisposeAll();
        (_colorGlyphBlob, _fillPath, _strokePath) = (colorGlyphBlob, fillPath, strokePath);
        _scaledCache.Clear();
    }

    /// <summary>
    /// The logical measure. Owns every vector artifact — the glyph path list, the fill path and the
    /// stroke path — which are resolution independent and therefore measured once at density 1.
    /// </summary>
    private (SKTextBlob? ColorGlyphBlob, SKPath FillPath, SKPath? StrokePath, FontMetrics Metrics, Rect Bounds, Rect ActualBounds)
        MeasureCore(float density)
    {
        density = NormalizeDensity(density);
        float spacing = Spacing * density;

        using SKFont font = ToSKFont(density);
        SKShaper.Result result = Shape(font);
        int glyphCount = result.Codepoints.Length;

        // SetCount truncates trailing entries without disposing them; release them first so their
        // owned glyph SKPaths don't leak to finalizers.
        for (int i = glyphCount; i < _pathList.Count; i++)
        {
            DisposePathListEntry(_pathList[i]);
        }

        CollectionsMarshal.SetCount(_pathList, glyphCount);
        Span<SKPathGeometry.Resource> pathList = CollectionsMarshal.AsSpan(_pathList);

        var fillPath = new SKPath();
        ColorGlyphCollector colorGlyphs = default;

        for (int i = 0; i < glyphCount; i++)
        {
            ushort glyph = (ushort)result.Codepoints[i];
            SKPoint point = result.Points[i];
            point.X += i * spacing;

            SKPath? tmp = font.GetGlyphPath(glyph);
            if (tmp != null)
            {
                fillPath.AddPath(tmp, point.X, point.Y);
                tmp.Transform(SKMatrix.CreateTranslation(point.X, point.Y));
            }
            else
            {
                colorGlyphs.Add(glyph, point);
            }

            ref SKPathGeometry.Resource? exist = ref pathList[i]!;
            if (exist is null)
            {
                var geom = new SKPathGeometry();
                geom.SetSKPath(tmp, false);
                exist = geom.ToResource(CompositionContext.Default);
            }
            else
            {
                // SetSKPath reuses the slot without bumping Version, so invalidate the caches explicitly.
                exist.GetOriginal().SetSKPath(tmp, false);
                exist.InvalidateCachedPaths();
            }
        }

        SKTextBlob? colorGlyphBlob = colorGlyphs.Build(font);

        SKPath? strokePath = null;
        // 空白で開始または、終了した場合
        var bounds = new Rect(0, 0, Math.Max(0, glyphCount - 1) * spacing + result.Width,
            InkBounds(fillPath, colorGlyphBlob).Height);
        Rect actualBounds = InkBounds(fillPath, colorGlyphBlob);

        if (glyphCount > 0 && Pen != null && Pen.Thickness > 0)
        {
            strokePath = PenHelper.CreateStrokePath(fillPath, Pen, actualBounds, density);
            actualBounds = actualBounds.Union(strokePath.TightBounds.ToGraphicsRect());
        }

        return (colorGlyphBlob, fillPath, strokePath, font.Metrics.ToFontMetrics(), bounds, actualBounds);
    }

    /// <summary>Re-shapes only the density-dependent colour-glyph blob; the vector artifacts are reused.</summary>
    private SKTextBlob? MeasureColorGlyphBlob(float density)
    {
        density = NormalizeDensity(density);
        float spacing = Spacing * density;

        using SKFont font = ToSKFont(density);
        SKShaper.Result result = Shape(font);

        ColorGlyphCollector colorGlyphs = default;
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            ushort glyph = (ushort)result.Codepoints[i];
            using SKPath? outline = font.GetGlyphPath(glyph);
            if (outline != null) continue;

            SKPoint point = result.Points[i];
            point.X += i * spacing;
            colorGlyphs.Add(glyph, point);
        }

        return colorGlyphs.Build(font);
    }

    private SKShaper.Result Shape(SKFont font)
    {
        using var shaper = new SKShaper(font.Typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();
        return shaper.Shape(buffer, font);
    }

    private static Rect InkBounds(SKPath fillPath, SKTextBlob? colorGlyphBlob)
    {
        Rect fill = fillPath.IsEmpty ? default : fillPath.TightBounds.ToGraphicsRect();
        if (colorGlyphBlob is null) return fill;

        Rect color = colorGlyphBlob.Bounds.ToGraphicsRect();
        return fill.IsEmpty ? color : fill.Union(color);
    }

    // Accumulates the glyphs that have no outline. Kept as a struct so the common all-outline case
    // allocates nothing.
    private struct ColorGlyphCollector
    {
        private List<ushort>? _glyphs;
        private List<SKPoint>? _positions;

        public void Add(ushort glyph, SKPoint position)
        {
            (_glyphs ??= []).Add(glyph);
            (_positions ??= []).Add(position);
        }

        public readonly SKTextBlob? Build(SKFont font)
        {
            if (_glyphs is not { Count: > 0 }) return null;

            using var builder = new SKTextBlobBuilder();
            SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, _glyphs.Count);
            CollectionsMarshal.AsSpan(_glyphs).CopyTo(run.Glyphs);
            CollectionsMarshal.AsSpan(_positions!).CopyTo(run.Positions);
            return builder.Build();
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_isDirty)
        {
            Measure();
            _isDirty = false;
        }
    }


    private static float NormalizeDensity(float density)
    {
        if (!float.IsFinite(density) || density <= 0f)
        {
            return 1f;
        }

        return MathF.Abs(density - 1f) < 1e-6f ? 1f : density;
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
               && EqualityComparer<Brush.Resource>.Default.Equals(Brush, other?.Brush)
               && EqualityComparer<Pen.Resource>.Default.Equals(Pen, other?.Pen);
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
