using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class IndividualContinuationTests
{
    [Test]
    public void DrawableRecordingKeepsNestedContinuationGroupsTogether()
    {
        int calls = 0;
        var inner = new FilterEffectGroup();
        inner.Children.Add(new RepeatingContinuationEffect(3));
        inner.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, _) => calls++)));
        var group = new FilterEffectGroup();
        group.Children.Add(new RepeatingContinuationEffect(2));
        group.Children.Add(inner);
        var shape = new Beutl.Graphics.Shapes.RectShape { FilterEffect = { Value = group } };
        using var resource = shape.ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        using (var context = new GraphicsContext2D(root, new Size(32, 24)))
            context.DrawDrawable(resource);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            Purpose = RenderRequestPurpose.Frame,
            TargetDomain = new Rect(0, 0, 32, 24),
        }, new CpuRenderTargetFactory());
        using var output = renderer.Rasterize();
        Assert.That(calls, Is.EqualTo(6));
    }

    private sealed class RepeatingContinuationEffect(int count) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
            => context.CustomEffect(count, static (repetitions, execution) =>
            {
                for (int index = 0; index < repetitions; index++)
                    execution.Continuation!.ApplyTo(execution.Targets);
            }, CustomFilterEffectOptions.ControlsContinuation);

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool changed = true;
            resource.Reconcile(this, context, ref changed);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public override bool ControlsContinuation => true;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ContinuationReevaluatesOnlyItsSuffixAndPreservesInstanceThroughNestedGroups(bool fail)
    {
        var seen = new List<(double Time, int Index, int Count)>();
        var resources = new List<FilterEffect.Resource>();
        var clock = new CompositionContext(MediaTime.FromSeconds(3)) { PreferProxy = true };
        int prefixCalls = 0;
        var prefix = new EffectItemSuffixCallbackFilterEffect((context, _) =>
        {
            prefixCalls++;
            context.CustomEffect(0, (_, execution) =>
            {
                foreach (int i in new[] { 2, 0, 1 })
                    execution.Continuation!.ApplyTo(execution.Targets,
                        new FilterEffectInstance(i, 3, MediaTime.FromSeconds(i), clock.Time));
            }, CustomFilterEffectOptions.ControlsContinuation);
        });
        var tail = new EffectItemSuffixCallbackFilterEffect((context, resource) =>
        {
            resources.Add(resource);
            double time = resource.EvaluationContext!.Time.ToSeconds();
            Assert.That(resource.EvaluationContext.PreferProxy, Is.True);
            Assert.That(resource.EvaluationContext.DisableResourceShare, Is.True);
            // Even a stage with no operations at the original time must be evaluated at the new time.
            if (time == 3) return;
            context.CustomEffect(time, (value, execution) =>
            {
                seen.Add((value, execution.Instance!.Value.Index, execution.Instance.Value.Count));
                if (fail) throw new InvalidOperationException("individual failed");
            });
        });
        var inner = new FilterEffectGroup();
        inner.Children.Add(tail);
        var group = new FilterEffectGroup();
        group.Children.Add(prefix);
        group.Children.Add(inner);
        using var snapshot = group.ToResource(clock);
        clock.Time = MediaTime.FromSeconds(99);
        var bounds = new Rect(0, 0, 8, 8);
        using var program = new FilterEffectContext(bounds);
        program.Apply(snapshot);
        using var target = new CpuRenderTarget(new PixelSize(8, 8));
        target.Value.Canvas.Clear(SKColors.Red);
        using var targets = new EffectTargets { new(target, bounds) };
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Delivery,
            RenderRequestPurpose.Frame, null, targetDomain: bounds);
        if (fail)
            Assert.That(() => executor.Apply(program), Throws.InvalidOperationException.With.Message.EqualTo("individual failed"));
        else
        {
            executor.Apply(program);
            executor.Flush();
            Assert.That(seen, Is.EqualTo(new[] { (2d, 2, 3), (0d, 0, 3), (1d, 1, 3) }));
        }
        Assert.That(prefixCalls, Is.EqualTo(1), "Reevaluating the suffix must not rerun the prefix.");
        Assert.That(snapshot.EvaluationContext!.Time.ToSeconds(), Is.EqualTo(3));
        Assert.That(resources, Is.Not.Empty);
        Assert.That(resources.All(resource => resource.IsDisposed), Is.True, "Temporary resources must be released, including after errors.");
    }
}
