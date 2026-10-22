using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A non-target TransformEffect after a symmetric blur must rotate about the blurred bounds' own centre, so the
// alpha centroid stays on the content centre; a stale chain anchor moves it.
[NonParallelizable]
[TestFixture]
public class TransformEffectFrameAnchorTests
{
    private const float ContentWidth = 160f;
    private const float ContentHeight = 104f;
    private static readonly PixelSize s_frame = new(320, 240);

    [TestCase(3f, 3f, 1f)]
    [TestCase(6f, 0f, 1f)]
    [TestCase(0f, 6f, 1f)]
    [TestCase(3f, 3f, 2f)]
    public void NoTargetTransform_AfterBlur_RotatesAboutItsOwnBoundsCentre(
        float sigmaX,
        float sigmaY,
        float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                MakeBlurTransformBrightnessChain(sigmaX, sigmaY),
                s_frame,
                scale,
                clearColor: Colors.Transparent);

            (double x, double y) = AlphaCentroid(rendered, scale);
            TestContext.WriteLine(
                $"sigma=({sigmaX},{sigmaY}) s={scale}: alpha centroid=({x:F4},{y:F4})");
            Assert.Multiple(() =>
            {
                Assert.That(x, Is.EqualTo(s_frame.Width / 2.0).Within(0.02),
                    "A rotation about the blurred bounds' own centre must not move the alpha centroid horizontally.");
                Assert.That(y, Is.EqualTo(s_frame.Height / 2.0).Within(0.02),
                    "A rotation about the blurred bounds' own centre must not move the alpha centroid vertically.");
            });
        });
    }

    // A split hands the transform several targets, each anchored in its own local frame. One shared
    // matrix still has to rotate them about one shared origin, so the centrally symmetric tile
    // arrangement must keep its centroid where the unsplit content had it.
    [TestCase(1f)]
    [TestCase(2f)]
    public void NoTargetTransform_AfterSplit_RotatesEveryTileAboutTheSharedOrigin(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(
                MakeSplitTransformChain(),
                s_frame,
                scale,
                clearColor: Colors.Transparent);

            (double x, double y) = AlphaCentroid(rendered, scale);
            TestContext.WriteLine($"split s={scale}: alpha centroid=({x:F4},{y:F4})");
            Assert.Multiple(() =>
            {
                Assert.That(x, Is.EqualTo(s_frame.Width / 2.0).Within(0.02),
                    "Every split tile must rotate about the same origin horizontally.");
                Assert.That(y, Is.EqualTo(s_frame.Height / 2.0).Within(0.02),
                    "Every split tile must rotate about the same origin vertically.");
            });
        });
    }

    private static Drawable.Resource MakeSplitTransformChain()
    {
        var shape = MakeContentShape();
        var split = new SplitEffect
        {
            HorizontalDivisions = { Value = 2 },
            VerticalDivisions = { Value = 2 },
            HorizontalSpacing = { Value = 10f },
            VerticalSpacing = { Value = 10f },
        };
        var group = new FilterEffectGroup();
        group.Children.Add(split);
        group.Children.Add(MakeNoTargetRotation());
        shape.FilterEffect.Value = group;
        return shape.ToResource(CompositionContext.Default);
    }

    private static RectShape MakeContentShape()
        => new()
        {
            AlignmentX = { Value = AlignmentX.Center },
            AlignmentY = { Value = AlignmentY.Center },
            Width = { Value = ContentWidth },
            Height = { Value = ContentHeight },
            Fill = { Value = Brushes.OrangeRed },
        };

    private static TransformEffect MakeNoTargetRotation()
        => new()
        {
            ApplyToTarget = { Value = false },
            TransformOrigin = { Value = RelativePoint.Center },
            Transform = { Value = new RotationTransform(25f) },
        };

    private static Drawable.Resource MakeBlurTransformBrightnessChain(float sigmaX, float sigmaY)
    {
        var shape = MakeContentShape();
        var group = new FilterEffectGroup();
        group.Children.Add(new Blur { Sigma = { Value = new Size(sigmaX, sigmaY) } });
        group.Children.Add(MakeNoTargetRotation());
        group.Children.Add(new Brightness { Amount = { Value = 150f } });
        shape.FilterEffect.Value = group;
        return shape.ToResource(CompositionContext.Default);
    }

    private static (double X, double Y) AlphaCentroid(Bitmap bitmap, float scale)
    {
        double mass = 0;
        double momentX = 0;
        double momentY = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                double alpha = (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
                if (!(alpha > 0))
                    continue;
                mass += alpha;
                momentX += alpha * (x + 0.5);
                momentY += alpha * (y + 0.5);
            }
        }

        Assert.That(mass, Is.GreaterThan(0), "the chain rendered nothing");
        return (momentX / mass / scale, momentY / mass / scale);
    }
}
