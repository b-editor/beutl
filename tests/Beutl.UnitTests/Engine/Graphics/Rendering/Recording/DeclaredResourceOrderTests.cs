using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins the declared resource list of the in-tree sources that borrow a captured engine resource.
/// </summary>
/// <remarks>
/// <c>RenderCacheResolver.AddResources</c> writes the list's count and then each binding name and
/// <c>CacheIdentity</c> in declaration order into the output-cache key, so the names, contents and order of a <c>resources:</c>
/// argument are part of every affected fragment's identity. <see cref="OutputIdentityDependsOnResourceOrder"/>
/// is what makes the pins below load-bearing rather than decorative.
/// </remarks>
[TestFixture]
public sealed class DeclaredResourceOrderTests
{


    private static void WithOpaqueRoot(RenderNode node, Action<OpaqueRenderFragmentPayload> assert)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        assert((OpaqueRenderFragmentPayload)GetSingleRoot(graph).Payload!);
    }

    private static RenderFragmentOutputIdentity OutputIdentity(RenderNode node, RenderRequestOwner owner)
    {
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        return RenderFragmentOutputIdentity.Create(GetSingleRoot(graph), graph.RequestId);
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
        private static readonly RenderResourceSlot<Payload> s_firstSlot = new();
        private static readonly RenderResourceSlot<Payload> s_secondSlot = new();
        private static readonly Rect s_bounds = new(0, 0, 8, 8);
        private static readonly object s_firstKey = "first";
        private static readonly object s_secondKey = "second";

        private readonly Payload _first = new();
        private readonly Payload _second = new();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> first = context.Borrow(_first);
            RenderResource<Payload> second = context.Borrow(_second);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                s_bounds,
                static (session, bounds) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: swapped
                    ? [s_secondSlot.Bind(second), s_firstSlot.Bind(first)]
                    : [s_firstSlot.Bind(first), s_secondSlot.Bind(second)]);
            context.Publish(context.OpaqueSource(description));
        }

        internal sealed class Payload;
    }
}

