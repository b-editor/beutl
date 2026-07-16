using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GraphSnapshotExceptionSafetyTests
{
    private static CompositionContext CreateContext() => new(TimeSpan.Zero);

    [Test]
    public void Build_CreateItemValueFailure_RollsBackAcquiredValuesAndCanRetry()
    {
        var primary = new TestLifecycleException("create-item");
        var cleanup = new TestLifecycleException("item-dispose");
        var node = new LifecycleProbeNode(memberCount: 2);
        node.Members[0].DisposeException = cleanup;
        node.Members[1].CreateException = primary;
        var model = CreateModel(node);
        using var snapshot = new GraphSnapshot();

        Exception? actual = Assert.Throws<TestLifecycleException>(() => snapshot.Build(model, CreateContext()));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(node.Members[0].CreatedValues, Has.Count.EqualTo(1));
            Assert.That(node.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(node.CreatedResources, Is.Empty);
            Assert.That(snapshot.GetResource(0), Is.Null);
        });

        node.Members[0].DisposeException = null;
        node.Members[1].CreateException = null;
        Assert.DoesNotThrow(() => snapshot.Build(model, CreateContext()));
        Assert.That(snapshot.GetResource(0), Is.Not.Null);
    }

    [Test]
    public void Build_ToResourceFailure_RollsBackAllAcquiredObjectsAndCanRetry()
    {
        var primary = new TestLifecycleException("to-resource");
        var first = new LifecycleProbeNode();
        var failing = new LifecycleProbeNode { ToResourceException = primary };
        first.DisposeException = new TestLifecycleException("resource-dispose");
        first.Members[0].DisposeException = new TestLifecycleException("item-dispose");
        var model = CreateModel(first, failing);
        using var snapshot = new GraphSnapshot();

        Exception? actual = Assert.Throws<TestLifecycleException>(() => snapshot.Build(model, CreateContext()));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(first.CreatedResources, Has.Count.EqualTo(1));
            Assert.That(first.CreatedResources[0].UninitializeCount, Is.Zero);
            Assert.That(first.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(first.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(failing.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(snapshot.GetResource(0), Is.Null);
        });

        first.DisposeException = null;
        first.Members[0].DisposeException = null;
        failing.ToResourceException = null;
        Assert.DoesNotThrow(() => snapshot.Build(model, CreateContext()));
        Assert.That(snapshot.GetResource(0), Is.Not.Null);
    }

    [Test]
    public void Build_BindFailure_UninitializesOnlyAttemptedResourcesAndSweepsEverything()
    {
        var primary = new TestLifecycleException("bind");
        var first = new LifecycleProbeNode
        {
            UninitializeException = new TestLifecycleException("uninitialize"),
            DisposeException = new TestLifecycleException("resource-dispose"),
        };
        first.Members[0].DisposeException = new TestLifecycleException("item-dispose");
        var failing = new LifecycleProbeNode { BindException = primary };
        var later = new LifecycleProbeNode();
        var model = CreateModel(first, failing, later);
        using var snapshot = new GraphSnapshot();

        Exception? actual = Assert.Throws<TestLifecycleException>(() => snapshot.Build(model, CreateContext()));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(first.CreatedResources[0].UninitializeCount, Is.EqualTo(1));
            Assert.That(failing.CreatedResources[0].UninitializeCount, Is.Zero);
            Assert.That(later.CreatedResources[0].UninitializeCount, Is.Zero);
            Assert.That(first.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(failing.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(later.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(first.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(failing.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(later.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(snapshot.GetResource(0), Is.Null);
        });

        first.UninitializeException = null;
        first.DisposeException = null;
        first.Members[0].DisposeException = null;
        failing.BindException = null;
        Assert.DoesNotThrow(() => snapshot.Build(model, CreateContext()));
        Assert.That(snapshot.GetResource(2), Is.Not.Null);
    }

    [Test]
    public void Build_InitializeFailure_MarksAttemptBeforeCallAndSweepsEverything()
    {
        var primary = new TestLifecycleException("initialize");
        var first = new LifecycleProbeNode
        {
            UninitializeException = new TestLifecycleException("uninitialize"),
        };
        var failing = new LifecycleProbeNode { InitializeException = primary };
        var later = new LifecycleProbeNode();
        var model = CreateModel(first, failing, later);
        using var snapshot = new GraphSnapshot();

        Exception? actual = Assert.Throws<TestLifecycleException>(() => snapshot.Build(model, CreateContext()));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(first.CreatedResources[0].UninitializeCount, Is.EqualTo(1));
            Assert.That(failing.CreatedResources[0].UninitializeCount, Is.EqualTo(1),
                "Initialize may partially subscribe before throwing, so it must be marked attempted first");
            Assert.That(later.CreatedResources[0].UninitializeCount, Is.Zero);
            Assert.That(first.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(failing.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(later.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(first.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(failing.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(later.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(snapshot.GetResource(0), Is.Null);
        });

        first.UninitializeException = null;
        failing.InitializeException = null;
        Assert.DoesNotThrow(() => snapshot.Build(model, CreateContext()));
        Assert.That(snapshot.GetResource(2), Is.Not.Null);
    }

    [Test]
    public void Rebuild_UninitializeFailure_DetachesStateSweepsAllAndCanRetry()
    {
        var first = new LifecycleProbeNode();
        var second = new LifecycleProbeNode();
        var model = CreateModel(first, second);
        var snapshot = new GraphSnapshot();
        snapshot.Build(model, CreateContext());
        LifecycleProbeNode.Resource firstResource = first.CreatedResources[0];
        LifecycleProbeNode.Resource secondResource = second.CreatedResources[0];
        LifecycleItemValue firstItem = first.Members[0].CreatedValues[0];
        LifecycleItemValue secondItem = second.Members[0].CreatedValues[0];
        var primary = new TestLifecycleException("uninitialize");
        first.UninitializeException = primary;
        first.DisposeException = new TestLifecycleException("resource-dispose");
        first.Members[0].DisposeException = new TestLifecycleException("item-dispose");
        snapshot.MarkDirty();

        Exception? actual = Assert.Throws<TestLifecycleException>(() => snapshot.Build(model, CreateContext()));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(snapshot.GetResource(0), Is.Null);
            Assert.That(firstResource.UninitializeCount, Is.EqualTo(1));
            Assert.That(secondResource.UninitializeCount, Is.EqualTo(1));
            Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
            Assert.That(secondResource.DisposeCount, Is.EqualTo(1));
            Assert.That(firstItem.DisposeCount, Is.EqualTo(1));
            Assert.That(secondItem.DisposeCount, Is.EqualTo(1));
        });

        Assert.DoesNotThrow(snapshot.Dispose, "detached cleanup must be idempotent after the first throw");
        Assert.Multiple(() =>
        {
            Assert.That(firstResource.UninitializeCount, Is.EqualTo(1));
            Assert.That(secondResource.UninitializeCount, Is.EqualTo(1));
            Assert.That(firstResource.DisposeCount, Is.EqualTo(1));
            Assert.That(secondResource.DisposeCount, Is.EqualTo(1));
            Assert.That(firstItem.DisposeCount, Is.EqualTo(1));
            Assert.That(secondItem.DisposeCount, Is.EqualTo(1));
        });

        first.UninitializeException = null;
        first.DisposeException = null;
        first.Members[0].DisposeException = null;
        Assert.DoesNotThrow(() => snapshot.Build(model, CreateContext()));
        Assert.That(snapshot.GetResource(1), Is.Not.Null);
        snapshot.Dispose();
    }

    [Test]
    public async Task BuildRollback_BusyResourceRetainsCompleteOwnershipForDisposeRetry()
    {
        var primary = new TestLifecycleException("initialize");
        var busy = new LifecycleProbeNode { StartBlockedUpdateDuringInitialize = true };
        var failing = new LifecycleProbeNode
        {
            InitializeException = primary,
            WaitForUpdateBeforeInitializeFailure = busy,
        };
        var model = CreateModel(busy, failing);
        var snapshot = new GraphSnapshot();

        Exception? actual = Assert.Throws<TestLifecycleException>(
            () => snapshot.Build(model, CreateContext()));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(snapshot.GetResource(0), Is.SameAs(busy.CreatedResources[0]),
                "a failed pre-reservation must leave the complete rollback state installed");
            Assert.That(snapshot.GetResource(1), Is.SameAs(failing.CreatedResources[0]));
            Assert.That(busy.CreatedResources[0].IsDisposed, Is.False);
            Assert.That(failing.CreatedResources[0].IsDisposed, Is.False);
            Assert.That(busy.Members[0].CreatedValues[0].DisposeCount, Is.Zero);
            Assert.That(failing.Members[0].CreatedValues[0].DisposeCount, Is.Zero);
        });

        busy.ContinueUpdate.Set();
        Assert.That(
            await busy.BlockedUpdateTask!.WaitAsync(TimeSpan.FromSeconds(10)),
            Is.Null);

        snapshot.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.GetResource(0), Is.Null);
            Assert.That(busy.CreatedResources[0].IsDisposed, Is.True);
            Assert.That(failing.CreatedResources[0].IsDisposed, Is.True);
            Assert.That(busy.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
            Assert.That(failing.Members[0].CreatedValues[0].DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void NodeGraphDrawable_Dispose_SweepsSnapshotAndBaseResources()
    {
        var primary = new TestLifecycleException("snapshot-uninitialize");
        var node = new LifecycleProbeNode();
        var model = CreateModel(node);
        var baseEffect = new DisposeProbeFilterEffect();
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = model;
        drawable.FilterEffect.CurrentValue = baseEffect;
        NodeGraphDrawable.Resource resource = drawable.ToResource(CreateContext());
        node.UninitializeException = primary;

        Exception? actual = Assert.Throws<TestLifecycleException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(baseEffect.CreatedResources, Has.Count.EqualTo(1));
            Assert.That(baseEffect.CreatedResources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(node.CreatedResources[0].DisposeCount, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(resource.Dispose);
    }

    [Test]
    public void NodeGraphFilterEffect_Dispose_SweepsSnapshotAndIsIdempotent()
    {
        var primary = new TestLifecycleException("snapshot-uninitialize");
        var node = new LifecycleProbeNode();
        var model = CreateModel(node);
        var effect = new NodeGraphFilterEffect();
        effect.Model.CurrentValue = model;
        NodeGraphFilterEffect.Resource resource = effect.ToResource(CreateContext());
        node.UninitializeException = primary;

        Exception? actual = Assert.Throws<TestLifecycleException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(node.CreatedResources[0].DisposeCount, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(resource.Dispose);
    }

    private static GraphModel CreateModel(params LifecycleProbeNode[] nodes)
    {
        var model = new GraphModel();
        foreach (LifecycleProbeNode node in nodes)
            model.Nodes.Add(node);
        return model;
    }
}

[SuppressResourceClassGeneration]
internal sealed class LifecycleProbeNode : GraphNode
{
    public LifecycleProbeNode(int memberCount = 1)
    {
        for (int i = 0; i < memberCount; i++)
        {
            var member = new LifecycleProbeNodeMember { Name = $"Item{i}" };
            Members.Add(member);
            Items.Add(member);
        }
    }

    public List<LifecycleProbeNodeMember> Members { get; } = [];

    public List<Resource> CreatedResources { get; } = [];

    public Exception? ToResourceException { get; set; }

    public Exception? BindException { get; set; }

    public Exception? InitializeException { get; set; }

    public bool StartBlockedUpdateDuringInitialize { get; set; }

    public LifecycleProbeNode? WaitForUpdateBeforeInitializeFailure { get; set; }

    public ManualResetEventSlim UpdateEntered { get; } = new();

    public ManualResetEventSlim ContinueUpdate { get; } = new();

    public Task<Exception?>? BlockedUpdateTask { get; set; }

    public Exception? UninitializeException { get; set; }

    public Exception? DisposeException { get; set; }

    public override GraphNode.Resource ToResource(CompositionContext context)
    {
        if (ToResourceException != null)
            throw ToResourceException;

        var resource = new Resource(this);
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        CreatedResources.Add(resource);
        return resource;
    }

    internal new sealed class Resource(LifecycleProbeNode owner) : GraphNode.Resource
    {
        public int BindCount { get; private set; }

        public int InitializeCount { get; private set; }

        public int UninitializeCount { get; private set; }

        public int DisposeCount { get; private set; }

        protected override void BindNodePortValuesCore()
        {
            BindCount++;
            if (owner.BindException != null)
                throw owner.BindException;
        }

        protected override void InitializeCore(GraphCompositionContext context)
        {
            InitializeCount++;
            if (owner.StartBlockedUpdateDuringInitialize)
            {
                owner.BlockedUpdateTask = Task.Run(() => Capture(() => Update(context)));
            }

            if (owner.WaitForUpdateBeforeInitializeFailure is { } busy
                && !busy.UpdateEntered.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked rollback resource update did not start.");

            if (owner.InitializeException != null)
                throw owner.InitializeException;
        }

        protected override void UpdateCore(GraphCompositionContext context)
        {
            if (!owner.StartBlockedUpdateDuringInitialize)
                return;

            owner.UpdateEntered.Set();
            if (!owner.ContinueUpdate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked rollback resource update was not released.");
        }

        protected override void UninitializeCore()
        {
            UninitializeCount++;
            if (owner.UninitializeException != null)
                throw owner.UninitializeException;
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
            if (owner.DisposeException != null)
                throw owner.DisposeException;
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
    }
}

internal sealed class LifecycleProbeNodeMember : NodeMember<int>, INodeMember
{
    public List<LifecycleItemValue> CreatedValues { get; } = [];

    public Exception? CreateException { get; set; }

    public Exception? DisposeException { get; set; }

    IItemValue INodeMember.CreateItemValue()
    {
        if (CreateException != null)
            throw CreateException;

        var value = new LifecycleItemValue(this);
        CreatedValues.Add(value);
        return value;
    }
}

internal sealed class LifecycleItemValue(LifecycleProbeNodeMember owner) : IItemValue
{
    public int DisposeCount { get; private set; }

    public PropagateResult PropagateFrom(IItemValue source) => PropagateResult.Success;

    public bool TryCopyFrom(Beutl.Extensibility.IPropertyAdapter source) => true;

    public void SetFromObject(object? value)
    {
    }

    public object? GetBoxed() => null;

    public bool TryLoadFromAnimation(Beutl.Animation.IAnimation animation, TimeSpan time) => true;

    public void Dispose()
    {
        DisposeCount++;
        if (owner.DisposeException != null)
            throw owner.DisposeException;
    }
}

[SuppressResourceClassGeneration]
internal sealed class DisposeProbeFilterEffect : FilterEffect
{
    public List<Resource> CreatedResources { get; } = [];

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override FilterEffect.Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        CreatedResources.Add(resource);
        return resource;
    }

    internal new sealed class Resource : FilterEffect.Resource
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
        }
    }
}

internal sealed class TestLifecycleException(string message) : Exception(message);
