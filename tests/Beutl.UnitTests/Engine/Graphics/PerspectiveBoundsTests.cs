using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public sealed class PerspectiveBoundsTests
{
    // The shape of transforms.rot3d.depth-0050: a 124x58 drawable centred in a 256x144 frame, whose
    // transform Drawable.GetTransformMatrix collapses into a single matrix.
    private static readonly Rect s_local = new(0, 0, 124, 58);

    // w(x, y) = 1 + persX * (x - 62) over s_local, so the rectangle crosses the plane at |persX| = 1/62.
    private const float StraddleThreshold = 1f / 62f;

    private static Matrix Compose(Matrix inner) =>
        Matrix.CreateTranslation(-62, -29) * inner * Matrix.CreateTranslation(128, 72);

    private static Matrix Perspective(float persX) => new(1, 0, persX, 0, 1, 0, 0, 0, 1);

    [TestCase(0f)]
    [TestCase(0.010f)]
    [TestCase(-0.010f)]
    [TestCase(0.0150f)]
    public void PerspectiveReachingTheCutoffEverywhere_IsBitIdenticalToTheMappedCornerBox(float persX)
    {
        Matrix matrix = Compose(Perspective(persX));
        foreach (Point corner in Corners(s_local))
        {
            Assert.That(
                matrix.GetTransformDivisor(corner),
                Is.GreaterThanOrEqualTo(Rect.DefaultNearPlane),
                "no corner may fall short of the cutoff, or the box is not the mapped-corner one");
        }

        Rect expected = s_local.TransformToMappedCornerAABB(matrix);
        Rect actual = s_local.TransformToAABB(matrix);

        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X));
            Assert.That(actual.Y, Is.EqualTo(expected.Y));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
        });
    }

    [TestCase(0.0160f)]
    [TestCase(0.0161f)]
    public void PerspectiveInFrontOfTheEyeButShortOfTheCutoff_IsStillClippedToIt(float persX)
    {
        // Between the cutoff and the straddle threshold: every corner is in front of the eye, so no
        // singularity lies inside, yet the near edge sits far closer than the cutoff. Mapping that edge
        // is the unbounded box the cutoff exists to refuse.
        Assert.That(MathF.Abs(persX), Is.LessThan(StraddleThreshold));
        Matrix matrix = Compose(Perspective(persX));
        foreach (Point corner in Corners(s_local))
            Assert.That(matrix.GetTransformDivisor(corner), Is.GreaterThan(0));

        float crossing = 62f + ((Rect.DefaultNearPlane - 1f) / persX);
        var reachingTheCutoff = new Rect(crossing, 0, s_local.Right - crossing, s_local.Height);

        Rect actual = s_local.TransformToAABB(matrix);
        Rect expected = reachingTheCutoff.TransformToMappedCornerAABB(matrix);
        Rect unclipped = s_local.TransformToMappedCornerAABB(matrix);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Left, Is.EqualTo(expected.Left).Within(0.05f));
            Assert.That(actual.Right, Is.EqualTo(expected.Right).Within(0.05f));
            Assert.That(actual.Width, Is.LessThan(unclipped.Width / 4f),
                "the fixture must exercise a case where the cutoff changes the answer");

            // What nearPlane documents: a lower cutoff widens the box. While the sign of the divisor
            // decided this case, the two answers were identical whatever cutoff was asked for.
            Assert.That(
                s_local.TransformToAABB(matrix, 0.005f).Width,
                Is.GreaterThan(actual.Width),
                "a cutoff the near edge does reach must keep more of the box");
        });
    }

    [Test]
    public void EntirelyBehindTheCameraPlane_IsStillTheExactMappedCornerBox()
    {
        // No crossing means the mapped-corner box is exact whichever side the rectangle sits on.
        Matrix matrix = Compose(Perspective(0.05f));
        var behind = new Rect(0, 0, 40, 58);
        Assert.That(matrix.GetTransformDivisor(new Point(40, 0)), Is.LessThan(0));

        Assert.That(behind.TransformToAABB(matrix), Is.EqualTo(behind.TransformToMappedCornerAABB(matrix)));
    }

    [Test]
    public void CrossingButNeverReachingTheNearPlane_IsEmpty()
    {
        // Everything in front sits closer than the near plane. The rasterizer still draws it — see
        // PerspectiveNearPlaneResidualTests for what Rect.DefaultNearPlane gives up here.
        Matrix matrix = Compose(Perspective(0.05f));
        var sliver = new Rect(0, 0, 42.5f, 58);
        Assert.That(matrix.GetTransformDivisor(new Point(0, 0)), Is.LessThan(0));
        Assert.That(
            matrix.GetTransformDivisor(new Point(42.5f, 0)),
            Is.GreaterThan(0).And.LessThan(Rect.DefaultNearPlane));

        Assert.That(sliver.TransformToAABB(matrix), Is.EqualTo(Rect.Empty));
    }

    // M33 alone, so the divisor is the same everywhere: the rectangle is wholly in front of the eye and
    // wholly nearer than the cutoff, which is the one thing that separates it from the cases above.
    private static Matrix UniformDivisor(float w) => new(1, 0, 0, 0, 1, 0, 0, 0, w);

    [Test]
    public void WhollyNearerThanTheCutoff_ReachesItNowhereAndIsEmpty()
    {
        Matrix matrix = UniformDivisor(0.01f);

        Assert.Multiple(() =>
        {
            Assert.That(matrix.ContainsPerspective(), Is.True);
            foreach (Point corner in Corners(s_local))
            {
                Assert.That(
                    matrix.GetTransformDivisor(corner),
                    Is.GreaterThan(0).And.LessThan(Rect.DefaultNearPlane),
                    "the fixture must put every corner in front of the eye and short of the cutoff");
            }

            Assert.That(s_local.TransformToAABB(matrix), Is.EqualTo(Rect.Empty));
        });
    }

    [Test]
    public void WhollyBeyondTheCutoff_IsStillTheMappedCornerBox()
    {
        Matrix matrix = UniformDivisor(0.5f);

        Assert.Multiple(() =>
        {
            foreach (Point corner in Corners(s_local))
                Assert.That(matrix.GetTransformDivisor(corner), Is.GreaterThan(Rect.DefaultNearPlane));

            Assert.That(
                s_local.TransformToAABB(matrix),
                Is.EqualTo(s_local.TransformToMappedCornerAABB(matrix)),
                "a rectangle the cutoff never touches keeps its exact box");
        });
    }

    [Test]
    public void TheCutoffAndNotTheSign_DecidesAWhollyPositiveRectangle()
    {
        Matrix matrix = UniformDivisor(0.01f);

        Assert.Multiple(() =>
        {
            Assert.That(
                s_local.TransformToAABB(matrix, 0.02f),
                Is.EqualTo(Rect.Empty),
                "a cutoff above the divisor reaches nothing");
            Assert.That(
                s_local.TransformToAABB(matrix, 0.005f),
                Is.EqualTo(s_local.TransformToMappedCornerAABB(matrix)),
                "the same rectangle under a cutoff below the divisor keeps its whole box");
        });
    }

    private static IEnumerable<Point> Corners(Rect rect)
    {
        yield return rect.TopLeft;
        yield return rect.TopRight;
        yield return rect.BottomRight;
        yield return rect.BottomLeft;
    }

    [TestCase(0.0163f)]
    [TestCase(0.0170f)]
    [TestCase(0.0250f)]
    [TestCase(0.0500f)]
    [TestCase(-0.0170f)]
    [TestCase(-0.0250f)]
    public void CrossingPerspective_ContainsTheFrontHalfThatTheMappedCornerBoxMisses(float persX)
    {
        Assert.That(MathF.Abs(persX), Is.GreaterThan(StraddleThreshold));
        Matrix matrix = Compose(Perspective(persX));

        Rect clipped = s_local.TransformToAABB(matrix);
        Rect mappedCorners = s_local.TransformToMappedCornerAABB(matrix);

        int outsideClipped = 0;
        int outsideMappedCorners = 0;
        foreach (Point sample in SampleFrontHalf(matrix))
        {
            Point image = sample.Transform(matrix);
            if (!Contains(clipped, image)) outsideClipped++;
            if (!Contains(mappedCorners, image)) outsideMappedCorners++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(clipped.Width, Is.GreaterThan(0).And.LessThan(float.PositiveInfinity));
            Assert.That(clipped.Height, Is.GreaterThan(0).And.LessThan(float.PositiveInfinity));
            Assert.That(outsideClipped, Is.Zero,
                "the clipped box must contain everything in front of the near plane");
            Assert.That(outsideMappedCorners, Is.GreaterThan(0),
                "the fixture must exercise a case the mapped-corner box gets wrong");
        });
    }

    [Test]
    public void Rotation3DAtDepth50_BoundsTheWedgeAtItsOnlyFiniteExtremity()
    {
        // Rotation3DTransform(0, 60, 0) at Depth 50 over a 120-wide drawable: z reaches 51.96 > 50.
        Matrix matrix = Compose(new Matrix(
            MathF.Cos(MathF.PI / 3f), 0, MathF.Sin(MathF.PI / 3f) / 50f,
            0, 1, 0,
            0, 0, 1));

        Rect clipped = s_local.TransformToAABB(matrix);

        Assert.Multiple(() =>
        {
            Assert.That(clipped.Right, Is.EqualTo(142.9479f).Within(0.001f),
                "the image is a left-opening wedge whose only finite extremity is its right edge");
            Assert.That(clipped.Left, Is.LessThan(0),
                "the wedge opens left, so the box must reach past the frame origin");
            Assert.That(s_local.TransformToMappedCornerAABB(matrix).Left, Is.EqualTo(142.9479f).Within(0.001f),
                "the mapped-corner box puts its LEFT edge where the image ends");
        });
    }

    [TestCase(0f)]
    [TestCase(-0.05f)]
    public void NonPositiveNearPlane_IsRejected(float nearPlane)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => s_local.TransformToAABB(Compose(Perspective(0.05f)), nearPlane));
    }

    [Test]
    public void NonFiniteNearPlane_IsRejectedWhateverTheMatrixHolds()
    {
        // The float.NaN constant carries the sign bit, so a sign test happens to reject that one value;
        // a NaN arithmetic produces need not, and neither it nor infinity is ever reached by a divisor.
        float unsignedNaN = BitConverter.UInt32BitsToSingle(0x7FC00000u);
        Matrix crossing = Compose(Perspective(0.05f));
        Matrix nonCrossing = Compose(Perspective(0.010f));
        Matrix affine = Matrix.CreateScale(2, 2) * Matrix.CreateTranslation(10, 10);

        Assert.Multiple(() =>
        {
            Assert.That(float.IsNegative(unsignedNaN), Is.False,
                "the fixture must be a NaN that a sign test lets through");

            foreach (Matrix matrix in new[] { crossing, nonCrossing, affine })
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => s_local.TransformToAABB(matrix, float.PositiveInfinity));
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => s_local.TransformToAABB(matrix, unsignedNaN));
            }
        });
    }

    // Only what DefaultNearPlane promises to cover, which is less than the rasterizer draws.
    // PerspectiveNearPlaneResidualTests samples down to Rect.RasterizerNearPlane and pins the difference.
    private static IEnumerable<Point> SampleFrontHalf(Matrix matrix)
    {
        for (int i = 0; i <= 200; i++)
        {
            for (int j = 0; j <= 40; j++)
            {
                var p = new Point(
                    s_local.Width * i / 200f,
                    s_local.Height * j / 40f);
                if (matrix.GetTransformDivisor(p) >= Rect.DefaultNearPlane)
                    yield return p;
            }
        }
    }

    private static bool Contains(Rect rect, Point p)
        => p.X >= rect.Left - 1e-3f && p.X <= rect.Right + 1e-3f
                                    && p.Y >= rect.Top - 1e-3f && p.Y <= rect.Bottom + 1e-3f;
}
