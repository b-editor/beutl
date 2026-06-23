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
    private SKTextBlob? _textBlob;
    private SKPath? _fillPath;
    private SKPath? _strokePath;
    // Caps the per-density blob/stroke cache so varying densities (e.g. window-resize
    // scaling) can't grow it without bound; the least-recently-used density is evicted.
    private const int MaxScaledTextCacheEntries = 8;
    private readonly Dictionary<float, ScaledTextCache> _scaledTextCache = [];
    private readonly LinkedList<float> _scaledTextCacheLru = new();
    private List<SKPathGeometry.Resource> _pathList = [];

    private sealed class ScaledTextCache : IDisposable
    {
        public ScaledTextCache(SKTextBlob? textBlob, SKPath? strokePath, LinkedListNode<float> lruNode)
        {
            TextBlob = textBlob;
            StrokePath = strokePath;
            LruNode = lruNode;
        }

        public SKTextBlob? TextBlob { get; }

        public SKPath? StrokePath { get; }

        public LinkedListNode<float> LruNode { get; }

        public void Dispose()
        {
            TextBlob?.Dispose();
            StrokePath?.Dispose();
        }
    }

    public FormattedText()
    {
    }

    public bool IsDisposed { get; private set; }

    /// <remarks>
    /// Disposal is idempotent and one-shot; the instance must not be used afterwards. Measuring members
    /// (e.g. <see cref="Bounds"/> or the density-scaled blob/stroke accessors) would re-allocate Skia
    /// handles that a later <see cref="Dispose"/> call will not release.
    /// </remarks>
    // No finalizer: every owned field (SKTextBlob / SKPath / SKPathGeometry.Resource) is itself
    // finalizable via SkiaSharp, so deterministic Dispose only speeds up release. If a non-SkiaSharp
    // unmanaged field is ever added, add ~FormattedText() here.
    public void Dispose()
    {
        if (IsDisposed) return;

        ClearScaledTextCache();
        (_textBlob, _fillPath, _strokePath).DisposeAll();
        foreach (SKPathGeometry.Resource? resource in _pathList)
        {
            DisposePathListEntry(resource);
        }

        _pathList = [];
        _textBlob = null;
        _fillPath = null;
        _strokePath = null;
        IsDisposed = true;
    }

    // Releases a single _pathList entry. The geometry must be disposed too: it owns the per-glyph
    // SKPath (set via SetSKPath(..., clone: false)), which the resource's cached render path does not
    // cover. Both calls release native handles, so the resource must not be accessed afterwards.
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

    internal SKPath? GetStrokePath(float density)
    {
        density = NormalizeDensity(density);
        if (density == 1f)
        {
            return GetStrokePath();
        }

        return GetScaledTextCache(density).StrokePath;
    }

    internal SKTextBlob? GetTextBlob()
    {
        MeasureAndSetField();
        return _textBlob;
    }

    internal SKTextBlob? GetTextBlob(float density)
    {
        density = NormalizeDensity(density);
        if (density == 1f)
        {
            return GetTextBlob();
        }

        return GetScaledTextCache(density).TextBlob;
    }

    internal SKFont ToSKFont(float density = 1f)
    {
        density = NormalizeDensity(density);
        var typeface = new Typeface(Font, Style, Weight);
        var font = new SKFont(typeface.ToSkia(), Size * density)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.Full
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
        (SKTextBlob? textBlob, SKPath fillPath, SKPath? strokePath, FontMetrics metrics, Rect bounds, Rect actualBounds)
            = MeasureCore(1f, updatePathList: true);

        (_metrics, _bounds, _actualBounds) = (metrics, bounds, actualBounds);

        (_textBlob, _fillPath, _strokePath).DisposeAll();
        (_textBlob, _fillPath, _strokePath) = (textBlob, fillPath, strokePath);
        ClearScaledTextCache();
    }

    private (SKTextBlob? TextBlob, SKPath FillPath, SKPath? StrokePath, FontMetrics Metrics, Rect Bounds, Rect ActualBounds)
        MeasureCore(float density, bool updatePathList)
    {
        density = NormalizeDensity(density);
        float spacing = Spacing * density;

        using SKFont font = ToSKFont(density);

        using var shaper = new SKShaper(font.Typeface);
        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();

        SKShaper.Result result = shaper.Shape(buffer, font);

        // create the text blob
        using var builder = new SKTextBlobBuilder();
        SKPositionedRunBuffer run = builder.AllocatePositionedRun(font, result.Codepoints.Length);

        var fillPath = new SKPath();
        Span<ushort> glyphs = run.Glyphs;
        Span<SKPoint> positions = run.Positions;
        Span<SKPathGeometry.Resource> pathList = default;
        if (updatePathList)
        {
            // When the glyph count shrank, SetCount truncates the trailing _pathList entries; those
            // dropped resources are then unreachable for Dispose() to release, so their owned glyph
            // SKPath / cached render handles would leak to finalizers. Dispose them before truncating.
            int glyphCount = result.Codepoints.Length;
            for (int i = glyphCount; i < _pathList.Count; i++)
            {
                DisposePathListEntry(_pathList[i]);
            }

            CollectionsMarshal.SetCount(_pathList, glyphCount);
            pathList = CollectionsMarshal.AsSpan(_pathList);
        }

        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            glyphs[i] = (ushort)result.Codepoints[i];

            SKPoint point = result.Points[i];
            point.X += i * spacing;
            positions[i] = point;

            SKPath? tmp = font.GetGlyphPath(glyphs[i]);
            if (tmp != null)
            {
                fillPath.AddPath(tmp, point.X, point.Y);

                if (updatePathList)
                {
                    tmp.Transform(SKMatrix.CreateTranslation(point.X, point.Y));

                    ref SKPathGeometry.Resource? exist = ref pathList[i]!;
                    if (exist is null)
                    {
                        var geom = new SKPathGeometry();
                        geom.SetSKPath(tmp, false);
                        exist = geom.ToResource(CompositionContext.Default);
                    }
                    else
                    {
                        exist.GetOriginal().SetSKPath(tmp, false);
                    }
                }
                else
                {
                    tmp.Dispose();
                }
            }
            else if (updatePathList)
            {
                ref SKPathGeometry.Resource? exist = ref pathList[i]!;
                if (exist is null)
                {
                    var geom = new SKPathGeometry();
                    geom.SetSKPath(tmp, false);
                    exist = geom.ToResource(CompositionContext.Default);
                }
                else
                {
                    exist.GetOriginal().SetSKPath(tmp, false);
                }
            }
        }

        SKPath? strokePath = null;
        // 空白で開始または、終了した場合
        var bounds = new Rect(0, 0, Math.Max(0, glyphs.Length - 1) * spacing + result.Width, fillPath.TightBounds.Height);
        Rect actualBounds = fillPath.TightBounds.ToGraphicsRect();
        SKTextBlob? textBlob = builder.Build();

        if (result.Codepoints.Length > 0)
        {
            if (Pen != null && Pen.Thickness > 0)
            {
                strokePath = PenHelper.CreateStrokePath(fillPath, Pen, actualBounds, density);
                actualBounds = strokePath.TightBounds.ToGraphicsRect();
            }
        }

        return (textBlob, fillPath, strokePath, font.Metrics.ToFontMetrics(), bounds, actualBounds);
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

    private ScaledTextCache GetScaledTextCache(float density)
    {
        MeasureAndSetField();
        if (_scaledTextCache.TryGetValue(density, out ScaledTextCache? cache))
        {
            _scaledTextCacheLru.Remove(cache.LruNode);
            _scaledTextCacheLru.AddFirst(cache.LruNode);
            return cache;
        }

        (SKTextBlob? textBlob, SKPath fillPath, SKPath? strokePath, _, _, _) =
            MeasureCore(density, updatePathList: false);
        fillPath.Dispose();

        while (_scaledTextCache.Count >= MaxScaledTextCacheEntries && _scaledTextCacheLru.Last is { } lru)
        {
            _scaledTextCacheLru.RemoveLast();
            if (_scaledTextCache.Remove(lru.Value, out ScaledTextCache? evicted))
            {
                evicted.Dispose();
            }
        }

        LinkedListNode<float> node = _scaledTextCacheLru.AddFirst(density);
        cache = new ScaledTextCache(textBlob, strokePath, node);
        _scaledTextCache.Add(density, cache);
        return cache;
    }

    private void ClearScaledTextCache()
    {
        foreach (ScaledTextCache item in _scaledTextCache.Values)
        {
            item.Dispose();
        }

        _scaledTextCache.Clear();
        _scaledTextCacheLru.Clear();
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
