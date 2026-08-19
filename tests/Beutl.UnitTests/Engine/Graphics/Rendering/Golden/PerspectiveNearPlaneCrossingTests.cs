using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class PerspectiveNearPlaneCrossingTests
{
    private static readonly PixelSize s_frame = new(256, 144);

    // At the default Depth of 500 any layer wider than the frame goes past the camera plane at ~31 degrees
    // of Y rotation, so this is the shape the defect takes in an ordinary project.
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DefaultDepth_WideLayerRotatedPastTheCameraPlane_MatchesTheAnalyticImage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const float Width = 1200f;
            const float Height = 54f;
            using Drawable.Resource straddling = CreateRotatedRect(Width, Height, 60f, depth: 500f)
                .ToResource(CompositionContext.Default);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                straddling, s_frame, 1f, clearColor: Colors.Transparent);

            Matrix matrix = ComposeCenteredRotation(Width, Height, 60f, depth: 500f);
            AnalyticAgreement agreement = CompareWithAnalyticImage(bitmap, matrix, Width, Height);
            TestContext.WriteLine(
                $"[wdefault 1200x54 @60deg depth500] rendered={CountCoveredPixels(bitmap)} "
                + $"analytic={agreement.AnalyticCovered} missing={agreement.MissingFromRender} "
                + $"agreement={agreement.Ratio:P3}");
            Assert.Multiple(() =>
            {
                Assert.That(agreement.AnalyticCovered, Is.GreaterThan(8000),
                    "the fixture must put a substantial wedge inside the frame");
                Assert.That(agreement.MissingFromRender, Is.LessThan(agreement.AnalyticCovered / 100),
                    "the render dropped part of the front half of a camera-plane-crossing layer");
                Assert.That(agreement.Ratio, Is.GreaterThan(0.99),
                    "the render must reproduce the near-plane-clipped image, not a mirrored one");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void NestedGroupTranslate_StraddlingChild_KeepsContentAtTheFrameOrigin()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new DrawableGroup();
            group.Children.Add(CreateRotatedRect(120, 54, 60f, depth: 50f));
            group.Transform.CurrentValue = new TranslateTransform(60f, 0f);
            using Drawable.Resource nested = group.ToResource(CompositionContext.Default);
            using Bitmap bitmap = GoldenImageHarness.RenderAtScale(
                nested, s_frame, 1f, clearColor: Colors.Transparent);
            int covered = CountCoveredPixels(bitmap);
            int leftmost = LeftmostCoveredColumn(bitmap);
            TestContext.WriteLine($"[nested group-t60 depth50] covered={covered} leftmost={leftmost}");
            Assert.Multiple(() =>
            {
                Assert.That(covered, Is.GreaterThan(8000),
                    "the straddling child must survive the group's own transform scope");
                Assert.That(leftmost, Is.Zero,
                    "a root-space bound would clip the wedge at the group's translate, not at the frame edge");
            });
        });
    }

    private static RectShape CreateRotatedRect(float width, float height, float rotationY, float depth)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.Fill.CurrentValue = Brushes.White;
        shape.Transform.CurrentValue = new Rotation3DTransform(0f, rotationY, 0f, 0f, 0f, 0f)
        {
            Depth = { CurrentValue = depth },
        };
        return shape;
    }

    /// <summary>
    /// Rebuilds the matrix <see cref="Drawable.GetTransformMatrix"/> collapses a centre-aligned
    /// <see cref="Rotation3DTransform"/> into, so the expected image is derived independently of the
    /// renderer rather than recorded from it.
    /// </summary>
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
    /// Back-projects every device pixel centre through <paramref name="matrix"/>, keeps it only when it
    /// recovers a positive homogeneous divisor (in front of the camera plane) and lands inside the local
    /// rectangle, and compares that mask against the render.
    /// </summary>
    private static AnalyticAgreement CompareWithAnalyticImage(
        Bitmap bitmap, Matrix matrix, float width, float height)
    {
        Matrix inverse = matrix.Invert();
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int analyticCovered = 0;
        int missing = 0;
        int agreeing = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var device = new Point(x + 0.5f, y + 0.5f);
                Point local = device.Transform(inverse);
                float recoveredDivisor =
                    (device.X * inverse.M13) + (device.Y * inverse.M23) + inverse.M33;
                bool expected = recoveredDivisor > 0
                                && local.X >= 0 && local.X <= width
                                && local.Y >= 0 && local.Y <= height;
                float alpha = (float)BitConverter.UInt16BitsToHalf(
                    pixels[(((y * bitmap.Width) + x) * 4) + 3]);
                bool actual = alpha > 0.5f;
                if (expected) analyticCovered++;
                if (expected && !actual) missing++;
                if (expected == actual) agreeing++;
            }
        }

        return new AnalyticAgreement(
            analyticCovered, missing, (double)agreeing / (bitmap.Width * bitmap.Height));
    }

    private static int CountCoveredPixels(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int count = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if ((float)BitConverter.UInt16BitsToHalf(pixels[i]) > 0.5f)
                count++;
        }

        return count;
    }

    private static int LeftmostCoveredColumn(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int leftmost = bitmap.Width;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < leftmost; x++)
            {
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[(((y * bitmap.Width) + x) * 4) + 3]);
                if (alpha > 0.5f)
                {
                    leftmost = x;
                    break;
                }
            }
        }

        return leftmost;
    }

    private readonly record struct AnalyticAgreement(int AnalyticCovered, int MissingFromRender, double Ratio);
}
