using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
[NonParallelizable]
public sealed class NestedEffectBrushLoweringTests
{





    private static DelayAnimationEffect MakeDelayed(FilterEffect child)
    {
        var delay = new DelayAnimationEffect();
        delay.Delay.CurrentValue = 0f;
        delay.Effect.CurrentValue = child;
        return delay;
    }

    private static void Record(FilterEffect effect, Action<LegacyFilterEffectRenderFragmentPayload[]> assert)
    {
        using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 40, 30), Brushes.Resource.White, null));
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        assert(graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Where(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect)
            .Select(static reference => (LegacyFilterEffectRenderFragmentPayload)reference.Payload!)
            .ToArray());
    }

    private static Brush.Resource MakeDrawableBrush()
        => MakeDrawableBrushSource().ToResource(CompositionContext.Default);

    private static DrawableBrush MakeDrawableBrushSource()
    {
        var content = new RectShape();
        content.Width.CurrentValue = 10;
        content.Height.CurrentValue = 10;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        return brush;
    }

    private static FlatShadow MakeDrawableBrushShadow()
    {
        var shadow = new FlatShadow();
        shadow.Length.CurrentValue = 4;
        shadow.Brush.CurrentValue = MakeDrawableBrushSource();
        return shadow;
    }

    private static EffectTargets CreateSolidTargets(Rect bounds)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.White);
        }

        return new EffectTargets { new EffectTarget(renderTarget, bounds, EffectiveScale.At(1)) };
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
