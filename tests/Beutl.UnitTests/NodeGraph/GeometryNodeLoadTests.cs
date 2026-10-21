using System.Text.Json.Nodes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Serialization;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

public class GeometryNodeLoadTests
{
    // A graph saved before Geometry registered FillType and Transform has neither the members nor the ports.
    [Test]
    public void AGraphSavedWithoutFillTypeAndTransform_KeepsItsConnections()
    {
        var graph = new GraphModel();
        var rect = new RectGeometryNode();
        var shape = new GeometryShapeNode();
        graph.Nodes.AddRange([rect, shape]);
        var width = (IInputPort)rect.Items.Single(item => item.Name == "Width");
        Connection widthConnection = graph.Connect(width, Source(graph, 40f));
        Connection geometryConnection = graph.Connect(shape.Geometry, rect.OutputPort);

        JsonObject json = CoreSerializer.SerializeToJsonObject(graph);
        JsonObject rectJson = json["Nodes"]![0]!.AsObject();
        JsonArray items = rectJson["Items"]!.AsArray();
        JsonNode?[] newPorts = [.. items.Where(item => item?["Name"]?.GetValue<string>() is "FillType" or "Transform")];
        JsonObject geometryJson = rectJson["Object"]!.AsObject();
        Assert.That(newPorts, Has.Length.EqualTo(2));
        Assert.That(geometryJson.Remove("FillType") && geometryJson.Remove("Transform"), Is.True);
        foreach (JsonNode? item in newPorts)
        {
            items.Remove(item);
        }

        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphModel));
        Evaluate(restored);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.AllConnections.Select(connection => connection.Id),
                Is.EquivalentTo(new[] { widthConnection.Id, geometryConnection.Id }));
            Assert.That(restored.AllConnections.Select(connection => connection.Status),
                Is.All.EqualTo(ConnectionStatus.Success));
            Assert.That(restored.FindNodePort(width.Id), Is.Not.Null);
            Assert.That(restored.Nodes[0].Items.Select(item => item.Name), Does.Contain("FillType").And.Contain("Transform"));
        }
    }
}
