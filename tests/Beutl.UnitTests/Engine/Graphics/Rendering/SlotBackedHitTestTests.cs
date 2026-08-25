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

    // The handle a call returns is only alive inside the Process that recorded it, and nothing commits a
    // resource before that Process returns. A recording-time hit test over a bound resource therefore has
    // to read a registration that is still pending, or it has no moment at which it can run at all.
    [Test]
    public void ACallReadsTheResourceItJustBoundWhileItIsStillRecording()
    {
        var shape = new HitShape(new Rect(0, 0, 40, 40));
        using var node = new RecordingTimeHitTestNode(shape);

        bool afterRecording = HitTest(node, new Point(20, 20));

        Assert.Multiple(() =>
        {
            Assert.That(node.Concrete, Is.True, "the fragment answered from concrete recording metadata");
            Assert.That(node.HitInside, Is.True, "recording-time answer inside the shape");
            Assert.That(node.HitOutside, Is.False, "recording-time answer outside the shape");
            Assert.That(afterRecording, Is.True, "the committed answer agrees with the recording-time one");
        });
    }

    // A nested recording seals into the one that absorbed it without committing its resources, so the
    // absorbing recording is the one whose rollback would now discard them - and the one that may read them.
    [Test]
    public void AnAbsorbingRecordingReadsTheResourceItsChildRegistered()
    {
        var shape = new HitShape(new Rect(0, 0, 40, 40));
        using var child = new RecordingTimeHitTestNode(shape);
        using var parent = new AbsorbingHitTestNode(child);

        HitTest(parent, new Point(20, 20));

        Assert.Multiple(() =>
        {
            Assert.That(parent.Concrete, Is.True);
            Assert.That(parent.HitInside, Is.True, "the parent reads the child's still-pending resource");
            Assert.That(parent.HitOutside, Is.False);
        });
    }

    // Reading a pending registration takes and returns the lease like any other read, so the recording it
    // belongs to can still roll the registration back afterwards.
    [Test]
    public void ARecordingTimeReadLeavesTheFailingRecordingFreeToRollBack()
    {
        var shape = new HitShape(new Rect(0, 0, 40, 40));
        using var node = new FailingRecordingTimeHitTestNode(shape);

        Assert.That(() => HitTest(node, new Point(20, 20)), Throws.InvalidOperationException);

        RenderResource<HitShape> token = node.Token!;
        Assert.Multiple(() =>
        {
            Assert.That(node.HitInside, Is.True, "the read succeeded while the recording was still alive");
            Assert.That(
                token.RegistrationState,
                Is.EqualTo(RenderResourceRegistrationState.Released),
                "the rollback still reached the resource the hit test had read");
            Assert.That(token.OwnershipState, Is.EqualTo(RenderResourceOwnershipState.ReleasedToken));
        });
    }

    private static bool HitTest(RenderNode node, Point point)
    {
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

    private static bool Hit(HitShape shape, Point point)
    {
        using var node = new SlotHitTestNode(shape);
        return HitTest(node, point);
    }

    private sealed class HitShape(Rect bounds)
    {
        public bool Contains(Point point) => bounds.Contains(point);
    }

    private class RecordingTimeHitTestNode(HitShape shape) : RenderNode
    {
        private static readonly RenderResourceSlot<HitShape> s_shapeSlot = new();

        private static readonly OpaqueRenderDefinition<Rect> s_definition =
            OpaqueRenderDefinition<Rect>.Create(
                static (session, bounds) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.FromSlot(
                    s_shapeSlot,
                    static (shape, point) => shape.Contains(point)),
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: [s_shapeSlot]);

        public bool Concrete { get; private set; }

        public bool HitInside { get; private set; }

        public bool HitOutside { get; private set; }

        public RenderResource<HitShape>? Token { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<HitShape> token = context.Borrow(shape);
            Token = token;
            RenderFragmentHandle handle = context.OpaqueSource(
                s_definition.Call(s_bounds, [s_shapeSlot.Bind(token)]));
            Concrete = handle.TryHitTest(new Point(20, 20), out bool inside);
            HitInside = inside;
            handle.TryHitTest(new Point(80, 80), out bool outside);
            HitOutside = outside;
            context.Publish(handle);
            AfterRecording(context);
        }

        protected virtual void AfterRecording(RenderNodeContext context)
        {
        }
    }

    private sealed class FailingRecordingTimeHitTestNode(HitShape shape) : RecordingTimeHitTestNode(shape)
    {
        protected override void AfterRecording(RenderNodeContext context)
            => throw new InvalidOperationException("The recording failed after the hit test.");
    }

    private sealed class AbsorbingHitTestNode(RenderNode child) : RenderNode
    {
        public bool Concrete { get; private set; }

        public bool HitInside { get; private set; }

        public bool HitOutside { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle handle = context.RecordNode(child, []).Single();
            Concrete = handle.TryHitTest(new Point(20, 20), out bool inside);
            HitInside = inside;
            handle.TryHitTest(new Point(80, 80), out bool outside);
            HitOutside = outside;
            context.Publish(handle);
        }
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
