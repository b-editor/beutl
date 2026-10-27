using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

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
        foreach (int second in new[] { 3, 1, 2 })
        {
            MediaTime time = MediaTime.FromSeconds(second);
            using var lease = services.EvaluateLayer(1, time);
            var resource = (RectShape.Resource)lease.Resources.Single();
            Assert.That(resource.Width, Is.EqualTo(100 + second * 100).Within(0.01));
            Assert.That(services.ReadLayer(1, time, resources => resources.Single()), Is.SameAs(resource));
            Assert.That(original.Width, Is.EqualTo(100), "A query must not reconcile the active preview's resources.");
            lease.Dispose();
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(() => lease.Resources, Throws.TypeOf<ObjectDisposedException>());
        }
        Assert.That(services.TryReadFrame(value => value.Objects, out var objects), Is.True);
        Assert.That(objects, Is.EqualTo(frame.Objects));
        Assert.That(original.IsDisposed, Is.False);
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
    public void DisposedLayerQueriesReleaseObjectSubscriptionsIncludingFailedEvaluation()
    {
        (Scene scene, EvaluationCaptureDrawable capture) = CreateScene();
        using var compositor = new SceneCompositor(scene);
        compositor.EvaluateGraphics(MediaTime.Zero);
        var services = capture.Services!;
        var field = typeof(Hierarchical).GetField("DetachedFromHierarchy",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        int Subscriptions() => ((Delegate?)field.GetValue(capture))?.GetInvocationList().Length ?? 0;
        int before = Subscriptions();
        for (int i = 0; i < 4; i++)
        {
            using (services.EvaluateLayer(0, MediaTime.FromSeconds(i))) { }
            Assert.That(Subscriptions(), Is.EqualTo(before));
        }
        capture.OnEvaluate = _ => throw new InvalidOperationException("query failed");
        Assert.That(() => services.EvaluateLayer(0, MediaTime.Zero), Throws.InvalidOperationException);
        Assert.That(Subscriptions(), Is.EqualTo(before));
        capture.OnEvaluate = null;
        using (services.EvaluateLayer(0, MediaTime.Zero)) { }
        Assert.That(Subscriptions(), Is.EqualTo(before));
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

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;
    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource) { }

    public partial class Resource
    {
        partial void PostReconcile(EvaluationCaptureDrawable obj, CompositionContext context)
        {
            obj.Services = context.EvaluationServices;
            obj.PreferProxy = context.PreferProxy;
            obj.OnEvaluate?.Invoke(context);
        }
    }
}
