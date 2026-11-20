using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Extensibility;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using static Beutl.AgentToolkit.Tests.NodeGraphs.NodeGraphTestScene;

namespace Beutl.AgentToolkit.Tests.NodeGraphs;

// The less travelled paths of the node graph tools: each refusal an agent can meet, and the edits besides adding.
public sealed class NodeGraphEdgeCaseTests
{
    private static NodeGraphOperation Op(string op) => new() { Op = op };

    public static IEnumerable<TestCaseData> MoreRefusedOperations()
    {
        NodeGraphOperation[] pass = OnePass(TintCode);
        yield return Case("An add without a type", "needs a type", Op("add"));
        yield return Case("A ref that starts with a digit", "cannot be a ref", Add("1st", "TimeNode"));
        yield return Case("A ref that is an Id", "cannot be a ref", Add(Guid.NewGuid().ToString(), "TimeNode"));
        yield return Case("A second GroupInput", "already has a GroupInput",
            Add("group", "GroupNode"), Add("in", "GroupInput", graph: "group"), Add("again", "GroupInput", graph: "group"));
        yield return Case("A connection naming no port", "Name the port on at least one end",
            Add("a", "TimeNode"), Add("b", "OutputNode"), Connect("a", "b"));
        yield return Case("A port from a node that adds none", "does not add ports",
            Add("time", "TimeNode"), Add("output", "OutputNode"), Connect("time", "output.Output"));
        yield return Case("An input added on a node that adds outputs", "adds outputs only",
            Add("group", "GroupNode"), Add("in", "GroupInput", graph: "group"), Add("time", "TimeNode", graph: "group"),
            Connect("time.Time", "in", graph: "group"));
        yield return Case("A port for an output on the to end", "is an output on the to end",
            Add("group", "GroupNode"), Add("in", "GroupInput", graph: "group"), Add("time", "TimeNode", graph: "group"),
            Connect("in", "time.Time", graph: "group"));
        yield return Case("A port for an input already connected", "already connected or cannot be connected now",
            [.. pass, Add("inputs", "LayerInputNode"), Connect("inputs", "pass.In.source")]);
        yield return Case("An end that stops at a dot", "ends in a dot", pass[0], pass[1], Connect("input.", "pass.In.source"));
        yield return Case("A disconnect with a port and an end", "Give either port",
            new NodeGraphOperation { Op = "disconnect", Port = "a.b", From = "a.c" });
        yield return Case("A disconnect of a port that has no connections", "has no connections",
            Add("time", "TimeNode"), new NodeGraphOperation { Op = "disconnect", Port = "time.Time" });
        yield return Case("A disconnect of ends that are not connected", "is not connected to",
            Add("time", "TimeNode"), Add("output", "OutputNode"),
            new NodeGraphOperation { Op = "disconnect", From = "time.Time", To = "output.Output" });
        yield return Case("A disconnect naming no port", "Name the port on both ends",
            Add("time", "TimeNode"), Add("output", "OutputNode"), new NodeGraphOperation { Op = "disconnect", From = "time", To = "output.Output" });
        yield return Case("A position of one number", "position takes [x, y]", new NodeGraphOperation { Op = "add", Type = "TimeNode", Position = [1] });
        yield return Case("A graph that is no group", "has no inner graph", Add("time", "TimeNode"), Add("other", "TimeNode", graph: "time"));
        yield return Case("A node of another graph", "is in another graph",
            Add("group", "GroupNode"), Add("time", "TimeNode", graph: "group"), Add("output", "OutputNode"), Connect("time.Time", "output.Output"));
        yield return Case("A type the graph has none of", "has no ShaderLoopInput",
            Add("output", "OutputNode"), Connect("@ShaderLoopInput.Image", "output.Output"));
        yield return Case("A type the graph has two of", "2 nodes of type TimeNode",
            Add("a", "TimeNode"), Add("b", "TimeNode"), Add("output", "OutputNode"), Connect("@TimeNode.Time", "output.Output"));
        yield return Case("A value for a monitor", "is a monitor", Add("pass", "ShaderFullscreenPassNode", new JsonObject { ["Error"] = "x" }));
        yield return Case("Keyframes for code", "cannot be animated",
            Add("pass", "ShaderFullscreenPassNode", new JsonObject { ["Code"] = new JsonObject { ["$kf"] = new JsonArray(new JsonArray(0, "x")) } }));
        yield return Case("Keyframes that are no list", "must be an array",
            Add("pass", "ShaderFullscreenPassNode", new JsonObject { ["Padding"] = new JsonObject { ["$kf"] = 5 } }));
        yield return Case("Null for a number", "null is not one", Add("pass", "ShaderFullscreenPassNode", new JsonObject { ["Padding"] = null }));
        yield return Case("An enum name that is none", "NoSuchFormat",
            Add("pass", "ShaderFullscreenPassNode", new JsonObject { ["Format"] = "NoSuchFormat" }));

        static TestCaseData Case(string name, string message, params NodeGraphOperation[] operations)
            => new TestCaseData((object)operations, message).SetName(name);
    }

    [TestCaseSource(nameof(MoreRefusedOperations))]
    public void Each_refusal_names_the_operation_and_leaves_the_graph(NodeGraphOperation[] operations, string message)
    {
        using var scene = new NodeGraphTestScene();
        string before = scene.Capture();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(operations);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected), result.Error?.Message ?? "applied");
            Assert.That(result.Error?.Message, Does.Contain(message));
            Assert.That(result.Error?.Target, Is.EqualTo($"operations[{operations.Length - 1}]"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
        });
    }

    [Test]
    public void Update_remove_and_disconnect_change_what_they_name()
    {
        using var scene = new NodeGraphTestScene();
        ToolResult<EditNodeGraphResponse> built = scene.Edit([.. OnePass(TintCode), Add("time", "TimeNode"), Connect("time.Progress", "pass.In.params.amount")]);
        Assert.That(built.IsSuccess, Is.True, built.Error?.Message);
        string pass = built.Value!.Created.Single(node => node.Ref == "pass").Id;
        string time = built.Value.Created.Single(node => node.Ref == "time").Id;
        string output = built.Value.Created.Single(node => node.Ref == "output").Id;

        ToolResult<EditNodeGraphResponse> result = scene.Edit(
            new NodeGraphOperation { Op = "update", Node = pass, Name = "Tint", Position = [40, 80] },
            new NodeGraphOperation { Op = "disconnect", From = $"{time}.Progress", To = $"{pass}.In.params.amount" },
            new NodeGraphOperation { Op = "remove", Node = time },
            new NodeGraphOperation { Op = "disconnect", Port = $"{output}.Output" },
            Add("again", "OutputNode"),
            Connect($"@ShaderFullscreenPassNode.Out.color", "again.Output"),
            new NodeGraphOperation { Op = "remove", Node = "again" });

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        GraphNode tint = scene.Node(pass);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Applied, Is.True);
            Assert.That(result.Value.Created, Is.Empty, "A node removed in the same call is not reported as created.");
            Assert.That(tint.Name.Value, Is.EqualTo("Tint"));
            Assert.That(tint.Position.Value, Is.EqualTo((40d, 80d)));
            Assert.That(scene.Graph.Nodes.OfType<TimeNode>(), Is.Empty);
            Assert.That(scene.Graph.Nodes.OfType<OutputNode>().Single().InputPort.Connection.Value.IsNull, Is.True);
            Assert.That(scene.Graph.AllConnections, Has.Count.EqualTo(1));
            Assert.That(result.Value.Diagnostics.Select(d => d.Message), Has.Some.Contain("Nothing is connected to this OutputNode"));
        });
    }

    [Test]
    public void A_group_input_adds_an_output_for_a_connection_without_a_port_name()
    {
        using var scene = new NodeGraphTestScene();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(
            Add("group", "GroupNode"),
            Add("in", "GroupInput", graph: "group"),
            Add("pass", "ShaderFullscreenPassNode", graph: "group"),
            Connect("in", "pass.In.source", graph: "group"));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(result.Value!.Ports["group"].Select(port => (port.Name, port.Kind)), Does.Contain(("In.source", "input")));
    }

    // A member that holds an object, such as a brush, gets inputs for that object's properties, named after both.
    [Test]
    public void Nested_inputs_are_named_after_their_member_and_property()
    {
        using var scene = new NodeGraphTestScene();
        var fill = new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SolidColorBrush)),
            ["Color"] = CoreSerializer.SerializeToJsonNode(Colors.Red)
        };

        ToolResult<EditNodeGraphResponse> result = scene.Edit(
            Add("text", "TextNode", new JsonObject { ["Fill.Opacity"] = 50, ["Fill"] = fill }));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        MemberView opacity = scene.Tools.ReadNodeGraph(scene.GraphId).Value!.Nodes!.Single().Members.Single(member => member.Name == "Fill.Opacity");
        Assert.Multiple(() =>
        {
            Assert.That(opacity.Kind, Is.EqualTo("input"));
            Assert.That(opacity.Value!.GetValue<float>(), Is.EqualTo(50f));
            Assert.That(result.Value!.Ports["text"].Select(port => port.Name), Does.Contain("Fill.Color"));
        });
    }

    [Test]
    public void Keyframe_entries_may_be_objects_with_a_spline()
    {
        using var scene = new NodeGraphTestScene();
        var keyFrames = new JsonArray(
            new JsonObject { ["t"] = 0, ["v"] = 0 },
            new JsonObject { ["t"] = 1, ["v"] = 1, ["easing"] = new JsonObject { ["X1"] = 0.25, ["Y1"] = 0, ["X2"] = 0, ["Y2"] = 1 } });

        ToolResult<EditNodeGraphResponse> result = scene.Edit(OnePass(TintCode, new JsonObject { ["In.params.amount"] = new JsonObject { ["$kf"] = keyFrames } }));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        GraphNode pass = scene.Graph.Nodes.OfType<ShaderFullscreenPassNode>().Single();
        var animation = (KeyFrameAnimation<float>)((IAnimatablePropertyAdapter)pass.Items.Single(item => item.Name.Value == "In.params.amount").Property!).Animation!;
        Assert.That(((SplineEasing)animation.KeyFrames[1].Easing.Value).X1, Is.EqualTo(0.25f));
    }

    [Test]
    public void Read_shows_a_monitor_and_a_graph_model_id_reads_the_graph()
    {
        using var scene = new NodeGraphTestScene();
        Assert.That(scene.Edit(OnePass(TintCode)).IsSuccess, Is.True);
        ShaderFullscreenPassNode pass = scene.Graph.Nodes.OfType<ShaderFullscreenPassNode>().Single();
        pass.Code.Value = BrokenCode;

        ToolResult<ReadNodeGraphResponse> result = scene.Tools.ReadNodeGraph(scene.Graph.Id.Value.ToString());
        ToolResult<ReadNodeGraphResponse> malformed = scene.Tools.ReadNodeGraph("not an id");

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        MemberView error = result.Value!.Nodes!.Single(node => node.Id == pass.Id.Value.ToString()).Members.Single(member => member.Name == "Error");
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.OwnerId, Is.EqualTo(scene.GraphId));
            Assert.That(error.Kind, Is.EqualTo("monitor"));
            Assert.That(error.Value!.GetValue<string>(), Does.Contain("undeclaredThing"));
            Assert.That(result.Value.Diagnostics!.Select(d => d.Message), Has.Some.Contain("fails because the node it comes from failed"));
            Assert.That(malformed.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public void A_drawable_graph_is_listed_read_and_warned_about()
    {
        NodeTypeCatalog.EnsureBuiltInNodesRegistered();
        Scene scene = TestWorkspace.CreateScene();
        var project = new Project();
        project.Items.Add(scene);
        _ = new BeutlApplication { Project = { Value = project } };
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var drawable = new NodeGraphDrawable();
        element.AddObject(drawable);
        using var session = new AgentToolkitTestSession(scene);
        var tools = new NodeGraphTools(session.CreateManager());

        NodeGraphSummary listed = tools.ReadNodeGraph().Value!.Graphs!.Single();
        ToolResult<ReadNodeGraphResponse> read = tools.ReadNodeGraph(drawable.Id.Value.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(listed.OwnerType, Is.EqualTo(nameof(NodeGraphDrawable)));
            Assert.That(read.Value!.Diagnostics!.Select(d => d.Message), Has.Some.Contain("draws nothing"));
        });
    }

    [Test]
    public void An_effect_without_an_input_is_warned_about()
    {
        using var scene = new NodeGraphTestScene();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(Add("output", "OutputNode"));

        Assert.That(result.Value!.Diagnostics.Select(d => d.Message), Has.Some.Contain("no FilterEffectInputNode"));
    }

    [TestCase(typeof(double), "double")]
    [TestCase(typeof(long), "long")]
    [TestCase(typeof(ulong), "ulong")]
    [TestCase(typeof(byte), "byte")]
    [TestCase(typeof(object), "object")]
    [TestCase(typeof(bool), "bool")]
    [TestCase(typeof(byte[]), "byte[]")]
    [TestCase(typeof(int?), "int?")]
    [TestCase(typeof(ShaderGraphValue<ShaderImage>), "ShaderGraphValue<ShaderImage>")]
    public void Types_read_as_in_code(Type type, string name)
    {
        Assert.That(NodeGraphMembers.FormatType(type), Is.EqualTo(name));
    }
}
