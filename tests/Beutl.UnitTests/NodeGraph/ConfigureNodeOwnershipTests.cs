using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class ConfigureNodeOwnershipTests
{
    [Test]
    public void BoundFilterInput_FansOutThroughTransformAndFilterEffectConfigureNodes()
    {
        var graph = new NodeGraphFilterEffect();
        GraphModel model = graph.Model.CurrentValue!;
        var input = new FilterEffectInputNode();
        var transform = new TransformNode();
        var filter = new FilterEffectNode<FilterEffectGroup>();
        var transformOutput = new OutputNode();
        var filterOutput = new OutputNode();
        model.Nodes.Add(input);
        model.Nodes.Add(transform);
        model.Nodes.Add(filter);
        model.Nodes.Add(transformOutput);
        model.Nodes.Add(filterOutput);
        model.Connect(GetConfigureInput(transform), input.Output);
        model.Connect(GetConfigureInput(filter), input.Output);
        model.Connect(transformOutput.InputPort, (IOutputPort)transform.Items[0]);
        model.Connect(filterOutput.InputPort, (IOutputPort)filter.Items[0]);

        Rect bounds = new(3, 5, 24, 18);
        using var resource = (NodeGraphFilterEffect.Resource)graph.ToResource(CompositionContext.Default);
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1), bounds),
            resource.CreateRenderNode());

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.Measure(pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(bounds));
        });
    }

    [Test]
    public void FanOut_DisposingOneConfigureConsumerLeavesSourceAndOtherConsumerUsable()
    {
        var source = new OwnedRenderNodeSource();
        var firstConsumer = new FanOutConsumerNode();
        var secondConsumer = new FanOutConsumerNode();
        var model = new GraphModel();
        model.Nodes.Add(source);
        model.Nodes.Add(firstConsumer);
        model.Nodes.Add(secondConsumer);
        model.Connect(firstConsumer.RenderInput, source.Output);
        model.Connect(secondConsumer.RenderInput, source.Output);

        using (var snapshot = new GraphSnapshot())
        {
            snapshot.Build(model, CompositionContext.Default);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

            ContainerRenderNode firstOutput = firstConsumer.OutputContainer
                ?? throw new AssertionException("The first ConfigureNode consumer did not produce a container.");
            ContainerRenderNode secondOutput = secondConsumer.OutputContainer
                ?? throw new AssertionException("The second ConfigureNode consumer did not produce a container.");

            firstOutput.Dispose();

            Assert.That(source.RenderNode.IsDisposed, Is.False,
                "disposing one ConfigureNode output must not dispose its producer-owned input");
            Assert.That(secondOutput.IsDisposed, Is.False);

            using var renderer = new RenderNodeRenderer(secondOutput);
            Assert.DoesNotThrow(() => renderer.Measure());
            Assert.That(source.RenderNode.ProcessCount, Is.EqualTo(1),
                "the remaining ConfigureNode branch must still record its shared source");
        }

        Assert.That(source.RenderNode.IsDisposed, Is.True,
            "the source owner releases its RenderNode when the graph snapshot is torn down");
    }

    [Test]
    public void ReferencesChildRenderNode_DisposeDoesNotDisposeReferencedChild()
    {
        var child = new TrackingRenderNode();

        using (var wrapper = new ReferencesChildRenderNode(child))
        {
        }

        Assert.That(child.IsDisposed, Is.False);
        child.Dispose();
    }

    [Test]
    public void SharedNonValueFilterInputThroughConfigureReferences_ThrowsAtSecondConsumer()
    {
        var graph = new NodeGraphFilterEffect();
        GraphModel model = graph.Model.CurrentValue!;
        var input = new FilterEffectInputNode();
        var firstTransform = new TransformNode();
        var secondTransform = new TransformNode();
        var firstOutput = new OutputNode();
        var secondOutput = new OutputNode();
        model.Nodes.Add(input);
        model.Nodes.Add(firstTransform);
        model.Nodes.Add(secondTransform);
        model.Nodes.Add(firstOutput);
        model.Nodes.Add(secondOutput);
        model.Connect(GetConfigureInput(firstTransform), input.Output);
        model.Connect(GetConfigureInput(secondTransform), input.Output);
        model.Connect(firstOutput.InputPort, (IOutputPort)firstTransform.Items[0]);
        model.Connect(secondOutput.InputPort, (IOutputPort)secondTransform.Items[0]);

        using var resource = (NodeGraphFilterEffect.Resource)graph.ToResource(CompositionContext.Default);
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            new NonValueCommandRenderNode(),
            resource.CreateRenderNode());
        using var renderer = new RenderNodeRenderer(pipeline, new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        });

        Assert.That(
            () => renderer.Measure(),
            Throws.InvalidOperationException.And.Message.Contains("used by more than one consumer"));
    }

    [Test]
    public void UpdatingConfigureInput_ReusesReferenceAndDisposesOnlyRemovedReferences()
    {
        var first = new TrackingRenderNode();
        var replacement = new TrackingRenderNode();
        var source = new MutableOwnedRenderNodeSource(first, replacement);
        var consumer = new FanOutConsumerNode();
        var model = new GraphModel();
        model.Nodes.Add(source);
        model.Nodes.Add(consumer);
        model.Connect(consumer.RenderInput, source.Output);

        using (var snapshot = new GraphSnapshot())
        {
            snapshot.Build(model, CompositionContext.Default);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

            ContainerRenderNode output = consumer.OutputContainer
                ?? throw new AssertionException("The ConfigureNode consumer did not produce a container.");
            var reference = output.Children.Single() as ReferencesChildRenderNode
                ?? throw new AssertionException("ConfigureNode must wrap its input in a non-owning reference.");
            output.HasChanges = false;

            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

            Assert.Multiple(() =>
            {
                Assert.That(output.Children.Single(), Is.SameAs(reference));
                Assert.That(output.HasChanges, Is.False,
                    "an unchanged input must retain its existing wrapper without dirtying the output");
            });

            source.Select(replacement);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

            Assert.Multiple(() =>
            {
                Assert.That(output.Children.Single(), Is.SameAs(reference));
                Assert.That(reference.Child, Is.SameAs(replacement));
                Assert.That(reference.IsDisposed, Is.False);
                Assert.That(first.IsDisposed, Is.False,
                    "retargeting a reference must not dispose its former producer");
                Assert.That(replacement.IsDisposed, Is.False);
                Assert.That(output.HasChanges, Is.True,
                    "retargeting a ConfigureNode input must invalidate its output");
            });

            output.HasChanges = false;
            source.Select(null);
            snapshot.Evaluate(CompositionTarget.Graphics, CompositionContext.Default);

            Assert.Multiple(() =>
            {
                Assert.That(output.Children, Is.Empty);
                Assert.That(reference.IsDisposed, Is.True,
                    "removing an input must dispose its no-longer-needed reference wrapper");
                Assert.That(first.IsDisposed, Is.False,
                    "removing an input must not dispose a producer it previously referenced");
                Assert.That(replacement.IsDisposed, Is.False);
                Assert.That(output.HasChanges, Is.True,
                    "removing a ConfigureNode input must invalidate its output");
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(replacement.IsDisposed, Is.True);
        });
    }

    private static IInputPort GetConfigureInput(ConfigureNode node)
        => (IInputPort)node.Items[1];
}

internal sealed partial class FanOutConsumerNode : ConfigureNode
{
    public IInputPort RenderInput => InputPort;

    public ContainerRenderNode? OutputContainer { get; private set; }

    public partial class Resource
    {
        protected override void UpdateCore(GraphCompositionContext context)
        {
            var output = OutputPort;
            if (output is null)
            {
                output = new ContainerRenderNode();
                OutputPort = output;
            }

            GetOriginal().OutputContainer = output;
        }

        partial void PostDispose(bool disposing)
        {
            OutputPort?.Dispose();
            OutputPort = null;
            GetOriginal().OutputContainer = null;
        }
    }
}

internal sealed partial class OwnedRenderNodeSource : GraphNode
{
    public OwnedRenderNodeSource()
    {
        Output = AddOutput<RenderNode?>("Output");
    }

    public TrackingRenderNode RenderNode { get; } = new();

    public OutputPort<RenderNode?> Output { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            OwnedRenderNodeSource source = GetOriginal();
            Output = source.RenderNode;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
                GetOriginal().RenderNode.Dispose();
        }
    }
}

internal sealed partial class MutableOwnedRenderNodeSource : GraphNode
{
    private readonly TrackingRenderNode[] _ownedNodes;

    public MutableOwnedRenderNodeSource(params TrackingRenderNode[] ownedNodes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownedNodes.Length);
        _ownedNodes = ownedNodes;
        Current = ownedNodes[0];
        Output = AddOutput<RenderNode?>("Output");
    }

    public RenderNode? Current { get; private set; }

    public OutputPort<RenderNode?> Output { get; }

    public void Select(RenderNode? node)
    {
        if (node is not null && !_ownedNodes.Contains(node))
            throw new ArgumentException("The source can only select a node it owns.", nameof(node));

        Current = node;
    }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = GetOriginal().Current;
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing) return;

            foreach (TrackingRenderNode node in GetOriginal()._ownedNodes)
            {
                node.Dispose();
            }
        }
    }
}

internal sealed class TrackingRenderNode : RenderNode
{
    public int ProcessCount { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ProcessCount++;
        context.PassThrough();
    }
}

internal sealed class NonValueCommandRenderNode : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.TargetCommand([], TargetCommandDescription.CreateRequestLocal(
            static _ => { },
            TargetRegion.Empty,
            Rect.Empty,
            RenderHitTestContract.None)));
    }
}
