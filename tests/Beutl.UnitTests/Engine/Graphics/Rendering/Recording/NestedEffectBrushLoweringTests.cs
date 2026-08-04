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
    // The probe re-runs the whole child ApplyTo to reach its brush registrations, then throws its operations away.
    // Anything those operations owned would otherwise be held for the whole request and never executed.
    [Test]
    public void NestedBrushLowering_KeepsTheBrushButRollsBackTheProbeOwnedResources()
    {
        var owned = new CountingDisposable();
        using Brush.Resource brush = MakeDrawableBrush();
        var child = new LegacySuffixCallbackFilterEffect((context, _) =>
        {
            context.Own(owned, "nested-probe-owned", 1);
            FilterEffectBrush handle = context.RegisterBrush(brush);
            context.CustomEffect(handle, static (_, _) => { }, static (_, bounds) => bounds);
        });

        // Asserted before the request is torn down, which would release every owned resource anyway.
        Record(MakeDelayed(child), segments => Assert.Multiple(() =>
        {
            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(
                segments[0].Brushes,
                Has.Length.EqualTo(1),
                "the nested brush must still be lowered into the segment");
            Assert.That(
                owned.DisposeCount,
                Is.EqualTo(1),
                "the probe's owned resource must not survive the discarded probe operations");
        }));
    }

    [Test]
    public void NestedBrushLowering_InsideAnotherProbe_KeepsTheBrushInTheConsumingSegment()
    {
        FilterEffect inner = MakeDelayed(new FilterEffectGroup { Children = { MakeDrawableBrushShadow() } });

        Record(MakeDelayed(inner), segments => Assert.Multiple(() =>
        {
            Assert.That(segments, Has.Length.EqualTo(1));
            Assert.That(
                segments[0].Brushes,
                Has.Length.EqualTo(1),
                "the doubly nested brush must still be lowered into the segment");
        }));
    }

    // The inner probe's own item index only diverges from the real recording's once the outer probe has authored an
    // operation of its own, so a preceding sibling in the outer nested group is what exposes the mis-indexing.
    [Test]
    public void NestedBrushLowering_AfterAnOperationInTheEnclosingProbe_KeepsTheBrushInTheConsumingSegment()
    {
        var precedingSibling = new LegacySuffixCallbackFilterEffect(
            static (context, _) => context.Blur(new Size(1, 1)));
        FilterEffect inner = MakeDelayed(new FilterEffectGroup { Children = { MakeDrawableBrushShadow() } });

        Record(
            MakeDelayed(new FilterEffectGroup { Children = { precedingSibling, inner } }),
            segments => Assert.Multiple(() =>
            {
                Assert.That(segments, Has.Length.EqualTo(1));
                Assert.That(
                    segments[0].Brushes,
                    Has.Length.EqualTo(1),
                    "the nested brush must be registered in the real recording's item order, not the probe's");
            }));
    }

    // A swallowed pre-pass failure otherwise surfaces only as the unlowered-DrawableBrush guard raised much later.
    [Test]
    public void FailedNestedBrushLowering_AttachesItsFailureToTheExecutionFailure()
    {
        var probeFailure = new InvalidOperationException("nested-probe-boom");
        var executionFailure = new InvalidOperationException("segment-boom");
        Rect bounds = new(0, 0, 8, 6);
        using var authored = new FilterEffectContext(bounds);
        authored.CustomEffect(
            0,
            (_, _) => throw executionFailure,
            static (_, rect) => rect);
        using FilterEffectContext segment = FilterEffectContext.CreateLegacySegment(
            bounds,
            outputScale: 1,
            workingScale: 1,
            authored._items,
            probeFailure);
        using EffectTargets targets = CreateSolidTargets(bounds);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(targets, builder);

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() => activator.Apply(segment));

        Assert.Multiple(() =>
        {
            Assert.That(thrown, Is.SameAs(executionFailure));
            Assert.That(
                thrown!.Data[FilterEffectContext.NestedBrushLoweringFailureKey],
                Is.SameAs(probeFailure));
        });
    }

    [Test]
    public void SucceedingNestedBrushLowering_LeavesTheExecutionFailureUnannotated()
    {
        var executionFailure = new InvalidOperationException("segment-boom");
        Rect bounds = new(0, 0, 8, 6);
        using var authored = new FilterEffectContext(bounds);
        authored.CustomEffect(
            0,
            (_, _) => throw executionFailure,
            static (_, rect) => rect);
        using FilterEffectContext segment = FilterEffectContext.CreateLegacySegment(
            bounds,
            outputScale: 1,
            workingScale: 1,
            authored._items);
        using EffectTargets targets = CreateSolidTargets(bounds);
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(targets, builder);

        InvalidOperationException? thrown = Assert.Throws<InvalidOperationException>(() => activator.Apply(segment));

        Assert.That(
            thrown!.Data.Contains(FilterEffectContext.NestedBrushLoweringFailureKey),
            Is.False);
    }

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
