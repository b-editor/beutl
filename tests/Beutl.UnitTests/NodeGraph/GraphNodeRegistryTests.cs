using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Utilities;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class GraphNodeRegistryTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // The registry is process-global; seed it once. Probe a script node (the regression subject),
        // not an always-registered one, so a partial prior registration still triggers RegisterAll().
        if (GraphNodeRegistry.FindItem(typeof(FilterEffectNode<CSharpScriptEffect>)) == null)
        {
            NodesRegistrar.RegisterAll();
        }
    }

    // Regression: the Script group lambda passed to AddGroup must call Register(), otherwise the
    // GroupableRegistryItem is silently discarded and the script filter-effect nodes become unreachable.
    [TestCase(typeof(FilterEffectNode<CSharpScriptEffect>))]
    [TestCase(typeof(FilterEffectNode<SKSLScriptEffect>))]
    [TestCase(typeof(FilterEffectNode<GLSLScriptEffect>))]
    public void RegisterAll_RegistersScriptFilterEffectNodes(Type nodeType)
    {
        Assert.That(GraphNodeRegistry.FindItem(nodeType), Is.Not.Null);
    }

    [TestCase(typeof(OutputNode))]
    [TestCase(typeof(FilterEffectNode<Blur>))]
    [TestCase(typeof(TranslateMatrixNode))]
    public void RegisterAll_RegistersTopLevelGroupAndNestedGroupNodes(Type nodeType)
    {
        Assert.That(GraphNodeRegistry.FindItem(nodeType), Is.Not.Null);
    }

    [Test]
    public void RegisterAll_UsesClearNamesForBuiltInNodes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GraphNodeRegistry.FindItem(typeof(LayerInputNode))?.DisplayName,
                Is.EqualTo(NodeGraphStrings.GraphInputs));
            Assert.That(GraphNodeRegistry.FindItem(typeof(OutputNode))?.DisplayName,
                Is.EqualTo(NodeGraphStrings.GraphOutput));
            Assert.That(GraphNodeRegistry.FindItem(typeof(GroupNode))?.DisplayName,
                Is.EqualTo(NodeGraphStrings.Group));
            Assert.That(GraphNodeRegistry.FindItem(typeof(MeasureNode))?.DisplayName,
                Is.EqualTo(NodeGraphStrings.MeasureBounds));
            Assert.That(GraphNodeRegistry.FindItem(typeof(RandomInt32Node))?.DisplayName,
                Is.EqualTo(NodeGraphStrings.RandomInt32));
            Assert.That(GraphNodeRegistry.FindItem(typeof(TranslateMatrixNode))?.DisplayName,
                Is.EqualTo(NodeGraphStrings.TranslationMatrix));
        });
    }

    [Test]
    public void GetRegistered_returns_a_copy_that_later_registrations_leave_unchanged()
    {
        IList<GraphNodeRegistry.BaseRegistryItem> before = GraphNodeRegistry.GetRegistered();
        int count = before.Count;
        GraphNodeRegistry.GroupableRegistryItem group = before.OfType<GraphNodeRegistry.GroupableRegistryItem>().First();
        int groupCount = group.Items.Count;
        try
        {
            GraphNodeRegistry.RegisterNode<RegistrySnapshotNode>("Snapshot node");
            GraphNodeRegistry.RegisterNodes(group.DisplayName).Add<RegistrySnapshotNode>("Snapshot node").Register();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(before, Has.Count.EqualTo(count));
                Assert.That(group.Items, Has.Count.EqualTo(groupCount));
                Assert.That(GraphNodeRegistry.GetRegistered(), Has.Count.EqualTo(count + 1));
            }
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(RegistrySnapshotNode)]);
        }

        Assert.That(GraphNodeRegistry.FindItem(typeof(RegistrySnapshotNode)), Is.Null);
    }
}

internal sealed partial class RegistrySnapshotNode : GraphNode;
