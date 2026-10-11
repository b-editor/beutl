using Beutl.Composition;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Threading;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class ExpressionNodeTests
{
    [TestCase(1)]
    [TestCase(-1)]
    public async Task AwaitIsReportedWithoutBlockingTheDispatcherAndCanBeCorrected(int delay)
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                var node = new ExpressionNode();
                node.Expression.Property!.SetValue($"await System.Threading.Tasks.Task.Delay({delay}); 42");
                var model = new GraphModel();
                model.Nodes.Add(node);
                using var snapshot = new GraphSnapshot();
                snapshot.Build(model, CompositionContext.Default);

                snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

                Assert.That(node.ErrorMonitor.Value, Does.Contain("synchronous").And.Contain("await"));
                var resource = (ExpressionNode.Resource)snapshot.GetResource(snapshot.FindSlotIndex(node))!;
                Assert.That(resource.Output, Is.Null);

                node.Expression.Property.SetValue("21 * 2");
                snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

                Assert.That(node.ErrorMonitor.Value, Is.Null);
                Assert.That(resource.Output, Is.EqualTo(42));
            }).WaitAsync(TimeSpan.FromSeconds(10));

            await dispatcher.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }
}
