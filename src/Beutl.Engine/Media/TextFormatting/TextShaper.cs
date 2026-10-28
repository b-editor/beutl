using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beutl.Media.TextFormatting;

/// <summary>
/// Shapes text with HarfBuzz at the variation position of the typeface it was created for.
/// </summary>
/// <remarks>
/// <see cref="SKShaper"/> builds its HarfBuzz font from the typeface's font data, which holds the default
/// instance of a variable font, so it lays out an instance at another weight with the default instance's
/// advances. Apart from applying the variation position this shapes exactly as <see cref="SKShaper"/> does,
/// so text in a static font is laid out identically.
/// </remarks>
internal sealed class TextShaper : IDisposable
{
    // SKShaper's scale: HarfBuzz reports positions in 1/512 em.
    private const int FontSizeScale = 512;

    private readonly HarfBuzzSharp.Font _font;

    public TextShaper(SKTypeface typeface)
    {
        // ToHarfBuzzBlob hands the stream to the blob, whose release callback disposes it once disposing _font frees
        // the blob. Disposing it here instead could free data the blob still points at, because a memory-backed stream
        // lends the blob its own memory.
        using (Blob blob = typeface.OpenStream(out int index).ToHarfBuzzBlob())
        using (var face = new Face(blob, index) { Index = index, UnitsPerEm = typeface.UnitsPerEm })
        {
            _font = new HarfBuzzSharp.Font(face);
        }

        _font.SetScale(FontSizeScale, FontSizeScale);
        _font.SetFunctionsOpenType();

        SKFontVariationPositionCoordinate[] position = typeface.VariationDesignPosition;
        if (position.Length > 0)
        {
            var variations = new Variation[position.Length];
            for (int i = 0; i < position.Length; i++)
            {
                variations[i] = new Variation { Tag = position[i].Axis, Value = position[i].Value };
            }

            _font.SetVariations(variations);
        }
    }

    internal readonly record struct Result(uint[] Codepoints, uint[] Clusters, SKPoint[] Points, SKPoint Advance);

    public SKShaper.Result Shape(HarfBuzzSharp.Buffer buffer, SKFont font)
    {
        Result result = ShapeWithAdvance(buffer, font);
        return new SKShaper.Result(result.Codepoints, result.Clusters, result.Points, result.Advance.X);
    }

    public Result ShapeWithAdvance(HarfBuzzSharp.Buffer buffer, SKFont font)
    {
        _font.Shape(buffer);

        int length = buffer.Length;
        ReadOnlySpan<GlyphInfo> infos = buffer.GetGlyphInfoSpan();
        ReadOnlySpan<GlyphPosition> positions = buffer.GetGlyphPositionSpan();

        float textSizeY = font.Size / FontSizeScale;
        float textSizeX = textSizeY * font.ScaleX;

        var codepoints = new uint[length];
        var clusters = new uint[length];
        var points = new SKPoint[length];
        float x = 0;
        float y = 0;
        for (int i = 0; i < length; i++)
        {
            codepoints[i] = infos[i].Codepoint;
            clusters[i] = infos[i].Cluster;
            points[i] = new SKPoint(x + positions[i].XOffset * textSizeX, y - positions[i].YOffset * textSizeY);

            x += positions[i].XAdvance * textSizeX;
            y -= positions[i].YAdvance * textSizeY;
        }

        return new Result(codepoints, clusters, points, new SKPoint(x, y));
    }

    public bool HasVariationGlyph(int unicode, uint selector) =>
        _font.TryGetVariationGlyph(unicode, selector, out uint glyph) && glyph != 0;

    public bool HasGlyphs(HarfBuzzSharp.Buffer buffer, bool requireSingleGlyph = false)
    {
        _font.Shape(buffer);
        if (requireSingleGlyph && buffer.Length != 1)
            return false;
        foreach (GlyphInfo info in buffer.GetGlyphInfoSpan())
        {
            if (info.Codepoint == 0)
                return false;
        }
        return true;
    }

    public void Dispose()
    {
        _font.Dispose();
    }
}
