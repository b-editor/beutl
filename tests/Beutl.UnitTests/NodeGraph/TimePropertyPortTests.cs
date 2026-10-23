using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

// Time properties and node time ports that #2885 moved to MediaTime take numbers and keep their saved ports.
[TestFixture]
public sealed class TimePropertyPortTests
{
    [Test]
    public void A_number_drives_a_time_property_input_in_seconds()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<SourceVideo>();
        graph.Nodes.Add(node);
        IInputPort input = node.Items.OfType<IInputPort>().Single(port => port.Name.Value == nameof(SourceVideo.OffsetPosition));
        graph.Connect(input, Source(graph, 2.5f));

        Evaluate(graph);

        Assert.Multiple(() =>
        {
            Assert.That(graph.AllConnections.Single().Status.Value, Is.EqualTo(ConnectionStatus.Convert));
            Assert.That(node.Object.Value.OffsetPosition.Evaluate(CompositionContext.Default), Is.EqualTo(MediaTime.FromSeconds(2.5)));
        });
    }

    // Earlier versions name the nested port of a time property, the expression its connection sets and the time port of
    // a scene node with its keyframes over TimeSpan. They read over MediaTime, keeping the connection and the keyframes.
    [Test]
    public void Time_ports_of_an_earlier_version_read_over_MediaTime_with_their_connection_and_keyframes()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<DrawableBrush>();
        var video = new SourceVideo();
        video.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(3)");
        node.Object.Value.Drawable.Value = video;
        graph.Nodes.Add(node);
        INestedInputPort port = Find(node, video.OffsetPosition);
        Connection connection = graph.Connect(port, Source(graph, 2f));
        var sceneNode = new SceneNode();
        var keyFrames = new KeyFrameAnimation<MediaTime>();
        keyFrames.KeyFrames.Add(new KeyFrame<MediaTime> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = MediaTime.FromSeconds(4) } });
        ((IAnimatablePropertyAdapter<MediaTime>)sceneNode.Time.Property!).Animation = keyFrames;
        graph.Nodes.Add(sceneNode);
        string directory = Path.Combine(Path.GetTempPath(), $"beutl-time-ports-{Guid.NewGuid():N}");
        try
        {
            Uri uri = SaveAsEarlierVersion(graph, directory);

            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            GraphModel restored = ((NodeGraphDrawable)element.Objects.Single()).Model.Value!;
            Evaluate(restored);
            IExpressionProperty<MediaTime> offset = ((SourceVideo)restored.Nodes.OfType<FactoryNode<DrawableBrush>>().Single()
                .Object.Value.Drawable.Value!).OffsetPosition;
            var restoredKeyFrames = ((IAnimatablePropertyAdapter<MediaTime>)restored.Nodes.OfType<SceneNode>().Single()
                .Time.Property!).Animation;
            string[] names = [.. TypeNames(CoreSerializer.SerializeToJsonObject(element))];

            Assert.Multiple(() =>
            {
                Assert.That(restored.AllConnections.Single().Id.Value, Is.EqualTo(connection.Id.Value));
                Assert.That(restored.FindNodePort(port.Id.Value), Is.InstanceOf<NestedInputPort<MediaTime>>());
                Assert.That(offset.Evaluate(CompositionContext.Default), Is.EqualTo(MediaTime.FromSeconds(2)));
                Assert.That((offset.Expression as NodePortExpression<MediaTime>)?.Replaced?.ExpressionString,
                    Is.EqualTo("TimeSpan.FromSeconds(3)"));
                Assert.That(restoredKeyFrames?.KeyFrames.OfType<KeyFrame<MediaTime>>().Select(k => (k.KeyTime.Value, k.Value.Value)),
                    Is.EqualTo(new[] { (MediaTime.FromSeconds(1), MediaTime.FromSeconds(4)) }));
                Assert.That(names, Has.Member("[Beutl.NodeGraph]:NestedInputPort<[Beutl.Core]Beutl:MediaTime>")
                    .And.Member("[Beutl.NodeGraph]:NodePortExpression<[Beutl.Core]Beutl:MediaTime>")
                    .And.Member("[Beutl.NodeGraph]:DefaultInputPort<[Beutl.Core]Beutl:MediaTime>"));
                Assert.That(names.Where(name => name.Contains(nameof(TimeSpan))), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // An element file holding the graph as 2.0.0-preview.8 wrote it: TimeSpan strings and type names over TimeSpan.
    private static Uri SaveAsEarlierVersion(GraphModel graph, string directory)
    {
        var drawable = new NodeGraphDrawable();
        drawable.Model.Value = graph;
        var element = new Element { Uri = new Uri(Path.Combine(directory, "graph.belm")) };
        element.AddObject(drawable);
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            CoreSerializer.StoreToUri(element, element.Uri);

        string file = element.Uri.LocalPath;
        JsonObject json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        json["minAppVersion"] = "2.0.0-preview.1";
        string text = json.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            .Replace("[Beutl.Core]Beutl:MediaTime", "[System.Private.CoreLib]System:TimeSpan");
        Assert.That(text, Does.Contain("DefaultInputPort<[System.Private.CoreLib]System:TimeSpan>"));
        File.WriteAllText(file, text);
        return element.Uri;
    }

    private static IEnumerable<string> TypeNames(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(pair => pair is { Key: "$type", Value: JsonValue value }
            ? [value.GetValue<string>()]
            : TypeNames(pair.Value)),
        JsonArray array => array.SelectMany(TypeNames),
        _ => [],
    };
}
