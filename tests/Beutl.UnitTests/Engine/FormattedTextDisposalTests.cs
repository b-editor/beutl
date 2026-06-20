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
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
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
