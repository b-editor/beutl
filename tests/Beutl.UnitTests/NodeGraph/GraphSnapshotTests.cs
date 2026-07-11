using Beutl.Composition;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GraphSnapshotTests
{
    [Test]
    public void Evaluate_RefreshesRoutingFlagsWithoutRebuild()
    {
        var model = new GraphModel();
        var node = new ContextCaptureNode();
        model.Nodes.Add(node);
        using var snapshot = new GraphSnapshot();
        var firstContext = new CompositionContext(TimeSpan.Zero)
        {
            DisableResourceShare = false,
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
        };
        var secondContext = new CompositionContext(TimeSpan.FromSeconds(1))
        {
            DisableResourceShare = true,
            PreferProxy = false,
            PreferredProxyPreset = ProxyPreset.Eighth,
        };

        snapshot.Build(model, firstContext);
        snapshot.Evaluate(CompositionTarget.Graphics, firstContext);
        snapshot.Evaluate(CompositionTarget.Graphics, secondContext);

        Assert.Multiple(() =>
        {
            Assert.That(node.CapturedContexts, Has.Count.EqualTo(2));
            Assert.That(node.CapturedContexts[0].PreferProxy, Is.True);
            Assert.That(node.CapturedContexts[0].PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(node.CapturedContexts[1].DisableResourceShare, Is.True);
            Assert.That(node.CapturedContexts[1].PreferProxy, Is.False);
            Assert.That(node.CapturedContexts[1].PreferredProxyPreset, Is.EqualTo(ProxyPreset.Eighth));
        });
    }
}

internal sealed partial class ContextCaptureNode : GraphNode
{
    public List<CapturedGraphContext> CapturedContexts { get; } = [];

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = (ContextCaptureNode)GetOriginal();
            node.CapturedContexts.Add(new CapturedGraphContext(
                context.DisableResourceShare,
                context.PreferProxy,
                context.PreferredProxyPreset));
        }
    }
}

internal readonly record struct CapturedGraphContext(
    bool DisableResourceShare,
    bool PreferProxy,
    ProxyPreset PreferredProxyPreset);
