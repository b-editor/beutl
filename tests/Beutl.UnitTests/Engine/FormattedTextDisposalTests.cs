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
    public void MeasuringMembers_AfterDispose_ThrowObjectDisposedException()
    {
        FormattedText ft = CreateText();
        _ = ft.Bounds;

        ft.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = ft.Bounds);
            Assert.Throws<ObjectDisposedException>(() => _ = ft.ActualBounds);
            Assert.Throws<ObjectDisposedException>(() => _ = ft.Metrics);
            Assert.Throws<ObjectDisposedException>(() => ft.GetTextBlob());
            Assert.Throws<ObjectDisposedException>(() => ft.GetFillPath());
            Assert.Throws<ObjectDisposedException>(() => ft.GetStrokePath());
            Assert.Throws<ObjectDisposedException>(() => ft.ToGeometies());
        });
    }

    [Test]
    public void ScaledAccessors_AfterDispose_ThrowObjectDisposedException()
    {
        FormattedText ft = CreateText();
        // Leave the instance dirty so a post-dispose access would otherwise re-measure and
        // re-populate the scaled cache, leaking handles the idempotent Dispose can no longer release.
        ft.Size = 32f;

        ft.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => ft.GetTextBlob(2f));
            Assert.Throws<ObjectDisposedException>(() => ft.GetStrokePath(2f));
        });
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
    public void LineEnumerable_GetEnumerator_AfterDispose_ThrowsObjectDisposedException()
    {
        TextElements elements = new(
        [
            new TextElement { Size = 24, Text = "ABC" }
        ]);

        elements.Lines.Dispose();

        Assert.Throws<ObjectDisposedException>(() => elements.Lines.GetEnumerator());
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
