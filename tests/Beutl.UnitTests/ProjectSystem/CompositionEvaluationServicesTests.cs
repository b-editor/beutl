using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class CompositionEvaluationServicesTests
{
    [Test]
    public void LayerQueriesSeekIndependentlyAndReuseAnActiveLease()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(4) }, Value = { Value = 500 } });
        shape.Width.SetKeyFrameAnimation(animation);
        Add(scene, shape, 1);
        using var compositor = new SceneCompositor(scene);
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        ICompositionEvaluationServices services = capture.Services!;
        var original = frame.Objects.OfType<RectShape.Resource>().Single();
        Assert.That(services.GetLayers(MediaTime.Zero).Select(x => x.ZIndex), Is.EqualTo(new[] { 0, 1 }));
        RectShape.Resource? previous = null;
        foreach (int second in new[] { 3, 1, 2 })
        {
            MediaTime time = MediaTime.FromSeconds(second);
            using var lease = services.EvaluateLayer(1, time);
            var resource = (RectShape.Resource)lease.Resources.Single();
            Assert.That(resource.Width, Is.EqualTo(100 + second * 100).Within(0.01));
            Assert.That(services.ReadLayer(1, time, resources => resources.Single()), Is.SameAs(resource));
            Assert.That(original.Width, Is.EqualTo(100), "A query must not reconcile the active preview's resources.");
            if (previous != null)
                Assert.That(resource, Is.SameAs(previous), "A released query is reevaluated in place for the next one.");
            previous = resource;
            lease.Dispose();
            Assert.That(() => lease.Resources, Throws.TypeOf<ObjectDisposedException>());
        }
        Assert.That(services.TryReadFrame(value => value.Objects, out var objects), Is.True);
        Assert.That(objects, Is.EqualTo(frame.Objects));
        Assert.That(original.IsDisposed, Is.False);
        Assert.That(previous!.IsDisposed, Is.False);
        compositor.Dispose();
        Assert.That(previous.IsDisposed, Is.True, "Disposing the owner releases its idle queries.");
    }

    [Test]
    public void IdleLayerQueriesAreReleasedOnceTheyStopBeingUsed()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        Add(scene, new RectShape(), 1);
        using var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        EngineObject.Resource layer;
        using (var lease = capture.Services!.EvaluateLayer(1, MediaTime.Zero)) layer = lease.Resources.Single();
        compositor.EvaluateGraphics(MediaTime.FromSeconds(1));
        compositor.EvaluateGraphics(MediaTime.FromSeconds(2));
        Assert.That(layer.IsDisposed, Is.False, "A query survives two frames without use.");
        compositor.EvaluateGraphics(MediaTime.FromSeconds(3));
        Assert.That(layer.IsDisposed, Is.True, "The next frame releases a query that stopped.");
    }

    [Test]
    public void LayerQueryUsesTheSamePortalFlowAsTheCompleteFrame()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        var group = new DrawableGroup();
        Element container = Add(scene, group, 1);
        ((PortalObject)container.Objects[0]).Count.Value = 1;
        var child = new RectShape();
        Add(scene, child, 2);
        using var compositor = new SceneCompositor(scene);
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        var original = frame.Objects.OfType<DrawableGroup.Resource>().Single();
        Assert.That(original.Children.Select(x => x.GetOriginal()), Does.Contain(child));
        using var layer = capture.Services!.EvaluateLayer(1, MediaTime.Zero);
        var queried = layer.Resources.OfType<DrawableGroup.Resource>().Single();
        Assert.That(queried.Children.Select(x => x.GetOriginal()), Does.Contain(child));
        Assert.That(queried.Children.Single(), Is.Not.SameAs(original.Children.Single()));
        Assert.That(capture.Services.TryReadFrame(value => value.Objects, out var objects), Is.True);
        Assert.That(objects, Is.EqualTo(frame.Objects), "A dependent query cannot replace its owning frame.");
    }

    [Test]
    public void FailedRecursiveQueryReleasesItsPathAndDoesNotPoisonAnotherSession()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        using var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        ICompositionEvaluationServices services = capture.Services!;
        capture.OnEvaluate = context => context.EvaluationServices!.ReadLayer(0, context.Time, resources => resources.Count);
        Assert.That(() => services.EvaluateLayer(0, MediaTime.Zero),
            Throws.InvalidOperationException.With.Message.Contains("Circular composition query"));
        capture.OnEvaluate = null;
        using var layer = services.EvaluateLayer(0, MediaTime.Zero);
        using var other = new SceneCompositor(scene) { ForceOriginalSource = true };
        other.EvaluateGraphics(MediaTime.Zero);
        ICompositionEvaluationServices independent = capture.Services!;
        Assert.That(independent.SessionIdentity, Is.Not.SameAs(services.SessionIdentity));
        using var otherLayer = independent.EvaluateLayer(0, MediaTime.Zero);
        Assert.That(otherLayer.Resources.Single(), Is.Not.SameAs(layer.Resources.Single()));
        Assert.That(capture.PreferProxy, Is.False);
        compositor.EvaluateGraphics(MediaTime.FromSeconds(1));
        Assert.That(capture.Services!.SessionIdentity, Is.SameAs(services.SessionIdentity));
        Assert.That(capture.Services.FrameIdentity, Is.Not.SameAs(services.FrameIdentity));
    }

    [Test]
    public void CompletedLayerLeaseDoesNotBlockAnIndependentInputSample()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        Add(scene, new RectShape { Width = { Value = 42 } }, 1);
        capture.FilterEffect.Value = new SampleInputEffect { Offsets = [1] };
        CompositionLayerLease? retained = null;
        EngineObject.Resource? retainedResource = null;
        int reads = 0;
        capture.OnEvaluate = context =>
        {
            if (context.Time == MediaTime.Zero)
            {
                retained = context.EvaluationServices!.EvaluateLayer(1, MediaTime.Zero);
                retainedResource = retained.Resources.Single();
            }
            else
            {
                context.EvaluationServices!.ReadLayer(1, MediaTime.Zero, resources =>
                {
                    var sampled = (RectShape.Resource)resources.Single();
                    Assert.That(sampled.Width, Is.EqualTo(42));
                    Assert.That(sampled, Is.Not.SameAs(retainedResource));
                    reads++;
                    return 0;
                });
            }
        };
        using var compositor = new SceneCompositor(scene);
        try
        {
            compositor.EvaluateGraphics(MediaTime.Zero);
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(retainedResource!.IsDisposed, Is.False);
        }
        finally { retained?.Dispose(); }
        Assert.That(() => retained!.Resources, Throws.TypeOf<ObjectDisposedException>());
        compositor.Dispose();
        Assert.That(retainedResource!.IsDisposed, Is.True);
    }

    [Test]
    public void OverlappingLayerLeasesDoNotRemoveAnotherEvaluationsCacheOrCycleGuard()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        using var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        var services = capture.Services!;
        using var first = services.EvaluateLayer(0, MediaTime.Zero);
        using var second = services.EvaluateLayer(0, MediaTime.Zero);
        var resource = second.Resources.Single();
        Assert.That(resource, Is.Not.SameAs(first.Resources.Single()));
        first.Dispose();
        Assert.That(resource.IsDisposed, Is.False);
        Assert.That(services.ReadLayer(0, MediaTime.Zero, resources => resources.Single()), Is.SameAs(resource));

        int evaluations = 0;
        capture.OnEvaluate = context =>
        {
            if (++evaluations > 1) throw new InvalidOperationException("active query guard was removed");
            second.Dispose();
            using var recursive = context.EvaluationServices!.EvaluateLayer(0, MediaTime.Zero);
        };
        Assert.That(() => services.EvaluateLayer(0, MediaTime.Zero),
            Throws.InvalidOperationException.With.Message.Contains("Circular composition query"));
        Assert.That(evaluations, Is.EqualTo(1));
        Assert.That(() => second.Resources, Throws.TypeOf<ObjectDisposedException>());
        capture.OnEvaluate = null;
        using var recovered = services.EvaluateLayer(0, MediaTime.Zero);
        Assert.That(recovered.Resources.Single().IsDisposed, Is.False);
    }

    [Test]
    public void SampledLayerQueriesShareCycleDetectionAndReleaseItAfterFailure()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        capture.FilterEffect.Value = new SampleInputEffect { Offsets = [1] };
        int queries = 0;
        capture.OnEvaluate = context =>
        {
            if (context.Time <= MediaTime.Zero) return;
            // Bound the regression on the broken implementation instead of exhausting the stack.
            if (++queries > 4) throw new InvalidOperationException("cycle guard was reset");
            context.EvaluationServices!.ReadLayer(0, MediaTime.Zero, resources => resources.Count);
        };
        using var compositor = new SceneCompositor(scene);
        Assert.That(() => compositor.EvaluateGraphics(MediaTime.Zero),
            Throws.InvalidOperationException.With.Message.Contains("Circular composition query"));
        Assert.That(queries, Is.EqualTo(2));
        capture.OnEvaluate = null;
        compositor.EvaluateGraphics(MediaTime.Zero);
        using (capture.Services!.EvaluateLayer(0, MediaTime.Zero)) { }

        var target = new RectShape();
        Add(scene, target, 1);
        var queried = new List<EngineObject.Resource>();
        capture.OnEvaluate = context =>
        {
            if (context.Time <= MediaTime.Zero) return;
            using var layer = context.EvaluationServices!.EvaluateLayer(1, MediaTime.Zero);
            queried.Add(layer.Resources.Single());
        };
        compositor.EvaluateGraphics(MediaTime.Zero);
        using var independent = new SceneCompositor(scene) { ForceOriginalSource = true };
        independent.EvaluateGraphics(MediaTime.Zero);
        Assert.That(queried.Count, Is.EqualTo(2));
        Assert.That(queried[0], Is.Not.SameAs(queried[1]));
        compositor.Dispose();
        independent.Dispose();
        Assert.That(queried.All(resource => resource.IsDisposed), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RecursiveAudioQueriesShareThePathAndCanRetry(bool throughLayer)
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        var sound = new EvaluationCaptureSound();
        Add(scene, sound, 1);
        using var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        var services = capture.Services!;
        MediaTime duration = MediaTime.FromSeconds(0.01);
        int audioEvaluations = 0;
        sound.OnEvaluate = context =>
        {
            if (++audioEvaluations > 4) throw new InvalidOperationException("audio cycle guard was reset");
            if (throughLayer)
                context.EvaluationServices!.ReadLayer(0, MediaTime.Zero, resources => resources.Count);
            else
                using (context.EvaluationServices!.ReadAudio(MediaTime.Zero, duration)) { }
        };
        if (throughLayer)
            capture.OnEvaluate = context =>
            {
                using var audio = context.EvaluationServices!.ReadAudio(MediaTime.Zero, duration);
            };
        Assert.That(() => services.ReadAudio(MediaTime.Zero, duration),
            Throws.InvalidOperationException.With.Message.Contains("Circular composition query"));
        Assert.That(audioEvaluations, Is.EqualTo(1));
        sound.OnEvaluate = null;
        capture.OnEvaluate = null;
        using var recovered = services.ReadAudio(MediaTime.Zero, duration);
        Assert.That(recovered, Is.Not.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public void SampledAudioQueriesPreserveTheHostSourcePolicy(bool forceOriginalSource)
    {
        PreviewSourceMode previous = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = PreviewSourceMode.PreferProxy;
            (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
            var sound = new EvaluationCaptureSound();
            Add(scene, sound, 1);
            capture.FilterEffect.Value = new SampleInputEffect { Offsets = [1] };
            capture.OnEvaluate = context =>
            {
                if (context.Time <= MediaTime.Zero) return;
                using var audio = context.EvaluationServices!.ReadAudio(MediaTime.Zero, MediaTime.FromSeconds(0.01));
                Assert.That(audio, Is.Not.Null);
            };
            using var compositor = new SceneCompositor(scene) { ForceOriginalSource = forceOriginalSource };
            compositor.EvaluateGraphics(MediaTime.Zero);
            Assert.That(sound.PreferProxy, Is.EqualTo(!forceOriginalSource));
            Assert.That(capture.PreferProxy, Is.EqualTo(!forceOriginalSource));
        }
        finally { GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = previous; }
    }

    [Test]
    public void ReusedLayerQueriesKeepOneSubscriptionAndReleaseItWithFailuresAndTheOwner()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        var field = typeof(Hierarchical).GetField("DetachedFromHierarchy",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        int Subscriptions() => ((Delegate?)field.GetValue(capture))?.GetInvocationList().Length ?? 0;
        int initial = Subscriptions();
        var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        var services = capture.Services!;
        int before = Subscriptions();
        for (int i = 0; i < 4; i++)
        {
            using (services.EvaluateLayer(0, MediaTime.FromSeconds(i))) { }
            Assert.That(Subscriptions(), Is.EqualTo(before + 1), "the released query is reused, not recreated");
        }
        capture.OnEvaluate = _ => throw new InvalidOperationException("query failed");
        Assert.That(() => services.EvaluateLayer(0, MediaTime.Zero), Throws.InvalidOperationException);
        Assert.That(Subscriptions(), Is.EqualTo(before), "a failed query is discarded");
        capture.OnEvaluate = null;
        using (services.EvaluateLayer(0, MediaTime.Zero)) { }
        Assert.That(Subscriptions(), Is.EqualTo(before + 1));
        compositor.Dispose();
        Assert.That(Subscriptions(), Is.EqualTo(initial));
    }

    [Test]
    public void AudioQueriesReevaluateTheirSoundsInPlace()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        var sound = new EvaluationCaptureSound();
        Add(scene, sound, 1);
        using var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        MediaTime duration = MediaTime.FromSeconds(0.01);
        using (capture.Services!.ReadAudio(MediaTime.Zero, duration)) { }
        Sound.Resource? first = sound.LastResource;
        using (capture.Services.ReadAudio(duration, duration)) { }
        Assert.That(first, Is.Not.Null);
        Assert.That(sound.LastResource, Is.SameAs(first), "the next range continues on the same composer");
    }

    [Test]
    public void AudioQueriesOwnTheirBuffersAndAreIndependentOfSeekOrder()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        Add(scene, new Beutl.UnitTests.Engine.Audio.LimiterTailSound { LimiterBeforeClip = false }, 1);
        using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };
        compositor.EvaluateGraphics(MediaTime.Zero);
        var services = capture.Services!;
        MediaTime duration = MediaTime.FromSeconds(0.01);
        using var first = services.ReadAudio(MediaTime.FromSeconds(1), duration);
        using var earlier = services.ReadAudio(MediaTime.Zero, duration);
        using var repeated = services.ReadAudio(MediaTime.FromSeconds(1), duration);
        Assert.That(first, Is.Not.Null);
        Assert.That(earlier, Is.Not.Null);
        Assert.That(repeated, Is.Not.Null);
        Assert.That(first!.GetChannelData(0).ToArray(), Is.EqualTo(repeated!.GetChannelData(0).ToArray()));
        Assert.That(first.GetChannelData(0).ToArray().Any(value => value != 0), Is.True);
        first.GetChannelData(0).Clear();
        Assert.That(repeated.GetChannelData(0).ToArray().Any(value => value != 0), Is.True);
        Assert.That(services.ReadAudio(MediaTime.Zero, MediaTime.Zero), Is.Null);
    }

    private static (Scene, EvaluationCaptureDrawable) CreateScene()
    {
        var scene = new Scene(320, 240, string.Empty);
        var application = new BeutlApplication { Project = { Value = new Project() } };
        application.Project.Value!.Items.Add(scene);
        var capture = new EvaluationCaptureDrawable();
        Add(scene, capture, 0);
        return (scene, capture);
    }

    private static Element Add(Scene scene, EngineObject obj, int layer)
    {
        var element = new Element { Length = { Value = MediaTime.FromSeconds(5) }, ZIndex = { Value = layer } };
        element.AddObject(obj);
        scene.Children.Add(element);
        return element;
    }
}

internal sealed partial class EvaluationCaptureDrawable : Drawable
{
    public ICompositionEvaluationServices? Services { get; private set; }
    public bool PreferProxy { get; private set; }
    public Action<CompositionContext>? OnEvaluate { get; set; }

    public partial class Resource
    {
        protected override Size MeasureCore(Size availableSize) => Size.Empty;
        protected override void OnDraw(GraphicsContext2D context) { }

        partial void PostReconcile(EvaluationCaptureDrawable obj, CompositionContext context)
        {
            obj.Services = context.EvaluationServices;
            obj.PreferProxy = context.PreferProxy;
            obj.OnEvaluate?.Invoke(context);
        }
    }
}

internal sealed partial class EvaluationCaptureSound : Sound
{
    public EvaluationCaptureSound() => ScanProperties<EvaluationCaptureSound>();
    public bool? PreferProxy { get; private set; }
    public Sound.Resource? LastResource { get; private set; }
    public Action<CompositionContext>? OnEvaluate { get; set; }
    public override void Compose(AudioContext context, Sound.Resource resource) { }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
        partial void PostReconcile(EvaluationCaptureSound obj, CompositionContext context)
        {
            obj.LastResource = this;
            obj.PreferProxy = context.PreferProxy;
            obj.OnEvaluate?.Invoke(context);
        }
    }
}
