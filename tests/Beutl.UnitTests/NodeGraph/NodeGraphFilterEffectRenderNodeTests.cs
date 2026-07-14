using System.Linq;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media.Proxy;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;

namespace Beutl.UnitTests.NodeGraph;

// NodeGraphFilterEffectRenderNode.Process forwards OutputScale / MaxWorkingScale into the inner
// processor that walks the graph. These tests assert those scales reach the effect inside the graph.
[TestFixture]
public class NodeGraphFilterEffectRenderNodeTests
{
    // Graph: FilterEffectInputNode -> FilterEffectNode<ScaleProbeEffect> -> OutputNode.
    private static NodeGraphFilterEffect.Resource BuildGraphResource()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;

        var inputNode = new FilterEffectInputNode();
        var probeNode = new FilterEffectNode<ScaleProbeEffect>();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(probeNode);
        model.Nodes.Add(outputNode);

        // The render-chain ports are not exposed publicly: Items[0] = Output, Items[1] = list Input
        // (per-property value inputs follow at Items[2+]). Reach them by index.
        var probeChainInput = (IInputPort)probeNode.Items[1];
        var probeChainOutput = (IOutputPort)probeNode.Items[0];
        model.Connect(probeChainInput, inputNode.Output);
        model.Connect(outputNode.InputPort, probeChainOutput);

        return effect.ToResource(CompositionContext.Default);
    }

    private static RenderNodeOperation SourceOp(float density)
        => RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 120, 90),
            _ => { },
            hitTest: _ => false,
            effectiveScale: EffectiveScale.At(density));

    // An At(1) source lets OutputScale drive the working scale: w = max(s_out, 1).
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)]
    [TestCase(4.0f, 4.0f)]
    public void Process_ForwardsOutputScale_IntoGraphOutputSubtree(float outputScale, float expectedW)
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        var context = new RenderNodeContext([SourceOp(1.0f)], outputScale: outputScale);

        RenderNodeOperation[] ops = node.Process(context);

        Assert.That(ops, Is.Not.Empty, "the graph dropped the input op");
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the forwarded OutputScale did not drive the working scale inside the graph");
        DisposeAll(ops);
    }

    // An At(4) source pushes supply above s_out, so only the forwarded MaxWorkingScale can cap it.
    [TestCase(float.PositiveInfinity, 4.0f)]
    [TestCase(2.0f, 2.0f)]
    public void Process_ForwardsMaxWorkingScale_IntoGraphOutputSubtree(float maxWorkingScale, float expectedW)
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        var context = new RenderNodeContext([SourceOp(4.0f)], outputScale: 1.0f, maxWorkingScale: maxWorkingScale);

        RenderNodeOperation[] ops = node.Process(context);

        Assert.That(ops, Is.Not.Empty, "the graph dropped the input op");
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the forwarded MaxWorkingScale did not cap the working scale inside the graph");
        DisposeAll(ops);
    }

    [Test]
    public void Process_ForwardsAuxiliaryPull_IntoGraphOutputSubtree()
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        ScaleProbeRenderNode.SawAuxiliaryPull = false;
        var context = new RenderNodeContext([SourceOp(1f)])
        {
            IsAuxiliaryPull = true,
        };

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ScaleProbeRenderNode.SawAuxiliaryPull, Is.True,
                "the hosted processor must preserve hit-test/bounds-only execution policy");
        }
        finally
        {
            DisposeAll(ops);
        }
    }

    [Test]
    public void ToResource_CapturesProxyPreferencesFromCompositionContext()
    {
        var effect = new NodeGraphFilterEffect();
        var context = new CompositionContext(TimeSpan.Zero)
        {
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Eighth,
            DisableResourceShare = true,
        };

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(context);

        // The render node replays the graph with a fresh context; without these captured flags a
        // VideoSourceNode inside the graph evaluates with PreferProxy=false and loses export-time
        // reader isolation (DisableResourceShare=false).
        Assert.Multiple(() =>
        {
            Assert.That(resource.PreferProxy, Is.True);
            Assert.That(resource.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Eighth));
            Assert.That(resource.DisableResourceShare, Is.True);
        });
    }

    [Test]
    public void ToResource_DefaultContext_LeavesProxyPreferencesOff()
    {
        var effect = new NodeGraphFilterEffect();

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);

        Assert.That(resource.PreferProxy, Is.False);
    }

    [Test]
    public void Process_NoOutput_ClearsInputWrapperBeforeReturningPassthrough()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        model.Nodes.Add(inputNode);

        using var resource = (NodeGraphFilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        RenderNodeOperation input = SourceOp(1f);
        RenderNodeOperation[] outputs = node.Process(new RenderNodeContext([input]));

        int slot = resource.Snapshot.FindSlotIndex(inputNode);
        var inputResource = (FilterEffectInputNode.Resource)resource.Snapshot.GetResource(slot)!;
        RenderNodeOperation[] retained = inputResource.Wrapper.Process(new RenderNodeContext([]));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Has.Length.EqualTo(1), "the no-output graph passes its input through");
                Assert.That(outputs[0], Is.Not.SameAs(input),
                    "the passthrough is a ref-counted proxy so clearing the wrapper cannot dispose it early");
                Assert.That(input.IsDisposed, Is.False,
                    "the returned proxy must keep the underlying input alive until the proxy is disposed");
                Assert.That(retained, Is.Empty,
                    "the wrapper must not retain the input after the early no-output return");
            });
        }
        finally
        {
            DisposeAll(retained);
            DisposeAll(outputs);
        }

        Assert.That(input.IsDisposed, Is.True,
            "disposing the passthrough proxy releases the wrapper's last ownership of the input");
    }

    [Test]
    public void Process_LaterOutputThrows_DisposesEarlierResultsAndClearsInputWrapper()
    {
        var host = new NodeGraphFilterEffect();
        GraphModel model = host.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var successfulNode = new FilterEffectNode<TrackedResultEffect>();
        var successfulOutput = new OutputNode();
        var throwingNode = new FilterEffectNode<ThrowingProcessEffect>();
        var throwingOutput = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(successfulNode);
        model.Nodes.Add(successfulOutput);
        model.Nodes.Add(throwingNode);
        model.Nodes.Add(throwingOutput);
        model.Connect((IInputPort)successfulNode.Items[1], inputNode.Output);
        model.Connect(successfulOutput.InputPort, (IOutputPort)successfulNode.Items[0]);
        model.Connect((IInputPort)throwingNode.Items[1], inputNode.Output);
        model.Connect(throwingOutput.InputPort, (IOutputPort)throwingNode.Items[0]);

        using var resource = (NodeGraphFilterEffect.Resource)host.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        RenderNodeOperation input = SourceOp(1f);
        TrackedResultRenderNode.ResultDisposed = false;

        Assert.Throws<InvalidOperationException>(() => node.Process(new RenderNodeContext([input])));

        int slot = resource.Snapshot.FindSlotIndex(inputNode);
        var inputResource = (FilterEffectInputNode.Resource)resource.Snapshot.GetResource(slot)!;
        RenderNodeOperation[] retained = inputResource.Wrapper.Process(new RenderNodeContext([]));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(TrackedResultRenderNode.ResultDisposed, Is.True,
                    "an operation returned by an earlier output must be swept when a later output throws");
                Assert.That(input.IsDisposed, Is.True,
                    "the failing pull must release the wrapper's ownership of the graph input");
                Assert.That(retained, Is.Empty,
                    "the input wrapper must be cleared even when an output pull throws");
            });
        }
        finally
        {
            DisposeAll(retained);
        }
    }

    [Test]
    public void Process_InputWrapperCleanupThrows_ReturnsSuccessfulOutputAndSweepsEveryInput()
    {
        var host = new NodeGraphFilterEffect();
        GraphModel model = host.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var successfulNode = new FilterEffectNode<TrackedResultEffect>();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(successfulNode);
        model.Nodes.Add(outputNode);
        model.Connect((IInputPort)successfulNode.Items[1], inputNode.Output);
        model.Connect(outputNode.InputPort, (IOutputPort)successfulNode.Items[0]);

        using var resource = (NodeGraphFilterEffect.Resource)host.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        bool firstDisposeAttempted = false;
        bool secondDisposed = false;
        RenderNodeOperation first = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 1, 1),
            static _ => { },
            onDispose: () =>
            {
                firstDisposeAttempted = true;
                throw new InvalidOperationException("simulated wrapper cleanup failure");
            });
        RenderNodeOperation second = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 1, 1),
            static _ => { },
            onDispose: () => secondDisposed = true);
        TrackedResultRenderNode.ResultDisposed = false;

        RenderNodeOperation[] outputs = node.Process(new RenderNodeContext([first, second]));

        int slot = resource.Snapshot.FindSlotIndex(inputNode);
        var inputResource = (FilterEffectInputNode.Resource)resource.Snapshot.GetResource(slot)!;
        RenderNodeOperation[] retained = inputResource.Wrapper.Process(new RenderNodeContext([]));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Has.Length.EqualTo(1),
                    "wrapper cleanup must not make an already-produced graph output unreachable");
                Assert.That(firstDisposeAttempted, Is.True);
                Assert.That(secondDisposed, Is.True,
                    "one throwing input must not abort cleanup of later wrapper references");
                Assert.That(retained, Is.Empty, "the wrapper must publish its cleared state before cleanup begins");
                Assert.That(TrackedResultRenderNode.ResultDisposed, Is.False,
                    "the successful output remains caller-owned after Process returns");
            });
        }
        finally
        {
            DisposeAll(retained);
            DisposeAll(outputs);
        }

        Assert.That(TrackedResultRenderNode.ResultDisposed, Is.True);
    }

    [Test]
    public void FilterEffectNode_ReplacesOutputWhenUpdatedFactoryChangesNodeType()
    {
        var host = new NodeGraphFilterEffect();
        GraphModel model = host.Model.CurrentValue!;
        var inputNode = new FilterEffectInputNode();
        var effectNode = new FilterEffectNode<SwitchingFactoryEffect>();
        var outputNode = new OutputNode();
        model.Nodes.Add(inputNode);
        model.Nodes.Add(effectNode);
        model.Nodes.Add(outputNode);
        model.Connect((IInputPort)effectNode.Items[1], inputNode.Output);
        model.Connect(outputNode.InputPort, (IOutputPort)effectNode.Items[0]);

        using var resource = (NodeGraphFilterEffect.Resource)host.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.RenderNodeFactory.Create(resource);
        SecondFactoryRenderNode.SawDisposedChild = false;
        RenderNodeOperation[] first = node.Process(new RenderNodeContext([SourceOp(1f)]));
        Assert.That(first.Single().EffectiveScale.Value, Is.EqualTo(1f));
        DisposeAll(first);

        effectNode.Object.UseSecond.CurrentValue = true;
        RenderNodeOperation[] second = node.Process(new RenderNodeContext([SourceOp(1f)]));
        Assert.That(second.Single().EffectiveScale.Value, Is.EqualTo(2f),
            "the graph must execute the new factory node type after the resource changes");
        Assert.That(SecondFactoryRenderNode.SawDisposedChild, Is.False,
            "replacing the effect node must transfer its input children before disposing the old container");
        DisposeAll(second);
    }

    private static void DisposeAll(RenderNodeOperation[] ops)
    {
        foreach (RenderNodeOperation op in ops)
        {
            op.Dispose();
        }
    }
}

// A GPU-free FilterEffect whose render node stamps the resolved working scale onto passthrough ops,
// exposing the scale that NodeGraphFilterEffectRenderNode forwarded.
[SuppressResourceClassGeneration]
internal sealed partial class ScaleProbeEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of<Resource, ScaleProbeRenderNode>(static r => new ScaleProbeRenderNode(r));
    }
}

internal sealed class ScaleProbeRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    public static bool SawAuxiliaryPull { get; set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        SawAuxiliaryPull |= context.IsAuxiliaryPull;
        // Resolve w as FilterEffectRenderNode.Process would, but skip its GPU path (buffer-budget clamp +
        // SkiaSharp build/rasterize). So w is the forwarded supply-driven scale, not a real final scale.
        EffectiveScale[] scales = context.Input.Select(i => i.EffectiveScale).ToArray();
        float w = RenderNodeContext.ResolveWorkingScale(scales, context.OutputScale, context.MaxWorkingScale);
        return context.Input.Select(input => RenderNodeOperation.CreateLambda(
                input.Bounds,
                input.Render,
                hitTest: input.HitTest,
                onDispose: input.Dispose,
                effectiveScale: EffectiveScale.At(w)))
            .ToArray();
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class SwitchingFactoryEffect : FilterEffect
{
    public SwitchingFactoryEffect()
    {
        ScanProperties<SwitchingFactoryEffect>();
    }

    public IProperty<bool> UseSecond { get; } = Property.Create(false);

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        private bool _useSecond;

        public override FilterEffectRenderNodeFactory RenderNodeFactory => _useSecond
            ? FilterEffectRenderNodeFactory.Of<Resource, SecondFactoryRenderNode>(static r => new SecondFactoryRenderNode(r))
            : FilterEffectRenderNodeFactory.Of<Resource, FirstFactoryRenderNode>(static r => new FirstFactoryRenderNode(r));

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            _useSecond = ((SwitchingFactoryEffect)obj).UseSecond.CurrentValue;
        }
    }
}

internal sealed class FirstFactoryRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => Stamp(context.Input, 1f);

    internal static RenderNodeOperation[] Stamp(RenderNodeOperation[] inputs, float scale)
    {
        foreach (RenderNodeOperation input in inputs)
            input.Dispose();

        return [RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 1, 1), _ => { }, effectiveScale: EffectiveScale.At(scale))];
    }
}

internal sealed class SecondFactoryRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    internal static bool SawDisposedChild { get; set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        SawDisposedChild |= Children.Any(child => child.IsDisposed);
        return FirstFactoryRenderNode.Stamp(context.Input, 2f);
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class TrackedResultEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of<Resource, TrackedResultRenderNode>(
                static resource => new TrackedResultRenderNode(resource));
    }
}

internal sealed class TrackedResultRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    internal static bool ResultDisposed { get; set; }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        DisposeAll(context.Input);
        return [RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 1, 1), _ => { }, onDispose: () => ResultDisposed = true)];
    }

    private static void DisposeAll(RenderNodeOperation[] operations)
    {
        foreach (RenderNodeOperation operation in operations)
            operation.Dispose();
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class ThrowingProcessEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of<Resource, ThrowingProcessRenderNode>(
                static resource => new ThrowingProcessRenderNode(resource));
    }
}

internal sealed class ThrowingProcessRenderNode(FilterEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        foreach (RenderNodeOperation operation in context.Input)
            operation.Dispose();

        throw new InvalidOperationException("simulated later output failure");
    }
}
