using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public class CompatibleNodeFinderTests
{
    [TestCase(typeof(float), typeof(double), true)]
    [TestCase(typeof(float), typeof(Point), true)]
    [TestCase(typeof(float), typeof(MediaTime), true)]
    [TestCase(typeof(RenderNode), typeof(Drawable), true)]
    [TestCase(typeof(Matrix), typeof(Transform), true)]
    [TestCase(typeof(RenderNode), typeof(ShaderGraphValue<ShaderImage>), true)]
    [TestCase(typeof(ContainerRenderNode), typeof(ShaderGraphValue<ShaderImage>), true)]
    [TestCase(typeof(ShaderGraphValue<ShaderImage>), typeof(RenderNode), true)]
    [TestCase(typeof(ShaderGraphValue<ShaderImage>), typeof(object), true)]
    [TestCase(typeof(ShaderGraphValue<ShaderBuffer>), typeof(RenderNode), false)]
    [TestCase(typeof(RenderNode), typeof(ShaderGraphValue<ShaderBuffer>), false)]
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
            Assert.That(results[dynamicItem].Ports, Is.EqualTo(new CompatibleNodeFinder.PortChoice?[] { null }));
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
    public void Find_ShaderImage_OffersGraphOutputAtRootButNotInsideGroup()
    {
        var outputItem = Item<OutputNode>("Graph output");
        var root = new GraphModel();
        var rootPass = new ShaderFullscreenPassNode();
        var buffer = new ShaderBufferNode();
        root.Nodes.Add(rootPass);
        root.Nodes.Add(buffer);
        var group = new GraphGroup();
        var groupPass = new ShaderFullscreenPassNode();
        group.Nodes.Add(groupPass);

        Assert.Multiple(() =>
        {
            Assert.That(CompatibleNodeFinder.Find(root, rootPass.Out("color"), [outputItem]).ContainsKey(outputItem),
                Is.True);
            Assert.That(CompatibleNodeFinder.Find(group, groupPass.Out("color"), [outputItem]).ContainsKey(outputItem),
                Is.False);
            Assert.That(CompatibleNodeFinder.Find(root, buffer.Output, [outputItem]).ContainsKey(outputItem),
                Is.False);
        });
    }

    [Test]
    public void APictureCanBeReadByAPassButNotDrawnOn()
    {
        var passItem = Item<ShaderFullscreenPassNode>("Fullscreen pass");
        var graph = new GraphModel();
        var picture = new FilterEffectInputNode();
        var earlier = new ShaderFullscreenPassNode();
        var pass = new ShaderFullscreenPassNode();
        graph.Nodes.Add(picture);
        graph.Nodes.Add(earlier);
        graph.Nodes.Add(pass);

        IEnumerable<string?> Offered(IOutputPort source)
            => CompatibleNodeFinder.Find(graph, source, [passItem])[passItem].Ports.Select(port => port?.Name);

        Assert.Multiple(() =>
        {
            Assert.That(Offered(picture.Output), Does.Contain("In.source").And.Not.Contain("Targets"));
            Assert.That(Offered(earlier.Out("color")), Does.Contain("In.source").And.Contain("Targets"));
            Assert.That(CompatibleNodeFinder.CanConnect(picture.Output, pass.In("source")), Is.True);
            Assert.That(CompatibleNodeFinder.CanConnect(picture.Output, pass.Targets), Is.False);
            Assert.That(CompatibleNodeFinder.CanConnect(pass.Targets, picture.Output), Is.False);
            Assert.That(CompatibleNodeFinder.CanConnect(earlier.Out("color"), pass.Targets), Is.True);
            Assert.That(CompatibleNodeFinder.Find(graph, pass.Targets, [Item<FilterEffectInputNode>("Effect input")]), Is.Empty);
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

    [Test]
    public void Find_CachesPortMetadataAndOnlyCreatesTheSelectedNodeAgain()
    {
        CountingNode.Constructions = 0;
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        graph.Nodes.Add(source);
        var item = Item<CountingNode>("Counting node");

        var first = CompatibleNodeFinder.Find(graph, source.Value, [item]);
        var second = CompatibleNodeFinder.Find(graph, source.Value, [item]);

        Assert.That(CountingNode.Constructions, Is.EqualTo(1));
        Assert.That(first[item].Ports, Has.Count.EqualTo(1));
        Assert.That(second[item].Ports, Has.Count.EqualTo(1));
        Assert.That(CompatibleNodeFinder.TryCreateSelection(first[item], first[item].Ports[0], source.Value,
            out GraphNode? selected, out INodePort? port), Is.True);
        Assert.That(CountingNode.Constructions, Is.EqualTo(2));
        Assert.That(port?.FindHierarchicalParent<GraphNode>(), Is.SameAs(selected));
    }

    [Test]
    public void Find_SkipsBrokenStaticInitializerAndKeepsOtherSuggestions()
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        graph.Nodes.Add(source);
        var broken = Item<BrokenNode>("Broken node");
        var valid = Item<SwitchNode>("Switch");

        var found = CompatibleNodeFinder.Find(graph, source.Value, [broken, valid]);

        Assert.That(found.ContainsKey(broken), Is.False);
        Assert.That(found.ContainsKey(valid), Is.True);
    }

    [Test]
    public void Find_RetriesNodeAfterTemporaryConstructionFailure()
    {
        var graph = new GraphModel();
        var source = new RandomSingleNode();
        graph.Nodes.Add(source);
        var item = Item<FlakyNode>("Flaky node");
        FlakyNode.Fail = true;
        try
        {
            Assert.That(CompatibleNodeFinder.Find(graph, source.Value, [item]).ContainsKey(item), Is.False);
            FlakyNode.Fail = false;
            Assert.That(CompatibleNodeFinder.Find(graph, source.Value, [item]).ContainsKey(item), Is.True);
        }
        finally
        {
            FlakyNode.Fail = false;
        }
    }

    private static GraphNodeRegistry.RegistryItem Item<T>(string name) where T : GraphNode
        => new(name, Colors.Teal, typeof(T));
}

public sealed partial class CountingNode : GraphNode
{
    public static int Constructions;

    public CountingNode()
    {
        Constructions++;
        Input = AddInput<float>("Input");
    }

    public InputPort<float> Input { get; }
}

public sealed partial class BrokenNode : GraphNode
{
    static BrokenNode() => throw new InvalidOperationException("Broken node initializer");

    public BrokenNode()
    {
    }
}

public sealed partial class FlakyNode : GraphNode
{
    public static bool Fail;

    public FlakyNode()
    {
        if (Fail) throw new InvalidOperationException("Temporary node construction failure");
        Input = AddInput<float>("Input");
    }

    public InputPort<float> Input { get; }
}
