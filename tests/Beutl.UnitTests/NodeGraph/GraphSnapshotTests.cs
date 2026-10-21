using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

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
        factory.Object.Brush.Value = brush;
        var probe = new NestedCycleProbeNode();
        model.Nodes.AddRange([factory, probe]);
        var nested = Find(factory, brush.Opacity);
        var invalid = model.Connect(nested, probe.Output);
        var valid = model.Connect(probe.Input, (IOutputPort)factory.Items.Single(p => p.Name == "Output"));
        Action invalidate;
        Action restore;
        if (invalidation == "alias")
        {
            var other = new FactoryNode<Pen>();
            var otherBrush = new SolidColorBrush();
            other.Object.Brush.Value = otherBrush;
            model.Nodes.Add(other);
            model.Connect(Find(other, otherBrush.Opacity), Source(model, 0f, "Opacity"));
            invalidate = () => other.Object.Brush.Value = brush;
            restore = () => other.Object.Brush.Value = otherBrush;
        }
        else if (invalidation == "expression")
        {
            invalidate = () => factory.Object.Brush.Expression = new ConstantExpression<Brush?>(new SolidColorBrush());
            restore = () => factory.Object.Brush.Expression = null;
        }
        else
        {
            var animation = new KeyFrameAnimation<Brush?>();
            animation.KeyFrames.Add(new KeyFrame<Brush?> { Value = new SolidColorBrush() });
            invalidate = () => factory.Object.Brush.SetKeyFrameAnimation(animation);
            restore = () => factory.Object.Brush.SetKeyFrameAnimation(null);
        }
        using var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        Assert.That(probe.EvaluationCount, Is.Zero, "A valid cycle must still be blocked.");

        invalidate();
        Assert.That(factory.CanConnectInput(nested), Is.False);
        snapshot.MarkDirty();
        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);

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
        snapshot.Evaluate(CompositionContext.Default);
        Assert.That(probe.EvaluationCount, Is.EqualTo(1), "Restoring the edge must restore cycle detection.");
        Assert.That(invalid.Status, Is.EqualTo(ConnectionStatus.Error));
        Assert.That(valid.Status, Is.EqualTo(ConnectionStatus.Error));
    }

    [Test]
    public void GroupRebuildsNestedConnectionsAndRefreshesInputOutputSlotsWhileSnapshotIsAlive()
    {
        var graph = new GraphModel();
        using SceneHistoryHarness scene = AttachToScene(graph, "group-snapshot-update");
        var group = new GroupNode();
        var probe = new GroupPenProbeNode();
        graph.Nodes.Add(group);
        graph.Nodes.Add(probe);
        var input = new GroupInput();
        var output = new GroupOutput();
        var factory = new FactoryNode<Pen>();
        factory.Object.Brush.Value = null;
        group.Group.Nodes.AddRange([input, output, factory]);
        input.AddNodePort((IInputPort)factory.Items.Single(p => p.Name == "Thickness"), out _);
        output.AddNodePort((IOutputPort)factory.Items.Single(p => p.Name == "Output"), out _);
        group.Items.Single(p => p.Name == "Thickness").Property!.SetValue(17f);
        graph.Connect(probe.Input, (IOutputPort)group.Items.Single(p => p.Name == "Output"));
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        Assert.That(probe.LastThickness, Is.EqualTo(17f));

        var brush = new SolidColorBrush();
        factory.Object.Brush.Value = brush;
        var source = new LayerInputNode();
        var value = new LayerInputNode.LayerInputPort<float>();
        value.SetupProperty("Opacity");
        value.Property!.SetValue(25f);
        source.Items.Add(value);
        // Both group I/O slot indices change when the new upstream node comes first.
        group.Group.Nodes.Insert(0, source);
        var nested = Find(factory, brush.Opacity);
        var connection = group.Group.Connect(nested, value);

        snapshot.Evaluate(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(probe.LastThickness, Is.EqualTo(17f));
            Assert.That(probe.LastOpacity, Is.EqualTo(25f));
            Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        });

        group.Group.Disconnect(connection);
        brush.Opacity.Value = 80f;
        group.Group.Nodes.Remove(source);
        snapshot.Evaluate(CompositionContext.Default);
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

    // Evaluation sets the status again on every pass, so a status change alone must not become an undo step.
    [Test]
    public void EvaluatedStatusAddsNoUndoEntry()
    {
        var model = new GraphModel();
        var factory = new FactoryNode<Pen>();
        model.Nodes.Add(factory);
        var value = Source(model, 17f);
        using var history = new HistoryHarness(model);
        var connection = model.Connect((IInputPort)factory.Items.Single(p => p.Name == "Thickness"), value);
        history.History.Commit("Connect");

        Evaluate(model);
        history.History.Commit("After evaluation");

        Assert.That(connection.Status, Is.EqualTo(ConnectionStatus.Success));
        Assert.That(history.History.UndoCount, Is.EqualTo(1));
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(model.AllConnections, Is.Empty, "The first Undo removes the connection itself.");
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
        snapshot.Evaluate(firstContext);
        snapshot.Evaluate(secondContext);

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

    public IExpressionProperty<Brush?> Brush { get; } = Property.CreateAnimatable<Brush?>();
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
