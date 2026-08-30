using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Pins that a stage which reads its input somewhere other than the pixel it is writing answers a hit test for
/// the pixels it actually produced.
/// </summary>
/// <remarks>
/// A whole-source stage with no declared contract forwards the query to its input at the queried point, which
/// is right only while the stage leaves content where it found it. <see cref="ColorShift"/> and
/// <see cref="MosaicEffect"/> both move it: one translates each channel, the other replaces every pixel of a
/// tile with the tile's centre sample. Each assertion below compares the hit test against the pixel the shader
/// wrote at the same logical point, so the contract can only stay right by being measured against the shader
/// rather than against a reading of it.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class ResampledStageHitTestTests
{
    private const int Size = 100;

    // Narrower than the surface, so a horizontal shift carries content into pixels the input never covered
    // and vacates pixels it did.
    private static readonly Rect s_shiftContent = new(0, 0, 60, 100);
    private static readonly Rect s_tileContent = new(0, 0, Size, Size);

    [TestCaseSource(nameof(ColorShiftCases))]
    [Category("GpuPassFusionGpu")]
    public void ColorShift_TheHitTestContract_AgreesWithWhatTheStagePainted(
        PixelPoint red,
        PixelPoint green,
        PixelPoint blue,
        PixelPoint alpha,
        Point point)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(s_shiftContent, Shift(red, green, blue, alpha));
            bool painted = Painted(rendered, (int)point.X, (int)point.Y);

            Assert.That(
                HitTest(s_shiftContent, Shift(red, green, blue, alpha), point),
                Is.EqualTo(painted),
                $"ColorShift r={red} g={green} b={blue} a={alpha} at {point}: the hit test "
                + $"{(painted ? "misses a point the stage painted" : "claims a point the stage left clear")}.");
        });
    }

    /// <remarks>
    /// The offsets are what make the stage move content, so at zero it reads the pixel it writes and the
    /// forwarded query is already exact. A contract that claimed the output rectangle whenever the stage exists
    /// would satisfy every painted case above and still be wrong here, where the point sits inside the output
    /// bounds and outside the ellipse.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void ColorShift_WithoutAnOffset_StillMissesTheClearCorner()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var corner = new Point(2, 2);
            using Bitmap rendered = Render(s_shiftContent, new ColorShift());

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)corner.X, (int)corner.Y),
                    Is.False,
                    "an unshifted ColorShift leaves the corner of the ellipse's box clear");
                Assert.That(
                    HitTest(s_shiftContent, new ColorShift(), corner),
                    Is.False,
                    "so the stage must not start claiming it");
            });
        });
    }

    /// <remarks>
    /// The other half of the same guard, at the settings where the stage does need a contract: the shifted
    /// output bounds reach x = 80, so a contract widened to those bounds would hit here, where every channel
    /// reads a point the ellipse never covered.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void ColorShift_WithAnOffset_StillMissesWhereNoChannelLanded()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var beyond = new Point(90, 50);
            ColorShift Effect() => Shift(Right(20), Right(20), Right(20), Right(20));
            using Bitmap rendered = Render(s_shiftContent, Effect());

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)beyond.X, (int)beyond.Y),
                    Is.False,
                    "20 to the right of a 60-wide ellipse leaves x = 90 clear");
                Assert.That(
                    HitTest(s_shiftContent, Effect(), beyond),
                    Is.False,
                    "so a hit test must not answer for the whole shifted output rectangle");
            });
        });
    }

    [TestCaseSource(nameof(MosaicCases))]
    [Category("GpuPassFusionGpu")]
    public void MosaicEffect_TheHitTestContract_AgreesWithWhatTheStagePainted(
        float tile,
        RelativePoint origin,
        Point point)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(s_tileContent, Mosaic(tile, origin));
            bool painted = Painted(rendered, (int)point.X, (int)point.Y);

            Assert.That(
                HitTest(s_tileContent, Mosaic(tile, origin), point),
                Is.EqualTo(painted),
                $"MosaicEffect tile={tile} origin={origin} at {point}: the hit test "
                + $"{(painted ? "misses a point the stage painted" : "claims a point the stage left clear")}.");
        });
    }

    /// <remarks>
    /// A tile whose centre sample is clear is erased whatever the input covered inside it, so the stage's
    /// output is not a subset of its input's coverage in either direction. This is the direction a contract
    /// widened to the output rectangle gets wrong: the point is inside the mosaic's bounds and the mosaic put
    /// nothing there.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void MosaicEffect_StillMissesATileTheStageErased()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Inside the ellipse; its tile's centre (0, 20) is outside, so the tile is erased.
            var erased = new Point(9, 25);
            using Bitmap rendered = Render(s_tileContent, Mosaic(20, RelativePoint.Center));

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)erased.X, (int)erased.Y),
                    Is.False,
                    "the tile holding this point samples its centre, which the ellipse does not cover");
                Assert.That(
                    HitTest(s_tileContent, Mosaic(20, RelativePoint.Center), erased),
                    Is.False,
                    "so the point the input covers is no longer selectable through the mosaic");
            });
        });
    }

    /// <remarks>
    /// The stage samples with <see cref="SkiaSharp.SKShaderTileMode.Clamp"/>, so a tile whose centre falls
    /// outside the input reads the input's edge rather than transparency. An origin of 5 puts the centre of the
    /// tile holding (2, 2) at (-5, -5), outside a full-bleed input that still paints the point opaque.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void MosaicEffect_ATileCentreOutsideTheInput_ReadsTheEdgeAndHits()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var corner = new Point(2, 2);
            var origin = new RelativePoint(0.05f, 0.05f, RelativeUnit.Relative);
            using Bitmap rendered = Render(s_tileContent, Mosaic(20, origin), fullBleed: true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)corner.X, (int)corner.Y),
                    Is.True,
                    "clamping reads the input's edge, so the corner tile is opaque");
                Assert.That(
                    HitTest(s_tileContent, Mosaic(20, origin), corner, fullBleed: true),
                    Is.True,
                    "so the hit test has to clamp the sample point the same way");
            });
        });
    }

    /// <remarks>
    /// Both contracts rebuild a shader-side quantity from logical values - <see cref="ColorShift"/> its offsets,
    /// <see cref="MosaicEffect"/> its tile grid, whose origin the binder resolves in device pixels. A hit test
    /// is asked in logical coordinates, so neither may start answering differently because the request asked
    /// for a denser output.
    /// </remarks>
    [TestCase(0.5f)]
    [TestCase(2f)]
    [TestCase(3f)]
    public void TheContracts_AnswerTheSameAtEveryOutputScale(float outputScale)
    {
        ColorShift Shifted() => Shift(Right(20), Right(20), Right(20), Right(20));

        Assert.Multiple(() =>
        {
            foreach (Point point in new Point[] { new(70, 50), new(10, 50), new(90, 50) })
            {
                Assert.That(
                    HitTest(s_shiftContent, Shifted(), point, outputScale: outputScale),
                    Is.EqualTo(HitTest(s_shiftContent, Shifted(), point)),
                    $"ColorShift changed its answer at {point} when the output scale became {outputScale}");
            }

            foreach (Point point in new Point[] { new(12, 12), new(9, 25), new(50, 50) })
            {
                Assert.That(
                    HitTest(s_tileContent, Mosaic(20, RelativePoint.Center), point, outputScale: outputScale),
                    Is.EqualTo(HitTest(s_tileContent, Mosaic(20, RelativePoint.Center), point)),
                    $"MosaicEffect changed its answer at {point} when the output scale became {outputScale}");
            }
        });
    }

    private static IEnumerable<TestCaseData> ColorShiftCases()
    {
        PixelPoint none = default;
        PixelPoint right = Right(20);

        // Every channel moves together: content arrives at x = 70 and leaves x = 10.
        yield return Case("AllChannels_ArrivedEdge", right, right, right, right, new Point(70, 50));
        yield return Case("AllChannels_VacatedEdge", right, right, right, right, new Point(10, 50));

        // Only alpha moves: coverage arrives at x = 70 while the colour it left behind stays at x = 10 with
        // zero alpha, which premultiplied compositing still shows.
        yield return Case("AlphaOnly_ArrivedCoverage", none, none, none, right, new Point(70, 50));
        yield return Case("AlphaOnly_StrandedColour", none, none, none, right, new Point(10, 50));

        // Only red moves: alpha never changes, so the arriving red is the whole of what the stage added.
        yield return Case("RedOnly_ArrivedColour", right, none, none, none, new Point(70, 50));
        yield return Case("RedOnly_Beyond", right, none, none, none, new Point(90, 50));

        yield return Case("NoOffset_Covered", none, none, none, none, new Point(10, 50));
        yield return Case("NoOffset_Clear", none, none, none, none, new Point(70, 50));

        static TestCaseData Case(
            string name,
            PixelPoint red,
            PixelPoint green,
            PixelPoint blue,
            PixelPoint alpha,
            Point point)
            => new TestCaseData(red, green, blue, alpha, point)
                .SetName($"ColorShift_TheHitTestContract_AgreesWithWhatTheStagePainted_{name}");
    }

    private static IEnumerable<TestCaseData> MosaicCases()
    {
        var absolute = new RelativePoint(0, 0, RelativeUnit.Absolute);

        // Centre origin puts the tile centres on 0, 20, ... 100; the absolute origin puts them on 10, 30, ...
        // so the same two points swap which of them the stage paints.
        yield return Case("Centre_PaintedOutsideTheEllipse", RelativePoint.Center, new Point(12, 12));
        yield return Case("Centre_ErasedInsideTheEllipse", RelativePoint.Center, new Point(9, 25));
        yield return Case("Centre_Middle", RelativePoint.Center, new Point(50, 50));
        yield return Case("Absolute_ErasedOutsideTheEllipse", absolute, new Point(12, 12));
        yield return Case("Absolute_PaintedInsideTheEllipse", absolute, new Point(9, 25));
        yield return Case("Absolute_Corner", absolute, new Point(2, 2));

        static TestCaseData Case(string name, RelativePoint origin, Point point)
            => new TestCaseData(20f, origin, point)
                .SetName($"MosaicEffect_TheHitTestContract_AgreesWithWhatTheStagePainted_{name}");
    }

    private static PixelPoint Right(int x) => new(x, 0);

    private static ColorShift Shift(PixelPoint red, PixelPoint green, PixelPoint blue, PixelPoint alpha)
        => new()
        {
            RedOffset = { CurrentValue = red },
            GreenOffset = { CurrentValue = green },
            BlueOffset = { CurrentValue = blue },
            AlphaOffset = { CurrentValue = alpha },
        };

    private static MosaicEffect Mosaic(float tile, RelativePoint origin)
        => new()
        {
            TileSize = { CurrentValue = new Size(tile, tile) },
            Origin = { CurrentValue = origin },
        };

    private static bool HitTest(
        Rect content,
        FilterEffect effect,
        Point point,
        bool fullBleed = false,
        float outputScale = 1f)
    {
        using RenderNode root = BuildTree(content, effect, fullBleed);
        using var renderer = new RenderNodeRenderer(root, Options(outputScale));
        return renderer.HitTest(point);
    }

    private static Bitmap Render(Rect content, FilterEffect effect, bool fullBleed = false)
    {
        using RenderTarget target = RenderTarget.Create(Size, Size)
            ?? throw new InvalidOperationException("Could not allocate the resampling render target.");
        using var canvas = new ImmediateCanvas(
            target, RenderIntent.Preview, 1f, logicalSize: new Size(Size, Size));
        canvas.Clear(Colors.Transparent);

        using (RenderNode root = BuildTree(content, effect, fullBleed))
        using (var renderer = new RenderNodeRenderer(root, Options()))
        {
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static RenderNode BuildTree(Rect content, FilterEffect effect, bool fullBleed)
    {
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(fullBleed
            ? new RectangleRenderNode(content, Brushes.Resource.White, null)
            : (RenderNode)new EllipseRenderNode(content, Brushes.Resource.White, null));
        return node;
    }

    private static RenderNodeRendererOptions Options(float outputScale = 1f)
        => new()
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(0, 0, Size, Size),
                OutputScale = outputScale,
                MaxWorkingScale = outputScale,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        };

    /// <remarks>
    /// Alpha alone does not say whether a stage put something at a point. <see cref="ColorShift"/> takes each
    /// channel from its own sample, so it can leave a pixel with zero alpha and a non-zero colour; a
    /// premultiplied compositor adds that colour to whatever is behind it, and it is visible.
    /// </remarks>
    private static bool Painted(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
        for (int channel = 0; channel < 4; channel++)
        {
            if ((float)BitConverter.UInt16BitsToHalf(row[(x * 4) + channel]) > 0f)
                return true;
        }

        return false;
    }
}
