using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using static Beutl.UnitTests.Engine.Graphics.Rendering.Golden.FilteredShapeHarness;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[TestFixture]
[NonParallelizable]
public sealed class ResampledStageHitTestTests
{
    private static readonly Rect s_content = new(0, 0, 60, 100);

    [TestCaseSource(nameof(Effects))]
    public void SamplingEffects_ForwardThePointToTheInput(Func<FilterEffect> effect)
    {
        foreach (float scale in new[] { 0.5f, 1f, 3f })
        {
            Assert.That(HitTest(s_content, effect(), new Point(30, 92), outputScale: scale), Is.True);
            Assert.That(HitTest(s_content, effect(), new Point(2, 92), outputScale: scale), Is.False);
            Assert.That(HitTest(s_content, effect(), new Point(100, 50), outputScale: scale), Is.False);
        }
    }

    [TestCaseSource(nameof(Effects))]
    public void SamplingEffects_DoNotHitEmptyInput(Func<FilterEffect> effect)
    {
        Assert.That(HitTest(new Rect(0, 0, 0, 100), effect(), new Point(0, 50)), Is.False);
        Assert.That(HitTest(new Rect(0, 0, 60, 0), effect(), new Point(30, 0)), Is.False);
    }

    [Test]
    public void ColorShift_DoesNotSelectPixelsShiftedBeyondTheInput()
    {
        var shifted = Shift();
        Assert.That(HitTest(s_content, shifted, new Point(70, 50)), Is.False);
    }

    [Test]
    public void MosaicEffect_AnErasedInputPointRemainsSelectable()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var content = new Rect(0, 0, 100, 100);
            var erased = new Point(9, 25);
            using Bitmap rendered = Render(content, Mosaic(RelativePoint.Center));

            Assert.That(ImageMetrics.AlphaAt(rendered, 9, 25), Is.Zero);
            Assert.That(HitTest(content, Mosaic(RelativePoint.Center), erased), Is.True);
        });
    }

    [Test]
    public void MosaicEffect_AtAFractionalOrigin_StillSamplesTheSameClearTexel()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var content = new Rect(0, 0, 100, 100);
            var origin = new RelativePoint(1.1f, 10, RelativeUnit.Absolute);
            using Bitmap rendered = Render(content, Mosaic(origin));
            ushort[] pixel = rendered.GetRow<ushort>(82).Slice(92 * 4, 4).ToArray();

            Assert.That(pixel, Is.All.Zero);
        });
    }

    [TestCase(92, 50)]
    [TestCase(50, 92)]
    public void MosaicEffect_ATileCentreOnTheExclusiveEdge_StillReadsTheEdge(int x, int y)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(new Rect(0, 0, 100, 100), Mosaic(RelativePoint.Center), fullBleed: true);
            Assert.That(ImageMetrics.AlphaAt(rendered, x, y), Is.EqualTo(1));
        });
    }

    private static IEnumerable<TestCaseData> Effects()
    {
        yield return Case("ColorShift", Shift);
        yield return Case("Mosaic", static () => Mosaic(RelativePoint.Center));
        yield return Case("DisplacementTranslate", static () => Displace(
            new DisplacementMapTranslateTransform { X = { Value = 20 } }));
        yield return Case("DisplacementScale", static () => Displace(
            new DisplacementMapScaleTransform { ScaleX = { Value = 200 }, ScaleY = { Value = 200 } }));
        yield return Case("DisplacementRotation", static () => Displace(
            new DisplacementMapRotationTransform { Rotation = { Value = 30 } }));

        static TestCaseData Case(string name, Func<FilterEffect> effect)
            => new TestCaseData(effect).SetArgDisplayNames(name);
    }

    private static DisplacementMapEffect Displace(DisplacementMapTransform transform)
        => new()
        {
            DisplacementMap = { Value = new SolidColorBrush(Colors.White) },
            Transform = { Value = transform },
        };

    private static ColorShift Shift()
        => new()
        {
            RedOffset = { Value = new PixelPoint(20, 0) },
            GreenOffset = { Value = new PixelPoint(20, 0) },
            BlueOffset = { Value = new PixelPoint(20, 0) },
            AlphaOffset = { Value = new PixelPoint(20, 0) },
        };

    private static MosaicEffect Mosaic(RelativePoint origin)
        => new() { TileSize = { Value = new Size(20, 20) }, Origin = { Value = origin } };
}
