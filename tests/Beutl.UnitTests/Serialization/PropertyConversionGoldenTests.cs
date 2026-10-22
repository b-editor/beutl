using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
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
// shapes and key order alike, and the paths the serialized graph walks give them. Set BEUTL_UPDATE_GOLDEN_JSON=1 to
// rewrite the files.
[TestFixture]
public partial class PropertyConversionGoldenTests
{
    [Test]
    public void AnElementAndItsObjects()
    {
        var element = new Element
        {
            Name = "golden",
            Start = MediaTime.FromSeconds(1),
            Length = MediaTime.FromSeconds(2),
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
    public void AnElementWithEachSavedMember()
    {
        AssertGolden(CoreSerializer.SerializeToJsonObject(CreateElement()), "element-members");
    }

    [Test]
    public void ASceneWithLayersMarkersAndGroups()
    {
        var scene = new Scene(1280, 720, "golden")
        {
            Start = MediaTime.FromSeconds(1),
            Duration = MediaTime.FromSeconds(30),
            Uri = new Uri("file:///golden/scene/golden.scene"),
        };
        // Fixed Ids, as a group writes its members in the order of their hashes.
        Element first = CreateElement();
        first.Id = new Guid("00000000-0000-0000-0000-0000000000f1");
        first.Uri = new Uri("file:///golden/scene/first.belm");
        var second = new Element
        {
            Id = new Guid("00000000-0000-0000-0000-0000000000f2"),
            Name = "second",
            Length = MediaTime.FromSeconds(1),
            Uri = new Uri("file:///golden/scene/second.belm"),
        };
        scene.Children.Add(first);
        scene.Children.Add(second);
        scene.Groups.Add([first.Id, second.Id]);
        scene.Layers.Add(new TimelineLayer
        {
            ZIndex = 2,
            Color = Colors.Blue,
            IsLocked = true,
            IsAudioMuted = true,
            IsVideoMuted = true,
            IsSolo = true,
            Name = "layer",
        });
        scene.Markers.Add(new SceneMarker { Time = MediaTime.FromSeconds(3), Color = Colors.Green, Note = "note" });

        AssertGolden(CoreSerializer.SerializeToJsonObject(scene), "scene");
        AssertGolden(
            CoreSerializer.SerializeToJsonObject(scene, new CoreSerializerOptions { Mode = CoreSerializationMode.EmbedReferencedObjects }),
            "scene-embedded");
    }

    [Test]
    public void AProjectWithItsVariables()
    {
        var project = new Project { Uri = new Uri("file:///golden/golden.bproj") };
        project.Items.Add(new Scene(1920, 1080, "scene") { Uri = new Uri("file:///golden/scene/scene.scene") });
        project.Variables[ProjectVariableKeys.FrameRate] = "30";

        AssertGolden(CoreSerializer.SerializeToJsonObject(project), "project");
    }

    // The keyframes are in the serialized graph too, so their paths are pinned with the element's.
    [Test]
    public void TheSerializedGraphPathsOfAnElement()
    {
        Element element = CreateElement();
        var lines = new List<string>();
        foreach ((CoreObject obj, SerializedGraphPath path) in SerializedGraphPaths.EnumerateSerializedGraphDescendantPaths(element))
            lines.Add($"{obj.GetType().Name} {path.Stable} {path.Positional}");
        lines.Add("");
        SerializedGraphTraversal.Visit(element, "$", (value, path) =>
        {
            lines.Add($"{value.GetType().Name} {path}");
            return false;
        });

        AssertGolden(string.Join("\n", lines), "element-paths", "txt");
    }

    [Test]
    public void AGraphWithEachKindOfNode()
    {
        AssertGolden(CoreSerializer.SerializeToJsonObject(CreateGraph()), "graph");
    }

    // The recovery metadata saves these paths, and the agent toolkit reports them.
    [Test]
    public void TheSerializedGraphPathsOfAGraph()
    {
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value = CreateGraph();
        var element = new Element { Name = "graph" };
        element.AddObject(drawable);

        var lines = new List<string>();
        foreach ((CoreObject obj, SerializedGraphPath path) in SerializedGraphPaths.EnumerateSerializedGraphDescendantPaths(element))
            lines.Add($"{obj.GetType().Name} {path.Stable} {path.Positional}");
        lines.Add("");
        SerializedGraphTraversal.Visit(element, "$", (value, path) =>
        {
            lines.Add($"{value.GetType().Name} {path}");
            return false;
        });

        AssertGolden(string.Join("\n", lines), "graph-paths", "txt");
    }

    // An element with each member it saves, and keyframes with each kind of easing.
    private static Element CreateElement()
    {
        var element = new Element
        {
            Name = "golden",
            Start = MediaTime.FromSeconds(1),
            Length = MediaTime.FromSeconds(4),
            ZIndex = 2,
            AccentColor = Colors.Red,
            IsEnabled = false,
            IsLocked = true,
            EnterTransition = new FadeTransition(),
            ExitTransition = new SlideTransition(),
        };
        var shape = new RectShape();
        var width = new KeyFrameAnimation<float>();
        width.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.Zero, Value = 10, Easing = new LinearEasing() });
        width.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.FromSeconds(1), Value = 20, Easing = new SplineEasing(0.1f, 0.2f, 0.3f, 0.4f) });
        width.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.FromSeconds(2), Value = 30, Easing = new SineEaseIn() });
        shape.Width.SetKeyFrameAnimation(width);
        var height = new KeyFrameAnimation<float> { UseGlobalClock = true };
        height.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.FromSeconds(1), Value = 40 });
        shape.Height.SetKeyFrameAnimation(height);
        element.AddObject(shape);
        return element;
    }

    private static GraphModel CreateGraph()
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
        return graph;
    }

    private static void AssertGolden(JsonObject json, string name, [CallerFilePath] string source = "")
        => AssertGolden(json.ToJsonString(JsonHelper.SerializerOptions), name, "json", source);

    private static void AssertGolden(string text, string name, string extension, [CallerFilePath] string source = "")
    {
        string actual = NumberIds(text) + "\n";
        string path = Path.Combine(Path.GetDirectoryName(source)!, "Golden", $"{name}.{extension}");
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
