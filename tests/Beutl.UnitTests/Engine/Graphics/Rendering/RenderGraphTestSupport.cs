using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Reads recorded render graphs and builds the plain request the recording tests share.
internal static class RenderGraphTestSupport
{
    /// <summary>Maps every fragment of <paramref name="graph"/> by its id.</summary>
    public static IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> References(RecordedRenderGraph graph)
        => graph.Fragments.ToDictionary(
            static fragment => fragment.Id!.Value,
            static fragment => fragment);

    /// <summary>The fragment of the graph's only publication root.</summary>
    public static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return graph.GetFragment(rootId);
    }

    /// <summary>A preview, auxiliary request at scale 1 that <paramref name="owner"/> owns.</summary>
    public static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));
}
