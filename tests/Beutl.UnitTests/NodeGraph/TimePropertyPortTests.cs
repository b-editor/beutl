using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Serialization;
using static Beutl.UnitTests.NodeGraph.NodeGraphTestHelpers;

namespace Beutl.UnitTests.NodeGraph;

// Time properties that #2885 moved to MediaTime still take numbers and keep their saved node ports.
[TestFixture]
public sealed class TimePropertyPortTests
{
    [Test]
    public void A_number_drives_a_time_property_input_in_seconds()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<SourceVideo>();
        graph.Nodes.Add(node);
        IInputPort input = node.Items.OfType<IInputPort>().Single(port => port.Name == nameof(SourceVideo.OffsetPosition));
        graph.Connect(input, Source(graph, 2.5f));

        Evaluate(graph);

        Assert.Multiple(() =>
        {
            Assert.That(graph.AllConnections.Single().Status, Is.EqualTo(ConnectionStatus.Convert));
            Assert.That(node.Object.OffsetPosition.Evaluate(CompositionContext.Default), Is.EqualTo(MediaTime.FromSeconds(2.5)));
        });
    }

    // Until #2885 step 4 the port and the expression it sets keep the TimeSpan names earlier versions read and write.
    [Test]
    public void A_connected_nested_time_port_keeps_its_connection_and_saved_types_through_a_reopen()
    {
        var graph = new GraphModel();
        var node = new FactoryNode<DrawableBrush>();
        var video = new SourceVideo();
        video.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(3)");
        node.Object.Drawable.Value = video;
        graph.Nodes.Add(node);
        INestedInputPort port = Find(node, video.OffsetPosition);
        Connection connection = graph.Connect(port, Source(graph, 2f));

        JsonObject saved = CoreSerializer.SerializeToJsonObject(graph);
        var restored = (GraphModel)CoreSerializer.DeserializeFromJsonObject(saved.DeepClone().AsObject(), typeof(GraphModel));
        Evaluate(restored);
        JsonObject resaved = CoreSerializer.SerializeToJsonObject(restored);
        IExpressionProperty<MediaTime> offset = ((SourceVideo)restored.Nodes.OfType<FactoryNode<DrawableBrush>>().Single()
            .Object.Drawable.Value!).OffsetPosition;

        Assert.Multiple(() =>
        {
            Assert.That(restored.AllConnections.Single().Id, Is.EqualTo(connection.Id));
            Assert.That(restored.FindNodePort(port.Id), Is.InstanceOf<NestedInputPort<TimeSpan>>());
            Assert.That(offset.Evaluate(CompositionContext.Default), Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That((offset.Expression as NodePortExpression<MediaTime>)?.Replaced?.ExpressionString,
                Is.EqualTo("TimeSpan.FromSeconds(3)"));
            foreach (JsonObject file in new[] { saved, resaved })
            {
                string[] names = [.. TypeNames(file)];
                Assert.That(names, Has.Member("[Beutl.NodeGraph]:NestedInputPort<[System.Private.CoreLib]System:TimeSpan>")
                    .And.Member("[Beutl.NodeGraph]:NodePortExpression<[System.Private.CoreLib]System:TimeSpan>"));
                Assert.That(names.Where(name => name.Contains(nameof(MediaTime))), Is.Empty);
            }
        });
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
