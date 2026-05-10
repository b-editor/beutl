using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

public class TileBrushCalculatorTests
{
    private static readonly RelativeRect FullRect = new(0, 0, 1, 1, RelativeUnit.Relative);

    [Test]
    public void Constructor_TileNone_AddsDestinationTranslation()
    {
        var calc = new TileBrushCalculator(
            TileMode.None,
            Stretch.Fill,
            AlignmentX.Left,
            AlignmentY.Top,
            FullRect,
            FullRect,
            new Size(100, 100),
            new Size(200, 200));

        Assert.Multiple(() =>
        {
            Assert.That(calc.IntermediateSize, Is.EqualTo(new Size(200, 200)));
            // TileMode.None の場合、宛先位置への translate が追加され、Identity ではない
            Assert.That(calc.IntermediateTransform, Is.Not.EqualTo(Matrix.Identity));
        });
    }

    [Test]
    public void Constructor_TileMode_DrawRectIsAtOrigin()
    {
        var calc = new TileBrushCalculator(
            TileMode.Tile,
            Stretch.None,
            AlignmentX.Left,
            AlignmentY.Top,
            FullRect,
            FullRect,
            new Size(50, 50),
            new Size(200, 200));

        Assert.Multiple(() =>
        {
            // タイルモードでは drawRect は (0,0) を起点にする
            Assert.That(calc.IntermediateClip.Position, Is.EqualTo(new Point(0, 0)));
            Assert.That(calc.IntermediateSize, Is.EqualTo(calc.DestinationRect.Size));
        });
    }

    [Test]
    public void NeedsIntermediate_SameAspectAndSize_ReturnsFalse()
    {
        var calc = new TileBrushCalculator(
            TileMode.None,
            Stretch.Fill,
            AlignmentX.Left,
            AlignmentY.Top,
            FullRect,
            FullRect,
            new Size(100, 100),
            new Size(100, 100));

        Assert.That(calc.NeedsIntermediate, Is.False);
    }

    [Test]
    public void CalculateTranslate_LeftTop_ReturnsZero()
    {
        Vector v = TileBrushCalculator.CalculateTranslate(
            AlignmentX.Left,
            AlignmentY.Top,
            sourceRect: new Rect(0, 0, 100, 100),
            destinationRect: new Rect(0, 0, 200, 200),
            scale: new Vector(1f, 1f));

        Assert.That(v, Is.EqualTo(new Vector(0f, 0f)));
    }

    [Test]
    public void CalculateTranslate_CenterCenter_CentersInDestination()
    {
        Vector v = TileBrushCalculator.CalculateTranslate(
            AlignmentX.Center,
            AlignmentY.Center,
            sourceRect: new Rect(0, 0, 100, 100),
            destinationRect: new Rect(0, 0, 200, 200),
            scale: new Vector(1f, 1f));

        Assert.That(v, Is.EqualTo(new Vector(50f, 50f)));
    }

    [Test]
    public void CalculateTranslate_RightBottom_AlignsToFarEdges()
    {
        Vector v = TileBrushCalculator.CalculateTranslate(
            AlignmentX.Right,
            AlignmentY.Bottom,
            sourceRect: new Rect(0, 0, 100, 100),
            destinationRect: new Rect(0, 0, 200, 200),
            scale: new Vector(1f, 1f));

        Assert.That(v, Is.EqualTo(new Vector(100f, 100f)));
    }

    [Test]
    public void CalculateIntermediateTransform_TileMode_DrawRectIsSizeOnly()
    {
        Matrix _ = TileBrushCalculator.CalculateIntermediateTransform(
            TileMode.Tile,
            sourceRect: new Rect(0, 0, 50, 50),
            destinationRect: new Rect(20, 20, 100, 100),
            scale: new Vector(1f, 1f),
            translate: new Vector(0f, 0f),
            out Rect drawRect);

        Assert.Multiple(() =>
        {
            Assert.That(drawRect.Position, Is.EqualTo(new Point(0, 0)));
            Assert.That(drawRect.Size, Is.EqualTo(new Size(100, 100)));
        });
    }

    [Test]
    public void CalculateIntermediateTransform_TileNone_DrawRectIsDestination()
    {
        Matrix _ = TileBrushCalculator.CalculateIntermediateTransform(
            TileMode.None,
            sourceRect: new Rect(0, 0, 50, 50),
            destinationRect: new Rect(20, 30, 100, 100),
            scale: new Vector(1f, 1f),
            translate: new Vector(0f, 0f),
            out Rect drawRect);

        Assert.That(drawRect, Is.EqualTo(new Rect(20, 30, 100, 100)));
    }
}
