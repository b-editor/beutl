using Beutl.Composition;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GraphSnapshotFailureTests
{
    [TestCase("member")]
    [TestCase("resource")]
    public void ConstructionFailure_ReleasesCompletedResourcesAndPartialItemValues(string fault)
    {
        var first = new ProbeNode();
        var failing = new ProbeNode { ConstructionFault = fault };
        var model = CreateModel(first, failing);
        var snapshot = new GraphSnapshot();

        Assert.That(Assert.Throws<InvalidOperationException>(() => snapshot.Build(model, CompositionContext.Default)),
            Is.SameAs(failing.Failure));

        Assert.Multiple(() =>
        {
            Assert.That(first.CreatedResources.Single().IsDisposed, Is.True);
            Assert.That(first.CreatedResources.Single().UninitializeCount, Is.Zero);
            Assert.That(first.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
            Assert.That(failing.Members.Sum(member => member.CreatedCount), Is.EqualTo(fault == "member" ? 1 : 2));
            Assert.That(failing.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
            Assert.DoesNotThrow(snapshot.Dispose);
        });
    }

    [TestCase("member")]
    [TestCase("resource")]
    public void ConstructionFailure_AllowsRebuildWithoutMarkDirty(string fault)
    {
        var first = new ProbeNode();
        var failing = new ProbeNode { ConstructionFault = fault };
        var model = CreateModel(first, failing);
        var snapshot = new GraphSnapshot();
        Assert.Throws<InvalidOperationException>(() => snapshot.Build(model, CompositionContext.Default));
        failing.ConstructionFault = null;

        Assert.DoesNotThrow(() => snapshot.Build(model, CompositionContext.Default));
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        snapshot.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(first.EvaluationCount, Is.EqualTo(1));
            Assert.That(failing.EvaluationCount, Is.EqualTo(1));
            Assert.That(first.CreatedResources.All(resource => resource.IsDisposed), Is.True);
            Assert.That(first.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
            Assert.That(failing.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InitializationFailure_UninitializesOnlyAttemptedInitializations(bool failDuringBind)
    {
        var first = new ProbeNode();
        var failing = new ProbeNode { FailDuringBind = failDuringBind, FailDuringInitialize = !failDuringBind };
        var last = new ProbeNode();
        var model = CreateModel(first, failing, last);
        var snapshot = new GraphSnapshot();

        Assert.That(Assert.Throws<InvalidOperationException>(() => snapshot.Build(model, CompositionContext.Default)),
            Is.SameAs(failing.Failure));

        Assert.Multiple(() =>
        {
            Assert.That(first.CreatedResources.Single().UninitializeCount, Is.EqualTo(1));
            Assert.That(failing.CreatedResources.Single().UninitializeCount, Is.EqualTo(failDuringBind ? 0 : 1));
            Assert.That(last.CreatedResources.Single().UninitializeCount, Is.Zero);
            foreach (ProbeNode node in new[] { first, failing, last })
            {
                Assert.That(node.CreatedResources.Single().IsDisposed, Is.True);
                Assert.That(node.ActiveSubscriptions, Is.Zero);
                Assert.That(node.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
            }

            Assert.DoesNotThrow(snapshot.Dispose);
        });
    }

    [TestCase("uninitialize")]
    [TestCase("dispose")]
    [TestCase("item")]
    public void BuildFailure_PreservesOriginalExceptionAndContinuesCleanup(string cleanupFault)
    {
        var first = new ProbeNode { CleanupFault = cleanupFault };
        var failing = new ProbeNode { FailDuringInitialize = true };
        var model = CreateModel(first, failing);
        var snapshot = new GraphSnapshot();

        Assert.That(Assert.Throws<InvalidOperationException>(() => snapshot.Build(model, CompositionContext.Default)),
            Is.SameAs(failing.Failure));

        Assert.Multiple(() =>
        {
            foreach (ProbeNode node in new[] { first, failing })
            {
                ProbeNode.Resource resource = node.CreatedResources.Single();
                Assert.That(resource.UninitializeCount, Is.EqualTo(1));
                Assert.That(resource.DisposeCount, Is.EqualTo(1));
                Assert.That(node.ActiveSubscriptions, Is.Zero);
                Assert.That(node.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
            }

            Assert.DoesNotThrow(snapshot.Dispose);
        });
    }

    [Test]
    public void DisposeFailure_ReleasesOtherOwnershipAndDoesNotRepeatCleanup()
    {
        var first = new ProbeNode { CleanupFault = "uninitialize" };
        var last = new ProbeNode();
        var model = CreateModel(first, last);
        var snapshot = new GraphSnapshot();
        snapshot.Build(model, CompositionContext.Default);

        Assert.That(Assert.Throws<InvalidOperationException>(snapshot.Dispose), Is.SameAs(first.CleanupFailure));
        Assert.DoesNotThrow(snapshot.Dispose);

        Assert.Multiple(() =>
        {
            foreach (ProbeNode node in new[] { first, last })
            {
                ProbeNode.Resource resource = node.CreatedResources.Single();
                Assert.That(resource.UninitializeCount, Is.EqualTo(1));
                Assert.That(resource.DisposeCount, Is.EqualTo(1));
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(node.Members.All(member => member.CreatedCount == member.DisposedCount), Is.True);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BuildReenteredDuringCleanup_IsRejectedWithoutInstallingOrLeakingNestedResources(bool disposing)
    {
        var original = new ProbeNode();
        var replacement = new ProbeNode();
        var nestedFirst = new ProbeNode();
        var nestedSecond = new ProbeNode();
        GraphModel originalModel = CreateModel(original);
        GraphModel replacementModel = CreateModel(replacement);
        GraphModel nestedModel = CreateModel(nestedFirst, nestedSecond);
        var snapshot = new GraphSnapshot();
        snapshot.Build(originalModel, CompositionContext.Default);
        original.UninitializeCallback = () =>
        {
            original.UninitializeCallback = null;
            snapshot.Build(nestedModel, CompositionContext.Default);
        };

        Exception? failure = disposing
            ? Assert.Catch(snapshot.Dispose)
            : Assert.Catch(() =>
            {
                snapshot.MarkDirty();
                snapshot.Build(replacementModel, CompositionContext.Default);
            });

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure!.Message, Does.Contain("already in progress"));
            Assert.That(nestedFirst.CreatedResources, Is.Empty);
            Assert.That(nestedSecond.CreatedResources, Is.Empty);
            Assert.That(original.CreatedResources.Single().UninitializeCount, Is.EqualTo(1));
            Assert.That(original.CreatedResources.Single().DisposeCount, Is.EqualTo(1));
            Assert.That(original.Members.All(m => m.CreatedCount == m.DisposedCount), Is.True);
            Assert.DoesNotThrow(snapshot.Dispose);
        });
        Assert.DoesNotThrow(() => snapshot.Build(replacementModel, CompositionContext.Default));
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        snapshot.Dispose();
        Assert.That(replacement.EvaluationCount, Is.EqualTo(1));
        Assert.That(replacement.CreatedResources.All(r => r.IsDisposed), Is.True);
        Assert.That(replacement.Members.All(m => m.CreatedCount == m.DisposedCount), Is.True);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ConcurrentLifecycleDuringCleanup_IsRejectedWithoutLosingResources(
        bool disposing, bool competingDispose)
    {
        var original = new ProbeNode();
        var replacement = new ProbeNode();
        var competing = new ProbeNode();
        GraphModel originalModel = CreateModel(original);
        GraphModel replacementModel = CreateModel(replacement);
        GraphModel competingModel = CreateModel(competing);
        var snapshot = new GraphSnapshot();
        snapshot.Build(originalModel, CompositionContext.Default);
        using var releaseCleanup = new ManualResetEventSlim();
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        original.UninitializeCallback = () =>
        {
            cleanupEntered.SetResult();
            if (!releaseCleanup.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release graph cleanup.");
        };
        Task owner = Task.Run(() =>
        {
            if (disposing)
                snapshot.Dispose();
            else
            {
                snapshot.MarkDirty();
                snapshot.Build(replacementModel, CompositionContext.Default);
            }
        });
        try
        {
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Exception? failure = competingDispose
                ? Assert.Catch(snapshot.Dispose)
                : Assert.Catch(() => snapshot.Build(competingModel, CompositionContext.Default));
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure!.Message, Does.Contain("already in progress"));
                Assert.That(competing.CreatedResources, Is.Empty);
            });
        }
        finally
        {
            releaseCleanup.Set();
            await owner.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.That(original.CreatedResources.Single().UninitializeCount, Is.EqualTo(1));
        Assert.That(original.CreatedResources.Single().DisposeCount, Is.EqualTo(1));
        snapshot.Dispose();
        Assert.DoesNotThrow(() => snapshot.Build(competingModel, CompositionContext.Default));
        snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);
        snapshot.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(competing.EvaluationCount, Is.EqualTo(1));
            foreach (ProbeNode node in new[] { original, replacement, competing })
            {
                Assert.That(node.CreatedResources.All(r => r.IsDisposed), Is.True);
                Assert.That(node.Members.All(m => m.CreatedCount == m.DisposedCount), Is.True);
            }
        });
    }

    private static GraphModel CreateModel(params ProbeNode[] nodes)
    {
        var model = new GraphModel();
        foreach (ProbeNode node in nodes)
            model.Nodes.Add(node);
        return model;
    }

    [SuppressResourceClassGeneration]
    private sealed class ProbeNode : GraphNode
    {
        public ProbeNode()
        {
            Members = [new ProbeMember(this, 0), new ProbeMember(this, 1)];
            foreach (ProbeMember member in Members)
                Items.Add(member);
        }

        public ProbeMember[] Members { get; }
        public List<Resource> CreatedResources { get; } = [];
        public InvalidOperationException Failure { get; } = new("Graph construction failed.");
        public InvalidOperationException CleanupFailure { get; } = new("Graph cleanup failed.");
        public string? ConstructionFault { get; set; }
        public string? CleanupFault { get; set; }
        public bool FailDuringBind { get; set; }
        public bool FailDuringInitialize { get; set; }
        public Action? UninitializeCallback { get; set; }
        public int ActiveSubscriptions { get; private set; }
        public int EvaluationCount { get; private set; }

        public override GraphNode.Resource ToResource(CompositionContext context)
        {
            if (ConstructionFault == "resource") throw Failure;
            var resource = new Resource(this);
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            CreatedResources.Add(resource);
            return resource;
        }

        public new sealed class Resource(ProbeNode owner) : GraphNode.Resource
        {
            private bool _subscribed;
            public int UninitializeCount { get; private set; }
            public int DisposeCount { get; private set; }

            public override void BindNodePortValues()
            {
                if (owner.FailDuringBind) throw owner.Failure;
            }

            public override void Initialize(GraphCompositionContext context)
            {
                owner.ActiveSubscriptions++;
                _subscribed = true;
                if (owner.FailDuringInitialize) throw owner.Failure;
            }

            public override void Uninitialize()
            {
                UninitializeCount++;
                if (_subscribed)
                {
                    owner.ActiveSubscriptions--;
                    _subscribed = false;
                }

                owner.UninitializeCallback?.Invoke();
                if (owner.CleanupFault == "uninitialize") throw owner.CleanupFailure;
            }

            public override void Update(GraphCompositionContext context) => owner.EvaluationCount++;

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                    if (owner.CleanupFault == "dispose") throw owner.CleanupFailure;
                }

                base.Dispose(disposing);
            }
        }
    }

    private sealed class ProbeMember(ProbeNode owner, int index) : NodeMember, INodeMember
    {
        public IPropertyAdapter? Property => null;
        public Type? AssociatedType => typeof(int);
        public int CreatedCount { get; private set; }
        public int DisposedCount { get; private set; }

        public IItemValue CreateItemValue()
        {
            if (owner.ConstructionFault == "member" && index == 1) throw owner.Failure;
            CreatedCount++;
            var value = new ItemValue<int>();
            value.RegisterDisposer(() =>
            {
                DisposedCount++;
                if (owner.CleanupFault == "item" && index == 0) throw owner.CleanupFailure;
            });
            return value;
        }
    }
}
