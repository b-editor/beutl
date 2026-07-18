using System.Reflection;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class NodeGraphResourceLifecycleTests
{
    [Test]
    public async Task GroupInput_OuterInputValuesAreSnapshotAndLifecycleGuarded()
    {
        var node = new GroupInput();
        var output = new GroupInput.GroupInputPort<int> { Name = "Output" };
        node.Items.Add(output);
        var resource = (GroupInput.Resource)node.ToResource(CompositionContext.Default);
        using var target = new BlockingPropagationItemValue();
        using var source = new ItemValue<int> { Value = 42 };
        using var replacement = new ItemValue<int> { Value = 84 };
        resource.InstallGraphState(
            0,
            [target],
            new Dictionary<INodeMember, int> { [output] = 0 });

        IItemValue[] callerOwned = [source];
        resource.OuterInputValues = callerOwned;
        IReadOnlyList<IItemValue> retained = resource.OuterInputValues!;
        callerOwned[0] = replacement;
        var mutationSurface = (IList<IItemValue>)retained;

        Assert.Multiple(() =>
        {
            Assert.That(retained, Has.Count.EqualTo(1));
            Assert.That(retained[0], Is.SameAs(source),
                "the resource must not retain the caller's mutable array");
            Assert.That(mutationSurface.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => mutationSurface.Add(replacement));
        });

        using var contextSnapshot = new GraphSnapshot();
        var context = new GraphCompositionContext(CompositionContext.Default)
        {
            Resource = resource,
            Snapshot = contextSnapshot,
        };
        Task<Exception?> updateTask = Task.Run(() => Capture(() => resource.Update(context)));

        Assert.That(target.Entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = resource.OuterInputValues);
                Assert.Throws<InvalidOperationException>(() => resource.OuterInputValues = [replacement]);
                Assert.Throws<InvalidOperationException>(resource.Dispose);
                Assert.That(resource.IsDisposed, Is.False);
            });
        }
        finally
        {
            target.Continue.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(target.ReceivedSource, Is.SameAs(source));

        resource.Dispose();
        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.OuterInputValues);
            Assert.Throws<ObjectDisposedException>(() => resource.OuterInputValues = [replacement]);
            Assert.That(retained, Has.Count.EqualTo(1));
            Assert.That(retained[0], Is.SameAs(source));
        });
    }

    [Test]
    public void FilterEffectInput_WrapperRejectsBusyAndDisposedReads()
    {
        var node = new FilterEffectInputNode();
        var resource = (FilterEffectInputNode.Resource)node.ToResource(CompositionContext.Default);
        var retained = resource.Wrapper;

        SetResourceBusy(resource, true);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _ = resource.Wrapper);
        }
        finally
        {
            SetResourceBusy(resource, false);
        }

        resource.Dispose();
        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Wrapper);
            Assert.That(retained.IsDisposed, Is.True);
        });
    }

    [Test]
    public async Task BindNodePortValues_WrapperRejectsConcurrentEntryAndDispose()
    {
        var node = new BlockingBindNode { BlockBind = true };
        var model = new GraphModel();
        model.Nodes.Add(node);
        var snapshot = new GraphSnapshot();
        Task<Exception?> buildTask = Task.Run(
            () => Capture(() => snapshot.Build(model, CompositionContext.Default)));

        Assert.That(node.BindEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        BlockingBindNode.Resource resource = node.CreatedResource!;
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(resource.BindNodePortValues);
                Assert.Throws<InvalidOperationException>(resource.Dispose);
                Assert.That(resource.IsDisposed, Is.False);
            });
        }
        finally
        {
            node.ContinueBind.Set();
        }

        Assert.That(await buildTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(resource.BindCount, Is.EqualTo(1));
        snapshot.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(resource.BindNodePortValues);
        });
    }

    [Test]
    public void GraphSnapshot_DisposeLeavesItReadyForACompleteRebuild()
    {
        var node = new CountingPortNode();
        var model = new GraphModel();
        model.Nodes.Add(node);
        var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);
        GraphNode.Resource first = snapshot.GetResource(0)!;
        var firstValue = (ItemValue<int>)first.ItemValues[0];
        int itemValueDisposeCount = 0;
        firstValue.RegisterDisposer(() => itemValueDisposeCount++);

        snapshot.Dispose();
        snapshot.Build(model, CompositionContext.Default);
        GraphNode.Resource second = snapshot.GetResource(0)!;
        var secondValue = (ItemValue<int>)second.ItemValues[0];
        secondValue.RegisterDisposer(() => itemValueDisposeCount++);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.IsDisposed, Is.False);
            Assert.That(itemValueDisposeCount, Is.EqualTo(1));
        });

        snapshot.Dispose();
        Assert.That(itemValueDisposeCount, Is.EqualTo(2));
    }

    [Test]
    public void GroupNodeResource_DirectConstructionDisposeDoesNotRequireAnOriginal()
    {
        var resource = new GroupNode.Resource();

        Assert.DoesNotThrow(resource.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.DoesNotThrow(resource.Dispose);
        });
    }

    [Test]
    public void GroupNodeResource_UnsubscribesOnlyTheGroupItInitialized()
    {
        var node = new GroupNode();
        var model = new GraphModel();
        model.Nodes.Add(node);
        int baseline = GetTopologySubscriberCount(node.Group);
        var snapshot = new GraphSnapshot();

        snapshot.Build(model, CompositionContext.Default);
        Assert.That(GetTopologySubscriberCount(node.Group), Is.EqualTo(baseline + 1));

        snapshot.Dispose();
        Assert.That(GetTopologySubscriberCount(node.Group), Is.EqualTo(baseline));
    }

    [Test]
    public async Task NodeGraphDrawable_TopologyChangeDuringUpdateIsRebuiltOnNextUpdate()
    {
        var drawable = new NodeGraphDrawable();
        GraphModel model = drawable.Model.CurrentValue!;
        var blockingNode = new BlockingTopologyNode();
        model.Nodes.Add(blockingNode);
        using var resource = (NodeGraphDrawable.Resource)drawable.ToResource(CompositionContext.Default);
        blockingNode.Block = true;
        bool updateOnly = false;
        Task<Exception?> update = Task.Run(
            () => Capture(() => resource.Update(drawable, CompositionContext.Default, ref updateOnly)));

        Assert.That(blockingNode.Entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        var addedNode = new TopologyInitializationProbeNode();
        try
        {
            Assert.DoesNotThrow(() => model.Nodes.Add(addedNode),
                "topology invalidation must not be rejected after the collection has already changed");
            Assert.That(addedNode.InitializeCount, Is.Zero);
        }
        finally
        {
            blockingNode.Block = false;
            blockingNode.Continue.Set();
        }

        Assert.That(await update.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        updateOnly = false;
        resource.Update(drawable, CompositionContext.Default, ref updateOnly);

        Assert.That(addedNode.InitializeCount, Is.EqualTo(1),
            "the topology request recorded during Update must force the next snapshot rebuild");
    }

    [Test]
    public async Task GroupNode_TopologyChangeDuringInnerUpdateIsRebuiltOnNextEvaluation()
    {
        var groupNode = new GroupNode();
        var blockingNode = new BlockingTopologyNode();
        groupNode.Group.Nodes.Add(blockingNode);
        var outerModel = new GraphModel();
        outerModel.Nodes.Add(groupNode);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(outerModel, CompositionContext.Default);
        blockingNode.Block = true;
        Task<Exception?> evaluation = Task.Run(
            () => Capture(() => snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default)));

        Assert.That(blockingNode.Entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        var addedNode = new TopologyInitializationProbeNode();
        try
        {
            Assert.DoesNotThrow(() => groupNode.Group.Nodes.Add(addedNode),
                "an inner-group edit must record invalidation while the group resource is busy");
            Assert.That(addedNode.InitializeCount, Is.Zero);
        }
        finally
        {
            blockingNode.Block = false;
            blockingNode.Continue.Set();
        }

        Assert.That(await evaluation.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

        Assert.That(addedNode.InitializeCount, Is.EqualTo(1),
            "GroupNode.UpdateCore must rebuild a dirty inner snapshot before evaluating it");
    }

    private static Exception? Capture(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void SetResourceBusy(EngineObject.Resource resource, bool busy)
    {
        typeof(EngineObject.Resource).GetField(
            "_resourceOperationDepth",
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(resource, busy ? 1 : 0);
    }

    private static int GetTopologySubscriberCount(GraphModel model)
    {
        var subscribers = (MulticastDelegate?)typeof(GraphModel).GetField(
            nameof(GraphModel.TopologyChanged),
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model);
        return subscribers?.GetInvocationList().Length ?? 0;
    }
}

internal sealed class BlockingPropagationItemValue : IItemValue
{
    public ManualResetEventSlim Entered { get; } = new();

    public ManualResetEventSlim Continue { get; } = new();

    public IItemValue? ReceivedSource { get; private set; }

    public PropagateResult PropagateFrom(IItemValue source)
    {
        ReceivedSource = source;
        Entered.Set();
        if (!Continue.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The blocked item-value propagation was not released.");

        return PropagateResult.Success;
    }

    public bool TryCopyFrom(IPropertyAdapter source) => false;

    public void SetFromObject(object? value)
    {
    }

    public object? GetBoxed() => null;

    public bool TryLoadFromAnimation(IAnimation animation, TimeSpan time) => false;

    public void Dispose()
    {
        Entered.Dispose();
        Continue.Dispose();
    }
}

[SuppressResourceClassGeneration]
internal sealed class BlockingBindNode : GraphNode
{
    public bool BlockBind { get; set; }

    public ManualResetEventSlim BindEntered { get; } = new();

    public ManualResetEventSlim ContinueBind { get; } = new();

    public Resource? CreatedResource { get; private set; }

    public override GraphNode.Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(this);
        try
        {
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            CreatedResource = resource;
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    internal new sealed class Resource(BlockingBindNode owner) : GraphNode.Resource
    {
        public int BindCount { get; private set; }

        protected override void BindNodePortValuesCore()
        {
            BindCount++;
            if (!owner.BlockBind)
                return;

            owner.BindEntered.Set();
            if (!owner.ContinueBind.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked node-port bind was not released.");
        }
    }
}

internal sealed partial class CountingPortNode : GraphNode
{
    public CountingPortNode()
    {
        Output = new OutputPort<int> { Name = "Output" };
        Items.Add(Output);
    }

    public OutputPort<int> Output { get; }
}

internal sealed partial class BlockingTopologyNode : GraphNode
{
    public bool Block { get; set; }

    public ManualResetEventSlim Entered { get; } = new();

    public ManualResetEventSlim Continue { get; } = new();

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            var owner = (BlockingTopologyNode)GetOriginal();
            if (!owner.Block)
                return;

            owner.Entered.Set();
            if (!owner.Continue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked topology node was not released.");
        }
    }
}

internal sealed partial class TopologyInitializationProbeNode : GraphNode
{
    public int InitializeCount { get; private set; }

    public partial class Resource
    {
        protected override void InitializeCore(GraphCompositionContext context)
        {
            ((TopologyInitializationProbeNode)GetOriginal()).InitializeCount++;
        }
    }
}
