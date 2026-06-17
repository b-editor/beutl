using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

public class FormattedTextDeviceScaleTests
{
    [Test]
    public void GetTextBlob_WithDeviceDensity_ShapesAtScaledFontSize()
    {
        FormattedText text = CreateText();
        Rect logicalLayoutBounds = text.Bounds;

        SKRect logicalBounds = text.GetTextBlob().Bounds;
        SKRect deviceBounds = text.GetTextBlob(2f).Bounds;

        Assert.That(deviceBounds.Width, Is.GreaterThan(logicalBounds.Width * 1.5f));
        Assert.That(deviceBounds.Height, Is.GreaterThan(logicalBounds.Height * 1.5f));
        Assert.That(text.Bounds, Is.EqualTo(logicalLayoutBounds));
    }

    [Test]
    public void GetStrokePath_WithDeviceDensity_KeepsLogicalPathUnscaled()
    {
        FormattedText text = CreateText();
        text.Pen = new Pen
        {
            Thickness = { CurrentValue = 2f },
            Brush = { CurrentValue = Brushes.White }
        }.ToResource(CompositionContext.Default);
        Rect logicalActualBounds = text.ActualBounds;

        SKPath logicalStroke = text.GetStrokePath()!;
        SKPath deviceStroke = text.GetStrokePath(2f)!;

        Assert.That(deviceStroke.Bounds.Width, Is.GreaterThan(logicalStroke.Bounds.Width * 1.5f));
        Assert.That(deviceStroke.Bounds.Height, Is.GreaterThan(logicalStroke.Bounds.Height * 1.5f));
        Assert.That(text.ActualBounds, Is.EqualTo(logicalActualBounds));
    }

    private static FormattedText CreateText()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        return new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = 24f,
            Spacing = 1f,
            Text = "Scale"
        };
    }
}
