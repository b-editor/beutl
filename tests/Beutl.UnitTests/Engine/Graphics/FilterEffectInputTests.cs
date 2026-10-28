using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class FilterEffectInputTests
{
    [Test]
    public void SamplesAreIndependentAndSameTimeIsReusedWithinTheFrame()
    {
        RectShape shape = Rectangle();
        var effect = new SampleInputEffect();
        shape.FilterEffect.Value = effect;
        var clock = new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) };
        using var source = shape.ToResource(clock);
        var prepared = (SampleInputEffect.Resource)source.FilterEffect!;
        Assert.Multiple(() =>
        {
            Assert.That(prepared.Samples.Select(s => ((RectShape.Resource)s.Resource!).Width),
                Is.EqualTo(new[] { 18.125f, 19.375f, 20.625f, 21.875f }));
            Assert.That(prepared.Samples.Select(s => s.Resource).Distinct().Count(), Is.EqualTo(4));
            Assert.That(prepared.Input!.Evaluate(prepared.Samples[0].Time), Is.SameAs(prepared.Samples[0]));
            Assert.That(source.Width, Is.EqualTo(20));
            Assert.That(source.FilterEffect!.GetOriginal(), Is.SameAs(effect));
            Assert.That(shape.Width.Value, Is.EqualTo(10));
            Assert.That(clock.Time, Is.EqualTo(Seconds(1)));
        });
    }

    [TestCase("group")]
    [TestCase("controller")]
    [TestCase("decorator")]
    public void ConsumedFlowAndControllerTimeMappingAreReplayed(string kind)
    {
        TestMediaHelper.RegisterTestDecoder();
        var media = new VideoSource();
        media.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(16, 16, new(30, 1), 300)));
        var video = new SourceVideo { Source = { Value = media } };
        var controller = new DrawableTimeController { OffsetPosition = { Value = Seconds(1) } };
        Drawable owner = kind switch
        {
            "group" => new DrawableGroup(),
            "decorator" => new DrawableDecorator(),
            _ => controller,
        };
        owner.FilterEffect.Value = new SampleInputEffect();
        var scene = ReferenceEquals(owner, controller) ? SceneWith(video, controller) : SceneWith(video, controller, owner);
        using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };
        var source = compositor.EvaluateGraphics(Seconds(1), new Rational(2, 1)).Objects.OfType<Drawable.Resource>().Single();
        var samples = ((SampleInputEffect.Resource)source.FilterEffect!).Samples;
        Assert.That(samples.Select(s => Video(s.Resource!).RequestedPosition.ToSeconds()),
            Is.EqualTo(new[] { 1.8125, 1.9375, 2.0625, 2.1875 }).Within(0.000001));
        using var independent = new SceneCompositor(scene);
        independent.EvaluateGraphics(Seconds(2));
        Assert.That(Video(samples[0].Resource!).RequestedPosition, Is.EqualTo(Seconds(1.8125)));

        static SourceVideo.Resource Video(Drawable.Resource source)
        {
            var controller = (DrawableTimeController.Resource)(source switch
            {
                DrawableGroup.Resource group => group.Children.Single(),
                DrawableDecorator.Resource decorator => decorator.Children.Single(),
                _ => source,
            });
            return (SourceVideo.Resource)controller.Target!;
        }
    }

    [Test]
    public void AnimationEditsWithTheSameCurrentValueInvalidateAndReleaseOldSamples()
    {
        var shape = Rectangle();
        shape.FilterEffect.Value = new SampleInputEffect();
        var context = new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) };
        using var source = shape.ToResource(context);
        var old = ((SampleInputEffect.Resource)source.FilterEffect!).Samples.ToArray();
        int version = source.Version;
        Animate(shape.Width, 0, 40);
        Reconcile(source, shape, context);
        Assert.That(source.Width, Is.EqualTo(20));
        Assert.That(source.Version, Is.GreaterThan(version));
        Assert.That(old.All(s => s.IsDisposed), Is.True);
        Assert.That(((RectShape.Resource)((SampleInputEffect.Resource)source.FilterEffect!).Samples[0].Resource!).Width,
            Is.EqualTo(16.25));
        Assert.Throws<ObjectDisposedException>(() => _ = old[0].Resource);
    }

    [Test]
    public void NextReconciliationReevaluatesSamplesInPlaceAtTheNearestOffset()
    {
        var shape = Rectangle();
        var effect = new SampleInputEffect();
        shape.FilterEffect.Value = effect;
        using var source = shape.ToResource(new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) });
        var first = Samples().ToArray();
        var resources = first.Select(s => s.Resource!).ToArray();

        Reconcile(source, shape, new CompositionContext(Seconds(1.5)) { FrameRate = new(2, 1) });
        Assert.Multiple(() =>
        {
            Assert.That(first.All(s => s.IsDisposed), Is.True);
            Assert.That(Samples().Select(s => ((RectShape.Resource)s.Resource!).Width),
                Is.EqualTo(new[] { 23.125f, 24.375f, 25.625f, 26.875f }));
            for (int i = 0; i < resources.Length; i++)
                Assert.That(Samples()[i].Resource, Is.SameAs(resources[i]), "a sample keeps its offset's resource");
        });

        effect.Offsets = [-0.125, 0.125];
        Reconcile(source, shape, new CompositionContext(Seconds(1.5)) { FrameRate = new(2, 1) });
        Assert.Multiple(() =>
        {
            Assert.That(Samples()[0].Resource, Is.SameAs(resources[1]));
            Assert.That(Samples()[1].Resource, Is.SameAs(resources[2]));
            Assert.That(new[] { resources[0].IsDisposed, resources[3].IsDisposed }, Is.All.True,
                "unclaimed samples are released");
        });

        shape.FilterEffect.Value = null;
        Reconcile(source, shape, new CompositionContext(Seconds(1.5)) { FrameRate = new(2, 1) });
        Assert.That(resources.All(r => r.IsDisposed), Is.True);

        List<FilterEffectInputSample> Samples() => ((SampleInputEffect.Resource)source.FilterEffect!).Samples;
    }

    [Test]
    public void VideoSamplesKeepTheirReadersAcrossFrames()
    {
        TestMediaHelper.RegisterTestDecoder();
        var media = new VideoSource();
        media.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(16, 16, new(30, 1), 300)));
        var video = new SourceVideo { Source = { Value = media } };
        video.FilterEffect.Value = new SampleInputEffect();
        using var compositor = new SceneCompositor(SceneWith(video)) { ForceOriginalSource = true };
        var rate = new Rational(30, 1);
        MediaReader?[] first = Readers(Seconds(1));
        MediaReader?[] next = Readers(Seconds(1) + MediaTime.FromFrames(1, rate));
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.All.Not.Null);
            Assert.That(first.Distinct().Count(), Is.EqualTo(first.Length), "each sample decodes independently");
            for (int i = 0; i < first.Length; i++)
                Assert.That(next[i], Is.SameAs(first[i]), "the next frame continues on the same reader");
        });

        MediaReader?[] Readers(MediaTime time)
        {
            var source = compositor.EvaluateGraphics(time, rate).Objects.OfType<Drawable.Resource>().Single();
            return ((SampleInputEffect.Resource)source.FilterEffect!).Samples
                .Select(s => ((SourceVideo.Resource)s.Resource!).Source!.MediaReader).ToArray();
        }
    }

    [Test]
    public void PrecedingEffectsOfReusedSamplesReconcileInPlace()
    {
        int disposed = 0;
        var shape = Rectangle();
        var chain = new FilterEffectGroup();
        chain.Children.Add(new SampleInputEffect { UseSamples = false, AfterDispose = () => disposed++ });
        chain.Children.Add(new SampleInputEffect());
        shape.FilterEffect.Value = chain;
        using var source = shape.ToResource(new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) });
        Reconcile(source, shape, new CompositionContext(Seconds(1.5)) { FrameRate = new(2, 1) });
        Assert.That(disposed, Is.Zero, "sample prefixes are reconciled rather than rebuilt");
        chain.Children.RemoveAt(0);
        Reconcile(source, shape, new CompositionContext(Seconds(1.5)) { FrameRate = new(2, 1) });
        Assert.That(disposed, Is.EqualTo(5), "the current chain and every sample prefix release the removed effect");
    }

    [Test]
    public void BoundaryFollowsTheEffectThroughChangingGroupsAndPresenters()
    {
        var shape = Rectangle();
        var effect = new SampleInputEffect();
        var presenter = new FilterEffectPresenter { Target = { Value = effect } };
        var a = new FilterEffectGroup();
        a.Children.Add(presenter);
        var b = new FilterEffectGroup();
        b.Children.Add(new Brightness());
        b.Children.Add(new Invert());
        b.Children.Add(presenter);
        var context = new SwitchingContext(shape.FilterEffect, a, b, Seconds(1)) { FrameRate = new(2, 1) };
        using var source = shape.ToResource(context);
        var current = (SampleInputEffect.Resource)((FilterEffectPresenter.Resource)((FilterEffectGroup.Resource)source.FilterEffect!).Children[2]).Target!;
        Assert.That(current.Samples.Select(s => ((FilterEffectGroup.Resource)s.Resource!.FilterEffect!).Children.Count),
            Is.EqualTo(new[] { 0, 0, 2, 2 }), "the boundary follows the authored effect, not a flattened index");
        Assert.That(a.Children.Count, Is.EqualTo(1));
        Assert.That(b.Children.Count, Is.EqualTo(3));
    }

    [Test]
    public void RepeatedPresenterOccurrencesAndMultipleTemporalEffectsTerminateAtTheirOwnInputs()
    {
        var shape = Rectangle();
        var effect = new SampleInputEffect { Offsets = [0] };
        var group = new FilterEffectGroup();
        group.Children.Add(new FilterEffectPresenter { Target = { Value = effect } });
        group.Children.Add(new FilterEffectPresenter { Target = { Value = effect } });
        group.Children.Add(new Invert());
        shape.FilterEffect.Value = group;
        using var source = shape.ToResource(new CompositionContext(Seconds(1)));
        var current = (SampleInputEffect.Resource)((FilterEffectPresenter.Resource)((FilterEffectGroup.Resource)source.FilterEffect!).Children[1]).Target!;
        var first = (SampleInputEffect.Resource)((FilterEffectGroup.Resource)current.Samples[0].Resource!.FilterEffect!).Children.Single();
        Assert.That(first.Samples, Has.Count.EqualTo(1));
        Assert.That(((FilterEffectGroup.Resource)first.Samples[0].Resource!.FilterEffect!).Children, Is.Empty);
    }

    [Test]
    public void RemovingAnotherPresenterDoesNotMoveTheSurvivingOccurrenceBoundary()
    {
        var shape = Rectangle();
        var effect = new SampleInputEffect { Offsets = [-0.25] };
        var first = new FilterEffectPresenter { Target = { Value = effect } };
        var second = new FilterEffectPresenter { Target = { Value = effect } };
        var before = new FilterEffectGroup();
        before.Children.Add(second);
        var current = new FilterEffectGroup();
        current.Children.Add(first);
        current.Children.Add(second);
        var clock = new SwitchingContext(shape.FilterEffect, before, current, Seconds(1));
        using var source = shape.ToResource(clock);
        var prepared = (SampleInputEffect.Resource)((FilterEffectPresenter.Resource)((FilterEffectGroup.Resource)source.FilterEffect!).Children[1]).Target!;
        Assert.That(((FilterEffectGroup.Resource)prepared.Samples.Single().Resource!.FilterEffect!).Children, Is.Empty);
    }

    [Test]
    public void ChangingTheEntireChainCanEvaluateADifferentTemporalEffectWithoutRecursion()
    {
        var shape = Rectangle();
        var a = new SampleInputEffect { Offsets = [0.25] };
        var b = new SampleInputEffect { Offsets = [-0.25] };
        var context = new SwitchingContext(shape.FilterEffect, a, b, Seconds(1)) { FrameRate = new(1, 1) };
        using var source = shape.ToResource(context);
        var current = (SampleInputEffect.Resource)source.FilterEffect!;
        var previous = (SampleInputEffect.Resource)((FilterEffectGroup.Resource)current.Samples[0].Resource!.FilterEffect!).Children.Single();
        Assert.That(previous.Samples.Single().Time, Is.EqualTo(Seconds(1)));
        Assert.That(((FilterEffectGroup.Resource)previous.Samples[0].Resource!.FilterEffect!).Children, Is.Empty);
    }

    [Test]
    public void SessionsCarryOutputConditionsAndCancellationDisposesPartialResults()
    {
        var shape = Rectangle();
        var effect = new SampleInputEffect();
        shape.FilterEffect.Value = effect;
        var context = new CompositionContext(Seconds(1))
        {
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
            TargetDomain = new Rect(0, 0, 96, 64),
            FrameRate = new(24, 1),
        };
        using var source = shape.ToResource(context);
        var initial = (SampleInputEffect.Resource)source.FilterEffect!;
        Assert.Multiple(() =>
        {
            Assert.That(initial.Input!.EvaluationContext.FrameRate, Is.EqualTo(context.FrameRate));
            Assert.That(initial.Input.EvaluationContext.PreferProxy, Is.True);
            Assert.That(initial.Input.EvaluationContext.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(initial.Input.EvaluationContext.TargetDomain, Is.EqualTo(context.TargetDomain));
        });
        using var cancellation = new CancellationTokenSource();
        var partial = new List<FilterEffectInputSample>();
        effect.AfterSample = sample => { partial.Add(sample); if (partial.Count == 2) cancellation.Cancel(); };
        context.CancellationToken = cancellation.Token;
        Assert.Throws<OperationCanceledException>(() => Reconcile(source, shape, context));
        Assert.That(partial, Has.Count.EqualTo(2));
        Assert.That(partial.All(s => s.IsDisposed), Is.True);
        effect.AfterSample = null;
        context.CancellationToken = default;
        Assert.DoesNotThrow(() => Reconcile(source, shape, context));
        Assert.That(((SampleInputEffect.Resource)source.FilterEffect!).Samples.All(s => !s.IsDisposed), Is.True);
    }

    [Test]
    public void NestedScenesInheritOutputRateAndClippedSamplesAreTransparent()
    {
        var shape = Rectangle();
        shape.FilterEffect.Value = new SampleInputEffect();
        var nested = new SceneDrawable { ReferencedScene = { Value = SceneWith(shape) } };
        using var compositor = new SceneCompositor(SceneWith(nested));
        var frame = compositor.EvaluateGraphics(MediaTime.Zero, new Rational(120, 1));
        var scene = (SceneDrawable.Resource)frame.Objects.OfType<Drawable.Resource>().Single();
        var source = scene.Frame!.Value.Objects.OfType<Drawable.Resource>().Single();
        var effect = (SampleInputEffect.Resource)source.FilterEffect!;
        Assert.That(effect.Input!.EvaluationContext.FrameRate, Is.EqualTo(new Rational(120, 1)));
        Assert.That(effect.Samples.Select(s => s.IsVisible), Is.EqualTo(new[] { false, false, true, true }));
        Assert.That(effect.Input.EvaluationContext.EvaluationServices!.FrameRate, Is.EqualTo(new Rational(120, 1)));
    }

    [Test]
    public void SampleEvaluationDoesNotReconcileTheBoundaryOrItsSuffix()
    {
        int prefix = 0, boundary = 0, suffix = 0;
        var shape = Rectangle();
        var chain = new FilterEffectGroup();
        chain.Children.Add(new SampleInputEffect { UseSamples = false, AfterReconcile = _ => prefix++ });
        chain.Children.Add(new SampleInputEffect { AfterReconcile = _ => boundary++ });
        chain.Children.Add(new SampleInputEffect
        {
            UseSamples = false,
            AfterReconcile = clock =>
        {
            suffix++;
            Assert.That(clock.Time, Is.EqualTo(Seconds(1)), "the suffix must only reconcile at the output time");
        }
        });
        shape.FilterEffect.Value = chain;
        using var source = shape.ToResource(new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) });
        Assert.That((prefix, boundary, suffix), Is.EqualTo((5, 1, 1)));
    }

    [Test]
    public void FailureWhileCreatingASampleDisposesEveryConstructedPrefixResource()
    {
        int created = 0, disposed = 0;
        var shape = Rectangle();
        var chain = new FilterEffectGroup();
        chain.Children.Add(new SampleInputEffect
        {
            UseSamples = false,
            AfterReconcile = clock =>
            {
                created++;
                if (clock.Time != Seconds(1)) throw new InvalidOperationException("sample failure");
            },
            AfterDispose = () => disposed++,
        });
        chain.Children.Add(new SampleInputEffect());
        shape.FilterEffect.Value = chain;
        Assert.That(() => shape.ToResource(new CompositionContext(Seconds(1))),
            Throws.InvalidOperationException.With.Message.EqualTo("sample failure"));
        Assert.That(created, Is.EqualTo(2));
        Assert.That(disposed, Is.EqualTo(created));
    }

    [Test]
    public void ExportSamplesUseTheExportSnapshotAfterTheEditorChanges()
    {
        var shape = Rectangle();
        shape.FilterEffect.Value = new SampleInputEffect();
        var live = SceneWith(shape);
        Scene snapshot = SceneSnapshot.Create(live);
        shape.Width.SetKeyFrameAnimation(null);
        shape.Width.Value = 80;
        using var export = new SceneCompositor(snapshot) { DisableResourceShare = true, ForceOriginalSource = true };
        using var preview = new SceneCompositor(live);
        var saved = export.EvaluateGraphics(Seconds(1), new Rational(2, 1)).Objects.OfType<Drawable.Resource>().Single();
        var edited = preview.EvaluateGraphics(Seconds(1), new Rational(2, 1)).Objects.OfType<Drawable.Resource>().Single();
        float Width(Drawable.Resource source) => ((RectShape.Resource)((SampleInputEffect.Resource)source.FilterEffect!).Samples[0].Resource!).Width;
        Assert.That(Width(saved), Is.EqualTo(18.125f));
        Assert.That(Width(edited), Is.EqualTo(80));
    }

    private sealed class SwitchingContext(IProperty<FilterEffect?> property, FilterEffect a, FilterEffect b, MediaTime time)
        : CompositionContext(time)
    {
        public override T Get<T>(IProperty<T> candidate)
            => ReferenceEquals(candidate, property) ? (T)(object)(Time < Seconds(1) ? a : b) : base.Get(candidate);
    }

    internal static RectShape Rectangle()
    {
        var shape = new RectShape { Width = { Value = 10 }, Height = { Value = 12 }, Fill = { Value = new SolidColorBrush(Colors.White) } };
        Animate(shape.Width, 10, 30);
        return shape;
    }

    internal static void Animate(IProperty<float> property, float start, float end) => property.SetKeyFrameAnimation(new KeyFrameAnimation<float>
    {
        KeyFrames =
        {
            new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = start } },
            new KeyFrame<float> { KeyTime = { Value = Seconds(2) }, Value = { Value = end } },
        },
    });

    internal static Scene SceneWith(params EngineObject[] objects)
    {
        var scene = new Scene(96, 64, "Input evaluation") { Uri = new Uri("file:///input-evaluation/scene") };
        var element = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = Seconds(4) }, Uri = new Uri("file:///input-evaluation/element") };
        foreach (var obj in objects) element.Objects.Add(obj);
        scene.Children.Add(element);
        return scene;
    }

    private static void Reconcile(Drawable.Resource resource, Drawable shape, CompositionContext context)
    {
        bool changed = false;
        resource.Reconcile(shape, context, ref changed);
    }

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);
}

// Exercises only public extension hooks. Sample scheduling and averaging belong to the effect, not the host.
public sealed partial class SampleInputEffect : FilterEffect
{
    public double[] Offsets { get; set; } = [-0.375, -0.125, 0.125, 0.375];
    public bool UseSamples { get; set; } = true;
    public Action? AfterDispose { get; set; }
    public Action<CompositionContext>? AfterReconcile { get; set; }
    public Action<FilterEffectInputSample>? AfterSample { get; set; }
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource) { }

    public partial class Resource
    {
        public FilterEffectInputContext? Input { get; private set; }
        public List<FilterEffectInputSample> Samples { get; } = [];
        public override bool UsesInputEvaluation => RequireOriginal().UseSamples;

        partial void PostReconcile(SampleInputEffect obj, CompositionContext context) => obj.AfterReconcile?.Invoke(context);

        partial void PostDispose(bool disposing) => GetOriginal()?.AfterDispose?.Invoke();

        public override bool PrepareInput(FilterEffectInputContext context)
        {
            Input = context;
            Samples.Clear();
            var original = RequireOriginal();
            foreach (double offset in original.Offsets)
            {
                var sample = context.Evaluate(context.EvaluationContext.Time + MediaTime.FromSeconds(offset / context.EvaluationContext.FrameRate.ToDouble()));
                Samples.Add(sample);
                original.AfterSample?.Invoke(sample);
            }
            return true;
        }

        public override void RenderInput(GraphicsContext2D context, FilterEffectInputContext input)
        {
            foreach (var sample in Samples)
            {
                using (context.PushBlendMode(BlendMode.Plus))
                using (context.PushOpacity(1f / Samples.Count))
                    sample.Draw(context);
            }
        }
    }
}
