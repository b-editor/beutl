using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderNodeRendererLifetimeTests
{
    [Test]
    public void Rasterize_EmptySelection_ReturnsEmptyResultWithoutAllocating()
    {
        var bounds = new Rect(0, 0, 8, 8);
        var emptySelection = new Rect(3, 4, 0, 2);
        using var source = new CpuRenderTarget(8, 8);
        using var node = new ShaderNode(source, bounds);
        using var factory = new FailureTestTargetFactory();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = bounds,
            RequestedRegion = emptySelection,
            OutputScale = 2,
            MaxWorkingScale = 2,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(emptySelection));
            Assert.That(rasterization.OutputScale, Is.EqualTo(2));
            Assert.That(rasterization.IsEmpty, Is.True);
            Assert.That(rasterization.Bitmap, Is.Null);
            Assert.That(factory.Targets, Is.Empty);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [TestCase(3, 4, 0, 2)]
    [TestCase(20, 20, 2, 2)]
    public void Render_EmptySelection_DrawsNothingAndLeavesNoLease(
        int x,
        int y,
        int width,
        int height)
    {
        var bounds = new Rect(0, 0, 8, 8);
        using var source = new CpuRenderTarget(8, 8);
        using var node = new ShaderNode(source, bounds);
        using var factory = new FailureTestTargetFactory();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = bounds,
            RequestedRegion = new Rect(x, y, width, height),
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory);
        using RenderTarget target = RenderTarget.CreateNull(8, 8);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);

        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(factory.Targets, Is.Empty,
                "A region that selects no pixels must not allocate an intermediate.");
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(source.IsDisposed, Is.False, "The borrowed input stays caller-owned.");
        });
    }

    [Test]
    public void Dispose_ReleasesRendererOwnedState_AndPreservesBorrowedState()
    {
        var bounds = new Rect(0, 0, 8, 8);
        using var source = new CpuRenderTarget(8, 8);
        source.Value.Canvas.Clear(new SKColor(80, 120, 160, 192));
        using var node = new ShaderNode(source, bounds);
        using var cacheSeed = new CpuRenderTarget(8, 8);
        RenderNodeCachePublication publication =
            RenderCacheTestSupport.CreatePublication(node.Cache, cacheSeed, bounds);
        RenderNodeCache.PublishAtomically([publication]);
        using var factory = new FailureTestTargetFactory();
        var renderer = FailureTestSupport.CreateRenderer(node, factory);

        RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The lifetime test requires a non-empty rasterization.");

        Assert.Multiple(() =>
        {
            Assert.That(renderer.StructuralPlanCacheStatistics.RetainedPlans, Is.EqualTo(1));
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.GreaterThanOrEqualTo(1));
            Assert.That(factory.Targets, Is.Not.Empty);
            Assert.That(factory.Targets, Has.All.Matches<FailureTestRenderTarget>(target => !target.IsDisposed));
        });

        renderer.Dispose();
        bool hasCachedOutput = node.Cache.TryGetCachedOutput(publication.Identity, out RenderNodeCachedOutput? cached);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.StructuralPlanCacheStatistics.RetainedPlans, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedBytes, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.OwnedBytes, Is.Zero);
            Assert.That(factory.Targets, Has.All.Matches<FailureTestRenderTarget>(target => target.IsDisposed));
            Assert.That(factory.Targets, Has.All.Matches<FailureTestRenderTarget>(target => target.DisposeCalls == 1));
            Assert.That(factory.IsDisposed, Is.False, "The caller owns the target factory.");
            Assert.That(node.IsDisposed, Is.False, "The caller owns the root node.");
            Assert.That(node.Cache.IsDisposed, Is.False, "The root owns its render cache.");
            Assert.That(hasCachedOutput, Is.True);
            Assert.That(cached!.Values.Single().Bounds, Is.EqualTo(bounds));
            Assert.That(cached.Values.Single().Target.IsDisposed, Is.False);
            Assert.That(source.IsDisposed, Is.False, "Borrowed materialized inputs remain caller-owned.");
            Assert.That(bitmap.IsDisposed, Is.False, "A returned rasterization owns its bitmap independently.");
        });

        rasterization.Dispose();
        Assert.That(bitmap.IsDisposed, Is.True);
    }

    private sealed class ShaderNode(RenderTarget source, Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> target = context.Borrow(source);
            RenderFragmentHandle input = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    target,
                    bounds,
                    EffectiveScale.At(1),
                    PixelRect.FromRect(bounds, 1),
                    default,
                    RenderHitTestContract.OutputBounds));
            ShaderDescription shader = ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                static bindings => bindings.Uniform("gain", 0.75f));
            context.Publish(context.Shader(input, shader));
        }
    }
}
