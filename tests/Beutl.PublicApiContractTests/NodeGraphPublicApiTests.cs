using System.Reflection;
using Beutl.Composition;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class NodeGraphPublicApiTests
{
    [Test]
    public void GraphEvaluationPolicy_IsReadableByANonFriendPluginNode()
    {
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Auxiliary);
        var probe = new PublicPolicyProbeNode();
        var model = new GraphModel();
        model.Nodes.Add(probe);
        using var snapshot = new GraphSnapshot();

        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);

        Assert.Multiple(() =>
        {
            Assert.That(probe.ObservedRenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(probe.ObservedPullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(typeof(GraphCompositionContext).GetConstructors(), Is.Empty,
                "the engine must initialize graph topology state before a plugin receives this context");
            Assert.That(
                typeof(CompositionContext).GetProperty(nameof(CompositionContext.RenderIntent))!
                    .SetMethod?.IsPublic,
                Is.Not.True,
                "plugins may observe ambient policy but must not mutate it");
            Assert.That(
                typeof(CompositionContext).GetProperty(nameof(CompositionContext.PullPurpose))!
                    .SetMethod?.IsPublic,
                Is.Not.True,
                "plugins may observe ambient policy but must not mutate it");
        });
    }

    [Test]
    public void GraphResourceLifecycleSurface_IsReadOnlyAndCoreHooksRemainExtensible()
    {
        var probe = new PublicPolicyProbeNode();
        using GraphNode.Resource resource = (GraphNode.Resource)probe.ToResource(CompositionContext.Default);
        using var drawableResource = new NodeGraphDrawable.Resource();
        using var renderNodeDrawableResource = new RenderNodeDrawable.Resource();
        IReadOnlyList<IItemValue> itemValues = resource.ItemValues;
        IReadOnlyDictionary<INodeMember, int> itemIndexMap = resource.ItemIndexMap;
        IReadOnlyList<RenderNode> outputRenderNodes = drawableResource.OutputRenderNode;
        RenderNode? graphNode = renderNodeDrawableResource.GraphNode;

        resource.BindNodePortValues();

        Type resourceType = typeof(GraphNode.Resource);
        Assert.Multiple(() =>
        {
            Assert.That(probe.BindCount, Is.EqualTo(1),
                "the public wrapper must dispatch to an out-of-tree protected Core override");
            Assert.That(itemValues, Is.Empty);
            Assert.That(itemIndexMap, Is.Empty);
            Assert.That(outputRenderNodes, Is.Empty);
            Assert.That(graphNode, Is.Null);
            Assert.That(resourceType.GetProperty(nameof(GraphNode.Resource.ItemValues))!.PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<IItemValue>)));
            Assert.That(resourceType.GetProperty(nameof(GraphNode.Resource.ItemValues))!.SetMethod, Is.Null);
            Assert.That(resourceType.GetProperty(nameof(GraphNode.Resource.ItemIndexMap))!.PropertyType,
                Is.EqualTo(typeof(IReadOnlyDictionary<INodeMember, int>)));
            Assert.That(resourceType.GetProperty(nameof(GraphNode.Resource.ItemIndexMap))!.SetMethod, Is.Null);
            Assert.That(resourceType.GetProperty(nameof(GraphNode.Resource.SlotIndex))!.SetMethod, Is.Null);
            Assert.That(resourceType.GetProperty(nameof(GraphNode.Resource.Renderer))!.SetMethod, Is.Null);
            AssertLifecycleWrapper(resourceType, nameof(GraphNode.Resource.Initialize), "InitializeCore");
            AssertLifecycleWrapper(resourceType, nameof(GraphNode.Resource.Uninitialize), "UninitializeCore");
            AssertLifecycleWrapper(resourceType, nameof(GraphNode.Resource.BindNodePortValues), "BindNodePortValuesCore");
            AssertLifecycleWrapper(resourceType, nameof(GraphNode.Resource.Update), "UpdateCore");
            Assert.That(
                typeof(NodeGraphFilterEffect.Resource).GetProperty(
                    "Snapshot",
                    BindingFlags.Instance | BindingFlags.Public),
                Is.Null,
                "the resource must not expose its mutable owned GraphSnapshot");
            Assert.That(
                typeof(GroupInput.Resource).GetProperty(nameof(GroupInput.Resource.OuterInputValues))!.PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<IItemValue>)));
            Assert.That(
                typeof(NodeGraphDrawable.Resource).GetProperty(nameof(NodeGraphDrawable.Resource.OutputRenderNode))!
                    .PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<RenderNode>)));
            Assert.That(
                typeof(NodeGraphDrawable.Resource).GetProperty(nameof(NodeGraphDrawable.Resource.OutputRenderNode))!
                    .SetMethod,
                Is.Null);
            Assert.That(
                typeof(RenderNodeDrawable.Resource).GetProperty(nameof(RenderNodeDrawable.Resource.GraphNode))!
                    .SetMethod,
                Is.Null);
        });
    }

    [Test]
    public void ConfigureNode_CustomizationUsesTheDedicatedConfiguredCoreHook()
    {
        var node = new PublicConfigureProbeNode();
        var model = new GraphModel();
        model.Nodes.Add(node);
        using var snapshot = new GraphSnapshot();

        snapshot.Build(model, CompositionContext.Default);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

        Assert.That(node.UpdateConfiguredCount, Is.EqualTo(1));
    }

    private static void AssertLifecycleWrapper(Type resourceType, string wrapperName, string coreName)
    {
        MethodInfo core = resourceType.GetMethod(coreName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        Type[] parameterTypes = core.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        MethodInfo wrapper = resourceType.GetMethod(
            wrapperName,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null)!;
        Assert.That(wrapper.IsVirtual && !wrapper.IsFinal, Is.False, $"{wrapperName} must be a non-virtual guard");
        Assert.That(core.IsFamily, Is.True, $"{coreName} must remain available to plugin resources");
        Assert.That(core.IsVirtual && !core.IsFinal, Is.True, $"{coreName} must be overridable");
    }
}

public sealed partial class PublicConfigureProbeNode : ConfigureNode
{
    public int UpdateConfiguredCount { get; private set; }

    public partial class Resource
    {
        protected override void UpdateConfiguredCore(GraphCompositionContext context)
        {
            GetOriginal().UpdateConfiguredCount++;
        }
    }
}

public sealed partial class PublicPolicyProbeNode : GraphNode
{
    public int BindCount { get; private set; }

    public RenderIntent ObservedRenderIntent { get; private set; } = RenderIntent.Preview;

    public RenderPullPurpose ObservedPullPurpose { get; private set; } = RenderPullPurpose.Frame;

    public partial class Resource
    {
        protected override void BindNodePortValuesCore()
        {
            GetOriginal().BindCount++;
        }

        protected override void UpdateCore(GraphCompositionContext context)
        {
            PublicPolicyProbeNode node = GetOriginal();
            node.ObservedRenderIntent = context.RenderIntent;
            node.ObservedPullPurpose = context.PullPurpose;
        }
    }
}
