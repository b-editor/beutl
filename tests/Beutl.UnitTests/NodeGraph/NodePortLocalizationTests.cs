using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class NodePortLocalizationTests
{
    [TestCase("ja-JP", "値", "最大値", "最小値", "左上")]
    [TestCase("en-US", "Value", "Maximum", "Minimum", "Top left")]
    public void BuiltInPortDisplayIsLocalizedWithoutChangingConnectionNames(
        string cultureName, string value, string maximum, string minimum, string topLeft)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            var random = new RandomSingleNode();
            var rectangle = new Beutl.NodeGraph.Nodes.Utilities.Struct.RectNode();
            Assert.Multiple(() =>
            {
                Assert.That(random.Value.Name, Is.EqualTo("Value"));
                Assert.That(random.Value.Display?.GetName(), Is.EqualTo(value));
                Assert.That(random.Maximum.Name, Is.EqualTo("Maximum"));
                Assert.That(random.Maximum.Display?.GetName(), Is.EqualTo(maximum));
                Assert.That(random.Minimum.Name, Is.EqualTo("Minimum"));
                Assert.That(random.Minimum.Display?.GetName(), Is.EqualTo(minimum));
                Assert.That(rectangle.Position.Name, Is.EqualTo("TopLeft"));
                Assert.That(rectangle.Position.Display?.GetName(), Is.EqualTo(topLeft));
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void DynamicGroupPortInheritsLocalizedNameFromItsConnectedInput()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            var graph = new GraphGroup();
            var random = new RandomSingleNode();
            var groupInput = new GroupInput();
            graph.Nodes.Add(random);
            graph.Nodes.Add(groupInput);

            bool added = groupInput.AddNodePort(random.Maximum, out _);

            Assert.That(added, Is.True);
            INodeMember port = groupInput.Items.Single();
            Assert.That(port.Name, Is.EqualTo("Maximum"));
            Assert.That(port.Display?.GetName(), Is.EqualTo("最大値"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void GroupNodeOutputPreservesTheInnerPortsLocalizedDisplay()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            using var scene = new SceneHistoryHarness("group-port-display");
            var application = new BeutlApplication();
            application.Items.Add(scene.Scene);
            var root = new GraphModel();
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = root;
            scene.AddElement().AddObject(drawable);
            var group = new GroupNode();
            root.Nodes.Add(group);
            var source = new RandomSingleNode();
            var innerOutput = new GroupOutput();
            group.Group.Nodes.Add(source);
            group.Group.Nodes.Add(innerOutput);

            Assert.That(innerOutput.AddNodePort(source.Value, out _), Is.True);
            INodeMember outerPort = group.Items.Single();
            Assert.That(outerPort.Name, Is.EqualTo("Value"));
            Assert.That(outerPort.Display?.GetName(), Is.EqualTo("値"));

            ((NodeMember)innerOutput.Items.Single()).Display = new DisplayAttribute
            {
                Name = nameof(NodeGraphStrings.Port_Maximum),
                ResourceType = typeof(NodeGraphStrings)
            };
            Assert.That(outerPort.Display?.GetName(), Is.EqualTo("最大値"));

            var consumer = new RandomSingleNode();
            var innerInput = new GroupInput();
            group.Group.Nodes.Add(consumer);
            group.Group.Nodes.Add(innerInput);
            Assert.That(innerInput.AddNodePort(consumer.Maximum, out _), Is.True);
            INodeMember outerInput = group.Items.OfType<IInputPort>().Single();
            Assert.That(outerInput.Display?.GetName(), Is.EqualTo("最大値"));
            ((NodeMember)innerInput.Items.Single()).Display = new DisplayAttribute
            {
                Name = nameof(NodeGraphStrings.Port_Minimum),
                ResourceType = typeof(NodeGraphStrings)
            };
            Assert.That(outerInput.Display?.GetName(), Is.EqualTo("最小値"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void SerializedNodeKeepsPortIdentifiersAndRebuildsLocalizedDisplay()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        try
        {
            var json = CoreSerializer.SerializeToJsonObject(new RandomSingleNode());
            var restored = (RandomSingleNode)CoreSerializer.DeserializeFromJsonObject(json, typeof(GraphNode));

            Assert.That(json.ToJsonString(), Does.Not.Contain("Port_Maximum"));
            Assert.That(restored.Maximum.Name, Is.EqualTo("Maximum"));
            Assert.That(restored.Maximum.Display?.GetName(), Is.EqualTo("最大値"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void RegisteredBuiltInNodesHaveLocalizedDisplayMetadataForMembers()
    {
        if (GraphNodeRegistry.FindItem(typeof(RandomSingleNode)) == null)
            NodesRegistrar.RegisterAll();

        string[] missing = GraphNodeRegistry.GetRegistered()
            .SelectMany(Leaves)
            .Where(item => item.Type.Assembly == typeof(NodesRegistrar).Assembly)
            .SelectMany(item => ((GraphNode)Activator.CreateInstance(item.Type)!).EnumerateMembers()
                .Where(member => member.Display is not { ResourceType: not null } display
                    || string.IsNullOrWhiteSpace(display.GetName()))
                .Select(member => $"{item.Type.Name}.{member.Name}"))
            .ToArray();

        Assert.That(missing, Is.Empty, string.Join(", ", missing));

        static IEnumerable<GraphNodeRegistry.RegistryItem> Leaves(GraphNodeRegistry.BaseRegistryItem item)
        {
            if (item is GraphNodeRegistry.RegistryItem leaf) yield return leaf;
            else if (item is GraphNodeRegistry.GroupableRegistryItem group)
            {
                foreach (GraphNodeRegistry.RegistryItem child in group.Items.SelectMany(Leaves))
                    yield return child;
            }
        }
    }
}
