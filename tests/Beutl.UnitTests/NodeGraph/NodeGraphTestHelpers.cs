using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

internal static class NodeGraphTestHelpers
{
    public static INestedInputPort Find(GraphNode node, IProperty property)
        => node.NestedInputPorts.Single(p => ReferenceEquals(p.Property?.GetEngineProperty(), property));

    // A layer input holding value, added to graph as the source of a connection.
    public static LayerInputNode.LayerInputPort<T> Source<T>(GraphModel graph, T value, string name = "Value")
    {
        var source = new LayerInputNode();
        var output = new LayerInputNode.LayerInputPort<T>();
        output.SetupProperty(name);
        output.Property!.SetValue(value);
        source.Items.Add(output);
        graph.Nodes.Add(source);
        return output;
    }

    public static void Evaluate(GraphModel graph)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
    }

    // Puts graph in an element of a scene under a hierarchy root, as in the editor: a group mirrors its inner
    // ports only once it is attached.
    public static SceneHistoryHarness AttachToScene(GraphModel graph, string prefix)
    {
        var scene = new SceneHistoryHarness(prefix);
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(scene.Scene);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = graph;
        scene.AddElement().AddObject(drawable);
        return scene;
    }
}

internal sealed class ConstantExpression<T>(T value) : IExpression<T>
{
    public string ExpressionString => "test constant";

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }

    public T Evaluate(ExpressionContext context) => value;
}
