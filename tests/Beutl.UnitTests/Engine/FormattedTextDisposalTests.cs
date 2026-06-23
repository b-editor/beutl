using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextDisposalTests
{
    [SetUp]
    public void Setup()
    {
        // Log.LoggerFactory is write-once (??=); skip allocating a factory we would only discard
        // when another fixture already set one.
        if (Log.LoggerFactory is null)
        {
            Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        }

        _ = TypefaceProvider.Typeface();
    }

    private static FormattedText CreateText(string text = "ABC", float size = 24f)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        return new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = size,
            Spacing = 1f,
            Text = text
        };
    }

    [Test]
    public void Dispose_MarksIsDisposed_True()
    {
        FormattedText ft = CreateText();
        _ = ft.Bounds;

        ft.Dispose();

        Assert.That(ft.IsDisposed, Is.True);
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        FormattedText ft = CreateText();
        _ = ft.Bounds;

        ft.Dispose();
        Assert.DoesNotThrow(() => ft.Dispose());
        Assert.That(ft.IsDisposed, Is.True);
    }

    [Test]
    public void Dispose_ClearsMainSkiaHandleFields()
    {
        FormattedText ft = CreateText();
        Assert.That(ft.GetTextBlob(), Is.Not.Null);
        Assert.That(ft.GetFillPath(), Is.Not.Null);

        ft.Dispose();

        Assert.That(ft.GetTextBlob(), Is.Null, "TextBlob field should be cleared after Dispose.");
        Assert.That(ft.GetFillPath(), Is.Null, "FillPath field should be cleared after Dispose.");
        Assert.That(ft.GetStrokePath(), Is.Null, "StrokePath field should be cleared after Dispose.");
    }

    [Test]
    public void Dispose_ClearsScaledTextCache()
    {
        FormattedText ft = CreateText();
        SKTextBlob? before = ft.GetTextBlob(2f);
        Assert.That(before, Is.Not.Null);

        ft.Dispose();

        SKTextBlob? after = ft.GetTextBlob(2f);
        Assert.That(after, Is.Not.Null);
        Assert.That(ReferenceEquals(before, after), Is.False,
            "Scaled cache should have been cleared by Dispose; a fresh blob must be produced on re-access.");
        ft.Dispose();
    }

    [Test]
    public void Dispose_ReleasesPathListResources()
    {
        FormattedText ft = CreateText("AB");
        IReadOnlyList<Geometry.Resource> geometries = ft.ToGeometies();
        Assert.That(geometries.Count, Is.GreaterThan(0));

        ft.Dispose();

        foreach (Geometry.Resource? g in geometries)
        {
            if (g is not null)
            {
                Assert.That(g.IsDisposed, Is.True, "Path-list resource should be disposed.");
            }
        }
    }

    [Test]
    public void Dispose_DisposesOwnedGlyphPaths()
    {
        FormattedText ft = CreateText("AB");
        IReadOnlyList<Geometry.Resource> geometries = ft.ToGeometies();

        // The per-glyph SKPath lives on the SKPathGeometry the resource wraps, not in the resource's
        // cached render path; capture those handles so we can assert they were released by Dispose.
        List<SKPath> glyphPaths = geometries
            .OfType<SKPathGeometry.Resource>()
            .Select(r => r.GetOriginal().Path)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        Assert.That(glyphPaths, Is.Not.Empty, "Split-glyph paths should be populated before disposal.");

        ft.Dispose();

        foreach (SKPath path in glyphPaths)
        {
            Assert.That(path.Handle, Is.EqualTo(IntPtr.Zero),
                "Owned glyph SKPath should be deterministically disposed.");
        }
    }

    [Test]
    public void MeasureCore_ShrinkingGlyphCount_DisposesTruncatedTrailingResources()
    {
        FormattedText ft = CreateText("ABCDE");

        // ToGeometies() returns the live _pathList; capture the long count before any shrink.
        IReadOnlyList<Geometry.Resource> longGeometries = ft.ToGeometies();
        int longCount = longGeometries.Count;
        // Font shaping is environment-dependent, so gate the glyph-count facts on Assume, not Assert.
        Assume.That(longCount, Is.GreaterThan(2), "Need more glyphs than the shrunk text to exercise truncation.");

        // Glyph count is driven by HarfBuzz Codepoints.Length, not Text.Length, so derive it from a probe.
        int shortCount;
        using (FormattedText probe = CreateText("AB"))
        {
            shortCount = probe.ToGeometies().Count;
        }

        Assume.That(shortCount, Is.LessThan(longCount), "Shrink must actually reduce the glyph count.");

        // Snapshot the trailing resources before the shrink: SetCount truncates _pathList in place,
        // so they become unreachable through it afterwards.
        List<Geometry.Resource> trailingResources = longGeometries.Skip(shortCount).ToList();
        Assert.That(trailingResources, Has.Count.EqualTo(longCount - shortCount));

        List<SKPath> trailingGlyphPaths = trailingResources
            .OfType<SKPathGeometry.Resource>()
            .Select(r => r.GetOriginal().Path)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        Assume.That(trailingGlyphPaths, Is.Not.Empty, "Trailing glyphs should own SKPaths before the shrink.");
        Assert.That(trailingResources.All(r => !r.IsDisposed), Is.True, "Trailing resources must be live before the shrink.");
        Assert.That(trailingGlyphPaths.All(p => p.Handle != IntPtr.Zero), Is.True,
            "Trailing glyph SKPaths must have live handles before the shrink.");

        // Shrink the text and force a re-measure -> MeasureCore truncates _pathList.
        ft.Text = "AB";
        _ = ft.Bounds;
        Assert.That(ft.ToGeometies().Count, Is.EqualTo(shortCount), "Re-measure should have shrunk the live _pathList.");

        foreach (Geometry.Resource r in trailingResources)
        {
            Assert.That(r.IsDisposed, Is.True, "Truncated trailing path-list resource must be disposed on shrink.");
        }

        foreach (SKPath p in trailingGlyphPaths)
        {
            Assert.That(p.Handle, Is.EqualTo(IntPtr.Zero),
                "Owned glyph SKPath of a truncated trailing entry must be deterministically released.");
        }

        ft.Dispose();
    }

    [Test]
    public void MeasureCore_ShrinkThenGrow_KeepsSurvivorsLiveAndRebuildsTruncatedSlots()
    {
        FormattedText ft = CreateText("ABCDE");

        // ToGeometies() returns the live _pathList; capture the long count before any shrink.
        IReadOnlyList<Geometry.Resource> longGeometries = ft.ToGeometies();
        int longCount = longGeometries.Count;
        // Font shaping is environment-dependent, so gate the glyph-count facts on Assume, not Assert.
        Assume.That(longCount, Is.GreaterThan(2), "Need more glyphs than the shrunk text to exercise truncation.");

        int shortCount;
        using (FormattedText probe = CreateText("AB"))
        {
            shortCount = probe.ToGeometies().Count;
        }

        Assume.That(shortCount, Is.GreaterThan(0), "Shrunk text must still produce glyphs.");
        Assume.That(shortCount, Is.LessThan(longCount), "Shrink must actually reduce the glyph count.");

        // Survivors [0, shortCount) are reused in place across the shrink, so the trailing-dispose loop
        // must not touch them (guards a front-edge off-by-one that would dispose a reused survivor).
        List<Geometry.Resource> survivors = longGeometries.Take(shortCount).ToList();
        Assert.That(survivors.All(r => !r.IsDisposed), Is.True, "Survivors must be live before the shrink.");

        // Shrink -> re-measure disposes + truncates [shortCount, longCount).
        ft.Text = "AB";
        _ = ft.Bounds;

        IReadOnlyList<Geometry.Resource> shrunk = ft.ToGeometies();
        Assert.That(shrunk.Count, Is.EqualTo(shortCount), "Re-measure should have shrunk the live _pathList.");
        for (int i = 0; i < shortCount; i++)
        {
            Assert.That(ReferenceEquals(shrunk[i], survivors[i]), Is.True,
                "Surviving entries must be reused in place, not recreated.");
            Assert.That(survivors[i].IsDisposed, Is.False,
                "Surviving path-list resource must stay live across a shrink (guards a front-edge off-by-one).");
        }

        // Grow back: the shrink cleared the truncated slots, so MeasureCore must build fresh resources
        // rather than resurrect a disposed one (which would throw ObjectDisposedException).
        ft.Text = "ABCDE";
        Assert.DoesNotThrow(() => _ = ft.Bounds, "Re-measure after grow-back must not touch a disposed resource.");

        IReadOnlyList<Geometry.Resource> regrown = ft.ToGeometies();
        Assert.That(regrown.Count, Is.EqualTo(longCount), "Re-measure should have regrown the live _pathList.");
        foreach (Geometry.Resource r in regrown)
        {
            Assert.That(r.IsDisposed, Is.False, "Regrown entry must be live (no disposed resource resurrected).");
            Assert.DoesNotThrow(() => _ = r.GetRenderBounds(null),
                "Regrown entry must be renderable; a resurrected disposed resource would throw here.");
        }

        ft.Dispose();
    }

    [Test]
    public void LineEnumerable_Dispose_DisposesContainedFormattedTexts()
    {
        TextElements elements = new(
        [
            new TextElement { Size = 24, Text = "ABC" }
        ]);

        FormattedText captured = null!;
        foreach (Span<FormattedText> line in elements.Lines)
        {
            captured = line[0];
            _ = captured.Bounds;
            break;
        }

        elements.Lines.Dispose();

        Assert.That(captured.IsDisposed, Is.True);
    }

    [Test]
    public void TextElements_Dispose_DisposesLineEnumerable()
    {
        TextElements elements = new(
        [
            new TextElement { Size = 24, Text = "ABC" }
        ]);
        _ = elements.Lines;

        elements.Dispose();

        Assert.That(elements.IsDisposed, Is.True);
        Assert.That(elements.Lines.IsDisposed, Is.True);
    }

    [Test]
    public void TextElements_Dispose_IsIdempotent()
    {
        TextElements elements = new(
        [
            new TextElement { Size = 24, Text = "ABC" }
        ]);

        elements.Dispose();
        Assert.DoesNotThrow(() => elements.Dispose());
    }

    [Test]
    public void TextBlock_Resource_Dispose_DisposesElements()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        TextBlock tb = new();
        tb.FontFamily.CurrentValue = typeface.FontFamily;
        tb.Size.CurrentValue = 24;
        tb.Text.CurrentValue = "ABC";
        tb.Fill.CurrentValue = Brushes.White;

        TextBlock.Resource resource = tb.ToResource(CompositionContext.Default);
        TextElements elements = resource.GetTextElements();
        foreach (Span<FormattedText> line in elements.Lines)
        {
            foreach (FormattedText ft in line)
            {
                _ = ft.Bounds;
            }
        }

        resource.Dispose();

        Assert.That(elements.IsDisposed, Is.True);
    }

    [Test]
    public void TextBlock_Resource_Update_WithChangedText_DisposesOldElements()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        TextBlock tb = new();
        tb.FontFamily.CurrentValue = typeface.FontFamily;
        tb.Size.CurrentValue = 24;
        tb.Text.CurrentValue = "ABC";
        tb.Fill.CurrentValue = Brushes.White;

        TextBlock.Resource resource = tb.ToResource(CompositionContext.Default);
        TextElements elements = resource.GetTextElements();

        tb.Text.CurrentValue = "DEF";
        bool updateOnly = false;
        resource.Update(tb, CompositionContext.Default, ref updateOnly);

        Assert.That(elements.IsDisposed, Is.True,
            "Old TextElements must be disposed when text changes so FormattedText Skia handles are released deterministically.");
    }
}
