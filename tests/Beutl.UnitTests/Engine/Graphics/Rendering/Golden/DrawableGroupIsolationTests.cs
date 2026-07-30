using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
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
            using Bitmap actual = RenderScene(group);
            using Bitmap expected = RenderScene(control);

            AssertByteIdentical(expected, actual, "overlapping children at group opacity 50%");
            Assert.Multiple(() =>
            {
                Assert.That(ReadPixel(actual, 40, 200).Alpha, Is.EqualTo(0.5f).Within(0.003f));
                Assert.That(ReadPixel(actual, 200, 200).Alpha, Is.EqualTo(0.5f).Within(0.003f));
                Assert.That(ReadPixel(actual, 360, 200).Alpha, Is.EqualTo(0.5f).Within(0.003f));
            });
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

            AssertByteIdentical(expected, actual, "identity effect on an overlapping group");
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

            AssertByteIdentical(
                expected,
                actual,
                "fractionally translated DstOut mask with and without an identity group effect");
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

    private static Drawable.Resource CreateTranslatedMaskGroup(FilterEffect? effect)
    {
        var group = new DrawableGroup();
        group.FilterEffect.CurrentValue = effect;
        group.Transform.CurrentValue = new TranslateTransform(0.25f, 0.25f);
        group.Children.Add(CreateRectangle(200, 200, Brushes.Blue));
        group.Children.Add(CreateRectangle(100, 100, Brushes.White, blendMode: BlendMode.DstIn));
        return group.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateTranslatedDstOutGroup(FilterEffect? effect)
    {
        var eraser = CreateRectangle(160, 160, Brushes.White, blendMode: BlendMode.DstOut);
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

    private static Bitmap RenderScene(float outputScale, params Drawable.Resource[] resources)
        => RenderScene(outputScale, FusionMode.Enabled, resources);

    private static Bitmap RenderScene(
        float outputScale,
        FusionMode fusionMode,
        params Drawable.Resource[] resources)
    {
        int width = (int)MathF.Ceiling(s_frame.Width * outputScale);
        int height = (int)MathF.Ceiling(s_frame.Height * outputScale);
        using RenderTarget target = RenderTarget.Create(width, height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, outputScale, logicalSize: s_frame.ToSize(1));
        canvas.Clear();

        using var root = new DrawableRenderNode(resources[0]);
        using (var context = new GraphicsContext2D(root, s_frame.ToSize(1), outputScale))
        {
            foreach (Drawable.Resource resource in resources)
                context.DrawDrawable(resource);
        }

        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(default, s_frame.ToSize(1)),
                OutputScale = outputScale,
                UseRenderCache = false,
                FusionMode = fusionMode,
            });
        renderer.Render(canvas);
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
        });
    }

    private readonly record struct Rgba(float Red, float Green, float Blue, float Alpha);
}
