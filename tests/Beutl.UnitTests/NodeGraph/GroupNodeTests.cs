using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class GroupNodeTests
{
    [Test]
    public void RemovingAnOutputPortAfterTheFirstStopsMirroringIt()
    {
        GroupNode group = CreateAttachedGroup();
        var output = new GroupOutput();
        group.Group.Nodes.Add(output);
        GroupOutput.GroupOutputPort<float>[] ports =
            [.. Enumerable.Range(0, 3).Select(i => new GroupOutput.GroupOutputPort<float> { Name = $"Out{i}" })];
        foreach (GroupOutput.GroupOutputPort<float> port in ports)
            output.Items.Add(port);
        INodeMember mirror = group.Items[2];

        output.Items.RemoveAt(2);
        ports[2].Name = "Renamed";

        Assert.That(mirror.Name, Is.EqualTo("Out2"));
    }

    [Test]
    public void RemovingAnInputPortAfterTheFirstStopsMirroringIt()
    {
        GroupNode group = CreateAttachedGroup();
        var input = new GroupInput();
        group.Group.Nodes.Add(input);
        GroupInput.GroupInputPort<float>[] ports =
            [.. Enumerable.Range(0, 3).Select(i => new GroupInput.GroupInputPort<float> { Name = $"In{i}" })];
        foreach (GroupInput.GroupInputPort<float> port in ports)
            input.Items.Add(port);
        INodeMember mirror = group.Items[2];

        input.Items.RemoveAt(2);
        ports[2].Name = "Renamed";

        Assert.That(mirror.Name, Is.EqualTo("In2"));
    }

    // GroupNode only follows the group's input and output nodes once it is attached to a hierarchy root.
    private static GroupNode CreateAttachedGroup()
    {
        var graph = new GraphModel();
        var group = new GroupNode();
        graph.Nodes.Add(group);
        ((IModifiableHierarchical)new TestRoot()).AddChild(graph);
        return group;
    }

    private sealed class TestRoot : Hierarchical, IHierarchicalRoot
    {
        public event EventHandler<IHierarchical>? DescendantAttached;

        public event EventHandler<IHierarchical>? DescendantDetached;

        public void OnDescendantAttached(IHierarchical descendant)
            => DescendantAttached?.Invoke(this, descendant);

        public void OnDescendantDetached(IHierarchical descendant)
            => DescendantDetached?.Invoke(this, descendant);
    }
}
