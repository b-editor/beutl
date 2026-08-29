using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Pins what <see cref="Threshold"/> paints where its input covers nothing, and that the hit-test contract it
/// declares still describes those pixels.
/// </summary>
/// <remarks>
/// The entry point returns <c>half4(t)</c> without consulting the input alpha, so a fully transparent
/// premultiplied pixel - luma 0 - leaves with whatever the threshold curve says about 0. An ellipse inside a
/// square gives the stage transparent pixels to answer for while keeping visible content in the same pass.
/// <see cref="Threshold.Smoothness"/> defaults to 0, which collapses the band onto the threshold: that is a
/// divide by zero inside <c>smoothstep</c>, whose result for equal edges is undefined in the shading
/// languages, so the grid below runs every combination of the two properties rather than the degenerate row
/// alone.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class ThresholdDegenerateBandTests
{
    private const int Size = 100;

    // Inside the ellipse's bounding box but outside the ellipse, so the stage is handed a transparent pixel.
    private static readonly Point s_corner = new(2, 2);

    private static readonly float[] s_values = [0f, 25f, 50f, 100f];
    private static readonly float[] s_smoothnesses = [0f, 1f, 50f, 100f];

    [TestCaseSource(nameof(ParameterGrid))]
    [Category("GpuPassFusionGpu")]
    public void EveryParameterPair_PaintsFinitePixels(float value, float smoothness, float strength)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(Effect(value, smoothness, strength));

            Assert.That(
                ImageMetrics.FirstNonFinite(("threshold", rendered)),
                Is.Null,
                $"Threshold Value = {value}, Smoothness = {smoothness}, Strength = {strength} "
                + "emitted a non-finite component.");
        });
    }

    /// <remarks>
    /// <see cref="Threshold"/> is the one built-in whose entry point can hand a fully transparent pixel a
    /// visible alpha, so it declares <see cref="RenderHitTestContract.OutputBounds"/> exactly at the settings
    /// where that happens and forwards the test to its input otherwise. The C# that decides which is a mirror
    /// of the SkSL, so it can only stay right by being measured against the pixels the SkSL produced.
    /// </remarks>
    [TestCaseSource(nameof(ParameterGrid))]
    [Category("GpuPassFusionGpu")]
    public void TheHitTestContract_AgreesWithWhatTheStagePainted(float value, float smoothness, float strength)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(Effect(value, smoothness, strength));
            bool painted = Alpha(rendered, (int)s_corner.X, (int)s_corner.Y) > 0f;

            Assert.That(
                HitTest(Effect(value, smoothness, strength)),
                Is.EqualTo(painted),
                $"Threshold Value = {value}, Smoothness = {smoothness}, Strength = {strength}: the hit test "
                + $"{(painted ? "misses a corner the stage painted" : "claims a corner the stage left clear")}.");
        });
    }

    /// <remarks>
    /// The band is centred on the threshold, so it evaluates to exactly 0.5 there at every positive width.
    /// A collapsed band keeping that value is what makes the property continuous across zero; a conventional
    /// <c>step</c> would answer 1 and turn the whole transparent surround opaque white as the slider lands
    /// on zero.
    /// </remarks>
    [TestCase(0f)]
    [TestCase(1f)]
    [TestCase(50f)]
    [TestCase(100f)]
    [Category("GpuPassFusionGpu")]
    public void AZeroThreshold_LeavesATransparentPixelHalfOpaqueAtEverySmoothness(float smoothness)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap rendered = Render(Effect(0f, smoothness, 100f));

            Assert.That(
                Alpha(rendered, (int)s_corner.X, (int)s_corner.Y),
                Is.EqualTo(0.5f).Within(0.003f),
                $"Threshold Value = 0, Smoothness = {smoothness} moved the value the stage carries at its "
                + "own threshold.");
        });
    }

    private static IEnumerable<TestCaseData> ParameterGrid()
    {
        foreach (float value in s_values)
        {
            foreach (float smoothness in s_smoothnesses)
                yield return new TestCaseData(value, smoothness, 100f);
        }

        // Strength scales the result away from the input luma, so it decides whether a transparent pixel is
        // painted at all; the collapsed band has to stay finite and honest across it too.
        yield return new TestCaseData(0f, 0f, 0f);
        yield return new TestCaseData(0f, 0f, 50f);
    }

    private static Threshold Effect(float value, float smoothness, float strength)
        => new()
        {
            Value = { CurrentValue = value },
            Smoothness = { CurrentValue = smoothness },
            Strength = { CurrentValue = strength },
        };

    private static bool HitTest(FilterEffect effect)
    {
        using RenderNode root = BuildTree(effect);
        using var renderer = new RenderNodeRenderer(root, Options());
        return renderer.HitTest(s_corner);
    }

    private static Bitmap Render(FilterEffect effect)
    {
        using RenderTarget target = RenderTarget.Create(Size, Size)
            ?? throw new InvalidOperationException("Could not allocate the threshold render target.");
        using var canvas = new ImmediateCanvas(
            target, RenderIntent.Preview, 1f, logicalSize: new Size(Size, Size));
        canvas.Clear(Colors.Transparent);

        using (RenderNode root = BuildTree(effect))
        using (var renderer = new RenderNodeRenderer(root, Options()))
        {
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static RenderNode BuildTree(FilterEffect effect)
    {
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(new EllipseRenderNode(new Rect(0, 0, Size, Size), Brushes.Resource.White, null));
        return node;
    }

    private static RenderNodeRendererOptions Options()
        => new()
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(0, 0, Size, Size),
                OutputScale = 1f,
                MaxWorkingScale = 1f,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        };

    private static float Alpha(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
        return (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
    }
}
