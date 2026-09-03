using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class DeclaredResourceOrderTests
{
    private static readonly Rect s_domain = new(0, 0, 8, 8);

    [Test]
    public void DeclaredSlotsReachTheDescriptionInTheirAuthoredOrder()
    {
        using var straight = new TwoResourceNode(swapped: false);
        using var reversed = new TwoResourceNode(swapped: true);
        using var writtenTheOtherWayRound = new TwoResourceNode(swapped: false);
        writtenTheOtherWayRound.SwapBindingOrder();

        RenderResourceSlot[] straightSlots = [];
        RenderResourceSlot[] reversedSlots = [];
        RenderResourceSlot[] rewrittenSlots = [];
        WithOpaqueRoot(straight, payload => straightSlots = SlotsOf(payload));
        WithOpaqueRoot(reversed, payload => reversedSlots = SlotsOf(payload));
        WithOpaqueRoot(writtenTheOtherWayRound, payload => rewrittenSlots = SlotsOf(payload));

        Assert.Multiple(() =>
        {
            Assert.That(
                straightSlots,
                Is.EqualTo(new[] { TwoResourceNode.FirstSlot, TwoResourceNode.SecondSlot }));
            Assert.That(
                reversedSlots,
                Is.EqualTo(new[] { TwoResourceNode.SecondSlot, TwoResourceNode.FirstSlot }),
                "Recording must not sort or canonicalize a slots: declaration.");
            Assert.That(
                rewrittenSlots,
                Is.EqualTo(new[] { TwoResourceNode.FirstSlot, TwoResourceNode.SecondSlot }),
                "The same declaration with its bindings written the other way round records identically.");
        });
    }

    [Test]
    public void SwappingTwoDeclaredSlots_CompilesASeparateStructuralPlan()
    {
        using var node = new TwoResourceNode(swapped: false);
        using var renderer = new RenderNodeRenderer(
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
                TargetFactory = new CpuTargetFactory(),
            });

        long afterAuthoredOrder = CompilationsAfterFrame(renderer);
        long afterUnchangedFrame = CompilationsAfterFrame(renderer);
        node.SwapBindingOrder();
        long afterBindingSwap = CompilationsAfterFrame(renderer);
        node.SwapDeclarationOrder();
        long afterDeclarationSwap = CompilationsAfterFrame(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(afterAuthoredOrder, Is.EqualTo(1));
            Assert.That(afterUnchangedFrame, Is.EqualTo(1),
                "The control: an unchanged frame replays the compiled plan, so a later increment is a swap.");
            Assert.That(afterBindingSwap, Is.EqualTo(1),
                "The bindings are reordered into the declaration, so writing them the other way round is the "
                + "same plan rather than a second one compiled for the same operation.");
            Assert.That(afterDeclarationSwap, Is.EqualTo(2),
                "Two slots swapped in the declaration are a different plan, not the same one.");
        });
    }

    private static long CompilationsAfterFrame(RenderNodeRenderer renderer)
    {
        renderer.Rasterize().Dispose();
        return renderer.StructuralPlanCacheStatistics.Compilations;
    }

    private static RenderResourceSlot[] SlotsOf(OpaqueRenderFragmentPayload payload)
        => [.. payload.Description.Resources.Select(static binding => binding.Slot)];

    private static void WithOpaqueRoot(RenderNode node, Action<OpaqueRenderFragmentPayload> assert)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        assert((OpaqueRenderFragmentPayload)GetSingleRoot(graph).Payload!);
    }

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

    private static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return graph.GetFragment(rootId);
    }

    private sealed class TwoResourceNode(bool swapped) : RenderNode
    {
        private static readonly RenderResourceSlot<FirstPayload> s_firstSlot = new();
        private static readonly RenderResourceSlot<SecondPayload> s_secondSlot = new();

        private readonly FirstPayload _first = new();
        private readonly SecondPayload _second = new();
        private bool _swappedDeclaration = swapped;
        private bool _swappedBindings;

        internal static RenderResourceSlot FirstSlot => s_firstSlot;

        internal static RenderResourceSlot SecondSlot => s_secondSlot;

        public void SwapDeclarationOrder()
        {
            _swappedDeclaration = !_swappedDeclaration;
            MarkChanged();
        }

        public void SwapBindingOrder()
        {
            _swappedBindings = !_swappedBindings;
            MarkChanged();
        }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<FirstPayload> first = context.Borrow(_first);
            RenderResource<SecondPayload> second = context.Borrow(_second);
            RenderResourceSlot[] declaredSlots = _swappedDeclaration
                ? [s_secondSlot, s_firstSlot]
                : [s_firstSlot, s_secondSlot];
            RenderResourceBinding[] bindings = _swappedDeclaration
                ? [s_secondSlot.Bind(second), s_firstSlot.Bind(first)]
                : [s_firstSlot.Bind(first), s_secondSlot.Bind(second)];
            if (_swappedBindings)
                (bindings[0], bindings[1]) = (bindings[1], bindings[0]);

            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                s_domain,
                static (session, bounds) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_domain),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: bindings,
                slots: declaredSlots);
            context.Publish(context.OpaqueSource(description));
        }

        internal sealed class FirstPayload;

        internal sealed class SecondPayload;
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
