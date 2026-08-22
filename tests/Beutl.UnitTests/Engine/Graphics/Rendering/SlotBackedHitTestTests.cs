using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A hit test that has to read a resource must reach it through a slot, because the definition that
// declares the test outlives every call that binds one. These pin that the slot resolves against the
// bindings of the call being tested, not against anything the definition captured.
[TestFixture]
public sealed class SlotBackedHitTestTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    [Test]
    public void OneDefinitionResolvesTheHitShapeEachCallBound()
    {
        var lowerLeft = new HitShape(new Rect(0, 0, 40, 40));
        var upperRight = new HitShape(new Rect(60, 60, 40, 40));

        Assert.Multiple(() =>
        {
            Assert.That(Hit(lowerLeft, new Point(20, 20)), Is.True, "lower-left shape at its own point");
            Assert.That(Hit(lowerLeft, new Point(80, 80)), Is.False, "lower-left shape at the other point");
            Assert.That(Hit(upperRight, new Point(80, 80)), Is.True, "upper-right shape at its own point");
            Assert.That(Hit(upperRight, new Point(20, 20)), Is.False, "upper-right shape at the other point");
        });
    }

    [Test]
    public void AnUnboundSlotFailsInsteadOfSilentlyMissing()
    {
        var slot = new RenderResourceSlot<HitShape>();
        RenderHitTestContract contract = RenderHitTestContract.FromSlot(
            slot,
            static (shape, point) => shape.Contains(point));

        KeyNotFoundException? exception = Assert.Throws<KeyNotFoundException>(
            () => contract.Evaluate(s_bounds, [], [], new Point(20, 20)));

        Assert.That(exception!.Message, Does.Contain("slot"));
    }

    [Test]
    public void TheContextOverloadSeesTheOperationBoundsAlongsideTheBoundResource()
    {
        var slot = new RenderResourceSlot<HitShape>();
        RenderHitTestContract contract = RenderHitTestContract.FromSlot(
            slot,
            static (shape, context, point) => context.OutputBounds.Contains(point) && shape.Contains(point));

        using var registry = new RenderRequestResourceRegistry();
        RenderResource<HitShape> token = registry.RegisterBorrowed(new HitShape(new Rect(0, 0, 200, 200)));
        registry.Commit(token);
        RenderResourceBinding[] bindings = [slot.Bind(token)];

        Assert.Multiple(() =>
        {
            Assert.That(
                contract.Evaluate(s_bounds, [], bindings, new Point(50, 50)),
                Is.True,
                "inside both the bounds and the shape");
            Assert.That(
                contract.Evaluate(s_bounds, [], bindings, new Point(150, 150)),
                Is.False,
                "inside the shape but outside the operation bounds");
        });
    }

    [Test]
    public void ASlotBackedTestCannotSmuggleAResourceThroughItsClosure()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<HitShape> token = registry.RegisterBorrowed(new HitShape(s_bounds));
        registry.Commit(token);
        var slot = new RenderResourceSlot<HitShape>();

        Assert.That(
            () => RenderHitTestContract.FromSlot(
                slot,
                (shape, point) => token is not null && shape.Contains(point)),
            Throws.ArgumentException);
    }

    private static bool Hit(HitShape shape, Point point)
    {
        using var node = new SlotHitTestNode(shape);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    OutputScale = 1f,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });
        return renderer.HitTest(point);
    }

    private sealed class HitShape(Rect bounds)
    {
        public bool Contains(Point point) => bounds.Contains(point);
    }

    private sealed class SlotHitTestNode(HitShape shape) : RenderNode
    {
        private static readonly RenderResourceSlot<Brush.Resource> s_fillSlot = new();
        private static readonly RenderResourceSlot<HitShape> s_shapeSlot = new();

        private static readonly OpaqueRenderDefinition<Rect> s_definition =
            OpaqueRenderDefinition<Rect>.Create(
                static (session, bounds) =>
                    session.UseResource(s_fillSlot, fill =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(bounds);
                        output.Canvas.Use(canvas => canvas.DrawRectangle(bounds, fill, null));
                        session.Publish(output);
                    }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.FromSlot(
                    s_shapeSlot,
                    static (shape, point) => shape.Contains(point)),
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: [s_fillSlot, s_shapeSlot]);

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Brush.Resource> fill = context.Borrow<Brush.Resource>(Brushes.Resource.White);
            RenderResource<HitShape> shapeResource = context.Borrow(shape);
            context.Publish(context.OpaqueSource(s_definition.Call(
                s_bounds,
                [s_fillSlot.Bind(fill), s_shapeSlot.Bind(shapeResource)])));
        }
    }
}
