using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class ProductionResourceLifetimeTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 16);


    [Test]
    public void FanOut_RetainsProducerThroughEveryConsumerThenReusesItsSlot()
    {
        using var node = new FanOutOpaqueNode();
        using var renderer = CreateRenderer(node);
        using RenderTarget target = new CpuRenderTarget(16, 16);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);
        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecutionCount, Is.EqualTo(6));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_bounds,
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        }, new CpuRenderTargetFactory());

    private sealed class FanOutOpaqueNode : RenderNode
    {
        public int ExecutionCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(CreateSourceDescription(() => ExecutionCount++));
            context.Publish(context.TargetCommand([source], CreateDrawDescription(() => ExecutionCount++)));
            context.Publish(context.TargetCommand([source], CreateDrawDescription(() => ExecutionCount++)));
        }
    }

    private static OpaqueRenderDescription CreateSourceDescription(Action? onExecute = null)
        => OpaqueRenderDescription.CreateRequestLocal(
            session =>
            {
                onExecute?.Invoke();
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);

    private static TargetCommandDescription CreateDrawDescription(Action onExecute)
        => TargetCommandDescription.CreateRequestLocal(
            session =>
            {
                onExecute();
                session.Canvas.Use(session.Inputs.Single().Draw);
            },
            TargetRegion.Region(s_bounds),
            s_bounds,
            RenderHitTestContract.OutputBounds);
}
