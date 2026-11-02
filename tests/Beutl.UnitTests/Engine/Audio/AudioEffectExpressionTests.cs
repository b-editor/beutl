using System.Diagnostics.CodeAnalysis;
using Beutl.Animation;
using Beutl.Audio.Effects;
using Beutl.Media;
using static Beutl.UnitTests.Engine.Audio.SoundExpressionTests;

namespace Beutl.UnitTests.Engine.Audio;

// Effect nodes chose their path by keyframes alone and read the stored value otherwise, so an expression on an
// effect's parameter was silently ignored (#3826).
[TestFixture]
public class AudioEffectExpressionTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => Beutl.UnitTests.Engine.Graphics.Rendering.TestMediaHelper.RegisterTestDecoder();

    private static TestCaseData Case<T>(string name, float value, Func<T, IExpressionProperty<float>> parameter, Func<T>? create = null)
        where T : AudioEffect, new()
    {
        create ??= static () => new T();
        return new TestCaseData(new Func<AudioEffect>(create), new Func<AudioEffect, IExpressionProperty<float>>(e => parameter((T)e)), value)
            .SetName($"Parameter_PlaysTheExpressionValue({name})");
    }

    private static IEnumerable<TestCaseData> Parameters()
    {
        yield return Case<DelayEffect>("Delay.DelayTime", 50, e => e.DelayTime);
        yield return Case<DelayEffect>("Delay.Feedback", 10, e => e.Feedback);
        yield return Case<DelayEffect>("Delay.DryMix", 20, e => e.DryMix);
        yield return Case<DelayEffect>("Delay.WetMix", 80, e => e.WetMix);
        yield return Case<CompressorEffect>("Compressor.Threshold", -30, e => e.Threshold);
        yield return Case<CompressorEffect>("Compressor.MakeupGain", 3, e => e.MakeupGain);
        yield return Case<GateEffect>("Gate.Threshold", 0, e => e.Threshold);
        yield return Case<LimiterEffect>("Limiter.Threshold", -20, e => e.Threshold);
        yield return Case<LimiterEffect>("Limiter.MakeupGain", -6, e => e.MakeupGain);
        yield return Case<LimiterEffect>("Limiter.Lookahead", 1, e => e.Lookahead);
        yield return Case<EqualizerEffect>("Equalizer.Gain", 12, e => e.Bands[4].Gain);
        yield return Case<EqualizerEffect>("Equalizer.Frequency", 440, e => e.Bands[4].Frequency,
            static () =>
            {
                var equalizer = new EqualizerEffect();
                equalizer.Bands[4].Gain.Value = 12;
                return equalizer;
            });
        yield return Case<NoiseReductionEffect>("NoiseReduction.Reduction", 100, e => e.Reduction);
    }

    [TestCaseSource(nameof(Parameters))]
    public void Parameter_PlaysTheExpressionValue(
        Func<AudioEffect> create, Func<AudioEffect, IExpressionProperty<float>> parameter, float value)
    {
        var expression = new CountingExpression<float>(value);
        float[] stored = Compose(sound => sound.Effect.Value = create());
        float[] expected = Compose(sound =>
        {
            AudioEffect effect = create();
            parameter(effect).Value = value;
            sound.Effect.Value = effect;
        });
        float[] actual = Compose(sound =>
        {
            AudioEffect effect = create();
            parameter(effect).Expression = expression;
            sound.Effect.Value = effect;
        });

        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.Not.EqualTo(stored).Within(1e-3f), "The value must change the output for this to test anything.");
            Assert.That(actual, Is.EqualTo(expected).Within(1e-3f));
            // A control rate, not one evaluation for each of the 44100 samples.
            Assert.That(expression.Evaluations, Is.InRange(1, 250));
        });
    }

    // A script can return what no keyframe can, and a delay line indexes by its time.
    [Test]
    public void Parameter_NotFiniteExpression_PlaysTheStoredValue()
    {
        float[] expected = Compose(sound => sound.Effect.Value = new DelayEffect());
        float[] actual = Compose(sound =>
        {
            var delay = new DelayEffect();
            delay.DelayTime.Expression = new CountingExpression<float>(float.NaN);
            sound.Effect.Value = delay;
        });

        Assert.That(actual, Is.EqualTo(expected).Within(1e-3f));
    }

    // A chunk's last sample can be alone in its span, and must not jump off the line its neighbours are on.
    [Test]
    public void SampleBuffer_OneSample_StaysOnTheGrid()
    {
        var delay = new DelayEffect();
        delay.DelayTime.Expression = new StepExpression(MediaTime.FromSeconds(0.02), 100, 200);
        Span<float> output = stackalloc float[1];

        new AnimationSampler().SampleBuffer(
            delay.DelayTime, new TimeRange(MediaTime.FromSeconds(0.015), MediaTime.Zero), 44100, output);

        Assert.That(output[0], Is.EqualTo(150).Within(1e-3f));
    }

    [Test]
    public void Latency_ExpressionOnLookahead_ReservesTheWorstCase()
    {
        var limiter = new LimiterEffect();
        int stored = limiter.GetLatencySamples(44100);
        limiter.Lookahead.Expression = new CountingExpression<float>(1);

        Assert.That(limiter.GetLatencySamples(44100), Is.EqualTo(LimiterParameters.ToLatencySamples(LimiterParameters.MaxLookaheadMs, 44100))
            .And.GreaterThan(stored));
    }

    [Test]
    public void Latency_ExpressionOnStretchSpeed_ReservesTheSlowestSpeed()
    {
        var stretch = new TimeStretchEffect();
        stretch.Speed.Expression = new CountingExpression<float>(200);

        Assert.That(stretch.GetLatencySamples(44100, 100), Is.EqualTo(400));
    }

    private sealed class StepExpression(MediaTime step, float before, float after) : IExpression<float>
    {
        public string ExpressionString => string.Empty;

        public float Evaluate(ExpressionContext context) => context.Time < step ? before : after;

        public bool Validate([NotNullWhen(false)] out string? error)
        {
            error = null;
            return true;
        }
    }
}
