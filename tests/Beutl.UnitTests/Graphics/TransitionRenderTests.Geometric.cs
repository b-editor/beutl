using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Graphics;

public partial class TransitionRenderTests
{
    private static readonly Type[] s_geometricTransitions =
    [
        typeof(BlindsTransition), typeof(CheckerboardTransition), typeof(ClockWipeTransition),
        typeof(BoxWipeTransition), typeof(DiamondWipeTransition),
    ];

    [TestCaseSource(nameof(s_geometricTransitions))]
    public void GeometricReveals_PreserveEndpointsAndOpaqueEdges_WhenSeeking(Type type)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var transition = (ClipTransition)Activator.CreateInstance(type)!;
            (var scene, _) = CreateScene(transition, withOutgoing: true);
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);

            // Reuse the resource while moving both forwards and backwards through the transition.
            foreach (double progress in new[] { 0, 0.23, 0.5, 0.77, 1, 0.23 })
            {
                renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
                using Bitmap snapshot = renderer.Snapshot();
                using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                Bgra8888[] pixels = ReadPixels(bitmap).ToArray();
                Assert.That(pixels.Min(p => p.A), Is.GreaterThanOrEqualTo(254), $"opaque at {progress}");
                if (progress == 0)
                    Assert.That(pixels.All(IsRed), Is.True, "starts with only the outgoing clip");
                else if (progress == 1)
                    Assert.That(pixels.All(IsBlue), Is.True, "ends with only the incoming clip, including corners");
                else
                    Assert.That(pixels.Any(IsRed) && pixels.Any(IsBlue), Is.True, $"both sides are visible at {progress}");

                if (Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR") is { Length: > 0 } root)
                {
                    string directory = Path.Combine(root, nameof(TransitionRenderTests));
                    Directory.CreateDirectory(directory);
                    Assert.That(bitmap.Save(Path.Combine(directory, $"{type.Name}-{progress * 100:00}.png")), Is.True);
                }
            }
        });
    }

    [TestCaseSource(nameof(s_geometricTransitions))]
    public void GeometricReveals_PreserveIncomingTransparency_WithOrWithoutAnOutgoingClip(Type type)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            foreach (bool withOutgoing in new[] { false, true })
            {
                var transition = (ClipTransition)Activator.CreateInstance(type)!;
                (var scene, var incoming) = CreateScene(transition, withOutgoing);
                incoming.Fill.Value = new SolidColorBrush(Color.FromArgb(128, 0, 0, 255));
                using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
                renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(0.5)));
                using Bitmap snapshot = renderer.Snapshot();
                using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                Bgra8888[] pixels = ReadPixels(bitmap).ToArray();
                Bgra8888[] revealed = pixels.Where(p => p.B > 240 && p.R < 8).ToArray();
                Assert.That(revealed, Is.Not.Empty);
                Assert.That(revealed.All(p => p.A <= 129), Is.True,
                    "the outgoing side must not remain behind the revealed translucent clip");
                Assert.That(revealed.Any(p => p.A >= 127), Is.True, "fully revealed pixels retain the clip's alpha");
                Assert.That(withOutgoing ? pixels.Any(IsRed) : pixels.Any(p => p.A == 0), Is.True,
                    "outside the reveal, keep the outgoing clip or the empty background");
            }
        });
    }

    [Test]
    public void Blinds_OpenEveryStripInTheChosenDirection([Values] ClipTransitionDirection direction)
    {
        using Bitmap bitmap = Render(new BlindsTransition { Count = { Value = 4 }, Direction = { Value = direction } }, 0.5);
        bool horizontal = direction is ClipTransitionDirection.LeftToRight or ClipTransitionDirection.RightToLeft;
        bool reverse = direction is ClipTransitionDirection.RightToLeft or ClipTransitionDirection.BottomToTop;
        int extent = (horizontal ? Width : Height) / 4;
        for (int i = 0; i < 4; i++)
        {
            int first = i * extent + extent / 4;
            int second = i * extent + extent * 3 / 4;
            Bgra8888 before = horizontal ? Pixel(bitmap, first, Height / 2) : Pixel(bitmap, Width / 2, first);
            Bgra8888 after = horizontal ? Pixel(bitmap, second, Height / 2) : Pixel(bitmap, Width / 2, second);
            Assert.That(IsBlue(reverse ? after : before), Is.True, $"revealed strip {i}");
            Assert.That(IsRed(reverse ? before : after), Is.True, $"unrevealed strip {i}");
        }
    }

    [TestCase(0.25)]
    [TestCase(0.5)]
    [TestCase(0.75)]
    public void Checkerboard_RevealsAlternatingTilesBeforeTheRemainingTiles(double progress)
    {
        using Bitmap bitmap = Render(new CheckerboardTransition { Columns = { Value = 4 }, Rows = { Value = 2 } }, progress);
        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(bitmap, 8, 8)), Is.True, "first tile expands in the first half");
            Assert.That(IsBlue(Pixel(bitmap, 24, 8)), Is.EqualTo(progress > 0.5), "adjacent tile expands in the second half");
            Assert.That(IsBlue(Pixel(bitmap, 8, 24)), Is.EqualTo(progress > 0.5), "rows alternate too");
            Assert.That(IsBlue(Pixel(bitmap, 24, 24)), Is.True, "diagonally adjacent tiles share a phase");
        });
    }

    [Test]
    public void ClockWipe_StartsAtTwelveAndSweepsInTheChosenDirection([Values] bool clockwise)
    {
        var transition = new ClockWipeTransition { Clockwise = { Value = clockwise } };
        using Bitmap firstQuarter = Render(transition, 0.25);
        using Bitmap thirdQuarter = Render(transition, 0.75);
        int leading = clockwise ? 40 : 23;
        int trailing = clockwise ? 23 : 40;
        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(firstQuarter, leading, 8)), Is.True);
            Assert.That(IsRed(Pixel(firstQuarter, leading, 24)), Is.True);
            Assert.That(IsRed(Pixel(firstQuarter, trailing, 8)), Is.True);
            Assert.That(IsBlue(Pixel(thirdQuarter, trailing, 24)), Is.True);
            Assert.That(IsRed(Pixel(thirdQuarter, trailing, 8)), Is.True);
        });
    }

    [Test]
    public void BoxAndDiamond_HaveDistinctCentredOutlines()
    {
        using Bitmap box = Render(new BoxWipeTransition(), 0.5);
        using Bitmap diamond = Render(new DiamondWipeTransition(), 0.5);
        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(box, 32, 16)) && IsBlue(Pixel(diamond, 32, 16)), Is.True);
            Assert.That(IsRed(Pixel(box, 1, 1)) && IsRed(Pixel(diamond, 1, 1)), Is.True);
            Assert.That(IsRed(Pixel(box, 32, 3)), Is.True, "above the rectangle");
            Assert.That(IsBlue(Pixel(diamond, 32, 3)), Is.True, "inside the diamond's upper point");
        });
    }

    private static IEnumerable<Bgra8888> ReadPixels(Bitmap bitmap)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return Pixel(bitmap, x, y);
    }
}
