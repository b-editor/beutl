using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class BrushSourceRecordingTests
{
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
}
