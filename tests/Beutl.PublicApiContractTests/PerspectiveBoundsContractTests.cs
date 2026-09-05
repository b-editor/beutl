using System.Reflection;

using Beutl.Graphics;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class PerspectiveBoundsContractTests
{
    // A 124x58 rectangle centred in a 256x144 frame, carrying a divisor that reaches zero across it.
    private static readonly Rect s_local = new(0, 0, 124, 58);

    private static Matrix Crossing(float persX) =>
        Matrix.CreateTranslation(-62, -29)
        * new Matrix(1, 0, persX, 0, 1, 0, 0, 0, 1)
        * Matrix.CreateTranslation(128, 72);

    [Test]
    public void OnlyTheCameraPlaneAwareBoundsAreReachable()
    {
        MethodInfo[] published = typeof(Rect)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.Name.EndsWith("AABB", StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            published.Select(static method => method.Name),
            Is.EquivalentTo(new[]
            {
                nameof(Rect.TransformToAABB),
                nameof(Rect.TransformToDeliveredAABB),
            }),
            "the mapped-corner box must not be published alongside the camera-plane aware ones");
        Assert.That(
            published.Single(static method => method.Name == nameof(Rect.TransformToAABB))
                .GetParameters().Select(static p => p.Name),
            Is.EqualTo(new[] { "matrix", "nearPlane" }),
            "the near plane must stay selectable so a caller can opt into Rect.RasterizerNearPlane");
        Assert.That(
            published.Single(static method => method.Name == nameof(Rect.TransformToDeliveredAABB))
                .GetParameters().Select(static p => p.Name),
            Is.EqualTo(new[] { "matrix", "deliveredTo" }),
            "the delivery region is the whole of what makes the exact near plane affordable, so it is "
            + "named rather than defaulted");
    }

    [Test]
    public void TheBoundsContainTheImageOfAPlaneCrossingRectangle()
    {
        Matrix matrix = Crossing(0.05f);
        Rect bounds = s_local.TransformToAABB(matrix);

        int sampled = 0;
        int outside = 0;
        for (float x = 0; x <= s_local.Width; x += 0.5f)
        {
            for (float y = 0; y <= s_local.Height; y += 0.5f)
            {
                var source = new Point(x, y);
                if (matrix.GetTransformDivisor(source) < Rect.DefaultNearPlane) continue;

                sampled++;
                Point image = source.Transform(matrix);
                if (image.X < bounds.Left || image.X > bounds.Right
                    || image.Y < bounds.Top || image.Y > bounds.Bottom)
                {
                    outside++;
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(sampled, Is.GreaterThan(0), "the fixture must straddle the camera plane");
            Assert.That(outside, Is.Zero, "the bounds must contain everything in front of the near plane");
            Assert.That(bounds.Width, Is.GreaterThan(0).And.LessThan(float.PositiveInfinity));
        });
    }

    [Test]
    public void AnAffineTransformIsUnaffectedByTheNearPlane()
    {
        Matrix affine = Matrix.CreateScale(2, 3) * Matrix.CreateTranslation(10, -4);

        Rect atDefault = s_local.TransformToAABB(affine);
        Rect atRasterizer = s_local.TransformToAABB(affine, Rect.RasterizerNearPlane);

        Assert.That(atRasterizer, Is.EqualTo(atDefault));
        Assert.That(atDefault, Is.EqualTo(new Rect(10, -4, 248, 174)));
    }
}
