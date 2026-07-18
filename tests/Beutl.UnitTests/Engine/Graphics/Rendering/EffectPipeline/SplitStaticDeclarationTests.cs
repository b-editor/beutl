using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Guards <see cref="SplitEffect"/>'s static-vs-dynamic branch declaration (feature 004, C3.6). A static
/// declaration promises an exact execution-time emit count, which only holds for a single linear input op: a
/// multi-op input set (a <c>DrawableGroup</c>'s children) executes the split PER op, so a small member whose tiles
/// fall below one pixel emits zero branches against the union-derived declaration and the executor faults the whole
/// frame ("The static split emitted 0 branches but declared N"). A runtime-dependent predecessor breaks the same
/// promise: a compute node's no-Vulkan Identity/Skip fallback replaces the declared bounds advance at execution.
/// </summary>
[NonParallelizable]
[TestFixture]
public class SplitStaticDeclarationTests
{
    private static readonly Rect s_bigBounds = new(0, 0, 160, 120);
    private static readonly Rect s_tinyBounds = new(0, 130, 1, 10);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    private static SplitEffect MakeSplit() => new()
    {
        HorizontalDivisions = { CurrentValue = 2 },
        VerticalDivisions = { CurrentValue = 2 },
    };

    private static SplitNodeDescriptor DescribeSplit(EffectGraphBuilder builder)
    {
        var split = MakeSplit();
        var resource = (FilterEffect.Resource)(object)split.ToResource(CompositionContext.Default);
        split.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        return (SplitNodeDescriptor)graph.Nodes[^1].Descriptor;
    }

    [Test]
    public void SingleOpInput_DeclaresStaticBranchCount()
    {
        var builder = new EffectGraphBuilder(
            s_bigBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);

        SplitNodeDescriptor descriptor = DescribeSplit(builder);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.IsDynamicOutputs, Is.False,
                "a single linear input keeps the renderable static declaration (C3.6)");
            Assert.That(descriptor.BranchCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void BranchedMultiOpInput_StaysDynamic()
    {
        var builder = new EffectGraphBuilder(
            s_bigBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery,
            hasBranchedInput: true);

        SplitNodeDescriptor descriptor = DescribeSplit(builder);

        Assert.That(descriptor.IsDynamicOutputs, Is.True,
            "a fanned-out input set has per-op bounds, so the union bounds cannot prove an exact emit count");
    }

    [Test]
    public void AfterIdentityFallbackCompute_StaysDynamic()
    {
        var builder = new EffectGraphBuilder(
            s_bigBounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Compute(ComputeNodeDescriptor.Create(
            static ctx => ctx.CopySourceToDestination(),
            passCount: 1,
            BoundsContract.Create(static r => r, static r => r),
            ComputeFallbackPolicy.Identity));

        SplitNodeDescriptor descriptor = DescribeSplit(builder);

        Assert.That(descriptor.IsDynamicOutputs, Is.True,
            "without Vulkan the compute node's fallback rewrites the bounds advance at execution, "
            + "so a later split cannot promise a static emit count");
    }

    // The end-to-end shape from the review: one effect applied over a group with a large member and a member whose
    // 2x2 tiles are sub-pixel. The render node seeds the builder's fan-out state from its input op count, the split
    // goes dynamic, and the small member emits nothing instead of faulting the frame.
    [Test]
    public void Process_MultiOpInputWithSubPixelMember_RendersInsteadOfThrowing()
    {
        var effect = MakeSplit();
        var resource = (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext(
            [MakeContentRect(s_bigBounds), MakeContentRect(s_tinyBounds)], RenderIntent.Delivery);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(4),
                "the big member fans out into its four tiles; the sub-pixel member emits nothing");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(ops);
        }
    }

    private static RenderNodeOperation MakeContentRect(Rect bounds)
        => RenderNodeOperation.CreateLambda(
            bounds,
            canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null),
            hitTest: bounds.Contains);
}
