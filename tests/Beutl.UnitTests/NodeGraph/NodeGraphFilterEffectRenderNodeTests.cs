using System.Linq;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;

namespace Beutl.UnitTests.NodeGraph;

// NodeGraphFilterEffectRenderNode.Process forwards context.OutputScale / MaxWorkingScale into the
// inner RenderNodeProcessor that walks the graph's output subtree. These tests build a real
// FilterEffectInputNode -> FilterEffectNode -> OutputNode graph and assert the forwarded scales
// reach the effect inside the graph, where they drive RenderNodeContext.ResolveWorkingScale.
[TestFixture]
public class NodeGraphFilterEffectRenderNodeTests
{
    // Graph: FilterEffectInputNode --> FilterEffectNode<ScaleProbeEffect> --> OutputNode.
    // The probe effect's render node stamps the resolved working scale onto its output ops, making
    // the scale forwarded by NodeGraphFilterEffectRenderNode observable without touching the GPU.
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

        // ConfigureNode ctor adds Items[0] = Output port, Items[1] = list Input port.
        model.Connect((IInputPort)probeNode.Items[1], inputNode.Output);
        model.Connect(outputNode.InputPort, (IOutputPort)probeNode.Items[0]);

        return effect.ToResource(CompositionContext.Default);
    }

    private static RenderNodeOperation SourceOp(float density)
        => RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 120, 90),
            _ => { },
            hitTest: _ => false,
            effectiveScale: EffectiveScale.At(density));

    // An At(1) source lets OutputScale drive the working scale: w = max(s_out, 1). A regression that
    // seeds the inner processor with a hardcoded s_out = 1 would resolve every case to 1.
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)]
    [TestCase(4.0f, 4.0f)]
    public void Process_ForwardsOutputScale_IntoGraphOutputSubtree(float outputScale, float expectedW)
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        using FilterEffectRenderNode node = resource.CreateRenderNode();
        var context = new RenderNodeContext([SourceOp(1.0f)], outputScale: outputScale);

        RenderNodeOperation[] ops = node.Process(context);

        Assert.That(ops, Is.Not.Empty, "the graph dropped the input op");
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the forwarded OutputScale did not drive the working scale inside the graph");
        DisposeAll(ops);
    }

    // An At(4) source pushes supply above s_out, so only the forwarded MaxWorkingScale can cap it.
    // A regression that seeds the inner processor with +Inf would never cap the 4.0 supply.
    [TestCase(float.PositiveInfinity, 4.0f)]
    [TestCase(2.0f, 2.0f)]
    public void Process_ForwardsMaxWorkingScale_IntoGraphOutputSubtree(float maxWorkingScale, float expectedW)
    {
        using NodeGraphFilterEffect.Resource resource = BuildGraphResource();
        using FilterEffectRenderNode node = resource.CreateRenderNode();
        var context = new RenderNodeContext([SourceOp(4.0f)], outputScale: 1.0f, maxWorkingScale: maxWorkingScale);

        RenderNodeOperation[] ops = node.Process(context);

        Assert.That(ops, Is.Not.Empty, "the graph dropped the input op");
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the forwarded MaxWorkingScale did not cap the working scale inside the graph");
        DisposeAll(ops);
    }

    private static void DisposeAll(RenderNodeOperation[] ops)
    {
        foreach (RenderNodeOperation op in ops)
        {
            op.Dispose();
        }
    }
}

// A GPU-free FilterEffect whose render node resolves the supply-driven working scale and stamps it
// onto passthrough ops, exposing the scale that NodeGraphFilterEffectRenderNode forwarded. Mirrors
// the manual-Resource + SuppressResourceClassGeneration pattern used by NodeGraphFilterEffect.
[SuppressResourceClassGeneration]
internal sealed partial class ScaleProbeEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
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
        public override FilterEffectRenderNode CreateRenderNode() => new ScaleProbeRenderNode(this);
    }
}

internal sealed class ScaleProbeRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
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
