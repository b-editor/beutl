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

    private sealed class HierarchicalValue : Hierarchical;
}
