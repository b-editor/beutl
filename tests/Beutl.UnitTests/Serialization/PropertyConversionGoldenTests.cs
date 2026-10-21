using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.Serialization;

// Pins the JSON text of the types whose CoreProperty members become IProperty (#2884 steps 7 and 8), keys, value
// shapes and key order alike. Set BEUTL_UPDATE_GOLDEN_JSON=1 to rewrite the files.
[TestFixture]
public partial class PropertyConversionGoldenTests
{
    [Test]
    public void AnElementAndItsObjects()
    {
        var element = new Element
        {
            Name = "golden",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2),
            ZIndex = 3,
        };
        element.AddObject(new RectShape { IsEnabled = false });
        var text = new TextBlock();
        // The default family is the platform's.
        text.FontFamily.Value = new FontFamily("Golden Sans");
        element.AddObject(text);

        AssertGolden(CoreSerializer.SerializeToJsonObject(element), "element");
    }

    [Test]
    public void AGraphWithEachKindOfNode()
    {
        var graph = new GraphModel();
        var brush = new SolidColorBrush();
        var factory = new FactoryNode<Pen> { Position = (120.5, 80.25), IsExpanded = false };
        factory.Object.Brush.Value = brush;
        graph.Nodes.Add(factory);
        graph.Connect(Find(factory, brush.Opacity), Source(graph, 42f, "Opacity"));
        var textNode = new TextNode();
        textNode.Object.FontFamily.Value = new FontFamily("Golden Sans");
        graph.Nodes.Add(textNode);
        graph.Nodes.Add(new SceneNode());
        graph.Nodes.Add(new GroupNode());

        var generative = new AiImageGenerationNode();
        generative.Seed.Property!.SetValue(1234);
        var record = new GenerationRecord
        {
            Fingerprint = "fingerprint",
            ParameterFingerprint = "parameters",
            ModelId = "model",
            Seed = 7,
            Summary = "kept result",
            CreatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            IsPinned = true,
        };
        generative.Generations.Add(record);
        generative.ActiveGenerationId = record.Id;
        graph.Nodes.Add(generative);
        Evaluate(graph);

        AssertGolden(CoreSerializer.SerializeToJsonObject(graph), "graph");
    }

    private static void AssertGolden(JsonObject json, string name, [CallerFilePath] string source = "")
    {
        string actual = NumberIds(json.ToJsonString(JsonHelper.SerializerOptions)) + "\n";
        string path = Path.Combine(Path.GetDirectoryName(source)!, "Golden", name + ".json");
        if (Environment.GetEnvironmentVariable("BEUTL_UPDATE_GOLDEN_JSON") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.That(File.Exists(path), Is.True, $"{path} is missing; run with BEUTL_UPDATE_GOLDEN_JSON=1 to write it.");
        Assert.That(actual, Is.EqualTo(File.ReadAllText(path).ReplaceLineEndings("\n")));
    }

    // Ids and request key seeds are random, so each is numbered in order of first appearance.
    private static string NumberIds(string text)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return GuidPattern().Replace(text, match =>
        {
            if (!ids.TryGetValue(match.Value, out string? id))
                ids[match.Value] = id = $"00000000-0000-0000-0000-{ids.Count + 1:D12}";
            return id;
        });
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\b[0-9a-fA-F]{32}\b")]
    private static partial Regex GuidPattern();
}
