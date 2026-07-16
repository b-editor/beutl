using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GraphSnapshotTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void SceneRenderer_AuxiliaryUiPull_ComposesNodeGraphWithAuxiliaryPolicy(bool hitTest)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            string basePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                $"node_graph_aux_{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            try
            {
                var model = new GraphModel();
                var capture = new ContextCaptureNode();
                model.Nodes.Add(capture);
                var drawable = new NodeGraphDrawable();
                drawable.Model.CurrentValue = model;
                var element = new Element
                {
                    Start = TimeSpan.Zero,
                    Length = TimeSpan.FromSeconds(1),
                    IsEnabled = true,
                    Uri = new Uri(Path.Combine(basePath, "element.belm")),
                };
                element.AddObject(drawable);
                var scene = new Scene(64, 64, string.Empty)
                {
                    Uri = new Uri(Path.Combine(basePath, "scene.scene")),
                };
                scene.Children.Add(element);
                using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
                CompositionFrame auxiliaryFrame = renderer.Compositor.EvaluateGraphics(
                    TimeSpan.Zero,
                    RenderPullPurpose.Auxiliary);

                if (hitTest)
                {
                    _ = renderer.HitTest(auxiliaryFrame, new Point(32, 32));
                }
                else
                {
                    _ = renderer.GetBoundaries(auxiliaryFrame, drawable.ZIndex);
                }

                Assert.That(capture.CapturedContexts, Is.Not.Empty);
                Assert.That(
                    capture.CapturedContexts[^1].PullPurpose,
                    Is.EqualTo(RenderPullPurpose.Auxiliary),
                    "UI hit-test/boundary composition must be auxiliary before the renderer starts its auxiliary pull");
            }
            finally
            {
                Directory.Delete(basePath, recursive: true);
            }
        });
    }

    [Test]
    public void NodeGraphDrawable_UsesAmbientCompositionPolicy()
    {
        var model = new GraphModel();
        var capture = new ContextCaptureNode();
        model.Nodes.Add(capture);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = model;
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Auxiliary);

        using NodeGraphDrawable.Resource resource = drawable.ToResource(context);

        Assert.That(capture.CapturedContexts, Has.Count.EqualTo(1));
        Assert.That(capture.CapturedContexts[0].RenderIntent, Is.EqualTo(RenderIntent.Delivery));
        Assert.That(capture.CapturedContexts[0].PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
    }

    [Test]
    public void SceneCompositor_InitializesOneGraphSnapshotPerPullPurpose()
    {
        string basePath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"node_graph_purpose_cache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(basePath);
        try
        {
            var model = new GraphModel();
            var capture = new ContextCaptureNode();
            model.Nodes.Add(capture);
            var drawable = new NodeGraphDrawable();
            drawable.Model.CurrentValue = model;
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
                Uri = new Uri(Path.Combine(basePath, "element.belm")),
            };
            element.AddObject(drawable);
            var scene = new Scene(64, 64, string.Empty)
            {
                Uri = new Uri(Path.Combine(basePath, "scene.scene")),
            };
            scene.Children.Add(element);
            using var compositor = new SceneCompositor(scene, RenderIntent.Preview);

            CompositionFrame firstFrame = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Frame);
            CompositionFrame auxiliaryFrame = compositor.EvaluateGraphics(
                TimeSpan.Zero,
                RenderPullPurpose.Auxiliary);
            CompositionFrame secondFrame = compositor.EvaluateGraphics(
                TimeSpan.FromMilliseconds(100),
                RenderPullPurpose.Frame);

            Assert.Multiple(() =>
            {
                Assert.That(auxiliaryFrame.Objects[0], Is.Not.SameAs(firstFrame.Objects[0]));
                Assert.That(secondFrame.Objects[0], Is.SameAs(firstFrame.Objects[0]));
                Assert.That(
                    capture.InitializedPurposes,
                    Is.EqualTo(new[] { RenderPullPurpose.Frame, RenderPullPurpose.Auxiliary }),
                    "each purpose owns one GraphSnapshot, and the second frame pull must reuse the frame snapshot");
                Assert.That(
                    capture.CapturedContexts.Select(item => item.PullPurpose),
                    Is.EqualTo(new[]
                    {
                        RenderPullPurpose.Frame,
                        RenderPullPurpose.Auxiliary,
                        RenderPullPurpose.Frame,
                    }));
            });
        }
        finally
        {
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void Evaluate_RefreshesRoutingFlagsWithoutRebuild()
    {
        var model = new GraphModel();
        var node = new ContextCaptureNode();
        model.Nodes.Add(node);
        using var snapshot = new GraphSnapshot();
        var firstContext = new CompositionContext(TimeSpan.Zero)
        {
            DisableResourceShare = false,
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
        };
        var secondContext = new CompositionContext(
            TimeSpan.FromSeconds(1),
            RenderIntent.Delivery,
            RenderPullPurpose.Auxiliary)
        {
            DisableResourceShare = true,
            PreferProxy = false,
            PreferredProxyPreset = ProxyPreset.Eighth,
        };

        snapshot.Build(model, firstContext);
        snapshot.Evaluate(CompositionTarget.Graphics, firstContext);
        snapshot.Evaluate(CompositionTarget.Graphics, secondContext);

        Assert.Multiple(() =>
        {
            Assert.That(node.CapturedContexts, Has.Count.EqualTo(2));
            Assert.That(node.CapturedContexts[0].PreferProxy, Is.True);
            Assert.That(node.CapturedContexts[0].PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(node.CapturedContexts[1].DisableResourceShare, Is.True);
            Assert.That(node.CapturedContexts[1].PreferProxy, Is.False);
            Assert.That(node.CapturedContexts[1].PreferredProxyPreset, Is.EqualTo(ProxyPreset.Eighth));
            Assert.That(node.CapturedContexts[0].RenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(node.CapturedContexts[0].PullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
            Assert.That(node.CapturedContexts[1].RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(node.CapturedContexts[1].PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
        });
    }

    [Test]
    public void NodeGraphDrawable_ExistingSnapshotTracksNodeAddAndRemove()
    {
        var model = new GraphModel();
        var first = new ContextCaptureNode();
        model.Nodes.Add(first);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = model;
        using NodeGraphDrawable.Resource resource = drawable.ToResource(CompositionContext.Default);
        Assert.That(first.CapturedContexts, Has.Count.EqualTo(1));

        var second = new ContextCaptureNode();
        model.Nodes.Add(second);
        bool updateOnly = false;
        resource.Update(drawable, CompositionContext.Default, ref updateOnly);

        Assert.Multiple(() =>
        {
            Assert.That(first.CapturedContexts, Has.Count.EqualTo(2));
            Assert.That(second.CapturedContexts, Has.Count.EqualTo(1),
                "adding a node must invalidate and rebuild an already installed snapshot");
        });

        model.Nodes.Remove(first);
        updateOnly = false;
        resource.Update(drawable, CompositionContext.Default, ref updateOnly);

        Assert.Multiple(() =>
        {
            Assert.That(first.CapturedContexts, Has.Count.EqualTo(2),
                "removing a node must invalidate and rebuild an already installed snapshot");
            Assert.That(second.CapturedContexts, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void NodeGraphDrawable_OutputRenderNodesPublishesImmutableSnapshotsTransactionally()
    {
        var failure = new InvalidOperationException("output update");
        using var first = new IntentProbeRenderNode();
        using var second = new IntentProbeRenderNode();
        var source = new TransactionalRenderNodeSourceNode(first);
        var output = new OutputNode();
        var model = new GraphModel();
        model.Nodes.Add(source);
        model.Nodes.Add(output);
        model.Connect(output.InputPort, source.Output);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue = model;
        using NodeGraphDrawable.Resource resource = drawable.ToResource(CompositionContext.Default);
        IReadOnlyList<RenderNode> retained = resource.OutputRenderNode;
        var mutationSurface = (IList<RenderNode>)retained;

        Assert.Multiple(() =>
        {
            Assert.That(retained, Has.Count.EqualTo(1));
            Assert.That(retained[0], Is.SameAs(first));
            Assert.That(mutationSurface.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => mutationSurface.Add(second));
        });

        source.UpdateException = failure;
        bool updateOnly = false;
        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => resource.Update(drawable, CompositionContext.Default, ref updateOnly));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(resource.OutputRenderNode, Is.SameAs(retained),
                "a failed evaluation must not publish a partial output snapshot");
            Assert.That(retained, Has.Count.EqualTo(1));
            Assert.That(retained[0], Is.SameAs(first));
        });

        source.UpdateException = null;
        source.Value = second;
        updateOnly = false;
        Assert.DoesNotThrow(() => resource.Update(drawable, CompositionContext.Default, ref updateOnly));
        IReadOnlyList<RenderNode> current = resource.OutputRenderNode;

        Assert.Multiple(() =>
        {
            Assert.That(current, Is.Not.SameAs(retained));
            Assert.That(current, Has.Count.EqualTo(1));
            Assert.That(current[0], Is.SameAs(second));
            Assert.That(retained, Has.Count.EqualTo(1),
                "publishing a successful update must not mutate a retained snapshot");
            Assert.That(retained[0], Is.SameAs(first));
        });
    }

    [Test]
    public void Evaluate_MeasureNode_PropagatesDeliveryIntentToAuxiliaryChildPull()
    {
        var model = new GraphModel();
        var probe = new IntentProbeRenderNode();
        var source = new RenderNodeSourceNode(probe);
        var measure = new MeasureNode();
        model.Nodes.Add(source);
        model.Nodes.Add(measure);
        model.Connect(measure.Input, source.Output);
        using var snapshot = new GraphSnapshot();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);

        snapshot.Build(model, context);
        snapshot.Evaluate(CompositionTarget.Graphics, context);

        Assert.That(probe.ObservedIntent, Is.EqualTo(RenderIntent.Delivery),
            "bounds-only Measure pulls must preserve delivery failure policy instead of silently using preview policy");
    }

    [Test]
    public void GeneratedNodePort_PostDisposeAssignment_IsClearedWhenResourceLifecycleSeals()
    {
        var node = new CleanupAssignmentNode();
        var model = new GraphModel();
        model.Nodes.Add(node);
        var snapshot = new GraphSnapshot();
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Frame);
        snapshot.Build(model, context);
        int slotIndex = snapshot.FindSlotIndex(node);
        int itemIndex = node.Items.IndexOf(node.Output);
        var itemValue = (ItemValue<object?>)snapshot.GetItemValue(slotIndex, itemIndex)!;
        object? disposerValue = null;
        itemValue.RegisterDisposer(() => disposerValue = itemValue.Value);

        snapshot.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(node.PostDisposeCalls, Is.EqualTo(1),
                "the generated setter must remain available to PostDispose before final sealing");
            Assert.That(disposerValue, Is.Not.Null,
                "the final seal must run the item-value disposer while it can still observe the cleanup assignment");
            Assert.That(itemValue.GetBoxed(), Is.Null,
                "the final seal must not let a cleanup-time port assignment remain rooted by the snapshot item value");
        });
    }

    [Test]
    public void ItemValue_Dispose_WhenDisposerThrows_ClearsValueAndDoesNotRetry()
    {
        var failure = new InvalidOperationException("item cleanup failure");
        var retained = new object();
        var itemValue = new ItemValue<object> { Value = retained };
        object? observed = null;
        int disposeCalls = 0;
        itemValue.RegisterDisposer(() =>
        {
            disposeCalls++;
            observed = itemValue.Value;
            throw failure;
        });

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(itemValue.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(observed, Is.SameAs(retained),
                "the disposer must see the value before the final reference is cleared");
            Assert.That(itemValue.Value, Is.Null);
            Assert.That(disposeCalls, Is.EqualTo(1));
        });
        Assert.DoesNotThrow(itemValue.Dispose);
        Assert.That(disposeCalls, Is.EqualTo(1));
    }
}

internal sealed partial class ContextCaptureNode : GraphNode
{
    public List<CapturedGraphContext> CapturedContexts { get; } = [];

    public List<RenderPullPurpose> InitializedPurposes { get; } = [];

    public partial class Resource
    {
        protected override void InitializeCore(GraphCompositionContext context)
        {
            ((ContextCaptureNode)GetOriginal()).InitializedPurposes.Add(context.PullPurpose);
        }

        protected override void UpdateCore(GraphCompositionContext context)
        {
            var node = (ContextCaptureNode)GetOriginal();
            node.CapturedContexts.Add(new CapturedGraphContext(
                context.DisableResourceShare,
                context.PreferProxy,
                context.PreferredProxyPreset,
                context.RenderIntent,
                context.PullPurpose));
        }
    }
}

internal readonly record struct CapturedGraphContext(
    bool DisableResourceShare,
    bool PreferProxy,
    ProxyPreset PreferredProxyPreset,
    RenderIntent RenderIntent,
    RenderPullPurpose PullPurpose);

internal sealed partial class RenderNodeSourceNode : GraphNode
{
    private readonly RenderNode _value;

    public RenderNodeSourceNode(RenderNode value)
    {
        _value = value;
        Output = AddOutput<RenderNode>("Output");
    }

    public OutputPort<RenderNode> Output { get; }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            Output = ((RenderNodeSourceNode)GetOriginal())._value;
        }
    }
}

internal sealed partial class TransactionalRenderNodeSourceNode : GraphNode
{
    public TransactionalRenderNodeSourceNode(RenderNode value)
    {
        Value = value;
        Output = AddOutput<RenderNode>("Output");
    }

    public OutputPort<RenderNode> Output { get; }

    public RenderNode Value { get; set; }

    public Exception? UpdateException { get; set; }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            TransactionalRenderNodeSourceNode node = GetOriginal();
            if (node.UpdateException != null)
                throw node.UpdateException;

            Output = node.Value;
        }
    }
}

internal sealed class IntentProbeRenderNode : RenderNode
{
    public RenderIntent ObservedIntent { get; private set; } = RenderIntent.Preview;

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        ObservedIntent = context.RenderIntent;
        return [RenderNodeOperation.CreateLambda(new Rect(0, 0, 10, 10), static _ => { })];
    }
}

internal sealed partial class CleanupAssignmentNode : GraphNode
{
    private readonly object _cleanupValue = new();

    public CleanupAssignmentNode()
    {
        Output = AddOutput<object?>("Output");
    }

    public OutputPort<object?> Output { get; }

    public int PostDisposeCalls { get; private set; }

    public partial class Resource
    {
        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            CleanupAssignmentNode node = GetOriginal();
            Output = node._cleanupValue;
            node.PostDisposeCalls++;
        }
    }
}
