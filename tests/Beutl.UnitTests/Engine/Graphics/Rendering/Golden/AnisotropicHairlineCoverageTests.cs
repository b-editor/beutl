using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// A drawable whose transform squeezes it below one device pixel keeps the same ink under a filter
/// effect as it does without one.
/// </summary>
/// <remarks>
/// A built-in Skia filter over a vector drawable used to be materialized into a buffer rasterized in
/// the drawable's own space and then composited back through the drawable's minifying transform with
/// a two-tap sampler. At a 10:1 minification that single tap either lands on the hairline or misses
/// it, so the bar arrived at up to 1.5x or as little as 0.0005x of its ink depending only on sub-pixel
/// phase. The remaining loss came from the filter's save layer, whose bound hugged the content: the
/// Ganesh backend keeps only (1 + w) / 2 of a w-device-pixel-wide feature inside such a layer.
///
/// The no-effect render is the reference rather than the analytic 0.6 x s_out, because antialiasing
/// phase alone moves a heavily sub-pixel feature by ~20% and only the effect's contribution is under
/// test here.
/// </remarks>
[NonParallelizable]
[TestFixture]
public class AnisotropicHairlineCoverageTests
{
    /// <summary>Skia publishes coverage in 1/255 steps, which accumulates over a whole bar.</summary>
    private const double InkTolerance = 0.06;

    [TestCase(0.25f)]
    [TestCase(0.333f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void ABlurredHairline_KeepsTheInkItHasWithoutTheBlur(float outputScale)
    {
        AssertEffectPreservesHairlineInk(Blur(), outputScale, "blur");
    }

    [TestCase(0.25f)]
    [TestCase(0.333f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void AShadowedHairline_KeepsTheInkItHasWithoutTheShadow(float outputScale)
    {
        AssertEffectPreservesHairlineInk(WhiteShadowOnly(), outputScale, "shadow-only");
    }

    /// <summary>
    /// A morphology radius that is sub-pixel on the squeezed axis neither grows nor erases the bar: it
    /// resolves against the destination's device grid, where Skia rounds it to no pixels at all.
    /// </summary>
    [TestCase(0.25f)]
    [TestCase(0.333f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void ADilatedHairline_KeepsTheInkItHasWithoutTheDilation(float outputScale)
    {
        AssertEffectPreservesHairlineInk(Dilate(), outputScale, "dilate");
    }

    /// <summary>
    /// The default authoring shape: <see cref="Drawable"/>'s constructor installs a
    /// <see cref="FilterEffectGroup"/>, so two stacked effects arrive as two filter segments and the
    /// outer one's input is the inner segment rather than the drawable.
    /// </summary>
    [TestCase(0.25f)]
    [TestCase(0.333f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void AHairlineUnderStackedEffects_KeepsTheInkItHasWithoutThem(float outputScale)
    {
        AssertEffectPreservesHairlineInk(
            Group(Blur(), WhiteShadowOnly()),
            outputScale,
            "blur over shadow-only");
    }

    /// <summary>
    /// The anisotropic rig the family was reported against: the same 10% squeeze in x with a 20x
    /// stretch in y, so the working density the effect resolves cannot describe both axes at once.
    /// </summary>
    [TestCase(0.25f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [Category("GpuPassFusionGpu")]
    public void AnAnisotropicallyScaledHairline_KeepsItsInkUnderABlur(float outputScale)
    {
        AssertEffectPreservesHairlineInk(Blur(), outputScale, "anisotropic blur", scaleY: 2000f);
    }

    private static void AssertEffectPreservesHairlineInk(
        FilterEffect effect,
        float outputScale,
        string scenario,
        float scaleY = 100f)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            double unfiltered = MeasureInk(null, scaleY, outputScale);
            double filtered = MeasureInk(effect, scaleY, outputScale);

            Assert.That(
                unfiltered,
                Is.GreaterThan(0),
                "the unfiltered hairline has to render, or the comparison proves nothing.");
            Assert.That(
                filtered / unfiltered,
                Is.EqualTo(1d).Within(InkTolerance),
                $"{scenario} at output scale {outputScale} painted {filtered:F4} of ink where the same "
                + $"content without the effect paints {unfiltered:F4}; an effect that conserves ink "
                + "must not gain or lose it to the transform the drawable is composited through.");
        });
    }

    private static FilterEffect Group(params FilterEffect[] children)
    {
        var group = new FilterEffectGroup();
        foreach (FilterEffect child in children)
            group.Children.Add(child);
        return group;
    }

    private static FilterEffect Blur()
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(1, 1);
        return blur;
    }

    private static FilterEffect Dilate()
    {
        var dilate = new Dilate();
        dilate.RadiusX.CurrentValue = 1f;
        dilate.RadiusY.CurrentValue = 1f;
        return dilate;
    }

    private static FilterEffect WhiteShadowOnly()
    {
        var shadow = new DropShadow();
        shadow.ShadowOnly.CurrentValue = true;
        shadow.Sigma.CurrentValue = new Size(1, 1);
        shadow.Color.CurrentValue = Colors.White;
        return shadow;
    }

    /// <summary>
    /// Renders a 6 x 100 bar squeezed to 0.6 logical units wide — below one device pixel at every
    /// output scale sampled here — and sums the alpha it leaves on the frame.
    /// </summary>
    private static double MeasureInk(FilterEffect? effect, float scaleY, float outputScale)
    {
        var rectangle = new RectShape();
        rectangle.Width.CurrentValue = 6f;
        // Keep the bar 100 logical units tall whatever the y stretch, so it never leaves the frame.
        rectangle.Height.CurrentValue = 10000f / scaleY;
        rectangle.Fill.CurrentValue = new SolidColorBrush(Colors.White);
        rectangle.AlignmentX.CurrentValue = AlignmentX.Left;
        rectangle.AlignmentY.CurrentValue = AlignmentY.Top;
        rectangle.TransformOrigin.CurrentValue = RelativePoint.TopLeft;
        if (effect is not null)
            rectangle.FilterEffect.CurrentValue = effect;

        var group = new TransformGroup();
        var translate = new TranslateTransform();
        translate.X.CurrentValue = 128;
        translate.Y.CurrentValue = 42;
        // A TransformGroup applies its last child first, so the squeeze runs in the shape's own space.
        group.Children.Add(translate);
        var scale = new ScaleTransform();
        scale.ScaleX.CurrentValue = 10f;
        scale.ScaleY.CurrentValue = scaleY;
        group.Children.Add(scale);
        rectangle.Transform.CurrentValue = group;

        var scene = new Scene(640, 360, "hairline") { Uri = new Uri("file:///hairline/scene") };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 0,
            IsEnabled = true,
            Uri = new Uri("file:///hairline/element"),
        };
        element.AddObject(rectangle);
        scene.Children.Add(element);

        using var renderer = new SceneRenderer(scene, outputScale, false, outputScale * 2f)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.Zero));
        using Bitmap bitmap = renderer.Snapshot();

        double ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y)[..(bitmap.Width * 4)];
            for (int x = 3; x < row.Length; x += 4)
                ink += (float)BitConverter.UInt16BitsToHalf(row[x]);
        }

        return ink;
    }
}
