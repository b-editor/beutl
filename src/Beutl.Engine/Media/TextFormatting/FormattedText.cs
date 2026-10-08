using System.Buffers;
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
    private Rect _rasterBounds;
    private bool _isDirty = false;
    private Pen.Resource? _pen;
    private SKTextBlob? _textBlob;
    private SKPath? _fillPath;
    private SKPath? _strokePath;
    private List<Geometry.Resource> _pathList = [];
    private readonly ScaledTextCache _scaledCache;
    private readonly Dictionary<int, (ShapedGlyph Glyph, FormattedText? Text)> _nonOutlineGlyphs = [];
    private readonly List<Rect> _nonOutlineBounds = [];
    private ShapedGlyph? _shapedGlyph;
    private long _fontRevision;
    private List<TextFontFallback.Run>? _fontRuns;
    private SKTypeface? _fontRunsPrimary;
    private long _fontRunsRevision;

    public FormattedText()
    {
        _scaledCache = new ScaledTextCache(MeasureScaledText);
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
        ClearNonOutlineGlyphs();
        (_textBlob, _fillPath, _strokePath).DisposeAll();
        foreach (Geometry.Resource? resource in _pathList)
        {
            resource?.Dispose();
        }

        _pathList = [];
        _textBlob = null;
        _fillPath = null;
        _strokePath = null;
        _fontRuns = null;
        _fontRunsPrimary = null;
        IsDisposed = true;
    }

    public FontWeight Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, value, invalidateFontRuns: true);
    }

    public FontStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, invalidateFontRuns: true);
    }

    public FontFamily Font
    {
        get => _font;
        set => SetProperty(ref _font, value, invalidateFontRuns: true);
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

            SetProperty(ref _text, value, invalidateFontRuns: true);
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

    /// <summary>
    /// Bounds of the glyph masks this text rasterizes, which contain <see cref="ActualBounds"/>.
    /// </summary>
    /// <remarks>
    /// Full hinting moves a mask off its unhinted outline, so a renderer must allocate this rather than
    /// <see cref="ActualBounds"/> or it clips what it draws. Only the allocated footprint may use it:
    /// brush mapping and layout stay on the semantic bounds.
    /// </remarks>
    public Rect RasterBounds
    {
        get
        {
            MeasureAndSetField();
            return _rasterBounds;
        }
    }

    /// <summary>
    /// <see cref="RasterBounds"/> widened so that its device footprint at <paramref name="scale"/> still
    /// clears the glyph masks by a whole device pixel.
    /// </summary>
    /// <remarks>
    /// <see cref="RasterBounds"/> measures the masks hinted for scale 1, and hinting at another scale
    /// moves them by more than rescaling that rectangle accounts for, so the footprint has to come from
    /// a measurement at the scale that will actually be drawn. The result never narrows
    /// <see cref="RasterBounds"/>, so a footprint can only gain room by asking for a scale.
    /// </remarks>
    public Rect GetRasterBounds(float scale)
    {
        MeasureAndSetField();
        scale = NormalizeDensity(scale);
        if (scale == 1f)
            return _rasterBounds;

        Rect scaled = _scaledCache.Get(scale).RasterBounds;
        return scaled.IsEmpty ? _rasterBounds : _rasterBounds.Union(scaled);
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

        MeasureAndSetField();
        return _scaledCache.Get(density).StrokePath;
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

        MeasureAndSetField();
        return _scaledCache.Get(density).TextBlob;
    }

    internal SKFont ToSKFont(float density = 1f)
    {
        density = NormalizeDensity(density);
        var typeface = new Typeface(Font, Style, Weight);
        return CreateSKFont(_shapedGlyph?.Typeface ?? typeface.ToSkia(), density);
    }

    private SKFont CreateSKFont(SKTypeface typeface, float density)
    {
        var font = new SKFont(typeface, Size * density)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            // Hinting refits outlines to the pixel grid at every size, so text that scales changes
            // shape from frame to frame under FreeType and DirectWrite. CoreText ignores it anyway.
            Hinting = SKFontHinting.None,
            // Baseline snapping quantizes vertical placement to whole device pixels, which makes an
            // animated transform advance the text in 1 px jumps.
            BaselineSnap = false
        };

        return font;
    }

    /// <summary>
    /// Gets the outline of every shaped glyph of <see cref="Text"/>, each already positioned relative to
    /// this run's origin, so a caller can draw the glyphs one by one instead of as a single blob.
    /// </summary>
    /// <returns>
    /// A borrowed span with one entry per shaped glyph, in visual order. Shaping is not a character
    /// mapping: a surrogate pair or a ligature collapses into a single glyph, so the entries do not line
    /// up index-for-index with <see cref="Text"/>. A glyph that has no outline, such as a space, still
    /// occupies an entry.
    /// </returns>
    /// <remarks>
    /// The span is valid only until the next measure of this <see cref="FormattedText"/> — assigning
    /// <see cref="Text"/>, <see cref="Size"/> or any other measured property re-measures the run — or
    /// until <see cref="Dispose"/>. The entries are engine-owned and must not be disposed by the caller.
    /// To keep a geometry beyond the span, replay it into your own geometry with
    /// <see cref="Geometry.Resource.ApplyTo(IGeometryContext)"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    public ReadOnlySpan<Geometry.Resource> ToGeometries()
    {
        MeasureAndSetField();
        return CollectionsMarshal.AsSpan(_pathList);
    }

    // A borrowed, already-shaped single-glyph run, owned by this text just like ToGeometries().
    // Keeping the glyph ID and position preserves ligatures, surrogate pairs and character spacing.
    internal FormattedText? GetNonOutlineGlyph(int index)
    {
        MeasureAndSetField();
        if (!_nonOutlineGlyphs.TryGetValue(index, out var entry))
            return null;

        if (entry.Text is null)
        {
            entry.Text = new FormattedText
            {
                Font = Font,
                Style = Style,
                Weight = Weight,
                Size = Size,
                Text = Text,
                Brush = Brush,
                Pen = Pen,
                _shapedGlyph = entry.Glyph,
            };
            _nonOutlineGlyphs[index] = entry;
        }

        return entry.Text;
    }

    internal bool NonOutlineContains(Point point)
    {
        // Bitmap glyphs use their ink rectangles, matching image hit testing without pixel readback.
        MeasureAndSetField();
        foreach (Rect bounds in _nonOutlineBounds)
        {
            if (bounds.Contains(point))
                return true;
        }

        return false;
    }

    private void ClearNonOutlineGlyphs()
    {
        foreach (var entry in _nonOutlineGlyphs.Values)
            entry.Text?.Dispose();
        _nonOutlineGlyphs.Clear();
        _nonOutlineBounds.Clear();
    }

    private List<ShapedRun> Shape(SKFont font, float density)
    {
        if (_shapedGlyph is { } glyph)
        {
            return [new ShapedRun(glyph.Typeface, new SKShaper.Result(
                [glyph.Id], [0],
                [new SKPoint(glyph.Position.X * density, glyph.Position.Y * density)],
                glyph.Width * density))];
        }

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(Text.AsSpan());
        buffer.GuessSegmentProperties();
        long revision = FontManager.Instance.Revision;
        if (_fontRuns is null || _fontRunsRevision != revision || !ReferenceEquals(_fontRunsPrimary, font.Typeface))
        {
            _fontRuns = TextFontFallback.GetRuns(Text.AsSpan(), font, Style, Weight);
            _fontRunsPrimary = font.Typeface;
            _fontRunsRevision = revision;
        }
        List<TextFontFallback.Run> runs = _fontRuns;
        if (runs.Count == 1)
        {
            using SKFont runFont = CreateSKFont(runs[0].Typeface, density);
            using var shaper = new TextShaper(runs[0].Typeface);
            return [new ShapedRun(runs[0].Typeface, shaper.Shape(buffer, runFont))];
        }

        var shapedRuns = new List<ShapedRun>(runs.Count);
        var shapers = new Dictionary<SKTypeface, TextShaper>();
        bool rightToLeft = buffer.Direction == HarfBuzzSharp.Direction.RightToLeft;
        try
        {
            for (int i = 0; i < runs.Count; i++)
            {
                TextFontFallback.Run run = runs[rightToLeft ? runs.Count - 1 - i : i];
                using SKFont runFont = CreateSKFont(run.Typeface, density);
                if (!shapers.TryGetValue(run.Typeface, out TextShaper? shaper))
                {
                    // Opening the same font data per run dominates alternating-font text.
                    shaper = new TextShaper(run.Typeface);
                    shapers.Add(run.Typeface, shaper);
                }
                using var runBuffer = new HarfBuzzSharp.Buffer();
                // Retain the surrounding text as HarfBuzz context at a font boundary.
                runBuffer.AddUtf16(Text.AsSpan(), run.Start, run.Length);
                runBuffer.Direction = buffer.Direction;
                runBuffer.Language = buffer.Language;
                runBuffer.GuessSegmentProperties();
                shapedRuns.Add(new ShapedRun(run.Typeface, shaper.Shape(runBuffer, runFont)));
            }
        }
        finally
        {
            foreach (TextShaper shaper in shapers.Values)
                shaper.Dispose();
        }

        return shapedRuns;
    }

    private readonly record struct ShapedRun(SKTypeface Typeface, SKShaper.Result Result);

    private readonly record struct ShapedGlyph(SKTypeface Typeface, ushort Id, SKPoint Position, float Width);

    // The caller owns TextBlob, FillPath and StrokePath.
    private readonly record struct MeasuredText(
        SKTextBlob? TextBlob,
        SKPath FillPath,
        SKPath? StrokePath,
        FontMetrics Metrics,
        Rect Bounds,
        Rect ActualBounds,
        Rect RasterBounds);

    private void Measure()
    {
        MeasuredText measured = MeasureCore(1f, updatePathList: true);

        (_metrics, _bounds, _actualBounds, _rasterBounds) =
            (measured.Metrics, measured.Bounds, measured.ActualBounds, measured.RasterBounds);

        (_textBlob, _fillPath, _strokePath).DisposeAll();
        (_textBlob, _fillPath, _strokePath) = (measured.TextBlob, measured.FillPath, measured.StrokePath);
        _scaledCache.Clear();
    }

    private MeasuredText MeasureCore(float density, bool updatePathList)
    {
        density = NormalizeDensity(density);
        float spacing = Spacing * density;

        using SKFont font = ToSKFont(density);

        List<ShapedRun> shapedRuns = Shape(font, density);
        int glyphCount = shapedRuns.Sum(run => run.Result.Codepoints.Length);

        // create the text blob
        using var builder = new SKTextBlobBuilder();
        using var fillBuilder = new SKPathBuilder();
        Span<Geometry.Resource> pathList = default;
        if (updatePathList)
        {
            ClearNonOutlineGlyphs();
            pathList = PreparePathList(glyphCount);
        }

        Rect nonOutlineBounds = default;
        Rect rasterBounds = default;
        FontMetrics metrics = font.Metrics.ToFontMetrics();
        int glyphOffset = 0;
        float advance = 0;
        Span<SKRect> glyphBounds = stackalloc SKRect[1];
        Span<float> glyphWidth = stackalloc float[1];
        foreach (ShapedRun shapedRun in shapedRuns)
        {
            using SKFont runFont = CreateSKFont(shapedRun.Typeface, density);
            SKShaper.Result result = shapedRun.Result;
            SKPositionedRunBuffer run = builder.AllocatePositionedRun(runFont, result.Codepoints.Length);
            Span<ushort> glyphs = run.Glyphs;
            Span<SKPoint> positions = run.Positions;
            for (int i = 0; i < result.Codepoints.Length; i++)
            {
                glyphs[i] = (ushort)result.Codepoints[i];

                int glyphIndex = glyphOffset + i;
                SKPoint point = result.Points[i];
                point.X += advance + glyphIndex * spacing;
                positions[i] = point;

                SKPath? tmp = runFont.GetGlyphPath(glyphs[i]);
                if (tmp is null || tmp.IsEmpty)
                {
                    runFont.GetGlyphWidths(glyphs.Slice(i, 1), glyphWidth, glyphBounds);
                    if (!glyphBounds[0].IsEmpty)
                    {
                        glyphBounds[0].Offset(point);
                        Rect inkBounds = glyphBounds[0].ToGraphicsRect();
                        nonOutlineBounds = nonOutlineBounds.Union(inkBounds);
                        if (updatePathList)
                        {
                            _nonOutlineBounds.Add(inkBounds);
                            if (_shapedGlyph is null)
                            {
                                _nonOutlineGlyphs.Add(glyphIndex,
                                    (new ShapedGlyph(shapedRun.Typeface, glyphs[i], point, glyphWidth[0]), null));
                            }
                        }
                    }
                }

                if (tmp != null)
                {
                    fillBuilder.AddPath(tmp, point.X, point.Y);

                    if (updatePathList)
                    {
                        tmp.Transform(SKMatrix.CreateTranslation(point.X, point.Y));
                        AssignGlyphGeometry(pathList, glyphIndex, tmp);
                    }
                    else
                    {
                        tmp.Dispose();
                    }
                }
                else if (updatePathList)
                {
                    AssignGlyphGeometry(pathList, glyphIndex, tmp);
                }
            }

            rasterBounds = rasterBounds.Union(MeasureGlyphMaskBounds(runFont, glyphs, positions));
            FontMetrics runMetrics = runFont.Metrics.ToFontMetrics();
            metrics = metrics with
            {
                Ascent = MathF.Min(metrics.Ascent, runMetrics.Ascent),
                Descent = MathF.Max(metrics.Descent, runMetrics.Descent),
                Leading = MathF.Max(metrics.Leading, runMetrics.Leading),
                Top = MathF.Min(metrics.Top, runMetrics.Top),
                Bottom = MathF.Max(metrics.Bottom, runMetrics.Bottom),
            };
            advance += result.Width;
            glyphOffset += glyphs.Length;
        }

        SKPath fillPath = fillBuilder.Detach();
        SKPath? strokePath = null;
        // 空白で開始または、終了した場合
        float width = MathF.Max(0, (Math.Max(0, glyphCount - 1) * spacing) + advance);
        Rect actualBounds = fillPath.TightBounds.ToGraphicsRect();
        Rect fillBounds = actualBounds.Union(nonOutlineBounds);
        // A split glyph is already positioned, just like the neighboring geometries. Its brush must
        // follow that ink rectangle; only a complete text run uses zero-origin layout bounds.
        var bounds = _shapedGlyph is null ? new Rect(0, 0, width, fillBounds.Height) : fillBounds;
        SKTextBlob? textBlob = builder.Build();

        if (glyphCount > 0)
        {
            if (Pen != null && Pen.Thickness > 0)
            {
                strokePath = PenHelper.CreateStrokePath(fillPath, Pen, actualBounds, density);
                actualBounds = strokePath.TightBounds.ToGraphicsRect();
            }
        }

        if (strokePath is not null)
            rasterBounds = rasterBounds.Union(InflateToRaster(strokePath.TightBounds).ToGraphicsRect());
        actualBounds = actualBounds.Union(nonOutlineBounds);
        rasterBounds = rasterBounds.IsEmpty ? actualBounds : rasterBounds.Union(actualBounds);

        return new MeasuredText(
            textBlob, fillPath, strokePath, metrics, bounds, actualBounds, rasterBounds);
    }

    // Sizes the glyph geometry list to one entry per glyph and returns it for filling in place.
    private Span<Geometry.Resource> PreparePathList(int glyphCount)
    {
        // SetCount truncates trailing entries without disposing them; release them first so their
        // owned glyph SKPaths don't leak to finalizers.
        for (int i = glyphCount; i < _pathList.Count; i++)
        {
            _pathList[i]?.Dispose();
        }

        CollectionsMarshal.SetCount(_pathList, glyphCount);
        return CollectionsMarshal.AsSpan(_pathList);
    }

    // The list is typed as Geometry.Resource so ToGeometries can hand out a well-typed
    // span; every entry is minted right here, so it is always an SKPathGeometry.Resource.
    private static void AssignGlyphGeometry(Span<Geometry.Resource> pathList, int index, SKPath? path)
    {
        ref Geometry.Resource? exist = ref pathList[index]!;
        exist ??= new SKPathGeometry().ToResource(CompositionContext.Default);
        ((SKPathGeometry.Resource)exist).SetSKPath(path, false);
    }

    private static Rect MeasureGlyphMaskBounds(SKFont font, ReadOnlySpan<ushort> glyphs, ReadOnlySpan<SKPoint> positions)
    {
        if (glyphs.Length == 0)
            return default;

        float[] widths = ArrayPool<float>.Shared.Rent(glyphs.Length);
        SKRect[] glyphBounds = ArrayPool<SKRect>.Shared.Rent(glyphs.Length);
        try
        {
            font.GetGlyphWidths(glyphs, widths.AsSpan(0, glyphs.Length), glyphBounds.AsSpan(0, glyphs.Length), null);

            var union = SKRect.Empty;
            bool any = false;
            for (int i = 0; i < glyphs.Length; i++)
            {
                SKRect glyph = glyphBounds[i];
                if (glyph.IsEmpty)
                    continue;

                glyph.Offset(positions[i]);
                union = any ? SKRect.Union(union, glyph) : glyph;
                any = true;
            }

            return any ? InflateToRaster(union).ToGraphicsRect() : default;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(widths);
            ArrayPool<SKRect>.Shared.Return(glyphBounds);
        }
    }

    // A glyph strike is measured at subpixel phase zero but drawn at the phase its position falls in,
    // and antialiasing samples the pixel an edge touches, so coverage reaches one pixel past the mask.
    private static SKRect InflateToRaster(SKRect bounds)
    {
        bounds.Inflate(1f, 1f);
        return bounds;
    }

    private void SetProperty<T>(ref T field, T value, bool invalidateFontRuns = false)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _isDirty = true;
            if (invalidateFontRuns)
                _fontRuns = null;
        }
    }

    private void MeasureAndSetField()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        long revision = FontManager.Instance.Revision;
        if (_isDirty || _fontRevision != revision)
        {
            Measure();
            _isDirty = false;
            _fontRevision = revision;
        }
    }

    private (SKTextBlob? TextBlob, SKPath? StrokePath, Rect RasterBounds) MeasureScaledText(float density)
    {
        MeasuredText measured = MeasureCore(density, updatePathList: false);
        measured.FillPath.Dispose();
        Rect rasterBounds = measured.RasterBounds;
        return (
            measured.TextBlob,
            measured.StrokePath,
            new Rect(
                rasterBounds.X / density,
                rasterBounds.Y / density,
                rasterBounds.Width / density,
                rasterBounds.Height / density));
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
               && _shapedGlyph == other?._shapedGlyph
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
        hash.Add(_shapedGlyph);
        hash.Add(Brush);
        hash.Add(Pen);
        return hash.ToHashCode();
    }

    public static bool operator ==(FormattedText left, FormattedText right) => left.Equals(right);

    public static bool operator !=(FormattedText left, FormattedText right) => !(left == right);
}
