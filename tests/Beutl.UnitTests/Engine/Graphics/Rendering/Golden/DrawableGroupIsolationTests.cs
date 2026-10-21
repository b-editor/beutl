using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class DrawableGroupIsolationTests
{
    private static readonly PixelSize s_frame = new(400, 400);

    [Test]
    public void NestedHalfOpacityGroups_ApplyEachFactorOnce()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var inner = new DrawableGroup();
            inner.Opacity.Value = 50;
            inner.Children.Add(CreateRectangle(400, 240, Brushes.White));
            using Drawable.Resource outer = CreateGroup(50, null, inner);
            using Bitmap bitmap = RenderScene(outer);
            Assert.That(ImageMetrics.ReadPixel(bitmap, 200, 200).Alpha, Is.EqualTo(0.25f).Within(0.003f));
        });
    }

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

            GoldenImageHarness.AssertByteIdentical(
                expected,
                actual,
                "overlapping children at group opacity 50%",
                requireVisibleContent: true);
            Rgba left = ImageMetrics.ReadPixel(actual, 40, 200);
            Rgba overlap = ImageMetrics.ReadPixel(actual, 200, 200);
            Rgba right = ImageMetrics.ReadPixel(actual, 360, 200);
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
            identity.Amount.Value = 100;
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

            // Both paths carry the group opacity in float precision, so the identity effect reproduces the
            // composition exactly.
            GoldenImageHarness.AssertByteIdentical(
                expected,
                actual,
                "identity effect on an overlapping group",
                requireVisibleContent: true);
            Assert.That(
                ImageMetrics.ReadPixel(actual, 200, 200).Alpha,
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
            left.FilterEffect.Value = CreateSplitEffect();
            RectShape right = CreateRectangle(
                240,
                240,
                Brushes.SteelBlue,
                alignmentX: AlignmentX.Right);
            right.FilterEffect.Value = CreateSplitEffect();
            using Drawable.Resource childrenFiltered = CreateGroup(
                opacity: 100,
                effect: null,
                left,
                right);

            using Bitmap actual = RenderScene(groupFiltered);
            using Bitmap expected = RenderScene(childrenFiltered);

            GoldenImageHarness.AssertByteIdentical(
                expected,
                actual,
                "a group SplitEffect and an equivalent SplitEffect on each child",
                requireVisibleContent: true);
        });
    }

    [Test]
    public void BlurOnGroupWithHairlineChild_ProducesOnlyFiniteChannels()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var blur = new Blur();
            blur.Sigma.Value = new Size(6, 6);
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
                Assert.That(
                    ImageMetrics.HasFiniteVisibleContent(actual),
                    Is.True,
                    "The fixture must render visible content.");
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
            offFrame.Transform.Value = new TranslateTransform(-180, -120);

            var blur = new Blur();
            blur.Sigma.Value = new Size(10, 10);
            var shadow = new DropShadow();
            shadow.Position.Value = new Point(10, 10);
            shadow.Sigma.Value = new Size(20, 20);
            shadow.Color.Value = Colors.Black;
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
                Assert.That(
                    ImageMetrics.HasFiniteVisibleContent(actual),
                    Is.True,
                    "The fixture must render visible content.");
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

            GoldenImageHarness.AssertByteIdentical(
                expected,
                actual,
                $"Multiply child against an isolated group at opacity {opacity}%",
                requireVisibleContent: true);
            Assert.That(
                ImageMetrics.ReadPixel(actual, 20, 20),
                Is.EqualTo(ImageMetrics.ReadPixel(expected, 20, 20)),
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

            Rgba center = ImageMetrics.ReadPixel(actual, 200, 200);
            Assert.Multiple(() =>
            {
                Assert.That(center.Alpha, Is.EqualTo(128f / 255f).Within(0.002f));
                Assert.That(center.Red, Is.EqualTo(center.Alpha).Within(0.002f));
                Assert.That(ImageMetrics.ReadPixel(actual, 20, 20).Alpha, Is.Zero);
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
            gradient.Opacity.Value = 50;
            gradient.GradientStops.Add(new GradientStop(Colors.White, 0));
            gradient.GradientStops.Add(new GradientStop(Colors.White, 1));
            using Drawable.Resource group = CreateGroup(
                opacity: 100,
                effect: null,
                CreateRectangle(240, 240, Brushes.White),
                CreateRectangle(240, 240, gradient, blendMode: BlendMode.DstIn));
            using Bitmap actual = RenderScene(group);

            Rgba center = ImageMetrics.ReadPixel(actual, 200, 200);
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
                Width = { Value = width },
                Height = { Value = height },
            };
            using Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
            var nonEmptyGeometry = new RectGeometry
            {
                Width = { Value = 4 },
                Height = { Value = 4 },
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

            GoldenImageHarness.AssertByteIdentical(
                expected,
                actual,
                $"fractional {width}x{height} {blendMode} rectangle",
                requireVisibleContent: true);
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
                Assert.That(ImageMetrics.ReadPixel(isolated, 200, 200).Alpha, Is.EqualTo(1).Within(0.001f));
                Assert.That(
                    ImageMetrics.ReadPixel(isolated, 80, 200).Alpha,
                    Is.Zero,
                    "Content outside the DstIn window must be removed across the group scope.");
                Assert.That(
                    ImageMetrics.ReadPixel(composited, 20, 20),
                    Is.EqualTo(ImageMetrics.ReadPixel(composited, 80, 200)),
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
            identity.Amount.Value = 100;
            using Drawable.Resource filtered = CreateTranslatedMaskGroup(identity);

            using Bitmap expected = RenderScene(plain);
            using Bitmap actual = RenderScene(filtered);

            Assert.Multiple(() =>
            {
                GoldenImageHarness.AssertByteIdentical(
                    expected,
                    actual,
                    "fractionally translated DstIn mask with an identity group effect",
                    requireVisibleContent: true);
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 150, 150).Alpha,
                    Is.EqualTo(0.75f * 0.75f).Within(0.003f),
                    "A corner pixel must contain the product of the horizontal and vertical mask coverage.");
                Assert.That(
                    ImageMetrics.ReadPixel(actual, 150, 150).Alpha,
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
            identity.Amount.Value = 100;
            using Drawable.Resource filtered = CreateTranslatedDstOutGroup(identity);

            using Bitmap expected = RenderScene(plain);
            using Bitmap actual = RenderScene(filtered);

            Assert.Multiple(() =>
            {
                GoldenImageHarness.AssertByteIdentical(
                    expected,
                    actual,
                    "fractionally translated DstOut mask with and without an identity group effect",
                    requireVisibleContent: true);
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 120, 200).Alpha,
                    Is.EqualTo(0.5f).Within(0.003f),
                    "The leading vertical eraser edge must preserve its absolute half-pixel coverage.");
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 200, 120).Alpha,
                    Is.EqualTo(0.25f).Within(0.003f),
                    "The leading horizontal eraser edge must retain one minus 75% eraser coverage.");
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 120, 120).Alpha,
                    Is.EqualTo(1 - (0.5f * 0.75f)).Within(0.003f),
                    "The leading corner must use the product of both eraser coverage axes.");
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 280, 280).Alpha,
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
            identity.Amount.Value = 100;
            using Drawable.Resource filtered = CreateTranslatedMaskGroup(identity);
            using Bitmap expected = RenderScene(0.25f, plain);
            using Bitmap actual = RenderScene(0.25f, filtered);

            Assert.Multiple(() =>
            {
                GoldenImageHarness.AssertByteIdentical(
                    expected,
                    actual,
                    "quarter-scale DstIn mask with and without an identity group effect",
                    requireVisibleContent: true);
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 37, 50).Alpha,
                    Is.GreaterThan(0),
                    "The first covered device column must remain present.");
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 36, 50).Alpha,
                    Is.Zero.Within(0.001f),
                    "No leading one-device-pixel fringe may escape the mask footprint.");
                Assert.That(
                    ImageMetrics.ReadPixel(expected, 63, 50).Alpha,
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

            Rgba backdropOnly = ImageMetrics.ReadPixel(actual, 20, 20);
            Rgba content = ImageMetrics.ReadPixel(actual, 80, 200);
            Rgba erased = ImageMetrics.ReadPixel(actual, 200, 200);
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
            inner.Opacity.Value = 50;
            inner.Children.Add(CreateRectangle(
                240,
                240,
                new SolidColorBrush(sourceColor)));

            var outer = new DrawableGroup();
            outer.Opacity.Value = 50;
            outer.Children.Add(inner);

            using Drawable.Resource resource = outer.ToResource(CompositionContext.Default);
            using Bitmap actual = RenderScene(resource);
            Rgba center = ImageMetrics.ReadPixel(actual, 200, 200);
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

            GoldenImageHarness.AssertByteIdentical(
                fusedBare,
                fusedGroup,
                $"fused 100%-opacity group antialiasing at scale {outputScale}",
                requireVisibleContent: true);
            GoldenImageHarness.AssertByteIdentical(
                replayBare,
                replayGroup,
                $"replayed 100%-opacity group antialiasing at scale {outputScale}",
                requireVisibleContent: true);
            GoldenImageHarness.AssertByteIdentical(
                fusedGroup,
                replayGroup,
                $"fused/replayed group parity at scale {outputScale}",
                requireVisibleContent: true);
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

            Rgba overlap = ImageMetrics.ReadPixel(actual, 200, 200);
            Assert.Multiple(() =>
            {
                Assert.That(overlap.Red, Is.Zero.Within(0.001f));
                Assert.That(overlap.Green, Is.Zero.Within(0.001f));
                Assert.That(overlap.Blue, Is.EqualTo(1).Within(0.001f));
                Assert.That(overlap.Alpha, Is.EqualTo(1).Within(0.001f));
            });
        });
    }

    [TestCase(100f)]
    [TestCase(50f)]
    public void SourceBackdropInsideGroup_MatchesBareBackdrop(float opacity)
    {
        var frame = new PixelSize(256, 144);

        Drawable.Resource[] CreateScene(bool grouped, bool includeBackdrop = true, float? backdropOpacity = null)
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
                Clear = { Value = false },
                FilterEffect = { Value = new Invert() },
            };
            float effectOpacity = backdropOpacity ?? opacity;
            Drawable effect = backdrop;
            if (grouped)
            {
                var group = new DrawableGroup();
                group.Children.Add(backdrop);
                group.Opacity.Value = effectOpacity;
                effect = group;
            }
            else
            {
                backdrop.Opacity.Value = effectOpacity;
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
        Drawable.Resource[] fullResources = CreateScene(grouped: false, backdropOpacity: 100);
        try
        {
            using Bitmap expected = RenderScene(frame, expectedResources);
            using Bitmap actual = RenderScene(frame, actualResources);
            using Bitmap omitted = RenderScene(frame, omittedResources);
            using Bitmap full = RenderScene(frame, fullResources);

            if (opacity == 100)
            {
                GoldenImageHarness.AssertByteIdentical(
                    expected,
                    actual,
                    "a SourceBackdrop nested in a DrawableGroup",
                    requireVisibleContent: true);
            }
            else
            {
                // SourceBackdrop applies its own Opacity in its Render override (SourceBackdropOpacityTests), so
                // the bare and the grouped form must both be the linear blend of the untouched opaque scene and
                // the full-strength effect. The control is built from those two, not from either form under test.
                var fullPixels = full.GetPixelSpan<Half>();
                var background = omitted.GetPixelSpan<Half>();
                var grouped = actual.GetPixelSpan<Half>();
                var bare = expected.GetPixelSpan<Half>();
                float factor = opacity / 100f;
                for (int i = 0; i < fullPixels.Length; i++)
                {
                    float reference = (float)background[i] * (1 - factor) + (float)fullPixels[i] * factor;
                    Assert.That((float)grouped[i], Is.EqualTo(reference).Within(0.002f), $"grouped component {i}");
                    Assert.That((float)bare[i], Is.EqualTo(reference).Within(0.002f), $"bare component {i}");
                }
            }
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
            foreach (Drawable.Resource resource in fullResources)
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

        Rgba pixel = ImageMetrics.ReadPixel(actual, 4, 4);
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
        group.Opacity.Value = opacity;
        group.FilterEffect.Value = effect;
        foreach (Drawable child in children)
            group.Children.Add(child);
        return group.ToResource(CompositionContext.Default);
    }

    private static SplitEffect CreateSplitEffect()
    {
        var effect = new SplitEffect();
        effect.HorizontalDivisions.Value = 2;
        effect.VerticalDivisions.Value = 2;
        effect.HorizontalSpacing.Value = 20;
        effect.VerticalSpacing.Value = 20;
        return effect;
    }

    private static Drawable.Resource CreateTranslatedMaskGroup(FilterEffect? effect)
    {
        var group = new DrawableGroup();
        group.FilterEffect.Value = effect;
        group.Transform.Value = new TranslateTransform(0.25f, 0.25f);
        group.Children.Add(CreateRectangle(200, 200, Brushes.Blue));
        group.Children.Add(CreateRectangle(100, 100, Brushes.White, blendMode: BlendMode.DstIn));
        return group.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateTranslatedDstOutGroup(FilterEffect? effect)
    {
        var eraser = CreateRectangle(
            160,
            160,
            Brushes.White,
            blendMode: BlendMode.DstOut);
        eraser.Transform.Value = new TranslateTransform(0.25f, 0.25f);

        var group = new DrawableGroup();
        group.FilterEffect.Value = effect;
        group.Transform.Value = new TranslateTransform(0.25f, 0);
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
        AlignmentX alignmentX = AlignmentX.Center)
    {
        var shape = new RectShape();
        shape.Width.Value = width;
        shape.Height.Value = height;
        shape.Fill.Value = fill;
        shape.Opacity.Value = opacity;
        shape.BlendMode.Value = blendMode;
        shape.AlignmentX.Value = alignmentX;
        shape.AlignmentY.Value = AlignmentY.Center;
        return shape;
    }

    private static EllipseShape CreateEllipse(float width, float height, Brush fill)
    {
        var shape = new EllipseShape();
        shape.Width.Value = width;
        shape.Height.Value = height;
        shape.Fill.Value = fill;
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

        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, frame.ToSize(1)),
            OutputScale = outputScale,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            FusionMode = fusionMode,
        });
        renderer.Render(canvas);
        statistics = renderer.LastExecutionStatistics;
        return target.Snapshot();
    }

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
