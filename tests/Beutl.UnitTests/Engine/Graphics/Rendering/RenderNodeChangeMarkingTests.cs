using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class RenderNodeChangeMarkingTests
{
    [Test]
    public void ClearChanges_KeepsAMarkThatLandedAfterTheObservedVersion()
    {
        using var node = new ContainerRenderNode();
        node.MarkChanged();
        long observed = node.ChangeVersion;
        node.MarkChanged();

        node.ClearChanges(observed);
        Assert.That(node.HasChanges, Is.True, "the later mark describes content the cleared recording lacks");

        node.ClearChanges(node.ChangeVersion);
        Assert.That(node.HasChanges, Is.False);
    }
}
