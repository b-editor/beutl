using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.Services;

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
    [TestCase(typeof(FilterEffectNode<FilmGrain>))]
    [TestCase(typeof(FilterEffectNode<Bloom>))]
    [TestCase(typeof(FilterEffectNode<LightRays>))]
    [TestCase(typeof(FilterEffectNode<WaveRefraction>))]
    [TestCase(typeof(FilterEffectNode<Puppet>))]
    [TestCase(typeof(FilterEffectNode<DelayAnimationEffect>))]
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
    public void RegisterAll_GroupsTheFormerRootNodesAndTools()
    {
        var groups = GraphNodeRegistry.GetRegistered().OfType<GraphNodeRegistry.GroupableRegistryItem>().ToArray();
        var inputAndOutput = groups.Single(group => group.DisplayName == NodeGraphStrings.InputAndOutput);
        var drawing = groups.Single(group => group.DisplayName == NodeGraphStrings.Drawing);
        var geometry = groups.Single(group => group.DisplayName == NodeGraphStrings.Geometry);
        var tools = groups.Single(group => group.DisplayName == NodeGraphStrings.ToolsAndValues);
        var matrices = tools.Items.OfType<GraphNodeRegistry.GroupableRegistryItem>()
            .Single(group => group.DisplayName == NodeGraphStrings.TransformMatrices);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(NodeTypes(inputAndOutput), Is.EqualTo(new[]
                { typeof(LayerInputNode), typeof(OutputNode), typeof(FilterEffectInputNode) }));
            Assert.That(NodeTypes(drawing), Is.EqualTo(new[]
                { typeof(TextNode), typeof(ImageSourceNode), typeof(VideoSourceNode), typeof(SceneNode) }));
            Assert.That(NodeTypes(geometry), Is.EqualTo(new[]
                { typeof(GeometryShapeNode), typeof(RectGeometryNode), typeof(EllipseGeometryNode), typeof(RoundedRectGeometryNode) }));
            Assert.That(tools.Items.Select(item => item.DisplayName), Is.EqualTo(new[]
                { NodeGraphStrings.Math, NodeGraphStrings.TransformMatrices, NodeGraphStrings.Values, NodeGraphStrings.Debugging }));
            Assert.That(NodeTypes(matrices), Does.Contain(typeof(TransformNode)));
        }
    }

    [Test]
    public void NodeMenu_UsesLibraryCategoryIdsAndPutsUncategorizedEffectsLast()
    {
        using var library = new LibraryService();
        library.RegisterGroup("Effects", null, KnownLibraryGroupIds.FilterEffect, root => root
            .AddGroup("Same name", null, KnownLibraryGroupIds.FilterEffectBlurAndSharpen, group => group
                .Add<Blur>(KnownLibraryItemFormats.FilterEffect, "Blur in library"))
            .AddGroup("Same name", null, KnownLibraryGroupIds.FilterEffectColorCorrection, group => group
                .Add<Brightness>(KnownLibraryItemFormats.FilterEffect, "Brightness in library"))
            .Add<Sharpen>(KnownLibraryItemFormats.FilterEffect, "Uncategorized in library"));
        GraphNodeRegistry.GroupableRegistryItem[] before = Sections();

        library.RegisterGroup("Translated effects", null, KnownLibraryGroupIds.FilterEffect, root => root
            .AddGroup("Lighting", null, KnownLibraryGroupIds.FilterEffectLightAndShadow, group => group
                .Add<Bloom>(KnownLibraryItemFormats.FilterEffect, "Late library entry")));
        GraphNodeRegistry.GroupableRegistryItem[] after = Sections();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Select(section => section.Id), Is.EqualTo(new[]
            {
                KnownLibraryGroupIds.FilterEffectBlurAndSharpen,
                KnownLibraryGroupIds.FilterEffectColorCorrection,
                NodesRegistrar.FilterEffectGroupId + ".Other"
            }));
            Assert.That(NodeTypes(before[0]), Is.EqualTo(new[] { typeof(FilterEffectNode<Blur>) }));
            Assert.That(NodeTypes(before[1]), Is.EqualTo(new[] { typeof(FilterEffectNode<Brightness>) }));
            Assert.That(NodeTypes(before[^1]), Does.Contain(typeof(FilterEffectNode<Sharpen>)).And.Contain(typeof(FilterEffectNode<Bloom>)));
            Assert.That(after[2].Id, Is.EqualTo(KnownLibraryGroupIds.FilterEffectLightAndShadow));
            Assert.That(NodeTypes(after[2]), Is.EqualTo(new[] { typeof(FilterEffectNode<Bloom>) }));
            Assert.That(after[^1].DisplayName, Is.EqualTo(NodeGraphStrings.OtherEffects));
            Assert.That(NodeTypes(after[^1]), Does.Not.Contain(typeof(FilterEffectNode<Bloom>)));
        }

        GraphNodeRegistry.GroupableRegistryItem[] Sections() => NodeMenuCatalog.GetItems(library)
            .OfType<GraphNodeRegistry.GroupableRegistryItem>().Single(group => group.Id == NodesRegistrar.FilterEffectGroupId)
            .Items.OfType<GraphNodeRegistry.GroupableRegistryItem>().ToArray();
    }

    private static IEnumerable<Type> NodeTypes(GraphNodeRegistry.GroupableRegistryItem group)
        => group.Items.OfType<GraphNodeRegistry.RegistryItem>().Select(item => item.Type);

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

    // Neither of the groups goes away, and the outer one has as many items as before.
    [Test]
    public void Unloading_a_node_says_so_from_inside_a_group_inside_a_group()
    {
        GraphNodeRegistry.RegisterNodes("Outer group")
            .AddGroup("Inner group", inner => inner
                .Add<RegistrySnapshotNode>("Snapshot node")
                .Add<RegistrySecondSnapshotNode>("Second snapshot node")
                .Register())
            .Register();
        int changes = 0;
        EventHandler onChanged = (_, _) => changes++;
        GraphNodeRegistry.Changed += onChanged;
        try
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(RegistrySnapshotNode)]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(GraphNodeRegistry.FindItem(typeof(RegistrySnapshotNode)), Is.Null);
                Assert.That(GraphNodeRegistry.FindItem(typeof(RegistrySecondSnapshotNode)), Is.Not.Null);
            }
        }
        finally
        {
            GraphNodeRegistry.Changed -= onChanged;
            TypeUnloadNotifier.NotifyUnloading([typeof(RegistrySecondSnapshotNode)]);
        }
    }

    [Test]
    public void A_node_group_id_survives_snapshotting_and_registration_under_a_different_title()
    {
        const string rootId = "Beutl.UnitTests.NodeGroup";
        const string categoryId = rootId + ".Category";
        try
        {
            GraphNodeRegistry.RegisterNodes("Original group").WithId(rootId)
                .AddGroup("Original category", group => group.WithId(categoryId).Add<RegistrySnapshotNode>("First node").Register())
                .Register();
            GraphNodeRegistry.RegisterNodes("Translated group").WithId(rootId)
                .AddGroup("Translated category", group => group.WithId(categoryId).Add<RegistrySecondSnapshotNode>("Second node").Register())
                .Register();

            var root = GraphNodeRegistry.GetRegistered().OfType<GraphNodeRegistry.GroupableRegistryItem>()
                .Single(group => group.Id == rootId);
            var category = (GraphNodeRegistry.GroupableRegistryItem)root.Items.Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(root.DisplayName, Is.EqualTo("Original group"));
                Assert.That(category.Id, Is.EqualTo(categoryId));
                Assert.That(category.Items.Select(item => item.DisplayName), Is.EqualTo(new[] { "First node", "Second node" }));
            }
        }
        finally
        {
            TypeUnloadNotifier.NotifyUnloading([typeof(RegistrySnapshotNode), typeof(RegistrySecondSnapshotNode)]);
        }
    }
}

internal sealed partial class RegistrySnapshotNode : GraphNode;

internal sealed partial class RegistrySecondSnapshotNode : GraphNode;
