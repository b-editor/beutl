using System.Diagnostics;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class OutputIdentityFanOutCostTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    // Twenty-six doublings are about 67 million paths and 27 edges. Walking the paths took 20 seconds on the
    // machine this was written on and walking the edges took none of it, so the bound below separates the two
    // by enough that a slower machine still lands on the right side.
    private const int Doublings = 26;

    [Test]

    public void HashingAndComparing_CostTheGraphsEdgesNotItsPaths()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        using var root = BuildDoublingChain(Doublings);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        RenderFragmentReference rootFragment = RootOf(graph);

        var elapsed = Stopwatch.StartNew();
        RenderFragmentOutputIdentity first = RenderFragmentOutputIdentity.Create(rootFragment, graph.RequestId);
        RenderFragmentOutputIdentity second = RenderFragmentOutputIdentity.Create(rootFragment, graph.RequestId);
        int hash = first.GetHashCode();
        bool equal = first.Equals(second);
        elapsed.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(equal, Is.True, "Two identities of one fragment must agree.");
            Assert.That(hash, Is.EqualTo(second.GetHashCode()));
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)),
                $"{Doublings} doublings must cost their edges, not their paths.");
        });
    }

    private static ContainerRenderNode BuildDoublingChain(int doublings)
    {
        var root = new DoublingContainer(s_bounds);
        root.AddChild(new SourceNode(s_bounds));
        for (int level = 0; level < doublings; level++)
        {
            var next = new DoublingContainer(s_bounds);
            next.AddChild(root);
            root = next;
        }

        return root;
    }

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

    private static RenderFragmentReference RootOf(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }

    private sealed class SourceNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(DescribeSource(bounds)));
    }

    /// <summary>Consumes its child's fragment twice, so one identity is reached by two edges.</summary>
    private sealed class DoublingContainer(Rect bounds) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.Inputs[0];
            context.Publish(context.OpaqueCombine([input, input], DescribeCombine(bounds)));
        }
    }

    private static OpaqueRenderDescription DescribeSource(Rect bounds)
        => OpaqueRenderDescription.Create(
            bounds,
            Draw,
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);

    private static OpaqueRenderDescription DescribeCombine(Rect bounds)
        => OpaqueRenderDescription.Create(
            bounds,
            Draw,
            OpaqueRenderBoundsContract.FullInputs(_ => bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);

    // Colors.White computes its value in a getter, which a recording callback cannot be shown answers
    // the same way twice; a snapshot of it can be.
    private static readonly Color s_white = Colors.White;

    private static void Draw(OpaqueRenderSession session, Rect area)
    {
        using OpaqueRenderOutput output = session.CreateOutput(area);
        output.Canvas.Use(static canvas => canvas.Clear(s_white));
        session.Publish(output);
    }
}
