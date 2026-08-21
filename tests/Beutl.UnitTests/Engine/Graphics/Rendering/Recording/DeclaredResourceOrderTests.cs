using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins that the order of a <c>resources:</c> argument survives recording and reaches the structural plan key.
/// </summary>
/// <remarks>
/// <c>StructuralPlanCache</c> writes the binding count and then each slot's value type in declaration order, so
/// two nodes that declare the same bindings in a different order are different plans. Recording therefore must
/// not sort or canonicalize the list on the way there.
/// </remarks>
[TestFixture]
public sealed class DeclaredResourceOrderTests
{
    private static readonly Rect s_domain = new(0, 0, 8, 8);

    [Test]
    public void DeclaredResourcesReachTheDescriptionInTheirAuthoredOrder()
    {
        using var straight = new TwoResourceNode(swapped: false);
        using var reversed = new TwoResourceNode(swapped: true);

        RenderResourceSlot[] straightSlots = [];
        RenderResourceSlot[] reversedSlots = [];
        WithOpaqueRoot(straight, payload => straightSlots = SlotsOf(payload));
        WithOpaqueRoot(reversed, payload => reversedSlots = SlotsOf(payload));

        Assert.Multiple(() =>
        {
            Assert.That(
                straightSlots,
                Is.EqualTo(new[] { TwoResourceNode.FirstSlot, TwoResourceNode.SecondSlot }));
            Assert.That(
                reversedSlots,
                Is.EqualTo(new[] { TwoResourceNode.SecondSlot, TwoResourceNode.FirstSlot }),
                "Recording must not sort or canonicalize a resources: argument.");
        });
    }

    [Test]
    public void SwappingTwoDeclaredResources_CompilesASeparateStructuralPlan()
    {
        using var node = new TwoResourceNode(swapped: false);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_domain,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        long afterAuthoredOrder = CompilationsAfterFrame(renderer);
        long afterUnchangedFrame = CompilationsAfterFrame(renderer);
        node.SwapDeclarationOrder();
        long afterSwap = CompilationsAfterFrame(renderer);

        Assert.Multiple(() =>
        {
            Assert.That(afterAuthoredOrder, Is.EqualTo(1));
            Assert.That(afterUnchangedFrame, Is.EqualTo(1),
                "The control: an unchanged frame replays the compiled plan, so a later increment is the swap.");
            Assert.That(afterSwap, Is.EqualTo(2),
                "Two bindings swapped in the declaration list are a different plan, not the same one.");
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
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }

    private sealed class TwoResourceNode(bool swapped) : RenderNode
    {
        private static readonly RenderResourceSlot<FirstPayload> s_firstSlot = new();
        private static readonly RenderResourceSlot<SecondPayload> s_secondSlot = new();

        private readonly FirstPayload _first = new();
        private readonly SecondPayload _second = new();
        private bool _swapped = swapped;

        internal static RenderResourceSlot FirstSlot => s_firstSlot;

        internal static RenderResourceSlot SecondSlot => s_secondSlot;

        public void SwapDeclarationOrder()
        {
            _swapped = !_swapped;
            HasChanges = true;
        }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<FirstPayload> first = context.Borrow(_first);
            RenderResource<SecondPayload> second = context.Borrow(_second);
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
                resources: _swapped
                    ? [s_secondSlot.Bind(second), s_firstSlot.Bind(first)]
                    : [s_firstSlot.Bind(first), s_secondSlot.Bind(second)]);
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
