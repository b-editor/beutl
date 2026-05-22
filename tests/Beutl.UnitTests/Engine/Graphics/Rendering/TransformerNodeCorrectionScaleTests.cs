using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class TransformerNodeCorrectionScaleTests
{
    private static readonly Rect s_bounds = new(0, 0, 1920, 1080);

    private static RenderNodeOperation MakeUpstream(RenderScale scale)
    {
        return RenderNodeOperation.CreateLambda(s_bounds, _ => { }, correctionScale: scale);
    }

    [Test]
    public void TransformRenderNode_PropagatesCorrectionScale()
    {
        var upstreamScale = new RenderScale(4f, 4f);
        var node = new TransformRenderNode(Matrix.CreateTranslation(10, 20), TransformOperator.Prepend);
        var ctx = new RenderNodeContext([MakeUpstream(upstreamScale)]);

        var outs = node.Process(ctx);

        Assert.That(outs, Has.Length.EqualTo(1));
        Assert.That(outs[0].CorrectionScale, Is.EqualTo(upstreamScale));
    }

    [Test]
    public void TransformRenderNode_IdentityUpstream_StaysIdentity()
    {
        var node = new TransformRenderNode(Matrix.CreateTranslation(10, 20), TransformOperator.Prepend);
        var ctx = new RenderNodeContext([MakeUpstream(RenderScale.Identity)]);

        var outs = node.Process(ctx);

        Assert.That(outs[0].CorrectionScale, Is.EqualTo(RenderScale.Identity));
    }

    [Test]
    public void ContainerRenderNode_ForwardsUpstreamScalesIndependently()
    {
        // Pattern X: different children can have different CorrectionScale; container does not unify.
        var scaleA = new RenderScale(4f, 4f);
        var scaleB = new RenderScale(2f, 2f);
        var node = new ContainerRenderNode();
        var ctx = new RenderNodeContext([MakeUpstream(scaleA), MakeUpstream(scaleB)]);

        var outs = node.Process(ctx);

        Assert.That(outs, Has.Length.EqualTo(2));
        Assert.That(outs[0].CorrectionScale, Is.EqualTo(scaleA));
        Assert.That(outs[1].CorrectionScale, Is.EqualTo(scaleB));
    }

    [Test]
    public void LayerRenderNode_UnifiesAtComponentWiseMaxOfInputs()
    {
        // Pattern Y / hybrid: PushLayer materializes a unified raster; use ComponentWiseMax.
        var scaleA = new RenderScale(4f, 2f);
        var scaleB = new RenderScale(2f, 3f);
        var node = new LayerRenderNode(s_bounds);
        var ctx = new RenderNodeContext([MakeUpstream(scaleA), MakeUpstream(scaleB)]);

        var outs = node.Process(ctx);

        Assert.That(outs, Has.Length.EqualTo(1));
        Assert.That(outs[0].CorrectionScale.ScaleX, Is.EqualTo(4f));
        Assert.That(outs[0].CorrectionScale.ScaleY, Is.EqualTo(3f));
    }

    [Test]
    public void LayerRenderNode_AllIdentityInputs_ReportsIdentity()
    {
        var node = new LayerRenderNode(s_bounds);
        var ctx = new RenderNodeContext([MakeUpstream(RenderScale.Identity), MakeUpstream(RenderScale.Identity)]);

        var outs = node.Process(ctx);

        Assert.That(outs[0].CorrectionScale, Is.EqualTo(RenderScale.Identity));
    }

    [Test]
    public void PushStateNodes_InheritCorrectionScaleViaDecorator()
    {
        var scale = new RenderScale(4f, 4f);

        // Sample a representative push-state node — all use CreateDecorator under the hood.
        var clipNode = new RectClipRenderNode(new Rect(0, 0, 500, 500), ClipOperation.Intersect);
        var ctx = new RenderNodeContext([MakeUpstream(scale)]);

        var outs = clipNode.Process(ctx);

        Assert.That(outs[0].CorrectionScale, Is.EqualTo(scale));
    }

    [Test]
    public void FilterEffectRenderNode_PropagatesUnifiedScaleOnOutput()
    {
        var scaleA = new RenderScale(4f, 4f);
        var scaleB = new RenderScale(2f, 8f);
        var blur = new Blur() { Sigma = { CurrentValue = new(10, 10) } };
        var resource = blur.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);
        var ctx = new RenderNodeContext([
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null),
                hitTest: _ => false,
                correctionScale: scaleA),
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null),
                hitTest: _ => false,
                correctionScale: scaleB),
        ]);

        var outs = node.Process(ctx);

        Assert.That(outs, Is.Not.Empty);
        // ComponentWiseMax of (4,4) and (2,8) → (4,8)
        foreach (var op in outs)
        {
            Assert.That(op.CorrectionScale.ScaleX, Is.EqualTo(4f));
            Assert.That(op.CorrectionScale.ScaleY, Is.EqualTo(8f));
        }
    }

    [Test]
    public void FilterEffectRenderNode_BlurBoundsUseAuthoredSigma()
    {
        // Bounds extend by 3*sigma per Skia convention.
        // With CorrectionScale = 4, Skia receives sigma/4 but the bounds must use the authored sigma.
        var scale = new RenderScale(4f, 4f);
        var blur = new Blur() { Sigma = { CurrentValue = new(10, 10) } };
        var resource = blur.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);
        var ctx = new RenderNodeContext([
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null),
                hitTest: _ => false,
                correctionScale: scale)
        ]);

        var outs = node.Process(ctx);

        // Authored sigma = 10 → bounds inflate by 30 each side → 160×160 from 100×100.
        // (If we accidentally used the raster-divided sigma 2.5, the bounds would only grow by 7.5.)
        Assert.That(outs[0].Bounds.Width, Is.GreaterThanOrEqualTo(160d).Within(2d));
        Assert.That(outs[0].Bounds.Height, Is.GreaterThanOrEqualTo(160d).Within(2d));
    }
}
