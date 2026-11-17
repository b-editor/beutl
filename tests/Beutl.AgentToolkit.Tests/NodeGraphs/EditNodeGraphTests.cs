using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using static Beutl.AgentToolkit.Tests.NodeGraphs.NodeGraphTestScene;

namespace Beutl.AgentToolkit.Tests.NodeGraphs;

public sealed class EditNodeGraphTests
{
    [Test]
    public void Builds_a_pass_from_its_code_in_one_history_entry_that_undo_reverts()
    {
        using var scene = new NodeGraphTestScene();
        int undoCount = scene.Session.History.UndoCount;

        ToolResult<EditNodeGraphResponse> result = scene.Edit(OnePass(TintCode, new JsonObject
        {
            ["In.params.tint"] = "1,0.5,0.25,1",
            ["In.params.amount"] = 0.5
        }));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        EditNodeGraphResponse response = result.Value!;
        GraphNode pass = scene.Node(response.Created.Single(node => node.Ref == "pass").Id);
        Assert.Multiple(() =>
        {
            Assert.That(response.Applied, Is.True);
            Assert.That(response.Diagnostics.Where(d => d.Severity == "error"), Is.Empty);
            Assert.That(response.Created.Select(node => node.Ref), Is.EqualTo(new[] { "input", "pass", "output" }));
            Assert.That(scene.Graph.Nodes, Has.Count.EqualTo(3));
            Assert.That(scene.Graph.AllConnections, Has.Count.EqualTo(2));
            Assert.That(Value(pass, "In.params.amount"), Is.EqualTo(0.5f));
            Assert.That(Value(pass, "In.params.tint"), Is.EqualTo(new System.Numerics.Vector4(1, 0.5f, 0.25f, 1)));
            Assert.That(response.Ports["pass"].Select(port => port.Name), Does.Contain("In.params.amount").And.Contain("Out.color"));
            Assert.That(scene.Session.History.UndoCount, Is.EqualTo(undoCount + 1));
        });

        Assert.That(scene.Session.History.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Graph.Nodes, Is.Empty);
            Assert.That(scene.Graph.AllConnections, Is.Empty);
        });

        Assert.That(scene.Session.History.Redo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Graph.Nodes, Has.Count.EqualTo(3));
            Assert.That(scene.Graph.AllConnections, Has.Count.EqualTo(2));
            Assert.That(Value(scene.Node(pass.Id.Value.ToString()), "In.params.amount"), Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void Validate_only_reports_without_applying()
    {
        using var scene = new NodeGraphTestScene();
        string before = scene.Capture();
        int undoCount = scene.Session.History.UndoCount;

        ToolResult<EditNodeGraphResponse> result = scene.Validate(OnePass(TintCode));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Applied, Is.False);
            Assert.That(result.Value.Valid, Is.True);
            Assert.That(result.Value.Ports["pass"].Select(port => port.Name), Does.Contain("In.params.tint"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
            Assert.That(scene.Session.History.UndoCount, Is.EqualTo(undoCount));
        });
    }

    [Test]
    public void Code_that_does_not_compile_is_refused_with_its_stage_and_line()
    {
        using var scene = new NodeGraphTestScene();
        string before = scene.Capture();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(OnePass(BrokenCode));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        NodeGraphDiagnostic error = result.Value!.Diagnostics.Single(d => d.Severity == "error" && d.Lines is not null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Applied, Is.False);
            Assert.That(result.Value.Valid, Is.False);
            Assert.That(error.IsNew, Is.True);
            Assert.That(error.NodeLabel, Is.EqualTo("pass"));
            Assert.That(error.Lines![0].Stage, Is.EqualTo("frag"));
            Assert.That(error.Lines[0].Line, Is.EqualTo(4));
            Assert.That(error.Lines[0].Message, Does.Contain("undeclaredThing"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
        });
    }

    [Test]
    public void A_buffer_left_unconnected_is_refused()
    {
        using var scene = new NodeGraphTestScene();
        string before = scene.Capture();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(OnePass(NeedsBufferCode));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Applied, Is.False);
            // The message is in the UI language, and names the buffer as the code does.
            Assert.That(
                result.Value.Diagnostics.Where(d => d.IsNew == true && d.NodeLabel == "pass").Select(d => d.Message),
                Has.Some.Contain("data"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
        });
    }

    // Only the picture finds this, and reports it on the node whose output it is; it takes that back when it goes.
    [Test]
    public void An_integer_image_reaching_the_output_is_refused()
    {
        using var scene = new NodeGraphTestScene();
        string before = scene.Capture();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(OnePass(IntegerOutputCode));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Applied, Is.False);
            Assert.That(result.Value.Diagnostics.Where(d => d.IsNew == true && d.Severity == "error").Select(d => d.NodeLabel), Does.Contain("pass"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
        });
    }

    [Test]
    public void A_value_of_another_type_on_the_wire_is_refused()
    {
        using var scene = new NodeGraphTestScene();
        NodeGraphOperation[] operations = [.. OnePass(TintCode), Add("time", "TimeNode"), Connect("time.Time", "pass.In.params.tint")];

        ToolResult<EditNodeGraphResponse> result = scene.Edit(operations);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        NodeGraphDiagnostic error = result.Value!.Diagnostics.Single(d => d.Connection is not null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Applied, Is.False);
            Assert.That(error.Connection, Is.EqualTo("time.Time -> pass.In.params.tint"));
            Assert.That(error.Message, Does.Contain("float").And.Contain("Vector4"));
            Assert.That(scene.Graph.Nodes, Is.Empty);
        });
    }

    public static IEnumerable<TestCaseData> RefusedOperations()
    {
        NodeGraphOperation[] pass = OnePass(TintCode);
        yield return new TestCaseData((object)new[] { Add("a", "TimeNode"), Add("a", "TimeNode") }, "already taken")
            .SetName("A ref given twice");
        yield return new TestCaseData((object)new[] { Add("a", "NoSuchNode") }, "is not a node type")
            .SetName("An unknown type");
        yield return new TestCaseData((object)new[] { new NodeGraphOperation { Op = "remove", Node = "ghost" } }, "is no node of this graph")
            .SetName("An unknown node");
        yield return new TestCaseData((object)new[] { pass[0], pass[1], Connect("input.Output", "pass.In.nope") }, "has no port 'In.nope'")
            .SetName("An unknown port");
        yield return new TestCaseData((object)new[] { pass[0], pass[1], pass[2], Connect("pass.In.source", "output.Output") }, "both inputs")
            .SetName("Two inputs");
        NodeGraphOperation[] connectedTwice = [.. pass, Connect("input.Output", "pass.In.source")];
        yield return new TestCaseData((object)connectedTwice, "already connected")
            .SetName("An input already connected");
        yield return new TestCaseData(
                (object)new[]
                {
                    Add("a", "ShaderFullscreenPassNode"), Add("b", "ShaderFullscreenPassNode"),
                    Connect("a.Out.color", "b.In.source"), Connect("b.Out.color", "a.In.source")
                },
                "would close a cycle")
            .SetName("A cycle");
        yield return new TestCaseData((object)new[] { Add("a", "TimeNode", new JsonObject { ["Nope"] = 1 }) }, "has no member 'Nope'")
            .SetName("An unknown member");
        yield return new TestCaseData((object)new[] { Add("a", "TimeNode", new JsonObject { ["Time"] = 1 }) }, "takes no value")
            .SetName("A value for an output");
        yield return new TestCaseData((object)new[] { pass[0], Add("pass", "ShaderFullscreenPassNode", new JsonObject { ["Padding"] = "wide" }) }, "takes float")
            .SetName("A value that does not convert");
        yield return new TestCaseData((object)new[] { new NodeGraphOperation { Op = "connect", From = "x.y", Ref = "r" } }, "takes no 'ref'")
            .SetName("A field the operation does not take");
        yield return new TestCaseData((object)new[] { new NodeGraphOperation { Op = "rename" } }, "is not an operation")
            .SetName("An unknown operation");
    }

    [TestCaseSource(nameof(RefusedOperations))]
    public void An_operation_that_cannot_apply_leaves_the_graph_as_it_was(NodeGraphOperation[] operations, string message)
    {
        using var scene = new NodeGraphTestScene();
        string before = scene.Capture();
        int undoCount = scene.Session.History.UndoCount;

        ToolResult<EditNodeGraphResponse> result = scene.Edit(operations);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain(message).And.StartWith($"operations[{operations.Length - 1}]"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
            Assert.That(scene.Session.History.UndoCount, Is.EqualTo(undoCount));
        });
    }

    [Test]
    public void An_unknown_port_names_the_ports_there_are()
    {
        using var scene = new NodeGraphTestScene();
        NodeGraphOperation[] pass = OnePass(TintCode);

        ToolResult<EditNodeGraphResponse> result = scene.Edit(pass[0], pass[1], Connect("input.Output", "pass.In.sourc"));

        Assert.That(result.Error?.Hint, Does.Contain("In.source (input)").And.Contain("In.params.amount (input)"));
    }

    [Test]
    public void Replace_swaps_the_connection_an_input_has()
    {
        using var scene = new NodeGraphTestScene();
        Assert.That(scene.Edit(OnePass(TintCode)).IsSuccess, Is.True);
        string pass = scene.Graph.Nodes.OfType<ShaderFullscreenPassNode>().Single().Id.Value.ToString();
        string input = scene.Graph.Nodes.Single(node => node is Beutl.NodeGraph.Nodes.FilterEffectInputNode).Id.Value.ToString();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(
            Add("other", "ShaderFullscreenPassNode"),
            Connect($"{input}.Output", "other.In.source"),
            Connect("other.Out.color", $"{pass}.In.source", replace: true));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value!.Applied, Is.True);
            Assert.That(scene.Graph.AllConnections, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void Keyframes_animate_a_member_and_a_plain_value_replaces_them()
    {
        using var scene = new NodeGraphTestScene();

        ToolResult<EditNodeGraphResponse> animated = scene.Edit(OnePass(TintCode, new JsonObject
        {
            ["In.params.amount"] = new JsonObject { ["$kf"] = new JsonArray(new JsonArray(0, 0), new JsonArray(1, 1, "CubicEaseOut")) }
        }));

        Assert.That(animated.IsSuccess, Is.True, animated.Error?.Message);
        GraphNode pass = scene.Node(animated.Value!.Created.Single(node => node.Ref == "pass").Id);
        var animation = (KeyFrameAnimation<float>)((IAnimatablePropertyAdapter)Member(pass, "In.params.amount").Property!).Animation!;
        Assert.Multiple(() =>
        {
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(animation.KeyFrames[1].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(((KeyFrame<float>)animation.KeyFrames[1]).Value.Value, Is.EqualTo(1f));
            Assert.That(animation.KeyFrames[1].Easing.Value, Is.TypeOf<Beutl.Animation.Easings.CubicEaseOut>());
        });

        ToolResult<EditNodeGraphResponse> plain = scene.Edit(new NodeGraphOperation
        {
            Op = "update", Node = pass.Id.Value.ToString(), Values = new JsonObject { ["In.params.amount"] = 0.25 }
        });

        Assert.That(plain.IsSuccess, Is.True, plain.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(((IAnimatablePropertyAdapter)Member(pass, "In.params.amount").Property!).Animation, Is.Null);
            Assert.That(Value(pass, "In.params.amount"), Is.EqualTo(0.25f));
        });
    }

    [Test]
    public void A_locked_element_is_validated_but_not_edited()
    {
        using var scene = new NodeGraphTestScene();
        scene.Element.IsLocked.Value = true;
        string before = scene.Capture();

        ToolResult<EditNodeGraphResponse> validated = scene.Validate(OnePass(TintCode));
        ToolResult<EditNodeGraphResponse> edited = scene.Edit(OnePass(TintCode));

        Assert.Multiple(() =>
        {
            Assert.That(validated.IsSuccess, Is.True, validated.Error?.Message);
            Assert.That(edited.IsSuccess, Is.False);
            Assert.That(edited.Error?.Message, Does.Contain("locked"));
            Assert.That(scene.Capture(), Is.EqualTo(before));
        });
    }

    [Test]
    public void A_loop_and_its_body_are_built_in_one_call()
    {
        using var scene = new NodeGraphTestScene();

        // From outside first: inside, a state port takes the name of the port it connects to, and so does the loop's.
        ToolResult<EditNodeGraphResponse> result = scene.Edit(
            Add("input", "FilterEffectInputNode"),
            Add("loop", "ShaderRepeatNode", new JsonObject { ["Count"] = 3 }),
            Add("output", "OutputNode"),
            Connect("input.Output", "loop.Image"),
            Connect("loop.Image", "output.Output"),
            Add("step", "ShaderFullscreenPassNode", graph: "loop"),
            Connect("@ShaderLoopInput.Image", "step.In.source", graph: "loop"),
            Connect("step.Out.color", "@ShaderLoopOutput.Image", graph: "loop"));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var loop = (ShaderRepeatNode)scene.Node(result.Value!.Created.Single(node => node.Ref == "loop").Id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Applied, Is.True, string.Join("; ", result.Value.Diagnostics.Select(d => d.Message)));
            Assert.That(result.Value.Diagnostics, Is.Empty);
            Assert.That(loop.Group.Value.Nodes, Has.Count.EqualTo(4));
            Assert.That(loop.Group.Value.AllConnections, Has.Count.EqualTo(2));
            Assert.That(scene.Graph.AllConnections, Has.Count.EqualTo(2));
            Assert.That(result.Value.Ports["loop"].Select(port => port.Name), Does.Contain("In.source").And.Contain("Out.color"));
        });
    }

    [Test]
    public void A_group_output_adds_a_port_for_a_connection_without_a_port_name()
    {
        using var scene = new NodeGraphTestScene();

        ToolResult<EditNodeGraphResponse> result = scene.Edit(
            Add("group", "GroupNode"),
            Add("in", "GroupInput", graph: "group"),
            Add("out", "GroupOutput", graph: "group"),
            Add("time", "TimeNode", graph: "group"),
            Connect("time.Progress", "out", graph: "group"));

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        var group = (GroupNode)scene.Node(result.Value!.Created.Single(node => node.Ref == "group").Id);
        GroupOutput output = group.Group.Value.Nodes.OfType<GroupOutput>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(output.Items, Has.Count.EqualTo(1));
            Assert.That(group.Items.OfType<IOutputPort>().Select(port => port.AssociatedType), Is.EqualTo(new[] { typeof(float) }));
            Assert.That(result.Value.Ports["group"].Select(port => port.Kind), Does.Contain("output"));
        });
    }

    private static INodeMember Member(GraphNode node, string name) => node.Items.Single(item => item.Name.Value == name);

    private static object? Value(GraphNode node, string name) => Member(node, name).Property!.GetValue();
}
