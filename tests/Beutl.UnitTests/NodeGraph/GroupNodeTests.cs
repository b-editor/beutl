using System.ComponentModel.DataAnnotations;
using Beutl.Collections;
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

    [Test]
    public void ClearingPortsWithResetRemovesMirrorsAndSubscriptions([Values] bool outputSide)
    {
        GroupNode group = CreateGroupWithPorts(2, 2, out GroupOutput output, out GroupInput input);
        var ports = (CoreList<INodeMember>)(outputSide ? output.Items : input.Items);
        ports.ResetBehavior = ResetBehavior.Reset;
        INodeMember[] oldPorts = [.. ports];
        INodeMember[] oldMirrors = [.. group.Items.Skip(outputSide ? 0 : output.Items.Count).Take(ports.Count)];
        INodeMember[] oppositeMirrors = [.. group.Items.Except(oldMirrors)];

        ports.Clear();

        AssertMirrors(group, output, input);
        Assert.That(group.Items, Is.EqualTo(oppositeMirrors));
        AssertStopsMirroring(oldPorts, oldMirrors);

        ports.Add(CreatePort(outputSide, "AddedAfterClear"));
        AssertMirrors(group, output, input);
        ports.RemoveAt(0);
        Assert.That(group.Items, Is.EqualTo(oppositeMirrors));
    }

    [Test]
    public void ReplacingPortsWithResetRebuildsMirrorsAndSubscriptions(
        [Values] bool outputSide, [Values(0, 2)] int oldCount, [Values(0, 1, 2, 5)] int newCount)
    {
        GroupNode group = CreateGroupWithPorts(outputSide ? oldCount : 2, outputSide ? 2 : oldCount,
            out GroupOutput output, out GroupInput input);
        var ports = (CoreList<INodeMember>)(outputSide ? output.Items : input.Items);
        ports.ResetBehavior = ResetBehavior.Reset;
        INodeMember[] oldPorts = [.. ports];
        INodeMember[] oldMirrors = [.. group.Items.Skip(outputSide ? 0 : output.Items.Count).Take(ports.Count)];
        INodeMember[] oppositeMirrors = [.. group.Items.Except(oldMirrors)];
        INodeMember[] newPorts = [.. Enumerable.Range(0, newCount).Select(i => CreatePort(outputSide, $"New{i}"))];

        ports.Replace(newPorts);

        AssertMirrors(group, output, input);
        Assert.That(group.Items.Where(oppositeMirrors.Contains), Is.EqualTo(oppositeMirrors));
        AssertStopsMirroring(oldPorts, oldMirrors);

        foreach (INodeMember port in newPorts)
        {
            port.Name += "Renamed";
            ((NodeMember)port).Display = new DisplayAttribute { Name = port.Name };
        }
        AssertMirrors(group, output, input);

        INodeMember[] newMirrors = [.. group.Items.Except(oppositeMirrors)];
        ports.Clear();
        Assert.That(group.Items, Is.EqualTo(oppositeMirrors));
        AssertStopsMirroring(newPorts, newMirrors);
    }

    [Test]
    public void EditingInputPortsAfterAnOutputResetKeepsBothSidesInOrder()
    {
        GroupNode group = CreateGroupWithPorts(2, 2, out GroupOutput output, out GroupInput input);
        ((CoreList<INodeMember>)output.Items).ResetBehavior = ResetBehavior.Reset;
        ((CoreList<INodeMember>)input.Items).ResetBehavior = ResetBehavior.Reset;
        INodeMember survivingInputMirror = group.Items[3];

        output.Items.Clear();
        input.Items.RemoveAt(0);

        Assert.That(group.Items, Is.EqualTo(new[] { survivingInputMirror }));
        AssertMirrors(group, output, input);

        output.Items.Add(CreatePort(true, "NewOutput"));
        ((CoreList<INodeMember>)input.Items).Replace([CreatePort(false, "NewInput")]);
        AssertMirrors(group, output, input);

        ((CoreList<INodeMember>)output.Items).Replace([CreatePort(true, "Out0"), CreatePort(true, "Out1")]);
        input.Items.Add(CreatePort(false, "AddedInput"));
        input.Items.RemoveAt(0);
        AssertMirrors(group, output, input);
    }

    private static GroupNode CreateGroupWithPorts(int outputCount, int inputCount,
        out GroupOutput output, out GroupInput input)
    {
        GroupNode group = CreateAttachedGroup();
        output = new GroupOutput();
        input = new GroupInput();
        group.Group.Nodes.Add(output);
        group.Group.Nodes.Add(input);
        for (int i = 0; i < outputCount; i++) output.Items.Add(CreatePort(true, $"Out{i}"));
        for (int i = 0; i < inputCount; i++) input.Items.Add(CreatePort(false, $"In{i}"));
        return group;
    }

    private static INodeMember CreatePort(bool outputSide, string name)
    {
        INodeMember port = outputSide
            ? new GroupOutput.GroupOutputPort<float>()
            : new GroupInput.GroupInputPort<float>();
        port.Name = name;
        ((NodeMember)port).Display = new DisplayAttribute { Name = name };
        return port;
    }

    private static void AssertMirrors(GroupNode group, GroupOutput output, GroupInput input)
    {
        INodeMember[] sources = [.. output.Items, .. input.Items];
        Assert.That(group.Items.Count, Is.EqualTo(sources.Length));
        for (int i = 0; i < sources.Length; i++)
        {
            INodeMember mirror = group.Items[i];
            Assert.That(mirror, i < output.Items.Count ? Is.InstanceOf<IOutputPort>() : Is.InstanceOf<IInputPort>());
            Assert.That(mirror.Name, Is.EqualTo(sources[i].Name));
            Assert.That(mirror.AssociatedType, Is.EqualTo(sources[i].AssociatedType));
            Assert.That(((NodeMember)mirror).Display, Is.SameAs(((NodeMember)sources[i]).Display));
        }
    }

    private static void AssertStopsMirroring(INodeMember[] ports, INodeMember[] mirrors)
    {
        Assert.That(mirrors.Length, Is.EqualTo(ports.Length));
        for (int i = 0; i < ports.Length; i++)
        {
            string name = mirrors[i].Name;
            DisplayAttribute? display = ((NodeMember)mirrors[i]).Display;
            ports[i].Name = $"Detached{i}";
            ((NodeMember)ports[i]).Display = new DisplayAttribute { Name = $"Detached{i}" };
            Assert.That(mirrors[i].Name, Is.EqualTo(name));
            Assert.That(((NodeMember)mirrors[i]).Display, Is.SameAs(display));
        }
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
