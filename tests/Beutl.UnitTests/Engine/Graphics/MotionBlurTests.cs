using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Rendering;
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
    [TestCase(ShutterPosition.Start, 720, 2, 1.125, 0.25)]
    [TestCase(ShutterPosition.Center, 720, 2, 0.625, 0.25)]
    [TestCase(ShutterPosition.End, 720, 2, 0.125, 0.25)]
    [TestCase(ShutterPosition.Center, 3600, 20, 0.8125, 0.125)]
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

    [TestCase(0, 8, true, 35)]
    [TestCase(360, 1, true, 35)]
    [TestCase(360, 4, false, 35)]
    [TestCase(360, 4, true, 100)]
    public void DisablingSamplingReleasesSamplesAndCanBeReenabled(float angle, int samples, bool enabled, float sourceMix)
    {
        var shape = Rectangle();
        var blur = Blur();
        blur.SourceMix.Value = 35;
        shape.FilterEffect.Value = blur;
        var context = new CompositionContext(Seconds(1));
        using var resource = shape.ToResource(context);
        var effect = (MotionBlur.Resource)resource.FilterEffect!;
        var previous = effect.TimeSamples.ToArray();
        blur.ShutterAngle.Value = angle;
        blur.Samples.Value = samples;
        blur.IsEnabled.Value = enabled;
        blur.SourceMix.Value = sourceMix;
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Is.Empty);
        Assert.That(previous.All(s => s.IsDisposed), Is.True);
        Assert.That(resource.Width, Is.EqualTo(20));
        blur.ShutterAngle.Value = 360;
        blur.Samples.Value = 4;
        blur.IsEnabled.Value = true;
        blur.SourceMix.Value = 0;
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Has.Count.EqualTo(4));
    }

    [Test]
    public void AnimatedSourceMixCanSwitchBetweenSamplingAndTheCurrentInput()
    {
        var shape = Rectangle();
        var blur = Blur();
        blur.Samples.Value = 3; // The center sample also supplies the current-time input.
        Animate(blur.SourceMix, 0, 100);
        shape.FilterEffect.Value = blur;
        var context = new CompositionContext(Seconds(1));
        using var resource = shape.ToResource(context);
        var effect = (MotionBlur.Resource)resource.FilterEffect!;
        var previous = effect.TimeSamples.ToArray();

        context.Time = Seconds(2);
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Is.Empty);
        Assert.That(previous.All(s => s.IsDisposed), Is.True);

        foreach (double time in new[] { 0, 0.5, 1.0 })
        {
            context.Time = Seconds(time);
            Reconcile(resource, shape, context);
            Assert.That(effect.SourceMix, Is.EqualTo(time * 50).Within(0.0001));
            Assert.That(effect.TimeSamples, Has.Count.EqualTo(3));
            Assert.That(effect.TimeSamples[1].Time, Is.EqualTo(context.Time));
        }
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

    [TestCase(false)]
    [TestCase(true)]
    public void ReferencedGroupKeepsItsInputsWhenItsAuthoredElementChangesVisibility(bool portal)
    {
        var preceding = Rectangle();
        var child = Rectangle();
        var group = new DrawableGroup { FilterEffect = { Value = Blur() } };
        group.Children.Add(child);
        var holder = Clip(0, 4, 2, preceding, group);
        var presenter = new DrawablePresenter { Target = { Value = group } };
        Scene scene = portal ? SceneWith(new PortalObject { Count = { Value = 1 } }, presenter) : SceneWith(presenter);
        scene.Children.Add(holder);
        var portalInput = Rectangle();
        scene.Children.Add(Clip(0.5, 0.5, 1, portalInput));
        using var compositor = new SceneCompositor(scene);

        foreach (bool enabled in new[] { false, true, false })
        {
            holder.IsEnabled.Value = enabled;
            var frame = compositor.EvaluateGraphics(Seconds(1.05), new Rational(2, 1));
            var reference = frame.Objects.OfType<DrawablePresenter.Resource>().Single();
            AssertInputs((DrawableGroup.Resource)reference.Target!, portal, child);
            if (enabled) AssertInputs(frame.Objects.OfType<DrawableGroup.Resource>().Single(), false, preceding, child);
        }

        void AssertInputs(DrawableGroup.Resource source, bool includesPortal, params Drawable[] expected)
        {
            Assert.That(source.Children.Select(c => c.RequireOriginal()), Is.EqualTo(expected));
            foreach (var sample in ((MotionBlur.Resource)source.FilterEffect!).TimeSamples)
            {
                IEnumerable<Drawable> sampleInputs = includesPortal && sample.Time < Seconds(1)
                    ? new[] { portalInput }.Concat(expected) : expected;
                Assert.That(((DrawableGroup.Resource)sample.Resource!).Children.Select(c => c.RequireOriginal()),
                    Is.EqualTo(sampleInputs), "sampling preserves the caller's input path, including sample-time portal clips");
            }
        }
    }

    [Test]
    public void StoredChildDoesNotReplayTheFlowAlreadyConsumedByItsParent()
    {
        var sibling = Rectangle();
        var content = Rectangle();
        var child = new DrawableGroup { FilterEffect = { Value = Blur() }, Children = { content } };
        var parent = new DrawableGroup { Children = { child } };
        using var compositor = new SceneCompositor(SceneWith(sibling, parent));
        var frame = compositor.EvaluateGraphics(Seconds(1), new Rational(2, 1));
        var container = frame.Objects.OfType<DrawableGroup.Resource>().Single();
        var source = container.Children.OfType<DrawableGroup.Resource>().Single();
        Assert.That(container.Children.Select(c => c.RequireOriginal()), Is.EqualTo(new Drawable[] { sibling, child }));
        Assert.That(source.Children.Select(c => c.RequireOriginal()), Is.EqualTo(new[] { content }));
        foreach (var sample in ((MotionBlur.Resource)source.FilterEffect!).TimeSamples)
            Assert.That(((DrawableGroup.Resource)sample.Resource!).Children.Select(c => c.RequireOriginal()), Is.EqualTo(new[] { content }));
    }

    [Test]
    public void PortalSamplesReleasePreviousClipsAndReuseActiveReaders()
    {
        TestMediaHelper.RegisterTestDecoder();
        string path = TestMediaHelper.CreateTestVideoFile(16, 16, new Rational(30, 1), 60);
        var blur = Blur();
        var group = new DrawableGroup { FilterEffect = { Value = blur } };
        Scene scene = SceneWith(new PortalObject { Count = { Value = 1 } }, group);
        scene.Children[0].Length.Value = Seconds(12);
        for (int i = 0; i < 10; i++)
        {
            var media = new VideoSource();
            media.ReadFrom(new Uri(path));
            scene.Children.Add(Clip(i, 1, 1, new SourceVideo { Source = { Value = media } }));
        }
        using var compositor = new SceneCompositor(scene) { ForceOriginalSource = true };
        var visited = new HashSet<SourceVideo.Resource>();
        foreach (int i in Enumerable.Range(0, 10).Concat([0, 5, 9]))
        {
            SourceVideo.Resource[] current = Samples(i + 0.5);
            visited.UnionWith(current);
            Assert.That(visited.Count(r => !r.IsDisposed), Is.EqualTo(blur.Samples.Value), "ended clips release their sample resources");
            var readers = current.Select(r => r.Source!.MediaReader).ToArray();
            Assert.That(readers, Is.All.Not.Null);
            Assert.That(readers.Distinct().Count(), Is.EqualTo(blur.Samples.Value));

            SourceVideo.Resource[] next = Samples(i + 0.51);
            for (int j = 0; j < current.Length; j++)
            {
                Assert.That(next[j], Is.SameAs(current[j]));
                Assert.That(next[j].Source!.MediaReader, Is.SameAs(readers[j]), "active readers survive the next frame");
            }
        }
        group.FilterEffect.Value = null;
        compositor.EvaluateGraphics(Seconds(9.5), new Rational(2, 1));
        Assert.That(visited.All(r => r.IsDisposed), Is.True);

        SourceVideo.Resource[] Samples(double time)
        {
            var frame = compositor.EvaluateGraphics(Seconds(time), new Rational(2, 1));
            var source = frame.Objects.OfType<DrawableGroup.Resource>().Single();
            return ((MotionBlur.Resource)source.FilterEffect!).TimeSamples
                .Select(sample => ((DrawableGroup.Resource)sample.Resource!).Children.OfType<SourceVideo.Resource>().Single()).ToArray();
        }
    }

    [Test]
    public void SerializationKeepsTheFilterInItsEffectChain()
    {
        var shape = Rectangle();
        var blur = Blur();
        blur.ShutterPosition.Value = ShutterPosition.End;
        blur.ShutterAngle.Value = 720;
        blur.SourceMix.Value = 35;
        var effects = new FilterEffectGroup();
        effects.Children.Add(new Blur());
        effects.Children.Add(blur);
        effects.Children.Add(new Brightness());
        shape.FilterEffect.Value = effects;
        var restored = (RectShape)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(shape), typeof(RectShape));
        var chain = (FilterEffectGroup)restored.FilterEffect.Value!;
        var actual = (MotionBlur)chain.Children[1];
        Assert.That(chain.Children.Select(x => x.GetType()), Is.EqualTo(new[] { typeof(Blur), typeof(MotionBlur), typeof(Brightness) }));
        Assert.That(actual.ShutterAngle.Value, Is.EqualTo(720));
        Assert.That(actual.ShutterPosition.Value, Is.EqualTo(ShutterPosition.End));
        Assert.That(actual.Samples.Value, Is.EqualTo(4));
        Assert.That(actual.SourceMix.Value, Is.EqualTo(35));
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
        var element = Clip(0, 4, 0, objects);
        element.Uri = new Uri("file:///motion-blur/element");
        scene.Children.Add(element);
        return scene;
    }

    internal static Element Clip(double start, double duration, int zIndex, params EngineObject[] objects)
    {
        var element = new Element
        {
            Start = { Value = Seconds(start) },
            Length = { Value = Seconds(duration) },
            ZIndex = { Value = zIndex },
        };
        foreach (EngineObject obj in objects) element.Objects.Add(obj);
        return element;
    }

    private static void Reconcile(Drawable.Resource resource, Drawable shape, CompositionContext context)
    {
        bool changed = false;
        resource.Reconcile(shape, context, ref changed);
    }

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);
}
