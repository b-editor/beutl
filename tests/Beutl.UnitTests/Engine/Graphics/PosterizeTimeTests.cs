using Beutl.Composition;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public sealed class PosterizeTimeTests
{
    [TestCase(2f, 2, 1, 1)]
    [TestCase(0.01f, 1, 100, 1)]
    [TestCase(0.1f, 1, 10, 1)]
    [TestCase(29.97f, 2997, 100, 2)]
    [TestCase(240f, 240, 1, 1)]
    public void SamplesSwitchAtTheElementRelativeBoundary(float fps, int numerator, int denominator, int frame)
    {
        var shape = new RectShape { FilterEffect = { Value = new PosterizeTime { FrameRate = { Value = fps } } } };
        var element = new Element
        {
            Start = { Value = MediaTime.FromSeconds(0.3) },
            Length = { Value = MediaTime.FromSeconds(102) },
            Objects = { shape },
        };
        var rate = new Rational(numerator, denominator);
        var flick = new MediaTime(1);
        MediaTime earlier = element.Start.Value + MediaTime.FromFrames(frame - 1, rate);
        MediaTime boundary = element.Start.Value + MediaTime.FromFrames(frame, rate);
        var context = new CompositionContext(boundary - flick);
        using var resource = shape.ToResource(context);
        var effect = (PosterizeTime.Resource)resource.FilterEffect!;
        FilterEffectInputSample first = effect.Sample!;
        var sampledDrawable = first.Resource;
        Assert.That(first.Time, Is.EqualTo(earlier));

        Seek(boundary, boundary);
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(effect.Sample!.Resource, Is.SameAs(sampledDrawable));
        Seek(boundary + flick, boundary);
        Seek(boundary - flick, earlier);

        void Seek(MediaTime time, MediaTime expected)
        {
            Reconcile(resource, shape, context, time);
            Assert.That(effect.Sample!.Time, Is.EqualTo(expected));
        }
    }

    [Test]
    public void MovingTheElementKeepsTheHeldAnimationAtTheSameLocalTime()
    {
        RectShape shape = MotionBlurTests.Rectangle();
        shape.FilterEffect.Value = new PosterizeTime { FrameRate = { Value = 2 } };
        Scene scene = MotionBlurTests.SceneWith(shape);
        var project = new Project();
        new TestHierarchicalRoot().HierarchicalChildren.Add(project);
        project.Items.Add(scene);
        Element element = scene.Children[0];
        element.Start.Value = MediaTime.FromSeconds(0.3);
        var context = new CompositionContext(MediaTime.FromSeconds(1.1));
        using var resource = shape.ToResource(context);
        var effect = (PosterizeTime.Resource)resource.FilterEffect!;
        float width = ((RectShape.Resource)effect.Sample!.Resource!).Width;
        Assert.That(effect.Sample.Time, Is.EqualTo(MediaTime.FromSeconds(0.8)));
        Assert.That(width, Is.EqualTo(15f));

        element.Start.Value = MediaTime.FromSeconds(1.3);
        Reconcile(resource, shape, context, MediaTime.FromSeconds(2.1));
        Assert.That(effect.Sample!.Time, Is.EqualTo(MediaTime.FromSeconds(1.8)));
        Assert.That(((RectShape.Resource)effect.Sample.Resource!).Width, Is.EqualTo(width));
    }

    [Test]
    public void RestoresTheFractionalRateAndKeyframesWhenLoaded()
    {
        var effect = new PosterizeTime { FrameRate = { Value = 29.97f } };
        MotionBlurTests.Animate(effect.FrameRate, 1, 2);
        var shape = new RectShape { FilterEffect = { Value = effect } };
        var restored = (RectShape)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(shape), typeof(RectShape));
        Assert.That(((PosterizeTime)restored.FilterEffect.Value!).FrameRate.Value, Is.EqualTo(29.97f));

        using var resource = restored.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        var evaluated = (PosterizeTime.Resource)resource.FilterEffect!;
        Assert.That(evaluated.FrameRate, Is.EqualTo(1.5f));
        Assert.That(evaluated.Sample!.Time, Is.EqualTo(MediaTime.FromFrames(1, new Rational(3, 2))));
    }

    [Test]
    public void InvalidRateReleasesTheOldSampleAndSamplingCanResume()
    {
        var effect = new PosterizeTime();
        var shape = new RectShape { FilterEffect = { Value = effect } };
        var context = new CompositionContext(MediaTime.FromSeconds(0.1));
        using var resource = shape.ToResource(context);
        var evaluated = (PosterizeTime.Resource)resource.FilterEffect!;
        FilterEffectInputSample first = evaluated.Sample!;

        EchoTests.SetUnvalidated(effect.FrameRate, float.NaN);
        Reconcile(resource, shape, context, context.Time);
        Assert.That(first.IsDisposed, Is.True);
        Assert.That(evaluated.Sample, Is.Null);

        effect.FrameRate.Value = 2;
        Reconcile(resource, shape, context, context.Time);
        Assert.That(evaluated.Sample!.Time, Is.EqualTo(MediaTime.Zero));
    }

    private static void Reconcile(RectShape.Resource resource, RectShape shape, CompositionContext context, MediaTime time)
    {
        bool changed = false;
        context.Time = time;
        resource.Reconcile(shape, context, ref changed);
    }
}
