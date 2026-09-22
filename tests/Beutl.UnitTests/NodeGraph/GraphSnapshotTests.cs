using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GraphSnapshotTests
{
    [TestCase("animation")]
    [TestCase("expression")]
    [TestCase("alias")]
    public void InvalidNestedEdgesDoNotBlockOtherwiseAcyclicNodes(string invalidation)
    {
        var model = new GraphModel();
        var factory = new FactoryNode<SnapshotBrushHolder>();
        var brush = new SolidColorBrush();
        factory.Object.Brush.CurrentValue = brush;
        var probe = new NestedCycleProbeNode();
        model.Nodes.AddRange([factory, probe]);
        var nested = factory.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Opacity));
        var invalid = model.Connect(nested, probe.Output);
        var valid = model.Connect(probe.Input, (IOutputPort)factory.Items.Single(p => p.Name == "Output"));
        Action invalidate;
        Action restore;
        if (invalidation == "alias")
        {
            var other = new FactoryNode<Pen>();
            var otherBrush = new SolidColorBrush();
            other.Object.Brush.CurrentValue = otherBrush;
            var source = new LayerInputNode();
            var output = new LayerInputNode.LayerInputPort<float>();
            output.SetupProperty("Opacity");
            source.Items.Add(output);
            model.Nodes.AddRange([other, source]);
            model.Connect(other.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), otherBrush.Opacity)), output);
            invalidate = () => other.Object.Brush.CurrentValue = brush;
            restore = () => other.Object.Brush.CurrentValue = otherBrush;
        }
        else if (invalidation == "expression")
        {
            invalidate = () => factory.Object.Brush.Expression = new ConstantBrushExpression(new SolidColorBrush());
            restore = () => factory.Object.Brush.Expression = null;
        }
        else
        {
            var animation = new KeyFrameAnimation<Brush?>();
            animation.KeyFrames.Add(new KeyFrame<Brush?> { Value = new SolidColorBrush() });
            invalidate = () => factory.Object.Brush.Animation = animation;
            restore = () => factory.Object.Brush.Animation = null;
        }
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        Assert.That(probe.EvaluationCount, Is.Zero, "A valid cycle must still be blocked.");

        invalidate();
        Assert.That(factory.CanConnectInput(nested), Is.False);
        snapshot.MarkDirty();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(nested.Connection.Value, Is.SameAs(invalid));
            Assert.That(invalid.Status, Is.EqualTo(ConnectionStatus.Error));
            Assert.That(valid.Status, Is.EqualTo(ConnectionStatus.Success));
            Assert.That(probe.EvaluationCount, Is.EqualTo(1));
            Assert.That(probe.LastInput, Is.SameAs(factory.Object));
        });

        restore();
        Assert.That(factory.CanConnectInput(nested), Is.True);
        snapshot.MarkDirty();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        Assert.That(probe.EvaluationCount, Is.EqualTo(1), "Restoring the edge must restore cycle detection.");
        Assert.That(invalid.Status, Is.EqualTo(ConnectionStatus.Error));
        Assert.That(valid.Status, Is.EqualTo(ConnectionStatus.Error));
    }

    [Test]
    public void GroupRebuildsNestedConnectionsAndRefreshesInputOutputSlotsWhileSnapshotIsAlive()
    {
        using var scene = new SceneHistoryHarness("group-snapshot-update");
        var application = new BeutlApplication();
        application.Items.Add(scene.Scene);
        var graph = new GraphModel();
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = graph;
        scene.AddElement().AddObject(drawable);
        var group = new GroupNode();
        var probe = new GroupPenProbeNode();
        graph.Nodes.Add(group);
        graph.Nodes.Add(probe);
        var input = new GroupInput();
        var output = new GroupOutput();
        var factory = new FactoryNode<Pen>();
        factory.Object.Brush.CurrentValue = null;
        group.Group.Nodes.AddRange([input, output, factory]);
        input.AddNodePort((IInputPort)factory.Items.Single(p => p.Name == "Thickness"), out _);
        output.AddNodePort((IOutputPort)factory.Items.Single(p => p.Name == "Output"), out _);
        group.Items.Single(p => p.Name == "Thickness").Property!.SetValue(17f);
        graph.Connect(probe.Input, (IOutputPort)group.Items.Single(p => p.Name == "Output"));
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        Assert.That(probe.LastThickness, Is.EqualTo(17f));

        var brush = new SolidColorBrush();
        factory.Object.Brush.CurrentValue = brush;
        var source = new LayerInputNode();
        var value = new LayerInputNode.LayerInputPort<float>();
        value.SetupProperty("Opacity");
        value.Property!.SetValue(25f);
        source.Items.Add(value);
        // Both group I/O slot indices change when the new upstream node comes first.
        group.Group.Nodes.Insert(0, source);
        var nested = factory.NestedInputPorts.Single(p => ReferenceEquals(p.Property!.GetEngineProperty(), brush.Opacity));
        var connection = group.Group.Connect(nested, value);

        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(probe.LastThickness, Is.EqualTo(17f));
            Assert.That(probe.LastOpacity, Is.EqualTo(25f));
            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        });

        group.Group.Disconnect(connection);
        brush.Opacity.CurrentValue = 80f;
        group.Group.Nodes.Remove(source);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        Assert.That(probe.LastThickness, Is.EqualTo(17f));
        Assert.That(probe.LastOpacity, Is.EqualTo(80f));
    }

    [Test]
    public void CycleConnectionsAreMarkedAsErrors()
    {
        var model = new GraphModel();
        var first = new CountingPassThroughGraphNode();
        var second = new CountingPassThroughGraphNode();
        model.Nodes.Add(first);
        model.Nodes.Add(second);
        model.Connect(first.Input, second.Output);
        model.Connect(second.Input, first.Output);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        Assert.That(model.AllConnections.Select(connection => connection.Status), Is.All.EqualTo(ConnectionStatus.Error));
    }

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
            TargetDomain = new Rect(0, 0, 1920, 1080),
        };
        var secondContext = new CompositionContext(TimeSpan.FromSeconds(1))
        {
            DisableResourceShare = true,
            PreferProxy = false,
            PreferredProxyPreset = ProxyPreset.Eighth,
            TargetDomain = new Rect(0, 0, 1280, 720),
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
            Assert.That(node.CapturedContexts[0].TargetDomain, Is.EqualTo(firstContext.TargetDomain));
            Assert.That(node.CapturedContexts[1].TargetDomain, Is.EqualTo(secondContext.TargetDomain));
        });
    }
    private sealed class ConstantBrushExpression(Brush value) : IExpression<Brush?>
    {
        public string ExpressionString => "test constant brush";
        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
        public Brush? Evaluate(ExpressionContext context) => value;
    }
}

internal sealed partial class NestedCycleProbeNode : GraphNode
{
    public NestedCycleProbeNode()
    {
        Input = AddInput<SnapshotBrushHolder?>("Input");
        Output = AddOutput<float>("Opacity");
    }

    public InputPort<SnapshotBrushHolder?> Input { get; }
    public OutputPort<float> Output { get; }
    public int EvaluationCount { get; set; }
    public SnapshotBrushHolder? LastInput { get; set; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            node.EvaluationCount++;
            node.LastInput = Input;
            Output = 42f;
        }
    }
}

internal sealed partial class SnapshotBrushHolder : EngineObject
{
    public SnapshotBrushHolder() => ScanProperties<SnapshotBrushHolder>();

    public IProperty<Brush?> Brush { get; } = Property.CreateAnimatable<Brush?>();
}

internal sealed partial class GroupPenProbeNode : GraphNode
{
    public GroupPenProbeNode() => Input = AddInput<Pen?>("Input");

    public InputPort<Pen?> Input { get; }

    public float? LastThickness { get; set; }

    public float? LastOpacity { get; set; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
            node.LastThickness = Input?.Thickness.GetValue(context);
            node.LastOpacity = Input?.Brush.GetValue(context)?.Opacity.GetValue(context);
        }
    }
}

internal sealed partial class ContextCaptureNode : GraphNode
{
    public List<CapturedGraphContext> CapturedContexts { get; } = [];

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            ContextCaptureNode node = RequireOriginal();
            node.CapturedContexts.Add(new CapturedGraphContext(
                context.DisableResourceShare,
                context.PreferProxy,
                context.PreferredProxyPreset,
                context.TargetDomain));
        }
    }
}

internal readonly record struct CapturedGraphContext(
    bool DisableResourceShare,
    bool PreferProxy,
    ProxyPreset PreferredProxyPreset,
    Rect? TargetDomain);
