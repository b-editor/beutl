using System.Numerics;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Shaders;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

public class ShaderPassNodeTests
{
    private const string Code = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(set = 0, binding = 1) uniform Parameters { float amount; vec4 tint; } parameters;
        void main() { color = texture(source, uv) * parameters.amount; }
        """;

    private static string[] PortNames(GraphNode node)
        => [.. node.Items.Where(item => item.Name.Value.StartsWith("In.") || item.Name.Value.StartsWith("Out.")).Select(item => item.Name.Value)];

    private static string[] Labels(GraphNode node) => [.. node.Items.OfType<INodePort>()
        .Where(item => item.Name.Value.StartsWith("In.") || item.Name.Value.StartsWith("Out."))
        .Select(item => item.Display.Value!.GetName()!)];

    private static void AssertNewNodeMatchesItsCode<T>() where T : ShaderPassNode, new()
    {
        var node = new T();
        string[] before = PortNames(node);
        Assert.That(before, Is.Not.Empty);
        Assert.That(node.RefreshPorts(), Is.True, node.Error.Value);
        Assert.That(PortNames(node), Is.EqualTo(before));
    }

    [Test]
    public void ANewNodesPortsAreTheOnesItsCodeAsksFor()
    {
        AssertNewNodeMatchesItsCode<ShaderFullscreenPassNode>();
        AssertNewNodeMatchesItsCode<ShaderComputePassNode>();
        AssertNewNodeMatchesItsCode<ShaderRenderPassNode>();
        AssertNewNodeMatchesItsCode<ShaderExtendedRenderPassNode>();
    }

    [Test]
    public void TheCodeDeclaresThePorts()
    {
        var node = new ShaderFullscreenPassNode();
        node.Code.Value = """
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 first;
            layout(location = 1) out uvec4 second;
            layout(constant_id = 3) const int taps = 5;
            layout(set = 0, binding = 0) uniform sampler2D source;
            layout(set = 0, binding = 1) uniform sampler2D mask;
            layout(set = 0, binding = 2) uniform Parameters { float amount; vec2 offset; vec4 tint; mat3 transform; float weights[4]; int steps; bool invert; } parameters;
            layout(set = 0, binding = 3, std430) readonly buffer Lookup { float values[]; } lookup;
            layout(push_constant) uniform Constants { float time; } constants;
            void main() { first = vec4(0); second = uvec4(0); }
            """;
        Assert.That(node.Error.Value, Is.Null);

        Assert.That(PortNames(node), Is.EqualTo(new[]
        {
            "Out.first", "Out.second",
            "In.source", "In.mask",
            "In.parameters", "In.parameters.amount", "In.parameters.offset", "In.parameters.tint",
            "In.parameters.transform", "In.parameters.weights", "In.parameters.steps", "In.parameters.invert",
            "In.lookup", "In.constants.time", "In.taps"
        }));
        Assert.That(Labels(node), Does.Contain("amount").And.Contain("taps").And.Contain("first"));
        Assert.That(node.In("parameters.amount").AssociatedType, Is.EqualTo(typeof(float)));
        Assert.That(node.In("parameters.offset").AssociatedType, Is.EqualTo(typeof(Vector2)));
        Assert.That(node.In("parameters.tint").AssociatedType, Is.EqualTo(typeof(Vector4)));
        Assert.That(node.In("parameters.transform").AssociatedType, Is.EqualTo(typeof(Matrix)));
        Assert.That(node.In("parameters.transform").Property!.GetValue(), Is.EqualTo(Matrix.Identity));
        Assert.That(node.In("parameters.weights").AssociatedType, Is.EqualTo(typeof(byte[])));
        Assert.That(node.In("parameters.steps").AssociatedType, Is.EqualTo(typeof(int)));
        Assert.That(node.In("parameters.invert").AssociatedType, Is.EqualTo(typeof(uint)), "A block stores a bool as a uint.");
        Assert.That(node.In("taps").AssociatedType, Is.EqualTo(typeof(int)));
        Assert.That(node.In("taps").Property!.GetValue(), Is.EqualTo(5), "A constant starts at the value the code gives it.");
        Assert.That(node.In("source").AssociatedType, Is.EqualTo(typeof(ShaderGraphValue<ShaderImage>)));
        Assert.That(node.In("lookup").AssociatedType, Is.EqualTo(typeof(ShaderGraphValue<ShaderBuffer>)));
    }

    [Test]
    public void WhatAShaderWritesComesOutAndWhatItOnlyReadsDoesNot()
    {
        var node = new ShaderComputePassNode();
        node.Code.Value = """
            #version 450
            layout(local_size_x = 1) in;
            layout(set = 0, binding = 0, rgba16f) uniform readonly image2D reference;
            layout(set = 0, binding = 1, rgba16f) uniform writeonly image2D result;
            layout(set = 0, binding = 2, rgba16f) uniform image2D history;
            layout(set = 0, binding = 3, std430) readonly buffer Input { float values[]; } input_;
            layout(set = 0, binding = 4, std430) buffer Output { float values[]; } output_;
            void main() { }
            """;
        Assert.That(node.Error.Value, Is.Null);

        Assert.That(PortNames(node).Where(name => name.StartsWith("Out.")),
            Is.EqualTo(new[] { "Out.result", "Out.history", "Out.output_" }));
        Assert.That(PortNames(node).Where(name => name.StartsWith("In.")),
            Is.EqualTo(new[] { "In.reference", "In.result", "In.history", "In.input_", "In.output_" }));
        Assert.That(node.Out("output_").AssociatedType, Is.EqualTo(typeof(ShaderGraphValue<ShaderBuffer>)));
    }

    [Test]
    public void DeclarationsInEveryStageMakeOnePort()
    {
        var node = new ShaderRenderPassNode();
        node.Vertex.Value = """
            #version 450
            layout(set = 0, binding = 0) uniform Shared { float amount; } shared_;
            layout(push_constant) uniform Constants { float time; } constants;
            void main() { gl_Position = vec4(shared_.amount, constants.time, 0, 1); }
            """;
        node.Fragment.Value = """
            #version 450
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 0) uniform Shared { float amount; } shared_;
            layout(set = 0, binding = 1) uniform sampler2D source;
            layout(push_constant) uniform Constants { float time; } constants;
            void main() { color = texture(source, vec2(shared_.amount, constants.time)); }
            """;
        Assert.That(node.Error.Value, Is.Null);

        Assert.That(PortNames(node), Is.EqualTo(new[]
        {
            "Out.color", "In.shared_", "In.shared_.amount", "In.source", "In.constants.time"
        }));
    }

    [Test]
    public void CodeThatKeepsADeclarationKeepsItsPortAndCodeThatFailsKeepsEverything()
    {
        var node = new ShaderFullscreenPassNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        node.Code.Value = Code;
        IInputPort port = node.In("parameters.amount");
        Connection connection = graph.Connect(port, NodeGraphTestHelpers.Source(graph, 0.5f));

        node.Code.Value = Code.Replace("* parameters.amount", "* parameters.amount * 2.0");
        Assert.That(node.In("parameters.amount"), Is.SameAs(port));
        Assert.That(port.Connection.Value.Value, Is.SameAs(connection));

        node.Code.Value = "not valid GLSL";
        Assert.That(node.Error.Value, Is.Not.Null.And.Not.Empty);
        Assert.That(node.In("parameters.amount"), Is.SameAs(port));
        Assert.That(port.Connection.Value.Value, Is.SameAs(connection));

        node.Code.Value = Code;
        Assert.That(node.Error.Value, Is.Null);
        Assert.That(node.In("parameters.amount"), Is.SameAs(port));
    }

    [Test]
    public void ReorderedDeclarationsMoveTheirPortsAndKeepTheirConnections()
    {
        static string Declaring(string first, string second) => $$"""
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 0) uniform sampler2D {{first}};
            layout(set = 0, binding = 1) uniform sampler2D {{second}};
            void main() { color = texture(a, uv) + texture(b, uv); }
            """;
        var node = new ShaderFullscreenPassNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        node.Code.Value = Declaring("a", "b");
        IInputPort a = node.In("a");
        IInputPort b = node.In("b");
        Assert.That(node.Items.IndexOf(a), Is.LessThan(node.Items.IndexOf(b)));
        Connection connection = graph.Connect(a, graph.Add(new ShaderFullscreenPassNode()).Out("color"));

        node.Code.Value = Declaring("b", "a");

        Assert.That(node.Error.Value, Is.Null);
        Assert.That(node.In("a"), Is.SameAs(a));
        Assert.That(node.In("b"), Is.SameAs(b));
        Assert.That(node.Items.IndexOf(b), Is.LessThan(node.Items.IndexOf(a)));
        Assert.That(a.Connection.Value.Value, Is.SameAs(connection));
    }

    [Test]
    public void EachScalarTypeGetsAPortOfThatTypeHoldingItsDefault()
    {
        var node = new ShaderFullscreenPassNode();
        node.Code.Value = """
            #version 450
            #extension GL_EXT_shader_explicit_arithmetic_types : require
            layout(location = 0) out vec4 color;
            layout(constant_id = 0) const bool flag = true;
            layout(constant_id = 1) const int whole = -2;
            layout(constant_id = 2) const uint count = 3u;
            layout(constant_id = 3) const float gain = 0.5;
            layout(constant_id = 4) const int64_t wide = -4l;
            layout(constant_id = 5) const uint64_t wider = 5ul;
            layout(constant_id = 6) const double exact = 0.25;
            void main() { color = vec4(flag ? gain : float(whole + int(count)), float(wide), float(wider), float(exact)); }
            """;

        Assert.That(node.Error.Value, Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(node.In("flag").Property!.GetValue(), Is.EqualTo(true));
            Assert.That(node.In("whole").Property!.GetValue(), Is.EqualTo(-2));
            Assert.That(node.In("count").Property!.GetValue(), Is.EqualTo(3u));
            Assert.That(node.In("gain").Property!.GetValue(), Is.EqualTo(0.5f));
            Assert.That(node.In("wide").Property!.GetValue(), Is.EqualTo(-4L));
            Assert.That(node.In("wider").Property!.GetValue(), Is.EqualTo(5UL));
            Assert.That(node.In("exact").Property!.GetValue(), Is.EqualTo(0.25d));
        });
    }

    [Test]
    public void ADeclarationThatGoesAwayTakesItsPortAndConnection()
    {
        var node = new ShaderFullscreenPassNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        node.Code.Value = Code;
        Connection connection = graph.Connect(node.In("parameters.amount"), NodeGraphTestHelpers.Source(graph, 0.5f));

        node.Code.Value = ShaderFullscreenPassNode.DefaultCode;

        Assert.That(node.Items.Any(item => item.Name.Value == "In.parameters.amount"), Is.False);
        Assert.That(graph.AllConnections, Does.Not.Contain(connection));
    }

    [Test]
    public void ChangingTheCodeRebuildsTheEvaluatedPorts()
    {
        var node = new ShaderFullscreenPassNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        int changes = 0;
        graph.TopologyChanged += (_, _) => changes++;

        node.Code.Value = Code;

        Assert.That(changes, Is.GreaterThan(0), "A snapshot built before the edit has no slots for the new ports.");
    }

    [Test]
    public void SavedPortsKeepTheirValuesWithoutCompilingEvenWhenTheCodeNoLongerDoes()
    {
        var original = new ShaderFullscreenPassNode();
        original.Code.Value = Code;
        IInputPort port = original.In("parameters.amount");
        port.Property!.SetValue(0.75f);
        original.Code.Value = "unfinished shader";

        var restored = new ShaderFullscreenPassNode();
        CoreSerializer.PopulateFromJsonObject(restored, CoreSerializer.SerializeToJsonObject(original));

        Assert.That(PortNames(restored), Is.EqualTo(PortNames(original)));
        IInputPort restoredPort = restored.In("parameters.amount");
        Assert.That(restoredPort.Id.Value, Is.EqualTo(port.Id.Value));
        Assert.That(restoredPort.Property!.GetValue(), Is.EqualTo(0.75f));
        Assert.That(restored.Code.Value, Is.EqualTo("unfinished shader"));
        Assert.That(Labels(restored), Does.Contain("amount"));

        restored.Code.Value = Code;
        Assert.That(restored.Error.Value, Is.Null);
        Assert.That(restored.In("parameters.amount"), Is.SameAs(restoredPort));
    }

    [Test]
    public void ACopiedGraphKeepsTheConnectionsOfGeneratedPorts()
    {
        var graph = new GraphModel();
        var source = graph.Add(new ShaderImageNode());
        var first = graph.Add(new ShaderFullscreenPassNode());
        var second = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(first.In("source"), source.Output);
        graph.Connect(second.In("source"), first.Out("color"));

        var restored = new GraphModel();
        CoreSerializer.PopulateFromJsonObject(restored, CoreSerializer.SerializeToJsonObject(graph));

        ShaderFullscreenPassNode[] passes = [.. restored.Nodes.OfType<ShaderFullscreenPassNode>()];
        Assert.That(passes[1].In("source").Connection.Value.Value!.Output.Value.Value, Is.SameAs(passes[0].Out("color")));
    }

    [Test]
    public void UndoAndRedoRestoreTheExactPortsAfterATypeChange()
    {
        var node = new ShaderFullscreenPassNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        node.Code.Value = Code;
        IInputPort original = node.In("parameters.amount");
        original.Property!.SetValue(0.25f);
        Connection connection = graph.Connect(original, NodeGraphTestHelpers.Source(graph, 0.5f));
        using var history = new HistoryHarness(graph);

        string changed = Code.Replace("float amount", "vec2 amount").Replace("* parameters.amount", "* parameters.amount.x");
        node.Code.Value = changed;
        history.History.Commit("Change a shader parameter's type");
        IInputPort replacement = node.In("parameters.amount");
        Assert.That(replacement, Is.Not.SameAs(original));
        Assert.That(graph.AllConnections, Does.Not.Contain(connection));

        history.History.Undo();
        Assert.That(node.Code.Value, Is.EqualTo(Code));
        Assert.That(node.In("parameters.amount"), Is.SameAs(original));
        Assert.That(original.Property!.GetValue(), Is.EqualTo(0.25f));
        Assert.That(original.Connection.Value.Value, Is.SameAs(connection));
        Assert.That(graph.AllConnections, Does.Contain(connection));
        NodeGraphTestHelpers.Evaluate(graph);

        history.History.Redo();
        Assert.That(node.Code.Value, Is.EqualTo(changed));
        Assert.That(node.In("parameters.amount"), Is.SameAs(replacement));
    }

    [Test]
    public void ADrawNodeShowsThePortsOfItsMode()
    {
        var node = new ShaderDrawNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        Assert.That(node.NamedInput("VertexCount").Property!.GetValue(), Is.EqualTo(3));
        using var history = new HistoryHarness(graph);

        node.Mode.Value = ShaderDrawMode.Indirect;
        history.History.Commit("Change the draw mode");
        Assert.That(node.Items.Select(item => item.Name.Value), Does.Contain("Commands").And.Not.Contain("VertexCount"));

        history.History.Undo();
        Assert.That(node.Items.Select(item => item.Name.Value), Does.Contain("VertexCount").And.Not.Contain("Commands"));

        node.Mode.Value = ShaderDrawMode.Indexed;
        node.NamedInput("IndexCount").Property!.SetValue(12);
        var restored = new ShaderDrawNode();
        CoreSerializer.PopulateFromJsonObject(restored, CoreSerializer.SerializeToJsonObject(node));
        Assert.That(restored.Mode.Value, Is.EqualTo(ShaderDrawMode.Indexed));
        Assert.That(restored.NamedInput("IndexCount").Property!.GetValue(), Is.EqualTo(12));
        Assert.That(restored.Items.Select(item => item.Name.Value), Does.Not.Contain("VertexCount"));
    }

    [Test]
    public void ALoopStateGetsBothEndsAndLosesBothTogether()
    {
        var loop = new ShaderRepeatNode();
        var root = new GraphModel();
        root.Nodes.Add(loop);
        GraphGroup body = loop.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode());
        using var history = new HistoryHarness(root);

        Assert.That(body.Input.Value, Is.TypeOf<ShaderLoopInput>());
        Assert.That(body.Output.Value, Is.TypeOf<ShaderLoopOutput>());
        Assert.That(loop.Items.OfType<IOutputPort>().Count(), Is.EqualTo(1), "A new loop passes one image through.");
        Assert.That(loop.Items.OfType<IInputPort>().Count(), Is.EqualTo(2), "The image and the count.");

        Assert.That(body.Input.Value!.AddNodePort(pass.In("source"), out _), Is.True);
        history.History.Commit("Add a state");
        Assert.That(body.Input.Value.Items, Has.Count.EqualTo(2));
        Assert.That(body.Output.Value!.Items, Has.Count.EqualTo(2));
        Assert.That(((IInputPort)body.Output.Value.Items[1]).Connection.Value.IsNull, Is.True,
            "Nothing has to be disconnected before wiring what the state becomes.");
        Assert.That(loop.Items.OfType<IOutputPort>().Count(), Is.EqualTo(2));
        Assert.That(loop.Items.OfType<IInputPort>().Count(), Is.EqualTo(3));

        Assert.That(body.Output.Value.AddNodePort(pass.Out("color"), out _), Is.True);
        history.History.Commit("Add a state from the other end");
        Assert.That(body.Input.Value.Items, Has.Count.EqualTo(3));

        Assert.That(new NodeGraphMutationService(history.History).RemovePort(body.Output.Value, (INodePort)body.Output.Value.Items[2]), Is.True);
        Assert.That(body.Input.Value.Items, Has.Count.EqualTo(2));
        Assert.That(body.Output.Value.Items, Has.Count.EqualTo(2));

        history.History.Undo();
        Assert.That(body.Input.Value.Items, Has.Count.EqualTo(3));
        Assert.That(body.Output.Value.Items, Has.Count.EqualTo(3));
    }

    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    public void RemovingAStateFromEitherEndTakesOnlyItsOwnPortsOffTheLoop(bool fromOutput, int state)
    {
        var loop = new ShaderRepeatNode();
        var root = new GraphModel();
        root.Nodes.Add(loop);
        GraphGroup body = loop.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode());
        using var history = new HistoryHarness(root);
        Assert.That(body.Input.Value!.AddNodePort(pass.In("source"), out _), Is.True);
        history.History.Commit("Add a state");
        INodeMember[] before = [.. loop.Items];
        Assert.That(before[..2], Has.All.InstanceOf<IOutputPort>());
        Assert.That(before[2..4], Has.All.InstanceOf<IInputPort>());

        GraphNode end = fromOutput ? body.Output.Value! : body.Input.Value!;
        Assert.That(new NodeGraphMutationService(history.History).RemovePort(end, (INodePort)end.Items[state]), Is.True);

        Assert.That(loop.Items, Is.EqualTo(new[] { before[1 - state], before[3 - state] }.Concat(before[4..])));

        history.History.Undo();
        Assert.That(loop.Items, Is.EqualTo(before));
    }

    [Test]
    public void ALoopSavesItsStatesAndItsOwnPorts()
    {
        var root = new GraphModel();
        var loop = root.Add(new ShaderRepeatNode());
        loop.Count.Property!.SetValue(7);
        GraphGroup body = loop.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode());
        body.Input.Value!.AddNodePort(pass.In("source"), out _);
        string[] names = [.. loop.Items.Select(item => item.Name.Value)];
        Guid[] ids = [.. loop.Items.Select(item => item.Id.Value)];

        var restored = new GraphModel();
        CoreSerializer.PopulateFromJsonObject(restored, CoreSerializer.SerializeToJsonObject(root));

        var restoredLoop = restored.Nodes.OfType<ShaderRepeatNode>().Single();
        Assert.That(restoredLoop.Items.Select(item => item.Name.Value), Is.EqualTo(names));
        Assert.That(restoredLoop.Items.Select(item => item.Id.Value), Is.EqualTo(ids));
        Assert.That(restoredLoop.Count.Property!.GetValue(), Is.EqualTo(7));
        Assert.That(restoredLoop.Group.Value.Nodes.OfType<ShaderLoopInfoNode>().Count(), Is.EqualTo(1));
        Assert.That(restoredLoop.Group.Value.Nodes.OfType<ShaderFullscreenPassNode>().Count(), Is.EqualTo(1));

        // Saved twice over the same object, as a project's save and reload does.
        CoreSerializer.PopulateFromJsonObject(restoredLoop, CoreSerializer.SerializeToJsonObject(loop));
        Assert.That(restoredLoop.Items.Select(item => item.Name.Value), Is.EqualTo(names));
    }
}
