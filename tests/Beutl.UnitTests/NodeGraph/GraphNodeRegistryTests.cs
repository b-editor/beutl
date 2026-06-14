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
}
