using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression (raster, GPU-less) for the shrink-intersection gating (feature 004, §C3.5): a linear non-invariant
/// pass fed by a render-time-resolved predecessor that emits a SHIFTED op (a dynamic CustomRenderNode / NestedGraph
/// translating its output) must size its output from the ACTUAL shifted op's forward map, not from the stale
/// frame-start ROI. The engage condition originally fired for any op that did not contain the resolver's expected
/// input — which is true for a shift as well as a shrink — and intersected the shifted forward with the stale ROI,
/// cropping (or emptying) the shifted content. The fix gates the intersection on a true shrink
/// (<c>expectedInput.Contains(op.Bounds)</c>) and otherwise maps the shifted op forward directly.
/// </summary>
[TestFixture]
public class ShiftedDynamicPredecessorBoundsTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory ??= Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A dynamic node shifts its op by (40,30); a zero-sigma DropShadow at +(20,20) keeps the original plus the
    // shadow. The published bounds must be the union of the shifted op and its shifted shadow — the full forward map
    // of the shifted content — not the frame-start ROI clipped to the shifted forward.
    [Test]
    public void ShiftedDynamicOp_ThenLinearDropShadow_CoversShiftedContent()
    {
        var expectedShiftedForward = new Rect(40, 30, 148, 116);

        var group = new FilterEffectGroup();
        group.Children.Add(new ShiftingCustomNodeEffect(new Point(40, 30)));
        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(20, 20);
        shadow.Sigma.CurrentValue = new Size(0, 0);
        shadow.Color.CurrentValue = Colors.Red;
        shadow.ShadowOnly.CurrentValue = false;
        group.Children.Add(shadow);

        FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeContentRect(s_bounds)]);

        RenderNodeOperation[] ops = node.Process(context);
        try
        {
            Assert.That(ops, Has.Length.EqualTo(1), "the shifted op then drop-shadow produces one output");

            Rect bounds = ops[0].Bounds;
            Assert.Multiple(() =>
            {
                Assert.That(bounds, Is.EqualTo(expectedShiftedForward),
                    "the drop-shadow output must be the forward map of the SHIFTED op, not the stale-ROI intersection");
                Assert.That(bounds.Right, Is.GreaterThanOrEqualTo(188).Within(0.01),
                    "the shifted shadow's far edge (x=188) must be covered, not cropped to the stale ROI");
                Assert.That(bounds.Bottom, Is.GreaterThanOrEqualTo(146).Within(0.01),
                    "the shifted shadow's far edge (y=146) must be covered, not cropped to the stale ROI");
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

// A custom-render-node effect (the dynamic-outputs, render-time-resolved primitive) whose node translates every
// input op by a fixed shift, reproducing a NodeGraph/CustomRenderNode predecessor that moves its content.
[SuppressResourceClassGeneration]
internal sealed partial class ShiftingCustomNodeEffect(Point shift) : CustomRenderNodeFilterEffect
{
    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource(shift);
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource(Point shift) : FilterEffect.Resource
    {
        public Point Shift => shift;

        public override FilterEffectRenderNodeFactory RenderNodeFactory
            => FilterEffectRenderNodeFactory.Of(static r => new ShiftingRenderNode((Resource)r));
    }
}

internal sealed class ShiftingRenderNode(ShiftingCustomNodeEffect.Resource resource) : FilterEffectRenderNode(resource)
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        Point shift = resource.Shift;
        var outputs = new RenderNodeOperation[context.Input.Length];
        for (int i = 0; i < context.Input.Length; i++)
        {
            RenderNodeOperation src = context.Input[i];
            Rect shifted = src.Bounds.Translate(shift);
            outputs[i] = RenderNodeOperation.CreateLambda(
                shifted,
                canvas =>
                {
                    using (canvas.PushTransform(Matrix.CreateTranslation((float)shift.X, (float)shift.Y)))
                        src.Render(canvas);
                },
                hitTest: shifted.Contains,
                onDispose: src.Dispose);
        }

        return outputs;
    }
}
