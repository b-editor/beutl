using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[TestFixture]
[NonParallelizable]
public sealed class ResampledStageHitTestTests
{
    private const int Size = 100;
    private const int LargeSize = 300;

    // Narrower than the surface, so a horizontal shift carries content into pixels the input never covered
    // and vacates pixels it did.
    private static readonly Rect s_shiftContent = new(0, 0, 60, 100);
    private static readonly Rect s_tileContent = new(0, 0, Size, Size);

    // A domain three times the input, with the input offset inside it: a coordinate can then be well outside
    // the stage and still be one the request is asked about.
    private static readonly Rect s_largeDomain = new(0, 0, LargeSize, LargeSize);
    private static readonly Rect s_offsetContent = new(Size, Size, Size, Size);

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

    [TestCaseSource(nameof(DisplacementMapCases))]
    [Category("GpuPassFusionGpu")]
    public void DisplacementMap_TheHitTestContract_AgreesWithWhatTheStagePainted(
        Func<FilterEffect> effect,
        Point point)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(s_shiftContent, effect());
            bool painted = Painted(rendered, (int)point.X, (int)point.Y);

            Assert.That(
                HitTest(s_shiftContent, effect(), point),
                Is.EqualTo(painted),
                $"the displacement map hit test at {point} "
                + $"{(painted ? "misses a point the stage painted" : "claims a point the stage left clear")}.");
        });
    }

    [Test]
    public void DisplacementMap_ResolvesTheMapItSamplesRatherThanForwardingThePoint()
    {
        // The map is opaque everywhere, so the translation is taken in full and the stage reads 20 to the
        // right of every fragment it writes. Both points are inside the ellipse's box and inside the stage's
        // own output, which is what makes forwarding answer them and answer them wrongly.
        Assert.Multiple(() =>
        {
            Assert.That(
                HitTest(s_shiftContent, Displace(Translated(20, 0)), new Point(2, 92)),
                Is.True,
                "the ellipse covers (22, 92), which is the point this fragment reads");
            Assert.That(
                HitTest(s_shiftContent, Displace(Translated(20, 0)), new Point(30, 92)),
                Is.False,
                "the ellipse does not cover (50, 92), so the stage vacated the point it used to cover");
        });
    }

    [Test]
    public void DisplacementMap_OverATransparentMap_LeavesTheInputWhereItFoundIt()
    {
        // Alpha zero displaces by nothing, so the stage reads the fragment it writes and the contract must
        // agree with the input exactly - the negative control for a contract that could widen anything.
        DisplacementMapEffect Effect() => Displace(Translated(20, 0), new SolidColorBrush(Colors.Transparent));

        Assert.Multiple(() =>
        {
            Assert.That(HitTest(s_shiftContent, Effect(), new Point(30, 92)), Is.True);
            Assert.That(HitTest(s_shiftContent, Effect(), new Point(2, 92)), Is.False);
        });
    }

    [Test]
    public void DisplacementMap_OverATileBrushMap_DeclaresNoContractRatherThanSamplingIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                DeclaredShaderContractOf(Displace(Translated(20, 0))),
                Is.Not.Null,
                "a brush whose shader is a pure function of the point carries the resolved contract");
            Assert.That(
                DeclaredShaderContractOf(Displace(Translated(20, 0), CreateImageBrush())),
                Is.Null,
                "a tile brush's shader rasterizes an intermediate render target, which a hit test must not "
                + "allocate, so the query stays forwarded");
        });
    }

    /// <summary>Reads back the hit-test contract an effect declared, or its absence.</summary>
    private static RenderHitTestContract? DeclaredShaderContractOf(FilterEffect effect)
    {
        using var context = new FilterEffectContext(s_shiftContent);
        effect.ApplyTo(context, (FilterEffect.Resource)effect.ToResource(CompositionContext.Default));
        return context.GetOrderedItems()
            .OfType<FEItem_Shader>()
            .Single()
            .Description
            .HitTest;
    }

    private static ImageBrush CreateImageBrush()
    {
        using var bitmap = new Bitmap(4, 4);
        using var stream = new MemoryStream();
        bitmap.Save(stream, EncodedImageFormat.Png);

        var source = new ImageSource();
        source.ReadFrom(UriHelper.CreateBase64DataUri("image/png", stream.ToArray()));
        return new ImageBrush(source);
    }

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

            // The clamped edge and the rectangle just outside the stage are where the two new bounds decide
            // the answer, and both are expressed in logical coordinates that no output density may move.
            foreach (Point point in new Point[] { new(92, 50), new(50, 92), new(100, 50), new(50, 100) })
            {
                Assert.That(
                    HitTest(
                        s_tileContent, Mosaic(20, RelativePoint.Center), point, fullBleed: true,
                        outputScale: outputScale),
                    Is.EqualTo(
                        HitTest(s_tileContent, Mosaic(20, RelativePoint.Center), point, fullBleed: true)),
                    $"MosaicEffect changed its answer at {point} when the output scale became {outputScale}");
            }

            // The displacement contracts rebuild a translation, a pivot and a map sample from logical values
            // that the shader binders express in device pixels, so density is exactly what could move them.
            foreach ((string name, Func<FilterEffect> effect) in new (string, Func<FilterEffect>)[]
                     {
                         ("translate", static () => Displace(Translated(20, 0))),
                         ("scale", static () => Displace(Scaled(200))),
                         ("rotation", static () => Displace(Rotated(30))),
                     })
            {
                foreach (Point point in new Point[] { new(2, 92), new(30, 92), new(10, 50), new(55, 85) })
                {
                    Assert.That(
                        HitTest(s_shiftContent, effect(), point, outputScale: outputScale),
                        Is.EqualTo(HitTest(s_shiftContent, effect(), point)),
                        $"the displacement {name} contract changed its answer at {point} when the output "
                        + $"scale became {outputScale}");
                }
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void MosaicEffect_OutsideItsOwnOutput_ClaimsNothing()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var far = new Point(0, 0);
            using Bitmap rendered = Render(
                s_offsetContent, Mosaic(20, RelativePoint.Center), fullBleed: true, surface: LargeSize,
                targetDomain: s_largeDomain);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)far.X, (int)far.Y),
                    Is.False,
                    "the mosaic covers (100, 100)-(200, 200), so the far corner of the surface stays clear");
                Assert.That(
                    HitTest(
                        s_offsetContent, Mosaic(20, RelativePoint.Center), far, fullBleed: true,
                        targetDomain: s_largeDomain),
                    Is.False,
                    "so the contract must not clamp the far corner's sample into the input and claim it");
            });
        });
    }

    [TestCase(92f, 50f, TestName = "MosaicEffect_ATileCentreOnTheRightEdge_ReadsTheEdgeAndHits")]
    [TestCase(50f, 92f, TestName = "MosaicEffect_ATileCentreOnTheBottomEdge_ReadsTheEdgeAndHits")]
    [Category("GpuPassFusionGpu")]
    public void MosaicEffect_ATileCentreOnTheExclusiveEdge_ReadsTheEdgeAndHits(float x, float y)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var edge = new Point(x, y);
            using Bitmap rendered = Render(s_tileContent, Mosaic(20, RelativePoint.Center), fullBleed: true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)edge.X, (int)edge.Y),
                    Is.True,
                    "the tile's centre sample clamps onto the edge of a full-bleed input, which is opaque");
                Assert.That(
                    HitTest(s_tileContent, Mosaic(20, RelativePoint.Center), edge, fullBleed: true),
                    Is.True,
                    "so the hit test has to land inside the input rather than on its exclusive edge");
            });
        });
    }

    [Test]
    public void TheExclusiveEdge_IsTheLastCoordinateTheInputAnswersFor()
    {
        var input = new Rect(0, 0, Size, Size);

        Assert.Multiple(() =>
        {
            Assert.That(
                input.ContainsExclusive(new Point(input.Right, 50)),
                Is.False,
                "the engine's content rule is bottom-right exclusive");
            Assert.That(
                input.ContainsExclusive(new Point(float.BitDecrement(input.Right), 50)),
                Is.True,
                "and the float below the right edge is inside it, so a clamp has that coordinate to aim at");
            Assert.That(
                input.ContainsExclusive(new Point(50, float.BitDecrement(input.Bottom))),
                Is.True,
                "the bottom edge behaves the same way");
        });
    }

    [Test]
    public void MosaicEffect_ClampsOntoTheLastCoordinateTheInputAnswersFor()
    {
        RenderHitTestContract contract = ShaderContractOf(Mosaic(20, RelativePoint.Center));
        var asked = new List<Point>();
        var input = new RenderHitTestInput(
            s_tileContent,
            point =>
            {
                asked.Add(point);
                return s_tileContent.ContainsExclusive(point);
            });

        bool hit = contract.Evaluate(
            s_tileContent, [input], Array.Empty<RenderResourceBinding>(), new Point(92, 50));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Has.Count.EqualTo(1), "the stage reads its input once, at the tile's centre");
            Assert.That(
                asked[0].X,
                Is.EqualTo(float.BitDecrement(s_tileContent.Right)),
                "the centre at the right edge is clamped to the last coordinate inside the input");
            Assert.That(
                asked[0].Y,
                Is.EqualTo(60f),
                "the vertical centre is already inside, so the clamp leaves it alone");
            Assert.That(hit, Is.True, "and that coordinate is one the input's own rule accepts");
        });
    }

    [TestCase(50f, 0f, 0f, 100f, TestName = "MosaicEffect_OverAnInputWithNoWidth_Misses")]
    [TestCase(0f, 50f, 100f, 0f, TestName = "MosaicEffect_OverAnInputWithNoHeight_Misses")]
    public void MosaicEffect_OverAnInputWithNoArea_Misses(float x, float y, float width, float height)
    {
        RenderHitTestContract contract = ShaderContractOf(Mosaic(20, RelativePoint.Center));
        var input = new RenderHitTestInput(new Rect(x, y, width, height), static _ => true);

        Assert.That(
            () => contract.Evaluate(
                s_tileContent, [input], Array.Empty<RenderResourceBinding>(), new Point(50, 50)),
            Is.False,
            "an input covering no area carries nothing for any tile to sample");
    }

    /// <summary>Reads back the hit-test contract the effect recorded, so it can be asked directly.</summary>
    private static RenderHitTestContract ShaderContractOf(FilterEffect effect)
    {
        using var context = new FilterEffectContext(s_tileContent);
        effect.ApplyTo(context, (FilterEffect.Resource)effect.ToResource(CompositionContext.Default));
        ShaderDescription description = context.GetOrderedItems()
            .OfType<FEItem_Shader>()
            .Single()
            .Description;
        return description.HitTest
               ?? throw new InvalidOperationException("The effect recorded no hit-test contract.");
    }

    [TestCaseSource(nameof(OutsideOutputCases))]
    public void TheContracts_NeverAnswerOutsideTheFragmentsOwnOutput(
        Func<FilterEffect> effect,
        Rect content,
        bool fullBleed)
    {
        Rect bounds = OutputBounds(content, effect(), fullBleed, s_largeDomain);
        Assert.That(bounds.Width, Is.GreaterThan(0), "the stage has to describe some output to be bounded by");

        var claimed = new List<Point>();
        for (float x = 0; x < LargeSize; x += 7)
        {
            for (float y = 0; y < LargeSize; y += 7)
            {
                var point = new Point(x, y);
                if (bounds.Contains(point))
                    continue;

                if (HitTest(content, effect(), point, fullBleed, targetDomain: s_largeDomain))
                    claimed.Add(point);
            }
        }

        Assert.That(
            claimed,
            Is.Empty,
            $"the stage wrote only {bounds}, so it must not answer for points outside it");
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ColorShift_OverANonWhiteInput_CannotDistinguishAZeroChannelFromANonZeroOne()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var arrived = new Point(70, 50);
            ColorShift GreenOnly() => Shift(default, Right(20), default, default);

            using Bitmap overRed = Render(
                s_shiftContent, GreenOnly(), fullBleed: true, fill: Brushes.Resource.Red);
            using Bitmap overGreen = Render(
                s_shiftContent, GreenOnly(), fullBleed: true, fill: Brushes.Resource.Lime);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(overRed, (int)arrived.X, (int)arrived.Y),
                    Is.False,
                    "a green-only shift over a red input carries a zero green channel, so the pixel stays empty");
                Assert.That(
                    Painted(overGreen, (int)arrived.X, (int)arrived.Y),
                    Is.True,
                    "the same shift over a green input paints it, from input coverage the contract cannot tell apart");
                Assert.That(
                    HitTest(s_shiftContent, GreenOnly(), arrived, fullBleed: true, fill: Brushes.Resource.Red),
                    Is.EqualTo(
                        HitTest(
                            s_shiftContent, GreenOnly(), arrived, fullBleed: true, fill: Brushes.Resource.Lime)),
                    "so no coverage-only contract can answer both, and it must answer the painted one");
                Assert.That(
                    HitTest(s_shiftContent, GreenOnly(), arrived, fullBleed: true, fill: Brushes.Resource.Red),
                    Is.True,
                    "which makes the red case an over-claim the contract accepts rather than miss the green one");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ColorShift_OverANonWhiteInput_StillMissesWhereNoChannelLanded()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var beyond = new Point(90, 50);
            ColorShift GreenOnly() => Shift(default, Right(20), default, default);
            using Bitmap rendered = Render(
                s_shiftContent, GreenOnly(), fullBleed: true, fill: Brushes.Resource.Red);

            Assert.Multiple(() =>
            {
                Assert.That(
                    Painted(rendered, (int)beyond.X, (int)beyond.Y),
                    Is.False,
                    "x = 90 is past both the input and its shifted copy");
                Assert.That(
                    HitTest(s_shiftContent, GreenOnly(), beyond, fullBleed: true, fill: Brushes.Resource.Red),
                    Is.False,
                    "so the contract misses it whatever colour the input carries");
            });
        });
    }

    private static IEnumerable<TestCaseData> OutsideOutputCases()
    {
        yield return Case(
            "Mosaic_Centre", static () => Mosaic(20, RelativePoint.Center), s_offsetContent, true);
        yield return Case(
            "Mosaic_Absolute",
            static () => Mosaic(20, new RelativePoint(0, 0, RelativeUnit.Absolute)),
            s_offsetContent,
            true);
        yield return Case(
            "Mosaic_UnalignedTile", static () => Mosaic(7, RelativePoint.Center), s_offsetContent, false);
        yield return Case(
            "ColorShift_AllChannels",
            static () => Shift(Right(20), Right(20), Right(20), Right(20)),
            s_offsetContent,
            false);
        yield return Case(
            "ColorShift_AlphaOnly",
            static () => Shift(default, default, default, Right(20)),
            s_offsetContent,
            true);
        yield return Case(
            "DisplacementMap_Translate", static () => Displace(Translated(20, 0)), s_offsetContent, true);
        yield return Case(
            "DisplacementMap_Scale", static () => Displace(Scaled(200)), s_offsetContent, true);
        yield return Case(
            "DisplacementMap_Rotation", static () => Displace(Rotated(90)), s_offsetContent, true);

        static TestCaseData Case(string name, Func<FilterEffect> effect, Rect content, bool fullBleed)
            => new TestCaseData(effect, content, fullBleed)
                .SetName($"TheContracts_NeverAnswerOutsideTheFragmentsOwnOutput_{name}");
    }

    private static IEnumerable<TestCaseData> DisplacementMapCases()
    {
        // Every point is inside the stage's own output and inside the ellipse's box, so what separates them
        // is only where the entry point read - which is the whole of what the contract has to restate.
        yield return Case("Translate_Arrived", static () => Displace(Translated(20, 0)), new Point(2, 92));
        yield return Case("Translate_Vacated", static () => Displace(Translated(20, 0)), new Point(30, 92));
        yield return Case("Translate_Middle", static () => Displace(Translated(20, 0)), new Point(10, 50));
        yield return Case("Translate_Beyond", static () => Displace(Translated(20, 0)), new Point(50, 92));
        yield return Case("Scale_Magnified_Arrived", static () => Displace(Scaled(200)), new Point(2, 80));
        yield return Case("Scale_Magnified_Middle", static () => Displace(Scaled(200)), new Point(30, 50));
        yield return Case("Scale_Reduced_Vacated", static () => Displace(Scaled(50)), new Point(12, 30));
        yield return Case("Scale_Reduced_Middle", static () => Displace(Scaled(50)), new Point(30, 50));
        yield return Case("Rotation_Arrived", static () => Displace(Rotated(30)), new Point(55, 85));
        yield return Case("Rotation_Vacated", static () => Displace(Rotated(30)), new Point(30, 97));
        yield return Case("Rotation_Pivot", static () => Displace(Rotated(30)), new Point(30, 50));

        static TestCaseData Case(string name, Func<FilterEffect> effect, Point point)
            => new TestCaseData(effect, point)
                .SetName($"DisplacementMap_TheHitTestContract_AgreesWithWhatTheStagePainted_{name}");
    }

    private static DisplacementMapEffect Displace(DisplacementMapTransform transform, Brush? map = null)
        => new()
        {
            DisplacementMap = { CurrentValue = map ?? new SolidColorBrush(Colors.White) },
            Transform = { CurrentValue = transform },
        };

    private static DisplacementMapTranslateTransform Translated(float x, float y)
        => new() { X = { CurrentValue = x }, Y = { CurrentValue = y } };

    private static DisplacementMapScaleTransform Scaled(float percent)
        => new() { ScaleX = { CurrentValue = percent }, ScaleY = { CurrentValue = percent } };

    private static DisplacementMapRotationTransform Rotated(float degrees)
        => new() { Rotation = { CurrentValue = degrees } };

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
        float outputScale = 1f,
        Rect? targetDomain = null,
        Brush.Resource? fill = null)
    {
        using RenderNode root = BuildTree(content, effect, fullBleed, fill);
        using var renderer = new RenderNodeRenderer(root, Options(outputScale, targetDomain));
        return renderer.HitTest(point);
    }

    /// <summary>The rectangle the request resolved for the stage - the extent it may answer for.</summary>
    private static Rect OutputBounds(
        Rect content,
        FilterEffect effect,
        bool fullBleed = false,
        Rect? targetDomain = null,
        Brush.Resource? fill = null)
    {
        using RenderNode root = BuildTree(content, effect, fullBleed, fill);
        using var renderer = new RenderNodeRenderer(root, Options(targetDomain: targetDomain));
        return renderer.Measure().OutputBounds;
    }

    private static Bitmap Render(
        Rect content,
        FilterEffect effect,
        bool fullBleed = false,
        int surface = Size,
        Rect? targetDomain = null,
        Brush.Resource? fill = null)
    {
        using RenderTarget target = RenderTarget.Create(surface, surface)
            ?? throw new InvalidOperationException("Could not allocate the resampling render target.");
        using var canvas = new ImmediateCanvas(
            target, RenderIntent.Preview, 1f, logicalSize: new Size(surface, surface));
        canvas.Clear(Colors.Transparent);

        using (RenderNode root = BuildTree(content, effect, fullBleed, fill))
        using (var renderer = new RenderNodeRenderer(root, Options(targetDomain: targetDomain)))
        {
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static RenderNode BuildTree(
        Rect content,
        FilterEffect effect,
        bool fullBleed,
        Brush.Resource? fill = null)
    {
        Brush.Resource brush = fill ?? Brushes.Resource.White;
        RenderNode leaf = fullBleed
            ? new RectangleRenderNode(content, brush, null)
            : new EllipseRenderNode(content, brush, null);

        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(leaf);
        return node;
    }

    private static RenderNodeRendererOptions Options(float outputScale = 1f, Rect? targetDomain = null)
        => new()
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = targetDomain ?? new Rect(0, 0, Size, Size),
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
