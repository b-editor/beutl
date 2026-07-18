using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the pure-GROW case of the linear non-invariant bake-window gating (feature 004,
/// §C3.5). A full-frame predecessor (a dynamic CustomRenderNode / NestedGraph) can emit an op whose bounds
/// GREW beyond the resolver's expected input (<c>op.Bounds ⊇ expectedInput</c>). The engage condition originally only
/// fired when the op did not CONTAIN the expected input — true for a shift or a shrink, but false for a pure grow —
/// so a grown op kept the stale frame-start resolved ROI (derived from the pre-grow describe-time bounds) and a later
/// bounds-inflating pass clipped the grown content. The fix engages whenever the op differs from the expected input
/// and maps the actual op forward for both a shift and a grow (only a true shrink still intersects the resolved ROI).
/// </summary>
[TestFixture]
public class GrownDynamicPredecessorBoundsTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A dynamic node inflates its op by 40px on every side (bounds grow to (-40,-40,208,176), containing the original
    // frame); a zero-sigma DropShadow at +(20,20) keeps the original plus the shadow. The published bounds must be the
    // forward map of the GROWN op — its union with the shifted shadow — not the frame-start ROI clipped to the
    // describe-time frame.
    [Test]
    public void GrownDynamicOp_ThenLinearDropShadow_CoversGrownContent()
    {
        var expectedGrownForward = new Rect(-40, -40, 228, 196);

        var group = new FilterEffectGroup();
        group.Children.Add(new GrowingCustomNodeEffect(40));
        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(20, 20);
        shadow.Sigma.CurrentValue = new Size(0, 0);
        shadow.Color.CurrentValue = Colors.Red;
        shadow.ShadowOnly.CurrentValue = false;
        group.Children.Add(shadow);

        FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_bounds)], RenderIntent.Delivery);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "the grown op then drop-shadow produces one output");

            Rect bounds = ops[0].Bounds;
            Assert.Multiple(() =>
            {
                Assert.That(bounds, Is.EqualTo(expectedGrownForward),
                    "the drop-shadow output must be the forward map of the GROWN op, not the stale describe-time ROI");
                Assert.That(bounds.Left, Is.LessThanOrEqualTo(-40).Within(0.01),
                    "the grown op's near edge (x=-40) must be covered, not clipped to the describe-time frame");
                Assert.That(bounds.Right, Is.GreaterThanOrEqualTo(188).Within(0.01),
                    "the grown shadow's far edge (x=188) must be covered, not cropped to the describe-time ROI");
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

// A custom-render-node effect (the dynamic-outputs, full-frame primitive) whose node inflates every input
// op's bounds by a fixed pad, reproducing a NodeGraph/CustomRenderNode predecessor that grows its content region.
[SuppressResourceClassGeneration]
internal sealed partial class GrowingCustomNodeEffect(float pad) : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(pad);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(float pad) : CustomRenderNodeFilterEffect.Resource
    {
        private static readonly FilterEffectRenderNodeFactory s_factory =
            FilterEffectRenderNodeFactory.Of<Resource, GrowingRenderNode>(static r => new GrowingRenderNode(r));

        public float Pad => pad;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => s_factory;
    }
}

internal sealed class GrowingRenderNode(GrowingCustomNodeEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        float pad = resource.Pad;
        var outputs = new RenderNodeOperation[context.Input.Length];
        for (int i = 0; i < context.Input.Length; i++)
        {
            RenderNodeOperation src = context.Input[i];
            Rect grown = src.Bounds.Inflate(pad);
            outputs[i] = RenderNodeOperation.CreateLambda(
                grown,
                canvas => src.Render(canvas),
                hitTest: grown.Contains,
                onDispose: src.Dispose);
        }

        return outputs;
    }
}
