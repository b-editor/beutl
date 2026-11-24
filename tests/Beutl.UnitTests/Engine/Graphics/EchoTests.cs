using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class EchoTests
{
    [TestCase(-0.125f)]
    [TestCase(0.125f)]
    public void SamplesFollowTheSignedIntervalAndDecay(float interval)
    {
        var echo = new Echo
        {
            Interval = { Value = interval },
            StartingIntensity = { Value = 80 },
            Decay = { Value = 50 },
        };
        RectShape shape = MotionBlurTests.Rectangle();
        shape.FilterEffect.Value = echo;
        var context = new CompositionContext(MediaTime.FromSeconds(1));
        using var resource = shape.ToResource(context);
        var samples = ((Echo.Resource)resource.FilterEffect!).TimeSamples;
        double[] times = Enumerable.Range(0, 4).Select(index => 1.0 + index * interval).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(samples.Select(copy => copy.Sample.Time.ToSeconds()), Is.EqualTo(times));
            Assert.That(samples.Select(copy => copy.Opacity), Is.EqualTo(new[] { 0.8f, 0.4f, 0.2f, 0.1f }).Within(0.000001));
            Assert.That(samples.Select(copy => ((RectShape.Resource)copy.Sample.Resource!).Width),
                Is.EqualTo(times.Select(time => (float)(10 + time * 10))).Within(0.0001));
        }
    }

    [TestCase(1, 70f, true)]
    [TestCase(4, 0f, true)]
    [TestCase(4, 70f, false)]
    public void PassthroughAndDisablingReleaseSamplesAndAllowSamplingAgain(int count, float decay, bool enabled)
    {
        var echo = new Echo();
        var shape = new RectShape { FilterEffect = { Value = echo } };
        var context = new CompositionContext(MediaTime.FromSeconds(1));
        using var resource = shape.ToResource(context);
        var effect = (Echo.Resource)resource.FilterEffect!;
        var previous = effect.TimeSamples.Select(copy => copy.Sample).ToArray();
        Assert.That(previous, Has.Length.EqualTo(4));

        echo.Count.Value = count;
        echo.Decay.Value = decay;
        echo.IsEnabled.Value = enabled;
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Is.Empty);
        Assert.That(previous, Has.All.Matches<FilterEffectInputSample>(sample => sample is { IsDisposed: true }));

        echo.Count.Value = 4;
        echo.Decay.Value = 70;
        echo.IsEnabled.Value = true;
        Reconcile(resource, shape, context);
        Assert.That(effect.TimeSamples, Has.Count.EqualTo(4));
        Assert.That(effect.TimeSamples, Has.All.Matches<(FilterEffectInputSample Sample, float Opacity)>(copy => !copy.Sample.IsDisposed));
    }

    [TestCase(0f)]
    [TestCase(-10f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void ZeroOrInvalidStartingIntensityProducesNoSamples(float intensity)
    {
        var echo = new Echo();
        SetUnvalidated(echo.StartingIntensity, intensity);
        var shape = new RectShape { FilterEffect = { Value = echo } };
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        Assert.That(((Echo.Resource)resource.FilterEffect!).TimeSamples, Is.Empty);
    }

    [TestCase(float.NaN, 0)]
    [TestCase(float.PositiveInfinity, 0)]
    [TestCase(float.NegativeInfinity, 0)]
    [TestCase(120f, 60)]
    [TestCase(-120f, -60)]
    public void InvalidOrOutOfRangeIntervalUsesSanitizedTimeStepsAndDrawOrder(float interval, int expected)
    {
        var echo = new Echo { Count = { Value = 2 }, StartingIntensity = { Value = 80 }, Decay = { Value = 50 } };
        SetUnvalidated(echo.Interval, interval);
        RectShape shape = MotionBlurTests.Rectangle();
        shape.FilterEffect.Value = echo;
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        var effect = (Echo.Resource)resource.FilterEffect!;
        Assert.That(effect.TimeSamples.Select(copy => copy.Sample.Time.ToSeconds()), Is.EqualTo(new double[] { 1, 1 + expected }));

        using var root = new ContainerRenderNode();
        using (var graphics = new GraphicsContext2D(root, new Size(96, 64)))
            resource.Render(graphics);
        var opacities = new List<float>();
        CollectCopyOpacities(root);
        Assert.That(opacities, Is.EqualTo(expected < 0 ? new[] { 0.4f, 0.8f } : new[] { 0.8f, 0.4f }));

        void CollectCopyOpacities(RenderNode node)
        {
            if (node is OpacityRenderNode { Opacity: < 1 } opacity)
                opacities.Add(opacity.Opacity);
            foreach (var child in node.ChildNodes)
                CollectCopyOpacities(child);
        }
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-10f)]
    public void ZeroDecayAfterSanitizingStillDrawsThePartialIntensityCopy(float decay)
    {
        var echo = new Echo { StartingIntensity = { Value = 25 } };
        SetUnvalidated(echo.Decay, decay);
        var shape = new RectShape { FilterEffect = { Value = echo } };
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        var effect = (Echo.Resource)resource.FilterEffect!;
        Assert.That(effect.TimeSamples.Single().Opacity, Is.EqualTo(0.25f));
    }

    [TestCase(0, 1, 25f, 0.25f)]
    [TestCase(int.MaxValue, 64, 110f, 1f)]
    public void SampleCountAndOpacityAreClamped(int count, int expectedCount, float intensity, float expectedOpacity)
    {
        var echo = new Echo { Interval = { Value = 0 } };
        SetUnvalidated(echo.Count, count);
        SetUnvalidated(echo.StartingIntensity, intensity);
        SetUnvalidated(echo.Decay, 110f);
        var shape = new RectShape { FilterEffect = { Value = echo } };
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        var effect = (Echo.Resource)resource.FilterEffect!;
        Assert.That(effect.TimeSamples, Has.Count.EqualTo(expectedCount));
        Assert.That(effect.TimeSamples.Select(copy => copy.Opacity), Is.All.EqualTo(expectedOpacity));
    }

    // At the default Decay of 70%, copies 29 to 63 together are fainter than half an 8-bit step over black,
    // 0.5 / 255 / 12.92, and copies 28 to 63 are not.
    [TestCase(EchoBlendMode.NewestOnTop)]
    [TestCase(EchoBlendMode.Add)]
    public void CopiesTooFaintToChangeAnEightBitOutputAreNotEvaluated(EchoBlendMode blendMode)
    {
        var times = new List<MediaTime>();
        var echo = new Echo { Count = { Value = 64 }, BlendMode = { Value = blendMode } };
        var shape = new RectShape { FilterEffect = { Value = echo } };
        shape.Width.Expression = new TimeRecordingExpression(10, times);
        using var resource = shape.ToResource(new CompositionContext(MediaTime.FromSeconds(10)));
        var samples = ((Echo.Resource)resource.FilterEffect!).TimeSamples;
        double[] evaluated = [.. times.Select(time => time.ToSeconds()).Distinct().Order()];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(samples, Has.Count.EqualTo(29));
            Assert.That(samples[^1].Opacity, Is.EqualTo(MathF.Pow(0.7f, 28)).Within(0.01).Percent);
            Assert.That(evaluated, Has.Length.EqualTo(29), "one evaluation time for each copy drawn, the current one included");
            Assert.That(evaluated[0], Is.EqualTo(10 - 28 * 0.1).Within(0.001), "nothing earlier than the last copy drawn");
        }
    }

    [Test]
    public void SerializationRestoresEchoInTheEffectChain()
    {
        var echo = new Echo
        {
            Interval = { Value = 0.25f },
            Count = { Value = 7 },
            StartingIntensity = { Value = 85 },
            Decay = { Value = 60 },
            BlendMode = { Value = EchoBlendMode.NewestOnTop },
        };
        var shape = new RectShape
        {
            FilterEffect = { Value = new FilterEffectGroup { Children = { new Blur(), echo, new Brightness() } } },
        };
        var restored = (RectShape)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(shape), typeof(RectShape));
        var chain = (FilterEffectGroup)restored.FilterEffect.Value!;
        var actual = (Echo)chain.Children[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(chain.Children.Select(effect => effect.GetType()), Is.EqualTo(new[] { typeof(Blur), typeof(Echo), typeof(Brightness) }));
            Assert.That(actual.Interval.Value, Is.EqualTo(0.25f));
            Assert.That(actual.Count.Value, Is.EqualTo(7));
            Assert.That(actual.StartingIntensity.Value, Is.EqualTo(85));
            Assert.That(actual.Decay.Value, Is.EqualTo(60));
            Assert.That(actual.BlendMode.Value, Is.EqualTo(EchoBlendMode.NewestOnTop));
        }
    }

    private static void Reconcile(RectShape.Resource resource, RectShape shape, CompositionContext context)
    {
        bool changed = false;
        resource.Reconcile(shape, context, ref changed);
    }

    // Bypass property coercion to exercise the resource's own validation.
    internal static void SetUnvalidated<T>(IProperty<T> property, T value)
    {
        property.SetValidator(new MultipleValidator<T>([]));
        property.Value = value;
    }

    // Records the time of every evaluation of the property it is set on.
    private sealed class TimeRecordingExpression(float value, List<MediaTime> times) : IExpression<float>
    {
        public string ExpressionString => "recorded";

        public float Evaluate(ExpressionContext context)
        {
            times.Add(context.Time);
            return value;
        }

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }
    }
}
