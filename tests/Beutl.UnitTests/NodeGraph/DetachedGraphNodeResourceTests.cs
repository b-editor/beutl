using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;

namespace Beutl.UnitTests.NodeGraph;

/// <summary>
/// A <c>GraphNode.Resource</c> carries two halves of one mechanism: the generated
/// <c>BindNodePortValues</c> and the hand-written <c>Update(GraphCompositionContext)</c> beside it. Both reach
/// for the backing node, so both report a detached resource the same way.
/// </summary>
[TestFixture]
public sealed class DetachedGraphNodeResourceTests
{
    private static IEnumerable<TestCaseData> DetachedResources()
    {
        yield return new TestCaseData((Func<GraphNode.Resource>)(() => new TimeNode.Resource()))
            .SetName("TimeNode");
        yield return new TestCaseData((Func<GraphNode.Resource>)(() => new TranslateMatrixNode.Resource()))
            .SetName("TranslateMatrixNode");
        yield return new TestCaseData((Func<GraphNode.Resource>)(() => new TransformNode.Resource()))
            .SetName("TransformNode");
        yield return new TestCaseData((Func<GraphNode.Resource>)(() => new ExpressionNode.Resource()))
            .SetName("ExpressionNode");
        yield return new TestCaseData((Func<GraphNode.Resource>)(() => new PreviewNode.Resource()))
            .SetName("PreviewNode");
    }

    [TestCaseSource(nameof(DetachedResources))]
    public void UpdateOnADetachedNodeResource_ThrowsAnInvalidOperationNamingTheResourceType(
        Func<GraphNode.Resource> create)
    {
        using GraphNode.Resource detached = create();
        var context = new GraphCompositionContext(TimeSpan.Zero);

        var exception = Assert.Throws<InvalidOperationException>(() => detached.Update(context));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.IsAttached, Is.False);
            Assert.That(exception!.Message, Does.Contain(detached.GetType().DeclaringType!.Name));
            Assert.That(exception.Message, Does.Contain(nameof(EngineObject.ToResource)));
        }
    }

    [TestCaseSource(nameof(DetachedResources))]
    public void BindNodePortValuesOnADetachedNodeResource_FailsTheSameWayAsUpdate(Func<GraphNode.Resource> create)
    {
        using GraphNode.Resource detached = create();

        Assert.Throws<InvalidOperationException>(() => detached.BindNodePortValues());
    }
}
