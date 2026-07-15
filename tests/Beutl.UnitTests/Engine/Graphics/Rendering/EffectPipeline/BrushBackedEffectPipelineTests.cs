using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the brush-backed nested-render contract for migrated filter effects (feature 004, contract A1 / FR-001,
/// FR-017): a <see cref="DrawableBrush"/> used inside an effect must (a) never render at describe time, and
/// (b) render its work through the OWNING renderer's <see cref="PipelineDiagnostics"/> so the nested activity is
/// observable on <c>IRenderer.Diagnostics</c>, not swallowed by a throwaway per-brush instance. Regression guard
/// for the boundary drop where <see cref="DisplacementMapTransform"/> probed a brush shader at describe time and
/// <see cref="BrushConstructor"/> built its inner <see cref="RenderNodeProcessor"/> without the parent diagnostics.
/// </summary>
[NonParallelizable]
[TestFixture]
public class BrushBackedEffectPipelineTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // (b) FR-017: an effect whose brush is a DrawableBrush wrapping a drawable that itself carries a FilterEffect
    // (a Gamma pass) must count that nested pass on the PARENT diagnostics. Before the fix the inner processor owned
    // a throwaway PipelineDiagnostics, so the nested Gamma pass was invisible and the parent saw only the outer
    // BlendEffect's single geometry pass.
    [Test]
    public void DrawableBrushHostedEffect_CountsNestedWorkOnParentDiagnostics()
    {
        PipelineDiagnosticsSnapshot frame = RenderOneFrameWithPool(MakeBlendOverDrawableBrushHostingGamma);

        // Outer BlendEffect geometry pass = 1; nested Gamma pass inside the DrawableBrush = 1 → the parent must
        // observe BOTH. Before the fix this read 1 (only the outer pass).
        Assert.That(frame.GpuPasses, Is.GreaterThanOrEqualTo(2),
            "the nested effect inside the DrawableBrush must count its GPU pass on the parent diagnostics (FR-017)");
    }

    // (a) FR-001 / contract A1: Describe must not render. A DrawableBrush displacement map wrapping a
    // render-counting drawable must NOT have that drawable rendered while the effect is only describing its graph.
    // Before the fix, DisplacementMapTransform.BuildMapChild probed the brush shader (rendering the drawable) at
    // describe time.
    [Test]
    public void Describe_DoesNotRenderDrawableBrushDisplacementMap()
    {
        VulkanTestEnvironment.EnsureAvailable();
        int renderCountAfterDescribe = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var counter = new RenderCountingDrawable();
            var effect = new DisplacementMapEffect();
            effect.DisplacementMap.CurrentValue = new DrawableBrush(counter);

            var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);
            var builder = new EffectGraphBuilder(
                new Rect(0, 0, 64, 64), outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery, maxWorkingScale: float.PositiveInfinity);
            resource.GetOriginal().Describe(builder, resource);
            return counter.RenderCount;
        });

        Assert.That(renderCountAfterDescribe, Is.Zero,
            "Describe must not render the displacement-map drawable (contract A1 / FR-001)");
    }

    // Guards against the opposite regression: the map render must still happen at EXECUTION (the deferred child
    // factory), so removing the describe-time probe must not silently drop the displacement work.
    [Test]
    public void Execution_RendersDrawableBrushDisplacementMap()
    {
        VulkanTestEnvironment.EnsureAvailable();
        int renderCount = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var counter = new RenderCountingDrawable();
            RenderOneFrame(() => MakeDisplacementOverDrawableBrushMap(counter));
            return counter.RenderCount;
        });

        Assert.That(renderCount, Is.GreaterThanOrEqualTo(1),
            "the displacement map must be rendered at execution time (the deferred child factory)");
    }

    private static PipelineDiagnosticsSnapshot RenderOneFrameWithPool(Func<Drawable.Resource> makeScene)
    {
        VulkanTestEnvironment.EnsureAvailable();
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            PixelSize size = SceneFixtures.ReferenceSize;
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            pool.Trim(0);
            diagnostics.Reset();

            using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
            canvas.Clear(Colors.Black);

            Drawable.Resource resource = makeScene();
            using var node = new DrawableRenderNode(resource);
            using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
            {
                resource.GetOriginal().Render(ctx, resource);
            }

            var processor = new RenderNodeProcessor(
                pool, node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f,
                diagnostics: diagnostics);
            RenderNodeOperation[] ops = processor.PullToRoot();
            foreach (RenderNodeOperation op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }

            return diagnostics.Snapshot();
        });
    }

    private static void RenderOneFrame(Func<Drawable.Resource> makeScene)
    {
        PixelSize size = SceneFixtures.ReferenceSize;
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: size.ToSize(1));
        canvas.Clear(Colors.Black);

        Drawable.Resource resource = makeScene();
        using var node = new DrawableRenderNode(resource);
        using (var ctx = new GraphicsContext2D(node, size.ToSize(1), 1f))
        {
            resource.GetOriginal().Render(ctx, resource);
        }

        var processor = new RenderNodeProcessor(node, useRenderCache: false, RenderIntent.Delivery, outputScale: 1f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        foreach (RenderNodeOperation op in ops)
        {
            op.Render(canvas);
            op.Dispose();
        }
    }

    private static Drawable.Resource MakeBlendOverDrawableBrushHostingGamma()
    {
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.5f;

        var innerFill = new LinearGradientBrush();
        innerFill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        innerFill.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        var inner = new RectShape();
        inner.Width.CurrentValue = 120;
        inner.Height.CurrentValue = 80;
        inner.Fill.CurrentValue = innerFill;
        inner.FilterEffect.CurrentValue = gamma;

        var blend = new BlendEffect();
        blend.Brush.CurrentValue = new DrawableBrush(inner);
        blend.BlendMode.CurrentValue = BlendMode.SrcOver;

        var outer = new RectShape();
        outer.Width.CurrentValue = 200;
        outer.Height.CurrentValue = 150;
        outer.Fill.CurrentValue = new SolidColorBrush(Colors.White);
        outer.FilterEffect.CurrentValue = blend;
        return outer.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource MakeDisplacementOverDrawableBrushMap(RenderCountingDrawable counter)
    {
        var effect = new DisplacementMapEffect();
        effect.DisplacementMap.CurrentValue = new DrawableBrush(counter);

        var shape = new RectShape();
        shape.Width.CurrentValue = 120;
        shape.Height.CurrentValue = 80;
        shape.Fill.CurrentValue = new SolidColorBrush(Colors.White);
        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }
}

// Counts how many times it is asked to draw. Top-level partial because EngineObjectResourceGenerator does not
// support nested types (mirrors FaultingDrawable in RendererExceptionSafetyTests).
internal sealed partial class RenderCountingDrawable : Drawable
{
    public int RenderCount { get; private set; }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(20, 20);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        RenderCount++;
    }
}
