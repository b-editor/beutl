using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

public class TileBrushCalculatorTests
{
    private static readonly RelativeRect FullRect = new(0, 0, 1, 1, RelativeUnit.Relative);

    [Test]
    public void Constructor_TileNone_AddsDestinationTranslation()
    {
        // 宛先矩形を targetSize に対する非ゼロ位置に置き、TileMode.None で
        // IntermediateTransform に destinationRect.Position への translate が
        // 実際に積まれていることを translation 成分で直接検証する。
        var destinationRel = new RelativeRect(0.25f, 0.5f, 0.5f, 0.5f, RelativeUnit.Relative);
        var calc = new TileBrushCalculator(
            TileMode.None,
            Stretch.Fill,
            AlignmentX.Left,
            AlignmentY.Top,
            FullRect,
            destinationRel,
            new Size(100, 100),
            new Size(200, 200));

        // destinationRel.ToPixels(target) = (50, 100, 100, 100)
        Assert.Multiple(() =>
        {
            Assert.That(calc.DestinationRect, Is.EqualTo(new Rect(50, 100, 100, 100)));
            Assert.That(calc.IntermediateSize, Is.EqualTo(new Size(200, 200)));
            // source/destination が同サイズかつ scale=1, translate=0 なので、
            // 最終 transform はちょうど destination 位置への translate になる
            Assert.That(calc.IntermediateTransform.M31, Is.EqualTo(50f).Within(1e-5));
            Assert.That(calc.IntermediateTransform.M32, Is.EqualTo(100f).Within(1e-5));
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
        _ = TileBrushCalculator.CalculateIntermediateTransform(
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
        _ = TileBrushCalculator.CalculateIntermediateTransform(
            TileMode.None,
            sourceRect: new Rect(0, 0, 50, 50),
            destinationRect: new Rect(20, 30, 100, 100),
            scale: new Vector(1f, 1f),
            translate: new Vector(0f, 0f),
            out Rect drawRect);

        Assert.That(drawRect, Is.EqualTo(new Rect(20, 30, 100, 100)));
    }
}
