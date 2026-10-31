using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
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

    [TestCase(0, 1)]
    [TestCase(2, 1)]
    [TestCase(2, 3)]
    public void PresentedContinuationControlsTheFollowingEffect(int repetitions, int presenterDepth)
    {
        int calls = 0;
        FilterEffect controller = new RepeatingContinuationEffect(repetitions);
        for (int i = 0; i < presenterDepth; i++)
            controller = new FilterEffectPresenter { Target = { Value = controller } };
        var group = new FilterEffectGroup();
        group.Children.Add(controller);
        group.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, _) => calls++)));
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
        Assert.That(calls, Is.EqualTo(repetitions));
    }

    // Groups are split and target work is routed from these declarations before recording, so an
    // undeclared recording would otherwise run silently wrong once the effect is placed in a group.
    [TestCase(false, nameof(FilterEffect.Resource.ControlsContinuation))]
    [TestCase(true, nameof(FilterEffect.Resource.MayAccessTarget))]
    public void RecordingMoreThanTheResourceDeclaresFails(bool target, string declaration)
    {
        void Record(FilterEffectContext context, FilterEffect.Resource _)
        {
            if (target) context.TargetEffect(0, static (_, _, _) => { });
            else context.ContinuationEffect(0, static (_, _, _) => { });
        }

        using var context = new FilterEffectContext(new Rect(0, 0, 8, 8));
        using var undeclared = new EffectItemSuffixCallbackFilterEffect(Record).ToResource(CompositionContext.Default);
        Assert.That(() => context.Apply(undeclared), Throws.InvalidOperationException.With.Message.Contains(declaration));
        Assert.That(context.CountItems(), Is.Zero, "the rejected recording is rolled back");

        using var declared = new EffectItemSuffixCallbackFilterEffect(Record,
            declaresContinuation: !target, declaresTargetAccess: target).ToResource(CompositionContext.Default);
        context.Apply(declared);
        Assert.That(context.CountItems(), Is.EqualTo(1));
    }

    // A node graph effect renders only through its own node, so the group keeps it out of the continuation's node.
    [Test]
    public void EffectsBeforeAContinuationKeepTheirOwnNodes()
    {
        int calls = 0;
        var group = new FilterEffectGroup();
        group.Children.Add(PassThroughGraph());
        group.Children.Add(new RepeatingContinuationEffect(2));
        group.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, _) => calls++)));
        RasterizeShape(group);
        Assert.That(calls, Is.EqualTo(2));
    }

    // The split follows the continuation into nested groups, keeping every earlier effect in its own node and order.
    [TestCase(1)]
    [TestCase(2)]
    public void NestedEffectsBeforeAContinuationKeepTheirOwnNodes(int depth)
    {
        var log = new List<string>();
        FilterEffect Logging(string name) => new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(name, (value, _) => log.Add(value)));

        var group = new FilterEffectGroup();
        group.Children.Add(PassThroughGraph());
        group.Children.Add(Logging("inner"));
        group.Children.Add(new RepeatingContinuationEffect(2));
        group.Children.Add(Logging("inner-tail"));
        for (int level = 0; level < depth; level++)
        {
            var outer = new FilterEffectGroup();
            outer.Children.Add(Logging($"outer{level}"));
            outer.Children.Add(group);
            outer.Children.Add(Logging($"outer{level}-tail"));
            group = outer;
        }

        RasterizeShape(group);
        string[] expected = depth == 1
            ? ["outer0", "inner", "inner-tail", "outer0-tail", "inner-tail", "outer0-tail"]
            : ["outer1", "outer0", "inner",
               "inner-tail", "outer0-tail", "outer1-tail", "inner-tail", "outer0-tail", "outer1-tail"];
        Assert.That(log, Is.EqualTo(expected));
    }

    // The inner group's earlier children have their own nodes, so only effects after the continuation count.
    [TestCase(false, false, false, false)]
    [TestCase(true, false, false, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, false, true, true)]
    public void NestedContinuationSuffixDeclaresTargetAccessOfItsEffects(
        bool innerPrefixAccessesTarget, bool innerTailAccessesTarget, bool outerTailAccessesTarget, bool expected)
    {
        static FilterEffect Effect(bool accessesTarget)
            => new EffectItemSuffixCallbackFilterEffect(static (_, _) => { }, declaresTargetAccess: accessesTarget);

        var inner = new FilterEffectGroup();
        inner.Children.Add(Effect(innerPrefixAccessesTarget));
        inner.Children.Add(new RepeatingContinuationEffect(2));
        inner.Children.Add(Effect(innerTailAccessesTarget));
        var group = new FilterEffectGroup();
        group.Children.Add(inner);
        group.Children.Add(Effect(outerTailAccessesTarget));
        using var resource = (FilterEffectGroup.Resource)group.ToResource(CompositionContext.Default);
        Assert.That(resource.NestedContinuationGroup, Is.Not.Null);
        Assert.That(resource.ContinuationSuffixResource!.MayAccessTarget, Is.EqualTo(expected));
    }

    [TestCase(true, 1, 2)]
    [TestCase(false, 0, 0)]
    public void SplitGroupFollowsItsEnabledState(bool enabled, int expectedPrefixCalls, int expectedSuffixCalls)
    {
        int prefixCalls = 0;
        int suffixCalls = 0;
        var group = new FilterEffectGroup { IsEnabled = { Value = enabled } };
        group.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, _) => prefixCalls++)));
        group.Children.Add(new RepeatingContinuationEffect(2));
        group.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, _) => suffixCalls++)));
        RasterizeShape(group);
        Assert.That(prefixCalls, Is.EqualTo(expectedPrefixCalls));
        Assert.That(suffixCalls, Is.EqualTo(expectedSuffixCalls));
    }

    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void NestedGroupWithoutAContinuationFollowsItsEnabledState(bool enabled, int expectedCalls)
    {
        int calls = 0;
        var inner = new FilterEffectGroup { IsEnabled = { Value = enabled } };
        inner.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, _) => calls++)));
        var group = new FilterEffectGroup();
        group.Children.Add(inner);
        RasterizeShape(group);
        Assert.That(calls, Is.EqualTo(expectedCalls));
    }

    [Test]
    public void EffectAfterAContinuationThatCannotBeRecordedFailsWithGuidance()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new RepeatingContinuationEffect(2));
        group.Children.Add(PassThroughGraph());
        Assert.That(() => RasterizeShape(group),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("Move it before"));
    }

    [Test]
    public void ContinuationSuffixIsReusedUntilItsChildrenChange()
    {
        var tail = new Blur();
        var continuation = new RepeatingContinuationEffect(2);
        var group = new FilterEffectGroup();
        group.Children.Add(new Blur());
        group.Children.Add(continuation);
        group.Children.Add(tail);
        using var resource = (FilterEffectGroup.Resource)group.ToResource(CompositionContext.Default);
        FilterEffect.Resource? suffix = resource.ContinuationSuffixResource;
        Assert.That(suffix, Is.Not.Null);
        Assert.That(resource.ContinuationStart, Is.EqualTo(1));
        int version = suffix!.Version;

        bool changed = false;
        resource.Reconcile(group, CompositionContext.Default, ref changed);
        Assert.That(resource.ContinuationSuffixResource, Is.SameAs(suffix));
        Assert.That(suffix.Version, Is.EqualTo(version));

        tail.Sigma.Value = new Size(3, 3);
        changed = false;
        resource.Reconcile(group, CompositionContext.Default, ref changed);
        Assert.That(resource.ContinuationSuffixResource, Is.SameAs(suffix));
        Assert.That(suffix.Version, Is.Not.EqualTo(version));

        continuation.IsEnabled.Value = false;
        changed = false;
        resource.Reconcile(group, CompositionContext.Default, ref changed);
        Assert.That(resource.ContinuationSuffixResource, Is.Null);
        Assert.That(suffix.IsDisposed, Is.True);
    }

    private static NodeGraphFilterEffect PassThroughGraph()
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel model = effect.Model.Value!;
        var input = new FilterEffectInputNode();
        var output = new OutputNode();
        model.Nodes.Add(input);
        model.Nodes.Add(output);
        model.Connect(output.InputPort, input.Output);
        return effect;
    }

    private static void RasterizeShape(FilterEffect effect)
    {
        var shape = new Beutl.Graphics.Shapes.RectShape { FilterEffect = { Value = effect } };
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
    }

    [Test]
    public void PresenterSkipsADisabledContinuationTarget()
    {
        var target = new RepeatingContinuationEffect(2) { IsEnabled = { Value = false } };
        var presenter = new FilterEffectPresenter { Target = { Value = target } };
        using var resource = presenter.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 8, 8));
        Assert.That(() => context.Apply(resource), Throws.Nothing);
        Assert.That(context.CountItems(), Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeferredTargetEffectRetainsAccessThroughNestedGroups(bool nested)
    {
        int calls = 0;
        var tail = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.TargetEffect(0, (_, _, target) =>
            {
                calls++;
                using var image = target.Capture();
                using var pixels = SKBitmap.FromImage(image.Image);
                Assert.That(pixels.GetPixel(1, 1), Is.EqualTo(SKColors.Red));
                target.Replace(image.Image);
            }), declaresTargetAccess: true);
        var group = new FilterEffectGroup();
        group.Children.Add(new RepeatingContinuationEffect(2));
        if (nested)
        {
            var inner = new FilterEffectGroup();
            inner.Children.Add(tail);
            group.Children.Add(inner);
        }
        else group.Children.Add(tail);
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 32, 24), Brushes.Resource.Red, null));
        var effectNode = new FilterEffectRenderNode(group.ToResource(CompositionContext.Default));
        effectNode.AddChild(new RectangleRenderNode(new Rect(0, 0, 32, 24), Brushes.Resource.Blue, null));
        root.AddChild(effectNode);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            Purpose = RenderRequestPurpose.Frame,
            TargetDomain = new Rect(0, 0, 32, 24),
        }, new CpuRenderTargetFactory());
        using var output = renderer.Rasterize();
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase(0, false, true)]
    [TestCase(1, false, true)]
    [TestCase(0, true, false)]
    [TestCase(1, true, false)]
    [TestCase(1, true, true)]
    [TestCase(2, true, true)]
    public void ContinuationRetainsRequirementsAcrossEvaluationTimes(int wrapper, bool accessesTarget, bool changesEnabled)
    {
        var clock = new CompositionContext(MediaTime.Zero);
        var seen = new List<int>();
        var following = new List<int>();
        var tail = new TimeDependentEffect((execution, target) =>
        {
            seen.Add(execution.Instance!.Value.Index);
            if (!accessesTarget) return;
            using var image = target!.Capture();
            using var pixels = SKBitmap.FromImage(image.Image);
            Assert.That(pixels.GetPixel(1, 1), Is.EqualTo(SKColors.Red));
            target.Replace(image.Image);
        }, changesEnabled, accessesTarget);
        var group = new FilterEffectGroup();
        group.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.ContinuationEffect(0, static (_, execution, continuation) =>
            {
                foreach (int second in new[] { 2, 0, 1 })
                    continuation.ApplyTo(execution.Targets,
                        new FilterEffectInstance(second, 3, MediaTime.FromSeconds(second), MediaTime.Zero));
            }), declaresContinuation: true));
        if (wrapper == 2)
            group.Children.Add(new FilterEffectPresenter { Target = { Value = tail } });
        else if (wrapper == 1)
        {
            var inner = new FilterEffectGroup();
            inner.Children.Add(tail);
            group.Children.Add(inner);
        }
        else group.Children.Add(tail);
        group.Children.Add(new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, execution) => following.Add(execution.Instance!.Value.Index))));
        using var root = new ContainerRenderNode();
        var domain = new Rect(0, 0, 32, 24);
        root.AddChild(new RectangleRenderNode(domain, Brushes.Resource.Red, null));
        var effectNode = new FilterEffectRenderNode(group.ToResource(clock));
        effectNode.AddChild(new RectangleRenderNode(domain, Brushes.Resource.Blue, null));
        root.AddChild(effectNode);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            Purpose = RenderRequestPurpose.Frame,
            TargetDomain = domain,
        }, new CpuRenderTargetFactory());
        using var output = renderer.Rasterize();
        Assert.That(seen, Is.EqualTo(new[] { 2, 1 }));
        Assert.That(following, Is.EqualTo(new[] { 2, 0, 1 }), "Skipping a disabled effect must retain its suffix.");
    }

    [Test]
    public void DirectApplicationSkipsDisabledResources()
    {
        var effect = new TimeDependentEffect((_, _) => { }, changesEnabled: true, accessesTarget: false);
        using var context = new FilterEffectContext(new Rect(0, 0, 8, 8));
        using var disabled = effect.ToResource(new CompositionContext(MediaTime.Zero));
        context.Apply(disabled);
        Assert.That(effect.Recordings, Is.Zero);
        Assert.That(context.CountItems(), Is.Zero);
        using var enabled = effect.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        context.Apply(enabled);
        Assert.That(effect.Recordings, Is.EqualTo(1));
        Assert.That(context.CountItems(), Is.EqualTo(1));
    }

    private sealed class TimeDependentEffect(
        Action<CustomFilterEffectContext, CompositionTargetAccess?> draw, bool changesEnabled, bool accessesTarget) : FilterEffect
    {
        public int Recordings { get; private set; }

        private void ApplyEffect(FilterEffectContext context, FilterEffect.Resource resource)
        {
            Recordings++;
            if (resource.EvaluationContext!.Time == MediaTime.Zero) return;
            if (accessesTarget) context.TargetEffect(0, (_, execution, target) => draw(execution, target));
            else context.CustomEffect(0, (_, execution) => draw(execution, null));
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(accessesTarget);
            bool changed = true;
            resource.Reconcile(this, context, ref changed);
            if (changesEnabled) resource.IsEnabled = context.Time != MediaTime.Zero;
            return resource;
        }

        public new sealed class Resource(bool accessesTarget) : FilterEffect.Resource
        {
            public override bool MayAccessTarget => accessesTarget;

            protected override void ApplyTo(FilterEffectContext context)
                => ((TimeDependentEffect)RequireOriginal()).ApplyEffect(context, this);
        }
    }

    private sealed class RepeatingContinuationEffect(int count) : FilterEffect
    {
        private void ApplyEffect(FilterEffectContext context)
            => context.ContinuationEffect(count, static (repetitions, execution, continuation) =>
            {
                for (int index = 0; index < repetitions; index++)
                    continuation.ApplyTo(execution.Targets);
            });

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

            protected override void ApplyTo(FilterEffectContext context)
                => ((RepeatingContinuationEffect)RequireOriginal()).ApplyEffect(context);
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
            context.ContinuationEffect(0, (_, execution, continuation) =>
            {
                foreach (int i in new[] { 2, 0, 1 })
                    continuation.ApplyTo(execution.Targets,
                        new FilterEffectInstance(i, 3, MediaTime.FromSeconds(i), clock.Time));
            });
        }, declaresContinuation: true);
        var tail = new EffectItemSuffixCallbackFilterEffect((context, resource) =>
        {
            double time = resource.EvaluationContext!.Time.ToSeconds();
            Assert.That(resource.EvaluationContext.PreferProxy, Is.True);
            Assert.That(resource.EvaluationContext.DisableResourceShare, Is.True);
            // Even a stage with no operations at the original time must be evaluated at the new time.
            if (time == 3) return;
            resources.Add(resource);
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
