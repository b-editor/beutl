using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public sealed class SteppedMotionTests
{
    [TestCase(2f, 2, 1, 1)]
    [TestCase(0.01f, 1, 100, 1)]
    [TestCase(0.1f, 1, 10, 1)]
    [TestCase(29.97f, 2997, 100, 2)]
    [TestCase(240f, 240, 1, 1)]
    public void SamplesSwitchAtTheElementRelativeBoundary(float fps, int numerator, int denominator, int frame)
    {
        var shape = new RectShape { FilterEffect = { Value = new SteppedMotion { FrameRate = { Value = fps } } } };
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
        var effect = (SteppedMotion.Resource)resource.FilterEffect!;
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
        shape.FilterEffect.Value = new SteppedMotion { FrameRate = { Value = 2 } };
        Scene scene = MotionBlurTests.SceneWith(shape);
        var project = new Project();
        new TestHierarchicalRoot().HierarchicalChildren.Add(project);
        project.Items.Add(scene);
        Element element = scene.Children[0];
        element.Start.Value = MediaTime.FromSeconds(0.3);
        var context = new CompositionContext(MediaTime.FromSeconds(1.1));
        using var resource = shape.ToResource(context);
        var effect = (SteppedMotion.Resource)resource.FilterEffect!;
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
        var effect = new SteppedMotion { FrameRate = { Value = 29.97f } };
        MotionBlurTests.Animate(effect.FrameRate, 1, 2);
        var shape = new RectShape { FilterEffect = { Value = effect } };
        var restored = (RectShape)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(shape), typeof(RectShape));
        Assert.That(((SteppedMotion)restored.FilterEffect.Value!).FrameRate.Value, Is.EqualTo(29.97f));

        using var resource = restored.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        var evaluated = (SteppedMotion.Resource)resource.FilterEffect!;
        Assert.That(evaluated.FrameRate, Is.EqualTo(1.5f));
        // Rising from 1 to 2 fps over 2 s, the rate has counted t + t² / 4 steps by t, the first at 2(√2 − 1) s.
        Assert.That(evaluated.Sample!.Time.ToSeconds(), Is.EqualTo(2 * (Math.Sqrt(2) - 1)).Within(1e-6));
    }

    // A linear rate from a to b fps over the element's first 2 s has counted a t + (b − a) t² / 4 steps by t, so
    // step k starts at 2(√(a² + (b − a) k) − a) / (b − a). At 30 fps that count is (120 a f + (b − a) f²) / 3600.
    [TestCase(24, 4)]
    [TestCase(2, 12)]
    public void AnimatedRateHoldsEachStepFromWhenItsIntegralReachedAWholeNumber(int a, int b)
    {
        MediaTime start = MediaTime.FromSeconds(0.3);
        MediaTime[] held = HeldTimes(Linear(a, b), start, Enumerable.Range(0, 61));

        for (int frame = 0; frame <= 60; frame++)
        {
            int step = (120 * a * frame + (b - a) * frame * frame) / 3600;
            double stepStart = 2 * (Math.Sqrt(a * a + (b - a) * step) - a) / (b - a);
            Assert.That((held[frame] - start).ToSeconds(), Is.EqualTo(stepStart).Within(1e-5), $"frame {frame}");
            if (frame == 0) continue;

            // Every frame of a step holds the very same time, and the next step's is later.
            int previous = (120 * a * (frame - 1) + (b - a) * (frame - 1) * (frame - 1)) / 3600;
            if (previous == step) Assert.That(held[frame], Is.EqualTo(held[frame - 1]), $"frame {frame}");
            else Assert.That(held[frame], Is.GreaterThan(held[frame - 1]), $"frame {frame}");
        }
    }

    // Hold key frames switch the rate from 2 to 6 fps at 0.9 s, 1.8 steps in, and to 3 fps at 2.1 s, 9 steps in. At
    // 30 fps the steps start on frames 0 and 15, then every 5 frames from 28, then every 10 from 63.
    [Test]
    public void HoldKeyframesChangeTheStepLengthAtTheirTime()
    {
        var effect = new SteppedMotion();
        effect.FrameRate.SetKeyFrameAnimation(Keys(Key(0, 2), Key(0.9, 6, new HoldEasing()), Key(2.1, 3, new HoldEasing())));
        MediaTime start = MediaTime.FromSeconds(0.3);
        MediaTime[] held = HeldTimes(effect, start, Enumerable.Range(0, 94));

        for (int frame = 0; frame <= 93; frame++)
        {
            int stepStart = frame < 28 ? frame / 15 * 15 : frame < 63 ? 28 + (frame - 28) / 5 * 5 : 63 + (frame - 63) / 10 * 10;
            Assert.That(held[frame], Is.EqualTo(start + MediaTime.FromFrames(stepStart, new Rational(30))), $"frame {frame}");
        }
    }

    // Inserting a key frame raises no Edited, so nothing kept from an earlier frame may decide the next one's steps:
    // 2 fps until 2 s holds 1 s at 1.2 s, and 6 fps from 0.9 s then holds 1.1 s there.
    [Test]
    public void InsertingAKeyframeRetimesTheNextFrame()
    {
        KeyFrameAnimation<float> animation = Keys(Key(0, 2), Key(2, 2.5f, new HoldEasing()));
        var effect = new SteppedMotion();
        effect.FrameRate.SetKeyFrameAnimation(animation);
        var shape = new RectShape { FilterEffect = { Value = effect } };
        Element element = InProject(shape, MediaTime.FromSeconds(0.3));
        var rate = new Rational(30);
        MediaTime time = element.Start.Value + MediaTime.FromFrames(36, rate);
        var context = new CompositionContext(time);
        using var resource = shape.ToResource(context);
        var evaluated = (SteppedMotion.Resource)resource.FilterEffect!;
        Assert.That(evaluated.Sample!.Time, Is.EqualTo(element.Start.Value + MediaTime.FromFrames(30, rate)));

        animation.KeyFrames.Insert(1, Key(0.9, 6, new HoldEasing()));
        Reconcile(resource, shape, context, time);
        Assert.That(evaluated.Sample!.Time, Is.EqualTo(element.Start.Value + MediaTime.FromFrames(33, rate)));
    }

    // A spline that overshoots to 3 clamps the rate at 240 fps at every sample of its 100 s piece, yet the rate rises
    // from 1 fps over the first 0.14 s: by 1/30 s it has counted about 3.6 steps rather than 8, and by 50 s some 8
    // fewer than 240 fps throughout would have.
    [TestCase(1)]
    [TestCase(1500)]
    public void ClampedOvershootStillCountsTheRiseToTheLimit(int frame)
    {
        KeyFrameAnimation<float> animation = Keys(Key(0, 1), Key(100, 240, new SplineEasing(0, 3, 0, 3)));
        var effect = new SteppedMotion();
        effect.FrameRate.SetKeyFrameAnimation(animation);
        MediaTime time = MediaTime.FromFrames(frame, new Rational(30));
        MediaTime[] held = HeldTimes(effect, MediaTime.Zero, [time]);

        // The clamped rate summed every 10 µs through the first second and every 1 ms after it, and where that sum
        // last reached a whole number.
        double seconds = time.ToSeconds();
        var points = new List<(double Time, double Count)> { (0, 0) };
        for (double t = 0; t < seconds;)
        {
            double width = Math.Min(t < 1 ? 1e-5 : 1e-3, seconds - t);
            float rate = animation.Interpolate(MediaTime.FromSeconds(t + width / 2));
            points.Add((t + width, points[^1].Count + Math.Clamp(rate, 0.01f, 240f) * width));
            t += width;
        }

        double steps = Math.Floor(points[^1].Count);
        int reached = points.FindIndex(point => point.Count >= steps);
        ((double t0, double c0), (double t1, double c1)) = (points[reached - 1], points[reached]);
        Assert.That(points[^1].Count, Is.LessThan(240 * seconds - 3));
        Assert.That(held[0].ToSeconds(), Is.EqualTo(t0 + (steps - c0) / (c1 - c0) * (t1 - t0)).Within(1e-5));
    }

    // A frame's held time follows from its own time alone, so the order frames are rendered in never matters.
    [Test]
    public void SeekingBackwardHoldsTheSameTimes()
    {
        MediaTime start = MediaTime.FromSeconds(0.3);
        MediaTime[] forward = HeldTimes(Linear(24, 4), start, Enumerable.Range(0, 61));
        MediaTime[] backward = HeldTimes(Linear(24, 4), start, Enumerable.Range(0, 61).Reverse());

        Assert.That(Enumerable.Reverse(backward), Is.EqualTo(forward));
    }

    // Steps count back from the start at the rate there: 2 + 2t fps on the scene's clock from 0 s, and 2 fps before it.
    // The second before an element at 1 s holds 3 steps, the second of which starts at √2 − 1 s, and the next one back,
    // past the key frame at 0 s, starts at −0.5 s.
    [TestCase(0.5, 1.41421356237309505 - 1)]
    [TestCase(0.25, 0)]
    [TestCase(-0.25, -0.5)]
    public void BeforeTheStartStepsCountBackFromIt(double seconds, double expected)
    {
        var effect = new SteppedMotion();
        KeyFrameAnimation<float> animation = Keys(Key(0, 2), Key(1, 4));
        animation.UseGlobalClock.Value = true;
        effect.FrameRate.SetKeyFrameAnimation(animation);
        MediaTime[] held = HeldTimes(effect, MediaTime.FromSeconds(1), [MediaTime.FromSeconds(seconds)]);

        Assert.That(held[0].ToSeconds(), Is.EqualTo(expected).Within(1e-6));
    }

    // Only evaluating an expression tells its rate, so the rate at the time applies to all the time since the start,
    // and the key frames below it count for nothing: at 1.2 s, 3 fps puts 3.6 steps there, where 2 fps until 0.75 s
    // would have counted 2.85, and the key frames 21.6.
    [Test]
    public void ExpressionRateAppliesToTheWholeTimeSinceTheStart()
    {
        SteppedMotion effect = Linear(24, 4);
        effect.FrameRate.Expression = new StringExpression<float>("Time < 0.75 ? 2 : 3");
        Assert.That(effect.FrameRate.GetKeyFrameAnimation(), Is.Not.Null);
        MediaTime[] held = HeldTimes(effect, MediaTime.Zero, [MediaTime.FromSeconds(1.2)]);

        Assert.That(held[0], Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [Test]
    public void InvalidRateReleasesTheOldSampleAndSamplingCanResume()
    {
        var effect = new SteppedMotion();
        var shape = new RectShape { FilterEffect = { Value = effect } };
        var context = new CompositionContext(MediaTime.FromSeconds(0.1));
        using var resource = shape.ToResource(context);
        var evaluated = (SteppedMotion.Resource)resource.FilterEffect!;
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

    private static MediaTime[] HeldTimes(SteppedMotion effect, MediaTime start, IEnumerable<int> frames)
        => HeldTimes(effect, start, [.. frames.Select(frame => start + MediaTime.FromFrames(frame, new Rational(30)))]);

    // The held time at each of the times in turn, from one evaluated shape whose element starts at start.
    private static MediaTime[] HeldTimes(SteppedMotion effect, MediaTime start, MediaTime[] times)
    {
        var shape = new RectShape { FilterEffect = { Value = effect } };
        InProject(shape, start);
        var context = new CompositionContext(times[0]);
        using var resource = shape.ToResource(context);
        var evaluated = (SteppedMotion.Resource)resource.FilterEffect!;
        return [.. times.Select(time =>
        {
            Reconcile(resource, shape, context, time);
            return evaluated.Sample!.Time;
        })];
    }

    // Key frames count from their element's start only once attached to a project, as in the app.
    private static Element InProject(RectShape shape, MediaTime start)
    {
        Scene scene = MotionBlurTests.SceneWith(shape);
        var project = new Project();
        new TestHierarchicalRoot().HierarchicalChildren.Add(project);
        project.Items.Add(scene);
        Element element = scene.Children[0];
        element.Start.Value = start;
        Assert.That(((SteppedMotion)shape.FilterEffect.Value!).Start, Is.EqualTo(start));
        return element;
    }

    private static SteppedMotion Linear(float a, float b)
    {
        var effect = new SteppedMotion();
        effect.FrameRate.SetKeyFrameAnimation(Keys(Key(0, a), Key(2, b)));
        return effect;
    }

    private static KeyFrameAnimation<float> Keys(params KeyFrame<float>[] keys)
    {
        var animation = new KeyFrameAnimation<float>();
        foreach (KeyFrame<float> key in keys) animation.KeyFrames.Add(key);
        return animation;
    }

    private static KeyFrame<float> Key(double seconds, float rate, Easing? easing = null) => new()
    {
        KeyTime = { Value = MediaTime.FromSeconds(seconds) },
        Value = { Value = rate },
        Easing = { Value = easing ?? new LinearEasing() },
    };
}
