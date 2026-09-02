using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class DrawableGroupIsolationTests
{
    private static readonly PixelSize s_frame = new(400, 400);

    [Test]
    public void OverlappingChildren_GroupOpacityAppliesOnceToComposite()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource group = CreateGroup(
                opacity: 50,
                effect: null,
                CreateRectangle(240, 240, Brushes.White, alignmentX: AlignmentX.Left),
                CreateRectangle(240, 240, Brushes.White, alignmentX: AlignmentX.Right));
            using Drawable.Resource control = CreateRectangle(
                    400,
                    240,
                    Brushes.White,
                    opacity: 50)
                .ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(out RenderExecutionStatistics statistics, group);
            using Bitmap expected = RenderScene(control);

            AssertByteIdentical(expected, actual, "overlapping children at group opacity 50%");
            Rgba left = ReadPixel(actual, 40, 200);
            Rgba overlap = ReadPixel(actual, 200, 200);
            Rgba right = ReadPixel(actual, 360, 200);
            Assert.Multiple(() =>
            {
                Assert.That(left.Alpha, Is.EqualTo(0.5f).Within(0.003f));
                Assert.That(left.Red, Is.EqualTo(left.Alpha).Within(0.003f));
                Assert.That(overlap.Alpha, Is.EqualTo(0.5f).Within(0.003f));
                Assert.That(overlap.Red, Is.EqualTo(overlap.Alpha).Within(0.003f));
                Assert.That(right.Alpha, Is.EqualTo(0.5f).Within(0.003f));
                Assert.That(right.Red, Is.EqualTo(right.Alpha).Within(0.003f));
                Assert.That(
                    statistics.ShaderRunExecutions,
                    Is.Zero,
                    "The fixture must exercise compatibility opacity through ImmediateCanvas.PushOpacity.");
            });
            TestContext.WriteLine(
                $"Group opacity path: shader runs {statistics.ShaderRunExecutions}, "
                + $"fused shader runs {statistics.FusedShaderRunExecutions}.");
        });
    }

    [Test]
    public void IdentityEffect_DoesNotChangeGroupOpacityComposition()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource plain = CreateGroup(
                opacity: 50,
                effect: null,
                CreateRectangle(240, 240, Brushes.White, alignmentX: AlignmentX.Left),
                CreateRectangle(240, 240, Brushes.White, alignmentX: AlignmentX.Right));
            using Drawable.Resource filtered = CreateGroup(
                opacity: 50,
                identity,
                CreateRectangle(240, 240, Brushes.White, alignmentX: AlignmentX.Left),
                CreateRectangle(240, 240, Brushes.White, alignmentX: AlignmentX.Right));
            using Bitmap expected = RenderScene(plain);
            using Bitmap actual = RenderScene(filtered);

            // Byte-identical: both paths now carry the group opacity in float precision, so an identity
            // effect reproduces the unfiltered composition exactly.
            AssertByteIdentical(expected, actual, "identity effect on an overlapping group");
            Assert.That(
                ReadPixel(actual, 200, 200).Alpha,
                Is.EqualTo(0.5f).Within(0.003f),
                "the group opacity must be applied exactly once.");
        });
    }

    [Test]
    public void SplitEffectOnGroup_MatchesSplitEffectOnEachChild()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource groupFiltered = CreateGroup(
                opacity: 100,
                CreateSplitEffect(),
                CreateRectangle(240, 240, Brushes.OrangeRed, alignmentX: AlignmentX.Left),
                CreateRectangle(240, 240, Brushes.SteelBlue, alignmentX: AlignmentX.Right));

            RectShape left = CreateRectangle(
                240,
                240,
                Brushes.OrangeRed,
                alignmentX: AlignmentX.Left);
            left.FilterEffect.CurrentValue = CreateSplitEffect();
            RectShape right = CreateRectangle(
                240,
                240,
                Brushes.SteelBlue,
                alignmentX: AlignmentX.Right);
            right.FilterEffect.CurrentValue = CreateSplitEffect();
            using Drawable.Resource childrenFiltered = CreateGroup(
                opacity: 100,
                effect: null,
                left,
                right);

            using Bitmap actual = RenderScene(groupFiltered);
            using Bitmap expected = RenderScene(childrenFiltered);

            AssertByteIdentical(
                expected,
                actual,
                "a group SplitEffect and an equivalent SplitEffect on each child");
        });
    }

    [Test]
    public void BlurOnGroupWithHairlineChild_ProducesOnlyFiniteChannels()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var blur = new Blur();
            blur.Sigma.CurrentValue = new Size(6, 6);
            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                blur,
                CreateRectangle(200, 1, Brushes.Magenta),
                CreateRectangle(40, 40, Brushes.SteelBlue));

            using Bitmap actual = RenderScene(0.5f, group);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("blurred group hairline", actual)),
                    Is.Null,
                    "A per-child filter layer must not sample outside the hairline child it replays.");
                Assert.That(HasFiniteVisibleContent(actual), Is.True, "The fixture must render visible content.");
            });
        });
    }

    [Test]
    public void EffectChainOnGroupWithOffFrameChild_ProducesOnlyFiniteChannels()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RectShape offFrame = CreateRectangle(160, 120, Brushes.OrangeRed);
            offFrame.Transform.CurrentValue = new TranslateTransform(-180, -120);

            var blur = new Blur();
            blur.Sigma.CurrentValue = new Size(10, 10);
            var shadow = new DropShadow();
            shadow.Position.CurrentValue = new Point(10, 10);
            shadow.Sigma.CurrentValue = new Size(20, 20);
            shadow.Color.CurrentValue = Colors.Black;
            var chain = new FilterEffectGroup();
            chain.Children.Add(blur);
            chain.Children.Add(shadow);

            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                chain,
                CreateRectangle(120, 80, Brushes.SteelBlue),
                offFrame);

            using Bitmap actual = RenderScene(0.5f, group);

            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("filtered group with off-frame child", actual)),
                    Is.Null,
                    "Each effect layer must stay within the off-frame child content it replays.");
                Assert.That(HasFiniteVisibleContent(actual), Is.True, "The fixture must render visible content.");
            });
        });
    }

    [TestCase(100f)]
    [TestCase(99f)]
    public void MultiplyChild_CompositesAgainstIsolatedGroupContent(float opacity)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource backdrop = CreateRectangle(400, 400, Brushes.Cyan)
                .ToResource(CompositionContext.Default);
            using Drawable.Resource multiplyGroup = CreateGroup(
                opacity,
                effect: null,
                CreateRectangle(240, 240, Brushes.Magenta, blendMode: BlendMode.Multiply));
            using Drawable.Resource sourceOverBackdrop = CreateRectangle(400, 400, Brushes.Cyan)
                .ToResource(CompositionContext.Default);
            using Drawable.Resource sourceOverGroup = CreateGroup(
                opacity,
                effect: null,
                CreateRectangle(240, 240, Brushes.Magenta));
            using Bitmap actual = RenderScene(backdrop, multiplyGroup);
            using Bitmap expected = RenderScene(sourceOverBackdrop, sourceOverGroup);

            AssertByteIdentical(
                expected,
                actual,
                $"Multiply child against an isolated group at opacity {opacity}%");
            Assert.That(
                ReadPixel(actual, 20, 20),
                Is.EqualTo(ReadPixel(expected, 20, 20)),
                "Pixels outside the group content must remain backdrop-only.");
        });
    }

    [Test]
    public void HalfAlphaDstIn_MasksTheGroupComposite()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var halfWhite = new SolidColorBrush(new Color(128, 255, 255, 255));
            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                effect: null,
                CreateRectangle(240, 240, Brushes.White),
                CreateRectangle(240, 240, halfWhite, blendMode: BlendMode.DstIn));
            using Bitmap actual = RenderScene(group);

            Rgba center = ReadPixel(actual, 200, 200);
            Assert.Multiple(() =>
            {
                Assert.That(center.Alpha, Is.EqualTo(128f / 255f).Within(0.002f));
                Assert.That(center.Red, Is.EqualTo(center.Alpha).Within(0.002f));
                Assert.That(ReadPixel(actual, 20, 20).Alpha, Is.Zero);
            });
        });
    }

    [Test]
    public void HalfOpacityGradientDstIn_MasksTheGroupCompositeAtHalfStrength()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var gradient = new LinearGradientBrush();
            gradient.Opacity.CurrentValue = 50;
            gradient.GradientStops.Add(new GradientStop(Colors.White, 0));
            gradient.GradientStops.Add(new GradientStop(Colors.White, 1));
            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                effect: null,
                CreateRectangle(240, 240, Brushes.White),
                CreateRectangle(240, 240, gradient, blendMode: BlendMode.DstIn));
            using Bitmap actual = RenderScene(group);

            Rgba center = ReadPixel(actual, 200, 200);
            Assert.Multiple(() =>
            {
                Assert.That(center.Alpha, Is.EqualTo(0.5f).Within(0.003f));
                Assert.That(center.Red, Is.EqualTo(center.Alpha).Within(0.003f));
            });
        });
    }

    [TestCase(BlendMode.DstIn, 0f, 120f)]
    [TestCase(BlendMode.DstIn, 120f, 0f)]
    [TestCase(BlendMode.DstOut, 0f, 120f)]
    [TestCase(BlendMode.DstOut, 120f, 0f)]
    public void FractionalZeroAreaDestructiveRectangle_HasNoEffect(
        BlendMode blendMode,
        float width,
        float height)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var geometry = new RectGeometry
            {
                Width = { CurrentValue = width },
                Height = { CurrentValue = height },
            };
            using Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
            var nonEmptyGeometry = new RectGeometry
            {
                Width = { CurrentValue = 4 },
                Height = { CurrentValue = 4 },
            };
            using Geometry.Resource nonEmptyGeometryResource =
                nonEmptyGeometry.ToResource(CompositionContext.Default);

            Bitmap Render(bool includeZeroAreaRectangle)
            {
                using RenderTarget target = RenderTarget.Create(32, 32)
                                            ?? throw new InvalidOperationException(
                                                "RenderTarget.Create returned null.");
                using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);
                canvas.Clear(Colors.White);
                using PushedState blend = blendMode == BlendMode.DstOut
                    ? canvas.PushDirectBlendMode(blendMode)
                    : canvas.PushBlendMode(blendMode);

                if (blendMode == BlendMode.DstIn)
                {
                    using (canvas.PushTransform(Matrix.CreateTranslation(2, 2)))
                    {
                        canvas.DrawGeometry(
                            nonEmptyGeometryResource,
                            Brushes.Resource.White,
                            pen: null);
                    }
                }

                if (includeZeroAreaRectangle)
                {
                    using (canvas.PushTransform(Matrix.CreateTranslation(16.5f, 8.5f)))
                    {
                        canvas.DrawGeometry(geometryResource, Brushes.Resource.White, pen: null);
                    }
                }

                return target.Snapshot();
            }

            using Bitmap expected = Render(includeZeroAreaRectangle: false);
            using Bitmap actual = Render(includeZeroAreaRectangle: true);

            AssertByteIdentical(
                expected,
                actual,
                $"fractional {width}x{height} {blendMode} rectangle");
        });
    }

    [Test]
    public void WindowDstIn_RemovesGroupContentOutsideTheWindow()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                effect: null,
                CreateRectangle(320, 320, Brushes.White),
                CreateRectangle(160, 160, Brushes.White, blendMode: BlendMode.DstIn));
            using Bitmap isolated = RenderScene(group);

            using Drawable.Resource backdrop = CreateRectangle(400, 400, Brushes.Blue)
                .ToResource(CompositionContext.Default);
            using Drawable.Resource groupOverBackdrop = CreateGroup(
                opacity: 100,
                effect: null,
                CreateRectangle(320, 320, Brushes.White),
                CreateRectangle(160, 160, Brushes.White, blendMode: BlendMode.DstIn));
            using Bitmap composited = RenderScene(backdrop, groupOverBackdrop);

            Assert.Multiple(() =>
            {
                Assert.That(ReadPixel(isolated, 200, 200).Alpha, Is.EqualTo(1).Within(0.001f));
                Assert.That(
                    ReadPixel(isolated, 80, 200).Alpha,
                    Is.Zero,
                    "Content outside the DstIn window must be removed across the group scope.");
                Assert.That(
                    ReadPixel(composited, 20, 20),
                    Is.EqualTo(ReadPixel(composited, 80, 200)),
                    "Removing group content must reveal, not modify, the outer backdrop.");
            });
        });
    }

    [Test]
    public void FractionalDstInCorners_IdentityEffectPreservesTwoDimensionalCoverage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource plain = CreateTranslatedMaskGroup(effect: null);
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource filtered = CreateTranslatedMaskGroup(identity);

            using Bitmap expected = RenderScene(plain);
            using Bitmap actual = RenderScene(filtered);

            Assert.Multiple(() =>
            {
                AssertByteIdentical(
                    expected,
                    actual,
                    "fractionally translated DstIn mask with an identity group effect");
                Assert.That(
                    ReadPixel(expected, 150, 150).Alpha,
                    Is.EqualTo(0.75f * 0.75f).Within(0.003f),
                    "A corner pixel must contain the product of the horizontal and vertical mask coverage.");
                Assert.That(
                    ReadPixel(actual, 150, 150).Alpha,
                    Is.EqualTo(0.75f * 0.75f).Within(0.003f),
                    "The effect path must preserve two-dimensional mask coverage.");
            });
        });
    }

    [Test]
    public void FractionalDstOutEdges_IdentityEffectDoesNotChangeCoverage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource plain = CreateTranslatedDstOutGroup(effect: null);
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource filtered = CreateTranslatedDstOutGroup(identity);

            using Bitmap expected = RenderScene(plain);
            using Bitmap actual = RenderScene(filtered);

            Assert.Multiple(() =>
            {
                AssertByteIdentical(
                    expected,
                    actual,
                    "fractionally translated DstOut mask with and without an identity group effect");
                Assert.That(
                    ReadPixel(expected, 120, 200).Alpha,
                    Is.EqualTo(0.5f).Within(0.003f),
                    "The leading vertical eraser edge must preserve its absolute half-pixel coverage.");
                Assert.That(
                    ReadPixel(expected, 200, 120).Alpha,
                    Is.EqualTo(0.25f).Within(0.003f),
                    "The leading horizontal eraser edge must retain one minus 75% eraser coverage.");
                Assert.That(
                    ReadPixel(expected, 120, 120).Alpha,
                    Is.EqualTo(1 - (0.5f * 0.75f)).Within(0.003f),
                    "The leading corner must use the product of both eraser coverage axes.");
                Assert.That(
                    ReadPixel(expected, 280, 280).Alpha,
                    Is.EqualTo(1 - (0.5f * 0.25f)).Within(0.003f),
                    "The trailing corner must use the product of both eraser coverage axes.");
            });
        });
    }

    [Test]
    public void QuarterScaleDstInMask_IdentityEffectDoesNotCreateADevicePixelFringe()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource plain = CreateTranslatedMaskGroup(effect: null);
            var identity = new Brightness();
            identity.Amount.CurrentValue = 100;
            using Drawable.Resource filtered = CreateTranslatedMaskGroup(identity);
            using Bitmap expected = RenderScene(0.25f, plain);
            using Bitmap actual = RenderScene(0.25f, filtered);

            Assert.Multiple(() =>
            {
                AssertByteIdentical(
                    expected,
                    actual,
                    "quarter-scale DstIn mask with and without an identity group effect");
                Assert.That(
                    ReadPixel(expected, 37, 50).Alpha,
                    Is.GreaterThan(0),
                    "The first covered device column must remain present.");
                Assert.That(
                    ReadPixel(expected, 36, 50).Alpha,
                    Is.Zero.Within(0.001f),
                    "No leading one-device-pixel fringe may escape the mask footprint.");
                Assert.That(
                    ReadPixel(expected, 63, 50).Alpha,
                    Is.Zero.Within(0.001f),
                    "No trailing one-device-pixel fringe may escape the mask footprint.");
            });
        });
    }

    [Test]
    public void DstOutChild_RemovesOnlyItsIntersectionWithGroupContent()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource backdrop = CreateRectangle(400, 400, Brushes.Blue)
                .ToResource(CompositionContext.Default);
            using Drawable.Resource group = CreateTranslatedDstOutGroup(effect: null);
            using Bitmap actual = RenderScene(backdrop, group);

            Rgba backdropOnly = ReadPixel(actual, 20, 20);
            Rgba content = ReadPixel(actual, 80, 200);
            Rgba erased = ReadPixel(actual, 200, 200);
            Assert.Multiple(() =>
            {
                Assert.That(
                    backdropOnly,
                    Is.EqualTo(new Rgba(0, 0, 1, 1)),
                    "The isolated group must not modify the outer backdrop.");
                Assert.That(
                    content,
                    Is.EqualTo(new Rgba(1, 1, 1, 1)),
                    "Group content outside the DstOut child bounds must remain opaque.");
                Assert.That(
                    erased,
                    Is.EqualTo(backdropOnly),
                    "The DstOut child must reveal the backdrop inside its intersection with group content.");
            });
        });
    }

    [Test]
    public void NestedGroupOpacity_MultipliesCompositeOpacity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var sourceColor = new Color(255, 128, 64, 32);
            var inner = new DrawableGroup();
            inner.Opacity.CurrentValue = 50;
            inner.Children.Add(CreateRectangle(
                240,
                240,
                new SolidColorBrush(sourceColor)));

            var outer = new DrawableGroup();
            outer.Opacity.CurrentValue = 50;
            outer.Children.Add(inner);

            using Drawable.Resource resource = outer.ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(resource);
            Rgba center = ReadPixel(actual, 200, 200);
            const float expectedAlpha = 0.25f;

            Assert.Multiple(() =>
            {
                Assert.That(center.Alpha, Is.EqualTo(expectedAlpha).Within(0.003f));
                Assert.That(
                    center.Red,
                    Is.EqualTo(Color.SrgbToLinear(sourceColor.R / 255f) * expectedAlpha).Within(0.001f));
                Assert.That(
                    center.Green,
                    Is.EqualTo(Color.SrgbToLinear(sourceColor.G / 255f) * expectedAlpha).Within(0.001f));
                Assert.That(
                    center.Blue,
                    Is.EqualTo(Color.SrgbToLinear(sourceColor.B / 255f) * expectedAlpha).Within(0.001f));
            });
        });
    }

    [TestCase(0.75f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void Opacity100Group_IsByteIdenticalToBareAntialiasedContent(float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                effect: null,
                CreateEllipse(241, 163, Brushes.White));
            using Drawable.Resource bare = CreateEllipse(241, 163, Brushes.White)
                .ToResource(CompositionContext.Default);
            using Bitmap fusedGroup = RenderScene(outputScale, FusionMode.Enabled, group);
            using Bitmap fusedBare = RenderScene(outputScale, FusionMode.Enabled, bare);
            using Bitmap replayGroup = RenderScene(outputScale, FusionMode.Disabled, group);
            using Bitmap replayBare = RenderScene(outputScale, FusionMode.Disabled, bare);

            AssertByteIdentical(
                fusedBare,
                fusedGroup,
                $"fused 100%-opacity group antialiasing at scale {outputScale}");
            AssertByteIdentical(
                replayBare,
                replayGroup,
                $"replayed 100%-opacity group antialiasing at scale {outputScale}");
            AssertByteIdentical(
                fusedGroup,
                replayGroup,
                $"fused/replayed group parity at scale {outputScale}");
        });
    }

    [Test]
    public void BareMultiplyDrawable_StillBlendsAgainstBackdrop()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource backdrop = CreateRectangle(400, 400, Brushes.Cyan)
                .ToResource(CompositionContext.Default);
            using Drawable.Resource multiply = CreateRectangle(
                    240,
                    240,
                    Brushes.Magenta,
                    blendMode: BlendMode.Multiply)
                .ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(backdrop, multiply);

            Rgba overlap = ReadPixel(actual, 200, 200);
            Assert.Multiple(() =>
            {
                Assert.That(overlap.Red, Is.Zero.Within(0.001f));
                Assert.That(overlap.Green, Is.Zero.Within(0.001f));
                Assert.That(overlap.Blue, Is.EqualTo(1).Within(0.001f));
                Assert.That(overlap.Alpha, Is.EqualTo(1).Within(0.001f));
            });
        });
    }

    [Test]
    public void SourceBackdropInsideGroup_MatchesBareBackdrop()
    {
        var frame = new PixelSize(256, 144);

        Drawable.Resource[] CreateScene(bool grouped, bool includeBackdrop = true)
        {
            var gradient = new LinearGradientBrush();
            gradient.GradientStops.Add(new GradientStop(Colors.Crimson, 0));
            gradient.GradientStops.Add(new GradientStop(Colors.Gold, 1));

            Drawable.Resource background = CreateRectangle(frame.Width, frame.Height, gradient)
                .ToResource(CompositionContext.Default);
            Drawable.Resource foreground = CreateRectangle(130, 95, Brushes.Navy)
                .ToResource(CompositionContext.Default);
            if (!includeBackdrop)
                return [background, foreground];

            var backdrop = new SourceBackdrop
            {
                Clear = { CurrentValue = false },
                FilterEffect = { CurrentValue = new Invert() },
            };
            Drawable effect = backdrop;
            if (grouped)
            {
                var group = new DrawableGroup();
                group.Children.Add(backdrop);
                effect = group;
            }

            return
            [
                background,
                foreground,
                effect.ToResource(CompositionContext.Default),
            ];
        }

        Drawable.Resource[] expectedResources = CreateScene(grouped: false);
        Drawable.Resource[] actualResources = CreateScene(grouped: true);
        Drawable.Resource[] omittedResources = CreateScene(grouped: false, includeBackdrop: false);
        try
        {
            using Bitmap expected = RenderScene(frame, expectedResources);
            using Bitmap actual = RenderScene(frame, actualResources);
            using Bitmap omitted = RenderScene(frame, omittedResources);

            AssertByteIdentical(
                expected,
                actual,
                "a SourceBackdrop nested in a DrawableGroup");
            Assert.That(
                actual.GetPixelSpan().SequenceEqual(omitted.GetPixelSpan()),
                Is.False,
                "the grouped SourceBackdrop must contribute visible pixels");
        }
        finally
        {
            foreach (Drawable.Resource resource in expectedResources)
                resource.Dispose();
            foreach (Drawable.Resource resource in actualResources)
                resource.Dispose();
            foreach (Drawable.Resource resource in omittedResources)
                resource.Dispose();
        }
    }

    [Test]
    public void GroupOpacityOverAFullTargetClear_StillCompositesTheClearedTarget()
    {
        var frame = new PixelSize(8, 8);
        using Drawable.Resource group = CreateGroup(
            opacity: 50,
            effect: null,
            new ClearOnlyDrawable(Colors.White));

        using Bitmap actual = RenderScene(frame, group);

        Rgba pixel = ReadPixel(actual, 4, 4);
        Assert.Multiple(() =>
        {
            Assert.That(pixel.Alpha, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(pixel.Red, Is.EqualTo(pixel.Alpha).Within(0.01f));
        });
    }

    private static Drawable.Resource CreateGroup(
        float opacity,
        FilterEffect? effect,
        params Drawable[] children)
    {
        var group = new DrawableGroup();
        group.Opacity.CurrentValue = opacity;
        group.FilterEffect.CurrentValue = effect;
        foreach (Drawable child in children)
            group.Children.Add(child);
        return group.ToResource(CompositionContext.Default);
    }

    private static SplitEffect CreateSplitEffect()
    {
        var effect = new SplitEffect();
        effect.HorizontalDivisions.CurrentValue = 2;
        effect.VerticalDivisions.CurrentValue = 2;
        effect.HorizontalSpacing.CurrentValue = 20;
        effect.VerticalSpacing.CurrentValue = 20;
        return effect;
    }

    private static Drawable.Resource CreateTranslatedMaskGroup(FilterEffect? effect)
    {
        var group = new DrawableGroup();
        group.FilterEffect.CurrentValue = effect;
        group.Transform.CurrentValue = new TranslateTransform(0.25f, 0.25f);
        group.Children.Add(CreateRectangle(200, 200, Brushes.Blue));
        group.Children.Add(CreateRectangle(100, 100, Brushes.White, blendMode: BlendMode.DstIn));
        return group.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateTranslatedDstOutGroup(
        FilterEffect? effect,
        float eraserOpacity = 100)
    {
        var eraser = CreateRectangle(
            160,
            160,
            Brushes.White,
            opacity: eraserOpacity,
            blendMode: BlendMode.DstOut);
        eraser.Transform.CurrentValue = new TranslateTransform(0.25f, 0.25f);

        var group = new DrawableGroup();
        group.FilterEffect.CurrentValue = effect;
        group.Transform.CurrentValue = new TranslateTransform(0.25f, 0);
        group.Children.Add(CreateRectangle(320, 320, Brushes.White));
        group.Children.Add(eraser);
        return group.ToResource(CompositionContext.Default);
    }

    private static RectShape CreateRectangle(
        float width,
        float height,
        Brush fill,
        float opacity = 100,
        BlendMode blendMode = BlendMode.SrcOver,
        AlignmentX alignmentX = AlignmentX.Center,
        AlignmentY alignmentY = AlignmentY.Center)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.Fill.CurrentValue = fill;
        shape.Opacity.CurrentValue = opacity;
        shape.BlendMode.CurrentValue = blendMode;
        shape.AlignmentX.CurrentValue = alignmentX;
        shape.AlignmentY.CurrentValue = alignmentY;
        return shape;
    }

    private static EllipseShape CreateEllipse(float width, float height, Brush fill)
    {
        var shape = new EllipseShape();
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.Fill.CurrentValue = fill;
        return shape;
    }

    private static Bitmap RenderScene(params Drawable.Resource[] resources)
        => RenderScene(1, resources);

    private static Bitmap RenderScene(
        out RenderExecutionStatistics statistics,
        params Drawable.Resource[] resources)
        => RenderScene(1, FusionMode.Enabled, out statistics, resources);

    private static Bitmap RenderScene(float outputScale, params Drawable.Resource[] resources)
        => RenderScene(outputScale, FusionMode.Enabled, resources);

    private static Bitmap RenderScene(
        float outputScale,
        FusionMode fusionMode,
        params Drawable.Resource[] resources)
        => RenderScene(outputScale, fusionMode, out _, resources);

    private static Bitmap RenderScene(
        float outputScale,
        FusionMode fusionMode,
        out RenderExecutionStatistics statistics,
        params Drawable.Resource[] resources)
        => RenderScene(s_frame, outputScale, fusionMode, useCpuTarget: false, out statistics, resources);

    private static Bitmap RenderScene(
        PixelSize frame,
        params Drawable.Resource[] resources)
        => RenderScene(frame, 1, FusionMode.Enabled, useCpuTarget: true, out _, resources);

    private static Bitmap RenderScene(
        PixelSize frame,
        float outputScale,
        FusionMode fusionMode,
        bool useCpuTarget,
        out RenderExecutionStatistics statistics,
        params Drawable.Resource[] resources)
    {
        int width = (int)MathF.Ceiling(frame.Width * outputScale);
        int height = (int)MathF.Ceiling(frame.Height * outputScale);
        using RenderTarget target = useCpuTarget
            ? new CpuRenderTarget(width, height)
            : RenderTarget.Create(width, height)
              ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, outputScale, logicalSize: frame.ToSize(1));
        canvas.Clear();

        using var root = new DrawableRenderNode(resources[0]);
        using (var context = new GraphicsContext2D(root, frame.ToSize(1), outputScale))
        {
            foreach (Drawable.Resource resource in resources)
                context.DrawDrawable(resource);
        }

        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, frame.ToSize(1)),
                    OutputScale = outputScale,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    FusionMode = fusionMode,
                },
            });
        renderer.Render(canvas);
        statistics = renderer.LastExecutionStatistics;
        return target.Snapshot();
    }

    private static Rgba ReadPixel(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int offset = ((y * bitmap.Width) + x) * 4;
        return new Rgba(
            (float)BitConverter.UInt16BitsToHalf(pixels[offset]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 1]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 2]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]));
    }

    private static void AssertByteIdentical(Bitmap expected, Bitmap actual, string scenario)
    {
        ReadOnlySpan<byte> expectedPixels = expected.GetPixelSpan();
        ReadOnlySpan<byte> actualPixels = actual.GetPixelSpan();
        bool identical = actualPixels.SequenceEqual(expectedPixels);
        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(
                identical,
                Is.True,
                $"{scenario} must be byte-identical.");
            Assert.That(
                HasFiniteVisibleContent(expected),
                Is.True,
                $"{scenario} must render finite visible content (SC-013 non-vacuity).");
        });
    }

    private static bool HasFiniteVisibleContent(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int i = 3; i < pixels.Length; i += 4)
        {
            float a = (float)BitConverter.UInt16BitsToHalf(pixels[i]);
            if (float.IsFinite(a) && a > 0f)
            {
                return true;
            }
        }
        return false;
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);

    private readonly record struct Rgba(float Red, float Green, float Blue, float Alpha);
}

internal sealed partial class ClearOnlyDrawable(Color color) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.Clear(color);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => availableSize;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}
