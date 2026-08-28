using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Pins when an opaque operation's declared bounds are fixed, as an external author sees it.
/// </summary>
/// <remarks>
/// A bounds contract is operation shape, so every factory on it - the source rectangle and the state a
/// combining mapping reads alike - answers from the moment the contract is built. None of them is a channel
/// for the state a call supplies later. A source that moves therefore builds its definition per recording.
/// </remarks>
[TestFixture]
public sealed class OpaqueSourceCallStateContractTests
{
    private static readonly Rect s_domain = new(0, 0, 200, 200);
    private static readonly Size s_size = new(10, 10);

    private static readonly OpaqueRenderDefinition<Point> s_fixedDefinition =
        OpaqueRenderDefinition<Point>.Create(
            static (session, origin) => Draw(session, new Rect(origin, s_size)),
            OpaqueRenderBoundsContract.Source(new Rect(default, s_size)),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector);

    private static void Draw(OpaqueRenderSession session, Rect bounds)
    {
        using OpaqueRenderOutput output = session.CreateOutput(bounds);
        session.Publish(output);
    }

    [Test]
    public void ASharedSourceDefinition_PublishesItsDefinitionTimeBounds_WhateverTheCallStateSays()
    {
        using var atOrigin = new SourceNode(context => context.Publish(
            context.OpaqueSource(s_fixedDefinition.Call(new Point(0, 0)))));
        using var moved = new SourceNode(context => context.Publish(
            context.OpaqueSource(s_fixedDefinition.Call(new Point(100, 40)))));

        Assert.Multiple(() =>
        {
            Assert.That(Measure(atOrigin).OutputBounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
            Assert.That(
                Measure(moved).OutputBounds,
                Is.EqualTo(new Rect(0, 0, 10, 10)),
                "the rectangle a source publishes is the one its definition declared, so a call state that "
                + "moves the drawing does not move the bounds with it");
        });

        ArgumentException? exception = Assert.Throws<ArgumentException>(() => RenderOnce(moved));
        Assert.That(
            exception!.Message,
            Does.Contain("must be contained by the declared output bounds"),
            "a drawing that leaves the declared bounds fails the request rather than silently escaping them");
    }

    [Test]
    public void ACombiningContractsState_IsBoundWhenTheContractIsBuilt_NotWhenACallSuppliesState()
    {
        var seen = new List<Vector>();
        OpaqueRenderBoundsContract bounds = OpaqueRenderBoundsContract.Combine(
            new Vector(7, 7),
            (offset, inputs) =>
            {
                seen.Add(offset);
                return inputs[0].Translate(offset);
            },
            static (offset, output, inputs) => new[] { output.Translate(-offset) });
        OpaqueRenderDefinition<Vector> definition = OpaqueRenderDefinition<Vector>.Create(
            static (session, _) => Draw(session, session.RequiredRegion),
            bounds,
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector);

        using var node = new ContainerNode(context => context.Publish(
            context.OpaqueCombine(context.Inputs, definition.Call(new Vector(999, 999)))));
        node.AddChild(new SourceNode(context => context.Publish(
            context.OpaqueSource(s_fixedDefinition.Call(new Point(0, 0))))));

        Assert.Multiple(() =>
        {
            Assert.That(
                Measure(node).OutputBounds,
                Is.EqualTo(new Rect(7, 7, 10, 10)),
                "the state-passing combine overload keeps the mapping static-declared; it is not a channel "
                + "for the state a call supplies");
            Assert.That(seen, Has.All.EqualTo(new Vector(7, 7)));
        });
    }

    [Test]
    public void ASourceThatMoves_DeclaresItsPlaceByBuildingItsDefinitionPerRecording()
    {
        using var moved = new SourceNode(context =>
        {
            var bounds = new Rect(new Point(100, 40), s_size);
            OpaqueRenderDefinition<Rect> definition = OpaqueRenderDefinition<Rect>.Create(
                static (session, own) => Draw(session, own),
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector);
            context.Publish(context.OpaqueSource(definition.Call(bounds)));
        });

        Assert.That(Measure(moved).OutputBounds, Is.EqualTo(new Rect(100, 40, 10, 10)));
        Assert.DoesNotThrow(() => RenderOnce(moved));
    }

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node);
        return renderer.Measure();
    }

    private static void RenderOnce(RenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node);
        renderer.Rasterize().Dispose();
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

    private sealed class SourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class ContainerNode(Action<RenderNodeContext> process) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
