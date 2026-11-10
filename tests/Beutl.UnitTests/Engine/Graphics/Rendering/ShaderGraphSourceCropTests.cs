using Beutl.Graphics;
using Beutl.Composition;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture, NonParallelizable]
public sealed class ShaderGraphSourceCropTests
{
    private static readonly Rect s_bounds = new(-11555.459f, -3329.1016f, 22556.04f, 12687.772f);

    // From a frame whose export failed: the copy asked for 16385 px on a device limited to 16384.
    private static readonly PixelRect s_target = new(-8394, -2419, 16384, 9216);
    private static readonly PixelRect s_footprint = new(-8395, -2419, 16385, 9216);

    [Test]
    public void A_partial_source_is_not_copied_for_rounding_alone()
    {
        Assert.That(RenderRequestExecutor.RequiresSourceCrop(true, s_target, s_footprint, s_bounds, s_bounds), Is.False);
    }

    [Test]
    public void A_partial_source_on_its_footprint_is_used_as_it_is()
    {
        Assert.That(RenderRequestExecutor.RequiresSourceCrop(true, s_footprint, s_footprint, s_bounds, s_bounds), Is.False);
    }

    [Test]
    public void A_partial_source_with_an_apron_is_cut_to_its_footprint()
    {
        var apron = new PixelRect(s_footprint.X - 1, s_footprint.Y - 1, s_footprint.Width + 2, s_footprint.Height + 2);

        Assert.That(RenderRequestExecutor.RequiresSourceCrop(true, apron, s_footprint, s_bounds, s_bounds), Is.True);
    }

    [Test]
    public void A_complete_source_still_has_to_match_its_footprint()
    {
        Assert.That(RenderRequestExecutor.RequiresSourceCrop(false, s_target, s_footprint, s_bounds, s_bounds), Is.True);
    }

    [Test]
    public void A_part_of_a_picture_is_cut_for_a_graph_that_reads_the_whole()
    {
        var part = new Rect(s_bounds.X, s_bounds.Y, s_bounds.Width / 2, s_bounds.Height);

        Assert.That(RenderRequestExecutor.RequiresSourceCrop(false, s_footprint, s_footprint, part, s_bounds), Is.True);
    }

    [TestCase(0f)]
    [TestCase(0.25f)]
    public void A_whole_source_graph_renders_an_imperative_source_at_the_limit(float fraction)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            int limit = BufferDimensionBudget.EngineCeiling.MaxDimension;
            if (BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation).MaxDimension < limit)
                Assert.Ignore("This case needs a device that can attach the engine's maximum buffer dimension.");

            using RenderTarget source = RenderTarget.Create(limit, 4)!;
            using (var fill = new ImmediateCanvas(source, RenderIntent.Delivery))
                fill.Clear(Colors.White);
            var bounds = new Rect(0.5f, 0, limit + fraction, 4);
            var effect = new EffectItemSuffixCallbackFilterEffect((context, _) => context.CustomEffect(
                (Source: source, Bounds: bounds),
                static (state, execution) =>
                {
                    execution.Targets.Clear();
                    execution.Targets.Add(new EffectTarget(state.Source, state.Bounds, EffectiveScale.At(1)));
                },
                static (state, _) => state.Bounds));
            using FilterEffect.Resource layer = effect.ToResource(CompositionContext.Default);
            using FilterEffect.Resource shader = new GLSLScriptEffect().ToResource(CompositionContext.Default);
            var imperative = new FilterEffectRenderNode(layer);
            imperative.AddChild(new RectangleRenderNode(new Rect(0, 0, 1, 1), Brushes.Resource.White, null));
            using var root = new FilterEffectRenderNode(shader);
            root.AddChild(imperative);
            using RenderTarget target = RenderTarget.Create(64, 8)!;
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery);
            canvas.Clear(Colors.Black);
            using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(0, 0, 64, 8),
                OutputScale = 1,
                CacheOptions = RenderCacheOptions.Disabled
            });

            renderer.Render(canvas);

            using Bitmap bitmap = target.Snapshot();
            float red = (float)bitmap.GetPixelSpan<Half>()[(2 * 64 + 8) * 4];
            Assert.That(red, Is.GreaterThan(0.95f), "The whole-source graph must draw the source, including at a fractional origin.");
            if (Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                bitmap.Save(Path.Combine(directory, $"whole-source-crop-{fraction}.png"));
            }
        });
    }
}
