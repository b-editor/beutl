using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class MotionBlurTests
{
    [TestCase(ShutterPosition.Start, 360, 2, 1.0625, 0.125)]
    [TestCase(ShutterPosition.Center, 360, 2, 0.8125, 0.125)]
    [TestCase(ShutterPosition.End, 360, 2, 0.5625, 0.125)]
    [TestCase(ShutterPosition.Center, 180, 2, 0.90625, 0.0625)]
    [TestCase(ShutterPosition.Center, 360, 4, 0.90625, 0.0625)]
    public void SamplesFollowShutterSettingsAndOutputFrameRate(
        ShutterPosition position, float angle, int frameRate, double first, double interval)
    {
        var shape = Rectangle();
        var blur = Blur();
        blur.ShutterPosition.Value = position;
        blur.ShutterAngle.Value = angle;
        shape.FilterEffect.Value = blur;
        var context = new CompositionContext(Seconds(1)) { FrameRate = new(frameRate, 1) };
        using var resource = shape.ToResource(context);
        var samples = ((MotionBlur.Resource)resource.FilterEffect!).TimeSamples;
        Assert.Multiple(() =>
        {
            Assert.That(samples.Select(s => s.Time.ToSeconds()),
                Is.EqualTo(Enumerable.Range(0, 4).Select(i => first + i * interval)).Within(0.000001));
            Assert.That(samples.Select(s => ((RectShape.Resource)s.Resource!).Width),
                Is.EqualTo(Enumerable.Range(0, 4).Select(i => (float)(10 + (first + i * interval) * 10))).Within(0.0001));
            Assert.That(samples.Select(s => s.Resource).Distinct().Count(), Is.EqualTo(4));
            Assert.That(shape.Width.Value, Is.EqualTo(10));
            Assert.That(shape.FilterEffect.Value, Is.SameAs(blur));
            Assert.That(context.Time, Is.EqualTo(Seconds(1)));
        });
    }

    [TestCase(0, 8, true)]
    [TestCase(360, 1, true)]
    [TestCase(360, 4, false)]
    public void DisablingSamplingReleasesSamplesAndCanBeReenabled(float angle, int samples, bool enabled)
    {
        var shape = Rectangle();
        var blur = Blur();
        shape.FilterEffect.Value = blur;
        var context = new CompositionContext(Seconds(1));
        using var resource = shape.ToResource(context);
        var effect = (MotionBlur.Resource)resource.FilterEffect!;
        var previous = effect.TimeSamples.ToArray();
        blur.ShutterAngle.Value = angle;
        blur.Samples.Value = samples;
        blur.IsEnabled.Value = enabled;
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Is.Empty);
        Assert.That(previous.All(s => s.IsDisposed), Is.True);
        Assert.That(resource.Width, Is.EqualTo(20));
        blur.ShutterAngle.Value = 360;
        blur.Samples.Value = 4;
        blur.IsEnabled.Value = true;
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Has.Count.EqualTo(4));
    }

    [Test]
    public void ChangingSampleCountAndAnimatedShutterRefreshesTheSchedule()
    {
        var shape = Rectangle();
        var blur = Blur();
        Animate(blur.ShutterAngle, 0, 360);
        shape.FilterEffect.Value = blur;
        var context = new CompositionContext(Seconds(1)) { FrameRate = new(2, 1) };
        using var resource = shape.ToResource(context);
        var effect = (MotionBlur.Resource)resource.FilterEffect!;
        Assert.That(Times(), Is.EqualTo(new[] { 0.90625, 0.96875, 1.03125, 1.09375 }).Within(0.000001));

        blur.Samples.Value = 2;
        Reconcile(resource, shape, context);
        Assert.That(Times(), Is.EqualTo(new[] { 0.9375, 1.0625 }).Within(0.000001));
        context.Time = Seconds(1.5);
        Reconcile(resource, shape, context);
        Assert.That(Times(), Is.EqualTo(new[] { 1.40625, 1.59375 }).Within(0.000001));
        context.Time = Seconds(1);
        Reconcile(resource, shape, context);
        Assert.That(Times(), Is.EqualTo(new[] { 0.9375, 1.0625 }).Within(0.000001));

        double[] Times() => effect.TimeSamples.Select(s => s.Time.ToSeconds()).ToArray();
    }

    [Test]
    public void SceneNodeSamplesInheritTheGraphRateAndCancellation()
    {
        RectShape shape = Rectangle();
        shape.FilterEffect.Value = Blur();
        Scene referenced = SceneWith(shape);
        var node = new SceneNode();
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        Scene host = SceneWith(new NodeGraphDrawable { Model = { Value = graph } });
        node.Object.Value.ReferencedScene.Expression = SceneReferenceTestHelper.Create(host, referenced);
        node.Time.Property!.SetValue(Seconds(1));
        using var cancellation = new CancellationTokenSource();
        var context = new CompositionContext(Seconds(2))
        {
            FrameRate = new(60, 1),
            CancellationToken = cancellation.Token,
        };
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, context);
        snapshot.Evaluate(context);
        var resource = (SceneNode.Resource)snapshot.GetResource(snapshot.FindSlotIndex(node))!;
        var source = resource.SceneResource!.Frame!.Value.Objects.OfType<RectShape.Resource>().Single();
        var samples = ((MotionBlur.Resource)source.FilterEffect!).TimeSamples;
        Assert.That(samples.Select(s => s.Time.ToSeconds()),
            Is.EqualTo(Enumerable.Range(0, 4).Select(i => 1 + ((i + 0.5) / 4 - 0.5) / 60)).Within(0.000001));
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => samples[0].CreateRenderNode(new(96, 64)));
    }

    [Test]
    public void SerializationKeepsTheFilterInItsEffectChain()
    {
        var shape = Rectangle();
        var blur = Blur();
        blur.ShutterPosition.Value = ShutterPosition.End;
        var effects = new FilterEffectGroup();
        effects.Children.Add(new Blur());
        effects.Children.Add(blur);
        effects.Children.Add(new Brightness());
        shape.FilterEffect.Value = effects;
        var restored = (RectShape)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(shape), typeof(RectShape));
        var chain = (FilterEffectGroup)restored.FilterEffect.Value!;
        var actual = (MotionBlur)chain.Children[1];
        Assert.That(chain.Children.Select(x => x.GetType()), Is.EqualTo(new[] { typeof(Blur), typeof(MotionBlur), typeof(Brightness) }));
        Assert.That(actual.ShutterAngle.Value, Is.EqualTo(360));
        Assert.That(actual.ShutterPosition.Value, Is.EqualTo(ShutterPosition.End));
        Assert.That(actual.Samples.Value, Is.EqualTo(4));
    }

    internal static MotionBlur Blur() => new() { ShutterAngle = { Value = 360 }, Samples = { Value = 4 } };

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

    private static void Reconcile(Drawable.Resource resource, Drawable shape, CompositionContext context)
    {
        bool changed = false;
        resource.Reconcile(shape, context, ref changed);
    }

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);
}
