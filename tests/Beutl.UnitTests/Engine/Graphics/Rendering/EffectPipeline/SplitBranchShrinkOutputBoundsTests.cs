using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the split-branch output tightening (feature 004, §C3): a split branch whose
/// render callback calls <see cref="GeometrySession.SetOutputBounds"/> must publish the tightened sub-rect as its
/// branch operation's bounds, mirroring the single-op geometry path. Before the fix <c>SplitEmitter.Emit</c> read
/// only <c>IsOutputDiscarded</c> and always published the full branch bounds, so a downstream bounds-dependent
/// consumer saw the un-shrunk rect.
/// </summary>
[NonParallelizable]
[TestFixture]
public class SplitBranchShrinkOutputBoundsTests
{
    private static readonly Rect s_input = new(0, 0, 100, 100);
    private static readonly Rect s_shrunk = new(20, 20, 40, 40);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    [Test]
    public void SplitBranch_SetOutputBounds_PublishesTightenedBranchBounds()
    {
        var effect = new ShrinkingSplitEffect(branchBounds: s_input, shrunkTo: s_shrunk);
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_input)]);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "the single-branch split produces one output");

            Rect bounds = ops[0].Bounds;
            Assert.Multiple(() =>
            {
                Assert.That(bounds, Is.EqualTo(s_shrunk),
                    "the branch op's bounds must be the SetOutputBounds sub-rect");
                Assert.That(bounds, Is.Not.EqualTo(s_input),
                    "the pre-fix full-branch bounds (shrink ignored) are the regression being restored");
            });
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

// A single-branch split effect whose branch draws the input then tightens its output to a sub-rect via
// SetOutputBounds, exercising the SplitEmitter shrink path.
[SuppressResourceClassGeneration]
internal sealed partial class ShrinkingSplitEffect(Rect branchBounds, Rect shrunkTo) : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        Rect bb = branchBounds;
        Rect sr = shrunkTo;
        builder.Split(SplitNodeDescriptor.Static(
            emitter => emitter.Emit(bb, session =>
            {
                ImmediateCanvas canvas = session.OpenCanvas();
                using (canvas.PushDeviceSpace())
                {
                    emitter.Input.Draw(canvas);
                }

                session.SetOutputBounds(sr);
            }),
            branchCount: 1,
            structuralToken: nameof(ShrinkingSplitEffect)));
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
    }
}
