using Beutl.Graphics.Effects;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class GraphNodeRegistryTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // The registry is process-global; only seed it once even if other fixtures run first.
        // Probe one of the script nodes (the regression subject) rather than an always-registered
        // node, so a partial prior registration still triggers RegisterAll().
        if (GraphNodeRegistry.FindItem(typeof(FilterEffectNode<CSharpScriptEffect>)) == null)
        {
            NodesRegistrar.RegisterAll();
        }
    }

    // Regression: the Script group lambda passed to AddGroup must call Register(), otherwise
    // the GroupableRegistryItem is constructed and silently discarded, making the script
    // filter-effect nodes unreachable from the node-add menu and library search.
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
}
