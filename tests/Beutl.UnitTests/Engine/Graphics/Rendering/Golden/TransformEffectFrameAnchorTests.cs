using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// A TransformEffect with ApplyToTarget=false is the only in-tree filter whose SKImageFilter is not
// translation-invariant: it conjugates the transform by an origin derived from the input bounds. The
// Skia chain executes in a frame anchored at the pending chain's InputBounds, so that anchor has to
// track the bounds the item maps; a stale anchor rotates the content about a point displaced by
// Bounds.Position - OriginalBounds.Position, which a preceding bounds-expanding pass makes non-zero.
//
// The invariant needs no golden baseline: a Gaussian blur is symmetric, so it leaves the input bounds
// centred where they were, and a rotation about the centre of those bounds cannot move the alpha
// centroid. The centroid must stay on the content centre for every sigma.
[NonParallelizable]
[TestFixture]
[Category("GpuPassFusionGpu")]
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
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 2 },
            HorizontalSpacing = { CurrentValue = 10f },
            VerticalSpacing = { CurrentValue = 10f },
        };
        var group = new FilterEffectGroup();
        group.Children.Add(split);
        group.Children.Add(MakeNoTargetRotation());
        shape.FilterEffect.CurrentValue = group;
        return shape.ToResource(CompositionContext.Default);
    }

    private static RectShape MakeContentShape()
        => new()
        {
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
            Width = { CurrentValue = ContentWidth },
            Height = { CurrentValue = ContentHeight },
            Fill = { CurrentValue = Brushes.OrangeRed },
        };

    private static TransformEffect MakeNoTargetRotation()
        => new()
        {
            ApplyToTarget = { CurrentValue = false },
            TransformOrigin = { CurrentValue = RelativePoint.Center },
            Transform = { CurrentValue = new RotationTransform(25f) },
        };

    private static Drawable.Resource MakeBlurTransformBrightnessChain(float sigmaX, float sigmaY)
    {
        var shape = MakeContentShape();
        var group = new FilterEffectGroup();
        group.Children.Add(new Blur { Sigma = { CurrentValue = new Size(sigmaX, sigmaY) } });
        group.Children.Add(MakeNoTargetRotation());
        group.Children.Add(new Brightness { Amount = { CurrentValue = 150f } });
        shape.FilterEffect.CurrentValue = group;
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
