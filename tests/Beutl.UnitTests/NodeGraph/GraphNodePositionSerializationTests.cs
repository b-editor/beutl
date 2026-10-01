using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Serialization;

namespace Beutl.UnitTests.NodeGraph;

public class GraphNodePositionSerializationTests
{
    [TestCase("en-US")]
    [TestCase("fr-FR")]
    [TestCase("de-DE")]
    public void DecimalPosition_RoundTripsForNormalAndFallbackNodes(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            JsonObject json = CreateGraphJson((120.5, 80.25));

            Assert.That(json["Nodes"]![0]!["Position"]!.GetValue<string>(), Is.EqualTo("120.5,80.25"));
            Assert.That(RestoreGraph(json, unavailable: false).Nodes[0].Position, Is.EqualTo((120.5, 80.25)));
            Assert.That(RestoreGraph(json, unavailable: true).Nodes[0].Position, Is.EqualTo((120.5, 80.25)));

            const double preciseX = 1.2345678901234567;
            JsonObject preciseJson = CreateGraphJson((preciseX, 80.25));
            Assert.That(RestoreGraph(preciseJson, unavailable: false).Nodes[0].Position.X, Is.EqualTo(preciseX));
            Assert.That(RestoreGraph(preciseJson, unavailable: true).Nodes[0].Position.X, Is.EqualTo(preciseX));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase("120,80", 120, 80)]
    [TestCase("120.5,80.25", 120.5, 80.25)]
    public void LegacyUnambiguousPosition_LoadsForNormalAndFallbackNodes(string saved, double x, double y)
    {
        JsonObject json = CreateGraphJson(default);
        json["Nodes"]![0]!["Position"] = saved;

        Assert.That(RestoreGraph(json, unavailable: false).Nodes[0].Position, Is.EqualTo((x, y)));
        Assert.That(RestoreGraph(json, unavailable: true).Nodes[0].Position, Is.EqualTo((x, y)));
    }

    [TestCase("120,5,8")]
    [TestCase("120,5,80,25")]
    [TestCase("Infinity,80")]
    public void AmbiguousOrNonFinitePosition_LeavesDefaultAndPreservesFallbackJson(string saved)
    {
        JsonObject json = CreateGraphJson(default);
        json["Nodes"]![0]!["Position"] = saved;
        JsonObject expected = (JsonObject)json.DeepClone();
        expected["Nodes"]![0]!["$type"] = "[Missing.Plugin]Missing:Node";

        Assert.That(RestoreGraph(json, unavailable: false).Nodes[0].Position, Is.EqualTo((0d, 0d)));

        GraphModel fallbackGraph = RestoreGraph(json, unavailable: true);
        Assert.That(fallbackGraph.Nodes[0], Is.InstanceOf<FallbackGraphNode>());
        Assert.That(fallbackGraph.Nodes[0].Position, Is.EqualTo((0d, 0d)));
        Assert.That(JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(fallbackGraph)["Nodes"]![0],
            expected["Nodes"]![0]), Is.True);
    }

    private static JsonObject CreateGraphJson((double X, double Y) position)
    {
        var graph = new GraphModel();
        graph.Nodes.Add(new RandomSingleNode { Position = position });
        return CoreSerializer.SerializeToJsonObject(graph);
    }

    private static GraphModel RestoreGraph(JsonObject json, bool unavailable)
    {
        var copy = (JsonObject)json.DeepClone();
        if (unavailable) copy["Nodes"]![0]!["$type"] = "[Missing.Plugin]Missing:Node";
        return (GraphModel)CoreSerializer.DeserializeFromJsonObject(copy, typeof(GraphModel));
    }
}
