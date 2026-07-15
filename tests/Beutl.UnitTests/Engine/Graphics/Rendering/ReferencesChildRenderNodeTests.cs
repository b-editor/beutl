using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class ReferencesChildRenderNodeTests
{
    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void Process_ForwardsIntentAndAuxiliaryPurposeToReferencedSubtree(RenderIntent intent)
    {
        var child = new AuxiliaryPullProbeNode();
        using var node = new ReferencesChildRenderNode(child);
        var context = new RenderNodeContext(
            [], intent, outputScale: 1f, maxWorkingScale: 1f,
            pullPurpose: RenderPullPurpose.Auxiliary);

        RenderNodeOperation[] outputs = node.Process(context);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.Multiple(() =>
        {
            Assert.That(child.ObservedIntent, Is.EqualTo(intent),
                "a nested processor must preserve preview/delivery failure policy");
            Assert.That(child.ObservedAuxiliaryPull, Is.True,
                "a NodeGraph reference boundary must not turn an auxiliary pull into a frame-cache pull");
        });
    }

    [Test]
    public void CreateChildProcessor_InheritsExecutionStateWithoutExposingPoolOwnership()
    {
        using var root = new AuxiliaryPullProbeNode();
        using var pool = new RenderTargetPool();
        var diagnostics = new PipelineDiagnostics();
        var requestedBounds = new Rect(3, 5, 17, 19);
        var context = new RenderNodeContext(
            [], RenderIntent.Preview, outputScale: 0.5f, maxWorkingScale: 1.25f,
            pullPurpose: RenderPullPurpose.Auxiliary)
        {
            Diagnostics = diagnostics,
            Pool = pool,
            RequestedBounds = requestedBounds,
        };

        RenderNodeProcessor processor = context.CreateChildProcessor(root, useRenderCache: false);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Root, Is.SameAs(root));
            Assert.That(processor.OutputScale, Is.EqualTo(0.5f));
            Assert.That(processor.MaxWorkingScale, Is.EqualTo(1.25f));
            Assert.That(processor.Diagnostics, Is.SameAs(diagnostics));
            Assert.That(processor.Pool, Is.SameAs(pool));
            Assert.That(processor.RenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(processor.PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(processor.RequestedBounds, Is.EqualTo(requestedBounds));
        });
    }

    private sealed class AuxiliaryPullProbeNode : RenderNode
    {
        public bool ObservedAuxiliaryPull { get; private set; }

        public RenderIntent ObservedIntent { get; private set; }

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            ObservedAuxiliaryPull = context.IsAuxiliaryPull;
            ObservedIntent = context.RenderIntent;
            return [];
        }
    }
}
