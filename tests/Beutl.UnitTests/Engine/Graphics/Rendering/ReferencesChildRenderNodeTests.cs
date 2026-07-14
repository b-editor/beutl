using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class ReferencesChildRenderNodeTests
{
    [Test]
    public void Process_ForwardsAuxiliaryPullToReferencedSubtree()
    {
        var child = new AuxiliaryPullProbeNode();
        using var node = new ReferencesChildRenderNode(child);
        var context = new RenderNodeContext([], outputScale: 1f, maxWorkingScale: 1f)
        {
            IsAuxiliaryPull = true,
        };

        RenderNodeOperation[] outputs = node.Process(context);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.That(child.ObservedAuxiliaryPull, Is.True,
            "a NodeGraph reference boundary must not turn a hit-test/bounds pull into a production cache pull");
    }

    private sealed class AuxiliaryPullProbeNode : RenderNode
    {
        public bool ObservedAuxiliaryPull { get; private set; }

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            ObservedAuxiliaryPull = context.IsAuxiliaryPull;
            return [];
        }
    }
}
