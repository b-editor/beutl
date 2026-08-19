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
    [TestCase(0.0160f)]
    [TestCase(0.0161f)]
    public void NonCrossingPerspective_IsBitIdenticalToTheMappedCornerBox(float persX)
    {
        Matrix matrix = Compose(Perspective(persX));
        Assert.That(matrix.GetTransformDivisor(s_local.TopLeft), Is.GreaterThan(0));

        Rect expected = s_local.TransformToAABB(matrix);
        Rect actual = s_local.TransformToClippedAABB(matrix);

        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X));
            Assert.That(actual.Y, Is.EqualTo(expected.Y));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
        });
    }

    [Test]
    public void EntirelyBehindTheCameraPlane_IsStillTheExactMappedCornerBox()
    {
        // No crossing means the mapped-corner box is exact whichever side the rectangle sits on.
        Matrix matrix = Compose(Perspective(0.05f));
        var behind = new Rect(0, 0, 40, 58);
        Assert.That(matrix.GetTransformDivisor(new Point(40, 0)), Is.LessThan(0));

        Assert.That(behind.TransformToClippedAABB(matrix), Is.EqualTo(behind.TransformToAABB(matrix)));
    }

    [Test]
    public void CrossingButNeverReachingTheNearPlane_IsEmpty()
    {
        // Everything in front sits closer than the near plane, so the rasterizer draws none of it.
        Matrix matrix = Compose(Perspective(0.05f));
        var sliver = new Rect(0, 0, 42.5f, 58);
        Assert.That(matrix.GetTransformDivisor(new Point(0, 0)), Is.LessThan(0));
        Assert.That(
            matrix.GetTransformDivisor(new Point(42.5f, 0)),
            Is.GreaterThan(0).And.LessThan(Rect.DefaultNearPlane));

        Assert.That(sliver.TransformToClippedAABB(matrix), Is.EqualTo(Rect.Empty));
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

        Rect clipped = s_local.TransformToClippedAABB(matrix);
        Rect mappedCorners = s_local.TransformToAABB(matrix);

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

        Rect clipped = s_local.TransformToClippedAABB(matrix);

        Assert.Multiple(() =>
        {
            Assert.That(clipped.Right, Is.EqualTo(142.9479f).Within(0.001f),
                "the image is a left-opening wedge whose only finite extremity is its right edge");
            Assert.That(clipped.Left, Is.LessThan(0),
                "the wedge opens left, so the box must reach past the frame origin");
            Assert.That(s_local.TransformToAABB(matrix).Left, Is.EqualTo(142.9479f).Within(0.001f),
                "the mapped-corner box puts its LEFT edge where the image ends");
        });
    }

    [TestCase(0f)]
    [TestCase(-0.05f)]
    public void NonPositiveNearPlane_IsRejected(float nearPlane)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => s_local.TransformToClippedAABB(Compose(Perspective(0.05f)), nearPlane));
    }

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
