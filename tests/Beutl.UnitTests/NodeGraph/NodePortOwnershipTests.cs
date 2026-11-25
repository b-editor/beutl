using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.NodeGraph;

// An edit reaches the frame cache and the media index through the element it finds above what it changed.
[TestFixture]
public class NodePortOwnershipTests
{
    private static (Element Element, VideoSourceNode Node) ElementWithNode()
    {
        var node = new VideoSourceNode();
        var graph = new NodeGraphDrawable();
        graph.Model.Value!.Nodes.Add(node);
        var element = new Element();
        element.Objects.Add(graph);
        return (element, node);
    }

    [Test]
    public void A_key_frame_of_a_port_finds_its_element()
    {
        (Element element, VideoSourceNode node) = ElementWithNode();
        var keyFrame = new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(1) } };
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(keyFrame);

        ((IAnimatablePropertyAdapter)node.Source.Property!).Animation = animation;

        Assert.That(keyFrame.EnumerateAncestors<Element>().FirstOrDefault(), Is.SameAs(element));
    }

    [Test]
    public void A_replaced_animation_leaves_the_port()
    {
        (_, VideoSourceNode node) = ElementWithNode();
        var adapter = (IAnimatablePropertyAdapter)node.Source.Property!;
        var first = new KeyFrameAnimation<VideoSource?>();
        var second = new KeyFrameAnimation<VideoSource?>();

        adapter.Animation = first;
        adapter.Animation = second;

        Assert.Multiple(() =>
        {
            Assert.That(((IHierarchical)first).HierarchicalParent, Is.Null);
            Assert.That(((IHierarchical)second).HierarchicalParent, Is.SameAs(node.Source));
        });
    }

    [Test]
    public void A_value_of_a_port_finds_its_element_and_leaves_when_it_is_replaced()
    {
        var port = new DefaultNodeMember<HierarchicalValue?>();
        var adapter = new NodePropertyAdapter<HierarchicalValue?>("Value");
        port.SetProperty(adapter);
        var first = new HierarchicalValue();
        var second = new HierarchicalValue();

        adapter.SetValue(first);
        Assert.That(first.HierarchicalParent, Is.SameAs(port));

        adapter.SetValue(second);

        Assert.Multiple(() =>
        {
            Assert.That(first.HierarchicalParent, Is.Null);
            Assert.That(second.HierarchicalParent, Is.SameAs(port));
        });
    }

    [Test]
    public void What_an_adapter_already_holds_goes_with_it_to_the_port()
    {
        var port = new DefaultNodeMember<HierarchicalValue?>();
        var value = new HierarchicalValue();
        var animation = new KeyFrameAnimation<HierarchicalValue?>();
        var adapter = new NodePropertyAdapter<HierarchicalValue?>("Value", value, animation);

        port.SetProperty(adapter);

        Assert.Multiple(() =>
        {
            Assert.That(value.HierarchicalParent, Is.SameAs(port));
            Assert.That(((IHierarchical)animation).HierarchicalParent, Is.SameAs(port));
        });
    }

    [Test]
    public void Editing_an_exposed_brush_does_not_change_the_disconnected_input()
    {
        var graph = new GraphModel();
        var inputs = new LayerInputNode();
        var shape = new GeometryShapeNode();
        var original = new SolidColorBrush(Colors.Red);
        shape.Fill.Property!.SetValue(original);
        graph.Nodes.Add(inputs);
        graph.Nodes.Add(shape);

        Assert.That(inputs.AddNodePort(shape.Fill, out Connection? connection), Is.True);
        var port = (LayerInputNode.ILayerInputPort)inputs.Items.Single();
        var exposed = (SolidColorBrush)port.Property!.GetValue()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exposed, Is.Not.SameAs(original));
            Assert.That(exposed.Id.Value, Is.Not.EqualTo(original.Id.Value));
            Assert.That(exposed.HierarchicalParent, Is.SameAs(port));
            Assert.That(exposed.Color.Value, Is.EqualTo(Colors.Red));
        }

        exposed.Color.Value = Colors.Blue;
        graph.Disconnect(connection!);

        Assert.That(shape.Fill.Property.GetValue(), Is.SameAs(original));
        Assert.That(original.Color.Value, Is.EqualTo(Colors.Red));
    }

    [Test]
    public void Exposing_a_media_source_copies_its_file_reference()
    {
        var graph = new GraphModel();
        var inputs = new LayerInputNode();
        var video = new VideoSourceNode();
        var original = new VideoSource();
        var uri = new Uri(Path.Combine(Path.GetTempPath(), "original.mp4"));
        original.ReadFrom(uri);
        video.Source.Property!.SetValue(original);
        graph.Nodes.Add(inputs);
        graph.Nodes.Add(video);

        Assert.That(inputs.AddNodePort(video.Source, out _), Is.True);
        var port = (LayerInputNode.ILayerInputPort)inputs.Items.Single();
        var exposed = (VideoSource)port.Property!.GetValue()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exposed, Is.Not.SameAs(original));
            Assert.That(exposed.HasUri, Is.True);
        }
        Assert.That(exposed.Uri, Is.EqualTo(uri));

        exposed.ReadFrom(new Uri(Path.Combine(Path.GetTempPath(), "replacement.mp4")));

        Assert.That(original.Uri, Is.EqualTo(uri));
    }

    private sealed class HierarchicalValue : Hierarchical;
}
