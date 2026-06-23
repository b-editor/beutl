using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextGeometryCacheTests
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

    private static FormattedText CreateText(string text, float size = 64f)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        return new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = size,
            Text = text
        };
    }

    // Regression: changing a FormattedText's text re-measures in place, reusing the per-glyph
    // SKPathGeometry slot via SetSKPath without bumping the resource Version. Geometry.Resource caches
    // its render path keyed on Version, so before the fix GetCachedPath kept returning the previous
    // glyph's outline (a narrow 'I' even after the text became a wide 'W').
    [Test]
    public void ReMeasure_ReusingGlyphSlot_RebuildsCachedGeometryPath()
    {
        using FormattedText text = CreateText("I");

        // Populate the per-glyph geometry resource cache for the narrow glyph 'I'.
        Geometry.Resource glyph = text.ToGeometies()[0];
        Rect narrowBounds = glyph.Bounds;

        // A single-codepoint re-measure reuses slot 0 in place; the outline is now the much wider 'W'.
        text.Text = "W";
        Geometry.Resource reused = text.ToGeometies()[0];
        Assert.That(reused, Is.SameAs(glyph),
            "single-codepoint re-measure should reuse the slot in place (the stale-cache precondition).");

        Rect wideBounds = reused.Bounds;

        // Ground truth: a freshly built FormattedText for 'W' has no stale cache to confuse it.
        using FormattedText reference = CreateText("W");
        Rect referenceBounds = reference.ToGeometies()[0].Bounds;

        Assert.That(wideBounds.Width, Is.GreaterThan(narrowBounds.Width),
            "after re-measure the cached path must reflect the wider 'W', not the stale 'I'.");
        Assert.That(wideBounds.Width, Is.EqualTo(referenceBounds.Width).Within(0.01),
            "the reused slot's cached path must match a freshly measured 'W'.");
        Assert.That(wideBounds.Height, Is.EqualTo(referenceBounds.Height).Within(0.01),
            "the reused slot's cached path must match a freshly measured 'W'.");
    }

    // The stroke-path cache shares the same Version gate; invalidation must clear it too.
    [Test]
    public void ReMeasure_ReusingGlyphSlot_RebuildsCachedStrokePath()
    {
        using FormattedText text = CreateText("I");
        using Pen.Resource pen = new Pen
        {
            Thickness = { CurrentValue = 4f },
            Brush = { CurrentValue = Brushes.White }
        }.ToResource(CompositionContext.Default);

        Geometry.Resource glyph = text.ToGeometies()[0];
        Rect narrowStroke = glyph.GetRenderBounds(pen);

        text.Text = "W";
        Geometry.Resource reused = text.ToGeometies()[0];
        Assert.That(reused, Is.SameAs(glyph),
            "single-codepoint re-measure should reuse the slot in place (the stale-cache precondition).");

        Rect wideStroke = reused.GetRenderBounds(pen);

        using FormattedText reference = CreateText("W");
        Rect referenceStroke = reference.ToGeometies()[0].GetRenderBounds(pen);

        Assert.That(wideStroke.Width, Is.GreaterThan(narrowStroke.Width),
            "the stroked render bounds must follow the new glyph, not the stale cached stroke path.");
        Assert.That(wideStroke.Width, Is.EqualTo(referenceStroke.Width).Within(0.01));
    }

    // Regression at the render-node layer. GeometryRenderNode diffs on a captured (resource, Version)
    // snapshot (ResourceExtension.Compare), and RenderNodeCache only resets a node's cache-eligibility
    // counter — and ultimately invalidates its rasterized tile — when the node reports HasChanges. A slot
    // reuse keeps the same resource reference, so unless the in-place mutation bumps Version the render node
    // sees "unchanged", keeps its cached tile, and the previous glyph stays on screen (the SplitByCharacters
    // TextBlock path). Asserting at the Geometry.Resource level alone does not cover this.
    [Test]
    public void ReMeasure_ReusingGlyphSlot_MarksGeometryRenderNodeChanged()
    {
        using FormattedText text = CreateText("I");
        Geometry.Resource glyph = text.ToGeometies()[0];

        using var node = new GeometryRenderNode(glyph, null, null);
        Assert.That(node.Update(glyph, null, null), Is.False,
            "an unchanged geometry must not mark the render node dirty (baseline).");

        text.Text = "W";
        Geometry.Resource reused = text.ToGeometies()[0];
        Assert.That(reused, Is.SameAs(glyph),
            "single-codepoint re-measure should reuse the slot in place (the stale-cache precondition).");

        Assert.That(node.Update(reused, null, null), Is.True,
            "slot reuse must mark the render node changed so its rasterized cache tile is invalidated.");
        Assert.That(node.HasChanges, Is.True);
    }
}
