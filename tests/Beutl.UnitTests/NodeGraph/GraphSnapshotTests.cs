using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes.Utilities;

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

    [Test]
    public void Evaluate_MeasureNode_PropagatesDeliveryIntentToAuxiliaryChildPull()
    {
        var model = new GraphModel();
        var probe = new IntentProbeRenderNode();
        var source = new RenderNodeSourceNode(probe);
        var measure = new MeasureNode();
        model.Nodes.Add(source);
        model.Nodes.Add(measure);
        model.Connect(measure.Input, source.Output);
        using var snapshot = new GraphSnapshot();
        var context = new GraphCompositionContext(TimeSpan.Zero)
        {
            RenderIntent = RenderIntent.Delivery,
        };

        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);

        Assert.That(probe.ObservedIntent, Is.EqualTo(RenderIntent.Delivery),
            "bounds-only Measure pulls must preserve delivery failure policy instead of silently using preview policy");
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

internal sealed partial class RenderNodeSourceNode : GraphNode
{
    private readonly RenderNode _value;

    public RenderNodeSourceNode(RenderNode value)
    {
        _value = value;
        Output = AddOutput<RenderNode>("Output");
    }

    public OutputPort<RenderNode> Output { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = ((RenderNodeSourceNode)GetOriginal())._value;
        }
    }
}

internal sealed class IntentProbeRenderNode : RenderNode
{
    public RenderIntent ObservedIntent { get; private set; } = RenderIntent.Preview;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        ObservedIntent = context.RenderIntent;
        return [RenderNodeOperation.CreateLambda(new Rect(0, 0, 10, 10), static _ => { })];
    }
}
