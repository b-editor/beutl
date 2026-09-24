using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Utilities;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class CompatibleNodeFinderTests
{
    [TestCase(typeof(float), typeof(double), true)]
    [TestCase(typeof(float), typeof(Point), true)]
    [TestCase(typeof(RenderNode), typeof(Drawable), true)]
    [TestCase(typeof(Matrix), typeof(Transform), true)]
    [TestCase(typeof(object), typeof(float), true)]
    [TestCase(typeof(float), typeof(RenderNode), false)]
    [TestCase(typeof(string), typeof(Point), false)]
    [TestCase(typeof(float), typeof(char), false)]
    public void CanPropagate_OnlySuggestsGuaranteedConversions(Type output, Type input, bool expected)
    {
        Assert.That(CompatibleNodeFinder.CanPropagate(output, input), Is.EqualTo(expected));
    }

    [Test]
    public void Find_OutputSource_OffersMatchingInputsOnly()
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        graph.Nodes.Add(source);
        var switchItem = Item<SwitchNode>("Switch");
        var measureItem = Item<MeasureNode>("Measure");
        var outputItem = Item<OutputNode>("Graph output");

        var results = CompatibleNodeFinder.Find(graph, source.Value, [switchItem, measureItem, outputItem]);

        Assert.Multiple(() =>
        {
            Assert.That(results.ContainsKey(switchItem), Is.True);
            Assert.That(results[switchItem].Ports.Select(port => port?.Name),
                Is.EquivalentTo(new[] { "True", "False" }));
            Assert.That(results.ContainsKey(measureItem), Is.False);
            Assert.That(results.ContainsKey(outputItem), Is.False);
        });
    }

    [Test]
    public void Find_InputSource_OffersNumericOutputAndAvailableDynamicNode()
    {
        var graph = new GraphGroup();
        var source = new RandomSingleNode();
        graph.Nodes.Add(source);
        var numericItem = Item<RandomDoubleNode>("Random double");
        var effectItem = Item<FilterEffectInputNode>("Effect input");
        var expressionItem = Item<ExpressionNode>("Expression");
        var dynamicItem = Item<GroupInput>("Group input");

        var results = CompatibleNodeFinder.Find(graph, source.Maximum,
            [numericItem, effectItem, expressionItem, dynamicItem]);

        Assert.Multiple(() =>
        {
            Assert.That(results[numericItem].Ports, Has.Count.EqualTo(1));
            Assert.That(results.ContainsKey(effectItem), Is.False);
            Assert.That(results[expressionItem].Ports, Has.Count.EqualTo(1));
            Assert.That(results[dynamicItem].Ports, Is.EqualTo(new INodePort?[] { null }));
        });

        graph.Nodes.Add(new GroupInput());
        results = CompatibleNodeFinder.Find(graph, source.Maximum, [dynamicItem]);
        Assert.That(results.ContainsKey(dynamicItem), Is.False);
    }

    [Test]
    public void Find_RenderSource_OffersGraphOutputAtRootButNotInsideGroup()
    {
        var outputItem = Item<OutputNode>("Graph output");
        var root = new GraphModel();
        var rootSource = new FilterEffectInputNode();
        root.Nodes.Add(rootSource);
        var group = new GraphGroup();
        var groupSource = new FilterEffectInputNode();
        group.Nodes.Add(groupSource);

        Assert.Multiple(() =>
        {
            Assert.That(CompatibleNodeFinder.Find(root, rootSource.Output, [outputItem]).ContainsKey(outputItem),
                Is.True);
            Assert.That(CompatibleNodeFinder.Find(group, groupSource.Output, [outputItem]).ContainsKey(outputItem),
                Is.False);
        });
    }

    [Test]
    public void Find_RootGraph_DoesNotOfferGroupBoundaryPorts()
    {
        var root = new GraphModel();
        var source = new RandomSingleNode();
        root.Nodes.Add(source);
        var groupOutput = Item<GroupOutput>("Group output");

        Assert.That(CompatibleNodeFinder.Find(root, source.Value, [groupOutput]), Is.Empty);
    }

    private static GraphNodeRegistry.RegistryItem Item<T>(string name) where T : GraphNode
        => new(name, Colors.Teal, typeof(T));
}
