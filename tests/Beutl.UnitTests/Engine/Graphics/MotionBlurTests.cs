using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class MotionBlurTests
{
    [TestCase(ShutterPosition.Start, 1.0625)]
    [TestCase(ShutterPosition.Center, 0.8125)]
    [TestCase(ShutterPosition.End, 0.5625)]
    public void SamplesUseTheDeclaredExposureAndKeepIndependentResources(ShutterPosition position, double firstTime)
    {
        var shape = Rectangle();
        var blur = Blur(shape);
        blur.ShutterPosition.Value = position;
        var context = new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) };
        using var resource = (MotionBlur.Resource)blur.ToResource(context);
        var samples = resource.Sampling.Samples;
        var widths = samples.Select(s => ((RectShape.Resource)s.Drawables.Single()).Width).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(widths, Is.EqualTo(Enumerable.Range(0, 4).Select(i => (float)(10 + (firstTime + i * 0.125) * 10))).Within(0.0001));
            Assert.That(samples.Select(s => s.Drawables[0]).Distinct().Count(), Is.EqualTo(4));
            Assert.That(shape.Width.Value, Is.EqualTo(10));
            Assert.That(context.Time, Is.EqualTo(Seconds(1)));
        });
    }

    [TestCase(0, 8)]
    [TestCase(360, 1)]
    public void DisabledSamplingDrawsTheCurrentTime(float angle, int samples)
    {
        var blur = Blur(Rectangle());
        blur.ShutterAngle.Value = angle;
        blur.Samples.Value = samples;
        blur.ShutterPosition.Value = ShutterPosition.End;
        using var resource = (MotionBlur.Resource)blur.ToResource(new CompositionContext(Seconds(1)));
        Assert.That(((RectShape.Resource)resource.Sampling.Samples.Single().Drawables.Single()).Width, Is.EqualTo(20));
    }

    [Test]
    public void RepeatedAndReorderedSeeksReuseSlotsWithoutRememberingFrameHistory()
    {
        var blur = Blur(Rectangle());
        var context = new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) };
        using var resource = (MotionBlur.Resource)blur.ToResource(context);
        Drawable.Resource[] originals = resource.Sampling.Samples.Select(s => s.Drawables[0]).ToArray();
        int version = resource.Version;
        Reconcile(resource, blur, context);
        Assert.That(resource.Version, Is.EqualTo(version), "unchanged subframes remain cacheable");
        foreach (double time in new[] { 1.5, 0.25, 1.25, 1.0 })
        {
            context.Time = Seconds(time);
            Reconcile(resource, blur, context);
        }
        Assert.Multiple(() =>
        {
            Assert.That(resource.Sampling.Samples.Select(s => s.Drawables[0]), Is.EqualTo(originals));
            Assert.That(((RectShape.Resource)originals[0]).Width, Is.EqualTo(18.125).Within(0.0001));
        });
        blur.Samples.Value = 2;
        Reconcile(resource, blur, context);
        Assert.That(originals.Skip(2).All(r => r.IsDisposed), Is.True);
        blur.Children.Clear();
        Reconcile(resource, blur, context);
        Assert.That(originals.All(r => r.IsDisposed), Is.True);
    }

    [Test]
    public void SamplingPreservesFlowTimeMappingAndDoesNotTouchProducerResources()
    {
        TestMediaHelper.RegisterTestDecoder();
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(16, 16, new(30, 1), 300)));
        var video = new SourceVideo { Source = { Value = source } };
        var controller = new DrawableTimeController { OffsetPosition = { Value = Seconds(1) } };
        var blur = Blur();
        var scene = SceneWith(video, new DrawableGroup(), controller, blur);
        using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };
        var resource = (MotionBlur.Resource)compositor.EvaluateGraphics(Seconds(1), default, new(2, 1)).Objects.Single();
        double[] positions = resource.Sampling.Samples.Select(sample =>
        {
            var mapped = (DrawableTimeController.Resource)sample.Drawables.Single();
            return ((SourceVideo.Resource)((DrawableGroup.Resource)mapped.Target!).Children.Single()).RequestedPosition.ToSeconds();
        }).ToArray();
        Assert.That(positions, Is.EqualTo(new[] { 1.8125, 1.9375, 2.0625, 2.1875 }).Within(0.000001));
        using var independent = new SceneCompositor(scene) { ForceOriginalSource = true };
        independent.EvaluateGraphics(Seconds(2));
        var first = (DrawableTimeController.Resource)resource.Sampling.Samples[0].Drawables.Single();
        Assert.That(((SourceVideo.Resource)((DrawableGroup.Resource)first.Target!).Children.Single()).RequestedPosition,
            Is.EqualTo(Seconds(1.8125)), "another evaluation session cannot overwrite these samples");
    }

    [Test]
    public void ClipEdgesContributeTransparencyAndAnExportSnapshotStaysDetached()
    {
        var shape = Rectangle();
        var blur = Blur();
        var scene = SceneWith(shape, blur);
        Scene snapshot = SceneSnapshot.Create(scene);
        using var preview = new SceneCompositor(scene);
        using var export = new SceneCompositor(snapshot) { DisableResourceShare = true, ForceOriginalSource = true };
        var edge = (MotionBlur.Resource)preview.EvaluateGraphics(MediaTime.Zero, default, new(2, 1)).Objects.Single();
        Assert.That(edge.Sampling.Samples.Select(s => s.Drawables.Count), Is.EqualTo(new[] { 0, 0, 1, 1 }));
        shape.Width.SetKeyFrameAnimation(null);
        shape.Width.Value = 80;
        var saved = (MotionBlur.Resource)export.EvaluateGraphics(Seconds(1), default, new(2, 1)).Objects.Single();
        var live = (MotionBlur.Resource)preview.EvaluateGraphics(Seconds(1), default, new(2, 1)).Objects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(((RectShape.Resource)saved.Sampling.Samples[0].Drawables[0]).Width, Is.EqualTo(18.125).Within(0.0001));
            Assert.That(((RectShape.Resource)live.Sampling.Samples[0].Drawables[0]).Width, Is.EqualTo(80));
        });
    }

    [Test]
    public void EvaluationSessionsPreserveDecodePolicyAndCancellationCanBeRetried()
    {
        var blur = Blur(Rectangle());
        var context = new CompositionContext(Seconds(1))
        {
            PreferProxy = true,
            DisableResourceShare = true,
            PreferredProxyPreset = ProxyPreset.Half,
            FrameRate = new(24, 1),
            TargetDomain = new Rect(0, 0, 1920, 1080),
        };
        using var resource = (MotionBlur.Resource)blur.ToResource(context);
        CompositionContext sampled = resource.Sampling.Samples[0].Session.Context;
        Assert.Multiple(() =>
        {
            Assert.That(sampled.PreferProxy, Is.True);
            Assert.That(sampled.DisableResourceShare, Is.True);
            Assert.That(sampled.PreferredProxyPreset, Is.EqualTo(ProxyPreset.Half));
            Assert.That(sampled.TargetDomain, Is.EqualTo(context.TargetDomain));
            Assert.That(sampled.FrameRate, Is.EqualTo(context.FrameRate));
        });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        context.CancellationToken = cancelled.Token;
        Assert.Throws<OperationCanceledException>(() => Reconcile(resource, blur, context));
        context.CancellationToken = default;
        context.PreferProxy = false;
        context.Time = Seconds(0.5);
        Assert.DoesNotThrow(() => Reconcile(resource, blur, context));
        Assert.That(sampled.PreferProxy, Is.False);
    }

    internal static MotionBlur Blur(params Drawable[] children)
    {
        var blur = new MotionBlur { ShutterAngle = { Value = 360 }, Samples = { Value = 4 } };
        foreach (Drawable child in children) blur.Children.Add(child);
        return blur;
    }

    internal static RectShape Rectangle()
    {
        var shape = new RectShape
        {
            Width = { Value = 10 },
            Height = { Value = 12 },
            Fill = { Value = new SolidColorBrush(Colors.White) },
        };
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
        var scene = new Scene(96, 64, "Motion blur") { Uri = new Uri("file:///motion-blur/scene") };
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = Seconds(4) },
            Uri = new Uri("file:///motion-blur/element"),
        };
        foreach (EngineObject obj in objects) element.Objects.Add(obj);
        scene.Children.Add(element);
        return scene;
    }

    private static void Reconcile(MotionBlur.Resource resource, MotionBlur blur, CompositionContext context)
    {
        bool changed = false;
        resource.Reconcile(blur, context, ref changed);
    }

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);
}
