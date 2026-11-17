using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Tools;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Shaders;
using static Beutl.AgentToolkit.Tests.NodeGraphs.NodeGraphTestScene;

namespace Beutl.AgentToolkit.Tests.NodeGraphs;

public sealed class ReadNodeGraphTests
{
    [Test]
    public void Without_a_graph_id_it_lists_the_graphs_of_the_scene()
    {
        using var scene = new NodeGraphTestScene();

        ToolResult<ReadNodeGraphResponse> result = scene.Tools.ReadNodeGraph();

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        NodeGraphSummary graph = result.Value!.Graphs!.Single();
        Assert.Multiple(() =>
        {
            Assert.That(graph.GraphId, Is.EqualTo(scene.GraphId));
            Assert.That(graph.OwnerType, Is.EqualTo(nameof(NodeGraphFilterEffect)));
            Assert.That(graph.ElementId, Is.EqualTo(scene.Element.Id.Value.ToString()));
            Assert.That(graph.NodeCount, Is.Zero);
        });
    }

    [Test]
    public void Reads_nodes_members_and_connections_as_edit_node_graph_takes_them()
    {
        using var scene = new NodeGraphTestScene();
        ToolResult<EditNodeGraphResponse> edit = scene.Edit(OnePass(TintCode, new JsonObject
        {
            ["In.params.amount"] = new JsonObject { ["$kf"] = new JsonArray(new JsonArray(0, 0.25), new JsonArray(1.5, 1, "CubicEaseOut")) }
        }));
        Assert.That(edit.IsSuccess, Is.True, edit.Error?.Message);
        string input = edit.Value!.Created.Single(node => node.Ref == "input").Id;
        string pass = edit.Value.Created.Single(node => node.Ref == "pass").Id;

        ToolResult<ReadNodeGraphResponse> result = scene.Tools.ReadNodeGraph(scene.GraphId);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        NodeView passView = result.Value!.Nodes!.Single(node => node.Id == pass);
        MemberView source = passView.Members.Single(member => member.Name == "In.source");
        MemberView amount = passView.Members.Single(member => member.Name == "In.params.amount");
        JsonArray keyFrames = amount.Value!["$kf"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(passView.Type, Is.EqualTo(nameof(ShaderFullscreenPassNode)));
            Assert.That(passView.Members.Single(member => member.Name == "Code").Value!.GetValue<string>(), Is.EqualTo(TintCode));
            Assert.That(source.Kind, Is.EqualTo("input"));
            Assert.That(source.From, Is.EqualTo(new[] { $"{input}.Output" }));
            Assert.That(passView.Members.Single(member => member.Name == "Out.color").To, Has.Count.EqualTo(1));
            Assert.That(amount.ValueType, Is.EqualTo("float"));
            Assert.That(keyFrames.Select(entry => entry![0]!.GetValue<double>()), Is.EqualTo(new[] { 0, 1.5 }));
            Assert.That(keyFrames.Select(entry => entry![1]!.GetValue<double>()), Is.EqualTo(new[] { 0.25, 1 }));
            Assert.That(keyFrames[1]![2]!.GetValue<string>(), Is.EqualTo("CubicEaseOut"));
            Assert.That(result.Value.Connections!.Select(connection => connection.Status), Is.All.Not.EqualTo("error"));
            Assert.That(result.Value.Diagnostics!.Where(d => d.Severity == "error"), Is.Empty);
        });

        // What read_node_graph returns for keyframes applies again as it is.
        ToolResult<EditNodeGraphResponse> again = scene.Edit(new NodeGraphOperation
        {
            Op = "update",
            Node = pass,
            Values = new JsonObject { ["In.params.amount"] = amount.Value!.DeepClone() }
        });
        Assert.That(again.IsSuccess, Is.True, again.Error?.Message);
    }

    [Test]
    public void Include_code_false_leaves_the_code_out()
    {
        using var scene = new NodeGraphTestScene();
        Assert.That(scene.Edit(OnePass(TintCode)).IsSuccess, Is.True);

        ToolResult<ReadNodeGraphResponse> result = scene.Tools.ReadNodeGraph(scene.GraphId, includeCode: false);

        MemberView code = result.Value!.Nodes!.Single(node => node.Type == nameof(ShaderFullscreenPassNode))
            .Members.Single(member => member.Name == "Code");
        Assert.Multiple(() =>
        {
            Assert.That(code.Value, Is.Null);
            Assert.That(code.CodeOmitted, Is.True);
        });
    }

    // Saved code is not compiled when a scene loads, and the scene's own monitors only show what its renders found, so
    // reading evaluates the graph again.
    [Test]
    public void Diagnostics_come_from_a_fresh_evaluation()
    {
        using var scene = new NodeGraphTestScene();
        Assert.That(scene.Edit(OnePass(TintCode)).IsSuccess, Is.True);
        ShaderFullscreenPassNode pass = scene.Graph.Nodes.OfType<ShaderFullscreenPassNode>().Single();
        pass.Code.Value = BrokenCode;
        pass.Error.Value = null;

        ToolResult<ReadNodeGraphResponse> result = scene.Tools.ReadNodeGraph(scene.GraphId);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        NodeGraphDiagnostic error = result.Value!.Diagnostics!.Single(d => d.Lines is not null);
        Assert.Multiple(() =>
        {
            Assert.That(error.Node, Is.EqualTo(pass.Id.Value.ToString()));
            Assert.That(error.Lines![0].Line, Is.EqualTo(4));
            Assert.That(pass.Error.Value, Is.Null, "Reading evaluates a copy and leaves the scene's monitors alone.");
        });
    }

    [Test]
    public void A_group_node_id_reads_its_inner_graph()
    {
        using var scene = new NodeGraphTestScene();
        ToolResult<EditNodeGraphResponse> edit = scene.Edit(NodeGraphTestScene.Add("loop", "ShaderRepeatNode"));
        string loop = edit.Value!.Created.Single().Id;

        ToolResult<ReadNodeGraphResponse> result = scene.Tools.ReadNodeGraph(loop);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.OwnerId, Is.EqualTo(scene.GraphId));
            Assert.That(result.Value.Nodes!.Select(node => node.Type),
                Is.EquivalentTo(new[] { nameof(ShaderLoopInput), nameof(ShaderLoopOutput), nameof(ShaderLoopInfoNode) }));
        });
    }

    [Test]
    public void An_id_that_holds_no_graph_is_refused()
    {
        using var scene = new NodeGraphTestScene();

        ToolResult<ReadNodeGraphResponse> shape = scene.Tools.ReadNodeGraph(scene.Shape.Id.Value.ToString());
        ToolResult<ReadNodeGraphResponse> missing = scene.Tools.ReadNodeGraph(Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(shape.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(shape.Error?.Message, Does.Contain("RectShape"));
            Assert.That(missing.Error?.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }
}
