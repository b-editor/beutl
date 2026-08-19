using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

/// <summary>
/// Pins the content <see cref="Rect.DefaultNearPlane"/> gives up. The default clips 820x in front of
/// <see cref="Rect.RasterizerNearPlane"/>, so a near-edge-on layer declares bounds that exclude pixels
/// Skia still draws — and the planner turns those bounds into a hard raster clip. These cases are the
/// documented limitation, not the desired behaviour: a change that closes the gap must make them fail.
/// </summary>
[TestFixture]
public sealed class PerspectiveNearPlaneResidualTests
{
    private static readonly PixelSize s_frame = new(256, 144);

    [TestCase(1200f, 54f, 60.0f, 500f, 13824, 0)]
    [TestCase(1200f, 54f, 89.5f, 500f, 18188, 6480)]
    [TestCase(1200f, 54f, 89.8f, 500f, 18340, 13680)]
    [TestCase(124f, 58f, 60.0f, 10f, 18232, 2592)]
    public void TheDeclaredBounds_ExcludeFramePixelsTheRasterizerStillDraws(
        float width, float height, float rotationY, float depth, int expectedDrawn, int expectedExcluded)
    {
        Matrix matrix = ComposeCenteredRotation(width, height, rotationY, depth);
        var local = new Rect(0, 0, width, height);
        Rect declared = local.TransformToClippedAABB(matrix);
        Rect rasterizerExact = local.TransformToClippedAABB(matrix, Rect.RasterizerNearPlane);

        int drawn = 0;
        int outsideDeclared = 0;
        int outsideRasterizerExact = 0;
        foreach (Point pixel in DrawnFramePixels(matrix, local))
        {
            drawn++;
            if (!Covers(declared, pixel)) outsideDeclared++;
            if (!Covers(rasterizerExact, pixel)) outsideRasterizerExact++;
        }

        TestContext.WriteLine(
            $"[{width}x{height} @{rotationY}deg depth{depth}] drawn={drawn} outsideDeclared={outsideDeclared} "
            + $"outsideExact={outsideRasterizerExact} declared={declared} exactWidth={rasterizerExact.Width}");

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.EqualTo(expectedDrawn),
                "the fixture must put the rasterizer's own image inside the frame");
            Assert.That(outsideDeclared, Is.EqualTo(expectedExcluded),
                "Rect.DefaultNearPlane's documented residual loss changed");
            Assert.That(outsideRasterizerExact, Is.Zero,
                "the loss is the default's alone: the rasterizer's own near plane bounds every drawn pixel");
        });
    }

    [TestCase(1200f, 54f, 60.0f, 500f)]
    [TestCase(1200f, 54f, 89.5f, 500f)]
    [TestCase(1200f, 54f, 89.8f, 500f)]
    [TestCase(124f, 58f, 60.0f, 10f)]
    public void TheDeclaredFarEdge_SitsWhereTheDocumentedFormulaPutsIt(
        float width, float height, float rotationY, float depth)
    {
        Matrix matrix = ComposeCenteredRotation(width, height, rotationY, depth);
        Rect declared = new Rect(0, 0, width, height).TransformToClippedAABB(matrix);

        float radians = MathF.PI * rotationY / 180f;
        float expectedLeft = (s_frame.Width / 2f)
                             - (((1f / Rect.DefaultNearPlane) - 1f) * depth * MathF.Cos(radians)
                                / MathF.Sin(radians));

        Assert.That(declared.Left, Is.EqualTo(expectedLeft).Within(0.05f));
    }

    [Test]
    public void ClippingAtTheRasterizerNearPlane_WouldCollapseTheWorkingScale()
    {
        Matrix matrix = ComposeCenteredRotation(1200f, 54f, 60f, 500f);
        var local = new Rect(0, 0, 1200f, 54f);
        Rect declared = local.TransformToClippedAABB(matrix);
        Rect rasterizerExact = local.TransformToClippedAABB(matrix, Rect.RasterizerNearPlane);

        Rect belowCrossover = local.TransformToClippedAABB(matrix, 0.03f);

        float exactScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(rasterizerExact, 1f);
        float declaredScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(declared, 2f);
        float belowCrossoverScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(belowCrossover, 2f);
        TestContext.WriteLine(
            $"exactWidth={rasterizerExact.Width} exactScale={exactScale} declaredScale={declaredScale} "
            + $"belowCrossoverScale={belowCrossoverScale}");

        Assert.Multiple(() =>
        {
            Assert.That(rasterizerExact.Width, Is.GreaterThan(4_000_000f));
            Assert.That(exactScale, Is.LessThan(0.005f),
                "this is why the default is not the rasterizer's near plane");
            Assert.That(declaredScale, Is.EqualTo(2f),
                "and why it is not lower: 0.05 keeps an ordinary 60 degree flip unclamped at a 2x preview");
            Assert.That(belowCrossoverScale, Is.LessThan(2f),
                "below the ~0.035 crossover the same flip stops fitting the buffer budget at 2x");
        });
    }

    private static Matrix ComposeCenteredRotation(float width, float height, float rotationY, float depth)
    {
        float radians = MathF.PI * rotationY / 180f;
        var rotation = new Matrix(
            MathF.Cos(radians), 0, MathF.Sin(radians) / depth,
            0, 1, 0,
            0, 0, 1);
        return Matrix.CreateTranslation(-width / 2, -height / 2)
               * rotation
               * Matrix.CreateTranslation(s_frame.Width / 2f, s_frame.Height / 2f);
    }

    /// <summary>
    /// The frame pixels Skia covers: back-project every pixel centre and keep it where the forward
    /// divisor it recovers reaches <see cref="Rect.RasterizerNearPlane"/> and it lands inside the local
    /// rectangle. The inverse divisor is the reciprocal of the forward one, so the bound inverts.
    /// </summary>
    private static IEnumerable<Point> DrawnFramePixels(Matrix matrix, Rect local)
    {
        Matrix inverse = matrix.Invert();
        for (int y = 0; y < s_frame.Height; y++)
        {
            for (int x = 0; x < s_frame.Width; x++)
            {
                var pixel = new Point(x + 0.5f, y + 0.5f);
                float inverseDivisor = inverse.GetTransformDivisor(pixel);
                if (inverseDivisor <= 0 || inverseDivisor > 1f / Rect.RasterizerNearPlane)
                    continue;

                Point source = pixel.Transform(inverse);
                if (source.X >= local.Left && source.X <= local.Right
                                           && source.Y >= local.Top && source.Y <= local.Bottom)
                {
                    yield return pixel;
                }
            }
        }
    }

    private static bool Covers(Rect rect, Point p)
        => p.X >= rect.Left && p.X <= rect.Right && p.Y >= rect.Top && p.Y <= rect.Bottom;
}
