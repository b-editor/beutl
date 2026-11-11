using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

using static Beutl.UnitTests.Engine.Audio.AudioTestWaves;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class ResamplingEffectTests
{
    // The project's rate, a source with six output samples to each of its own, and one that falls between them.
    private const int SampleRate = 48000;
    private const int SourceRate = 8000;
    private const int OddRate = 11025;
    private const int Window = 4800;

    private static readonly ResamplingMethod[] s_methods = Enum.GetValues<ResamplingMethod>();

    // Never repeats and moves fast, so a sample read from the wrong position shows.
    private static float Signal(long sample)
        => (float)(0.4 * Math.Sin(sample * 0.0123) + 0.3 * Math.Sin(sample * 0.731 + 1) + 0.2 * Math.Sin(sample * 2.17));

    private static float Sine(long sample, double frequency, int rate)
        => 0.5f * (float)Math.Sin(Math.PI * 2 * frequency * sample / rate);

    private static ResamplingEffect Create(ResamplingMethod method) => new() { Method = { Value = method } };

    [Test]
    public void The_method_defaults_to_hold()
    {
        var effect = new ResamplingEffect();

        Assert.Multiple(() =>
        {
            Assert.That(effect.Method.Name, Is.EqualTo("Method"));
            Assert.That(effect.Method.Value, Is.EqualTo(ResamplingMethod.Hold));
        });
    }

    // Read from the attributes, as the editor caches each name for the rest of the process once it has resolved it.
    [TestCase("en", new[] { "Hold", "Linear", "Catmull–Rom", "4-point Lagrange", "Sinc 16", "Sinc 64" })]
    [TestCase("ja", new[] { "ホールド", "線形", "Catmull–Rom", "4点Lagrange", "Sinc 16", "Sinc 64" })]
    public void The_methods_are_named_as_asked(string language, string[] names)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            string?[] shown = s_methods
                .Select(method => typeof(ResamplingMethod).GetField(method.ToString())!
                    .GetCustomAttribute<DisplayAttribute>()!.GetName())
                .ToArray();

            Assert.That(shown, Is.EqualTo(names));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void Hold_repeats_each_source_sample()
    {
        float[] left = Run(Create(ResamplingMethod.Hold), SourceRate, Signal, Window * 2);
        float[] right = Run(Create(ResamplingMethod.Hold), SourceRate, Signal, Window * 2, channel: 1);

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.EqualTo(Expected(sample => Signal(sample / 6), Window * 2)));
            Assert.That(right, Is.EqualTo(Expected(sample => -Signal(sample / 6) / 2, Window * 2)));
        });
    }

    [Test]
    public void Hold_takes_the_source_sample_at_or_before_each_output_sample()
    {
        float[] output = Run(Create(ResamplingMethod.Hold), OddRate, Signal, Window * 2);

        Assert.That(output, Is.EqualTo(Expected(
            sample => Signal((long)Math.Floor(sample * (double)OddRate / SampleRate)), Window * 2)));
    }

    [Test]
    public void Linear_draws_straight_lines_between_the_source_samples()
    {
        float[] output = Run(Create(ResamplingMethod.Linear), SourceRate, Signal, Window * 2);

        Assert.That(output, Is.EqualTo(Expected(sample =>
        {
            long held = sample / 6;
            double fraction = (sample - held * 6) / 6d;
            return (float)(Signal(held) + fraction * (Signal(held + 1) - Signal(held)));
        }, Window * 2)).Within(1e-6f));
    }

    // Built from the textbook forms rather than the weights the kernel uses.
    [TestCase(ResamplingMethod.CatmullRom)]
    [TestCase(ResamplingMethod.Lagrange)]
    public void A_cubic_passes_through_the_four_source_samples_around_it(ResamplingMethod method)
    {
        float[] output = Run(Create(method), SourceRate, Signal, Window * 2);

        Assert.That(output, Is.EqualTo(Expected(sample =>
        {
            long held = sample / 6;
            double fraction = (sample - held * 6) / 6d;
            double[] points = [Source(held - 1), Source(held), Source(held + 1), Source(held + 2)];
            return (float)(method == ResamplingMethod.CatmullRom ? CatmullRom(points, fraction) : Lagrange(points, fraction));
        }, Window * 2)).Within(1e-6f));

        // Before the timeline origin the source is silence.
        static double Source(long index) => index < 0 ? 0 : Signal(index);
    }

    private static double CatmullRom(double[] p, double t)
    {
        double m1 = (p[2] - p[0]) / 2;
        double m2 = (p[3] - p[1]) / 2;
        double t2 = t * t;
        double t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p[1] + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p[2] + (t3 - t2) * m2;
    }

    private static double Lagrange(double[] p, double t)
    {
        double sum = 0;
        for (int i = 0; i < 4; i++)
        {
            double basis = 1;
            for (int j = 0; j < 4; j++)
            {
                if (j != i)
                    basis *= (t - (j - 1)) / (i - j);
            }

            sum += basis * p[i];
        }

        return sum;
    }

    // At 8 kHz a 3 kHz tone has an image at 5 kHz, which only a band-limited method takes out.
    [TestCase(ResamplingMethod.Sinc16, -45d)]
    [TestCase(ResamplingMethod.Sinc64, -80d)]
    public void A_sinc_takes_out_the_images_of_the_source_rate(ResamplingMethod method, double ceiling)
    {
        float[] output = Run(Create(method), SourceRate, sample => Sine(sample, 3000, SourceRate), Window * 2);

        Assert.Multiple(() =>
        {
            Assert.That(ToneDb(output.AsSpan(Window), 5000), Is.LessThan(ceiling));
            Assert.That(ToneDb(output.AsSpan(Window), 3000), Is.EqualTo(0).Within(0.1));
        });
    }

    [Test]
    public void Hold_leaves_the_images_of_the_source_rate()
    {
        float[] output = Run(Create(ResamplingMethod.Hold), SourceRate, sample => Sine(sample, 3000, SourceRate), Window * 2);

        Assert.That(ToneDb(output.AsSpan(Window), 5000), Is.GreaterThan(-10));
    }

    [TestCase(ResamplingMethod.Sinc16, 1e-3f)]
    [TestCase(ResamplingMethod.Sinc64, 1e-4f)]
    public void A_sinc_keeps_a_tone_the_source_rate_holds(ResamplingMethod method, float tolerance)
    {
        float[] output = Run(Create(method), SourceRate, sample => Sine(sample, 1000, SourceRate), Window * 2);

        Assert.That(output[Window..],
            Is.EqualTo(Expected(sample => Sine(sample, 1000, SampleRate), Window * 2)[Window..]).Within(tolerance));
    }

    // Going down from 96 kHz, a 30 kHz tone is past what 48 kHz can hold and would fold back to 18 kHz.
    [TestCase(ResamplingMethod.Hold, -1d, 1d)]
    [TestCase(ResamplingMethod.Sinc64, double.NegativeInfinity, -80d)]
    public void Going_down_only_a_sinc_takes_out_what_the_project_rate_cannot_hold(
        ResamplingMethod method, double floor, double ceiling)
    {
        float[] output = Run(Create(method), 96000, sample => Sine(sample, 30000, 96000), Window * 2);

        Assert.That(ToneDb(output.AsSpan(Window), 18000), Is.InRange(floor, ceiling));
    }

    [Test]
    public void Audio_at_the_project_rate_is_left_alone()
    {
        using var context = new AudioContext(SampleRate, 2);
        AudioNode input = context.AddNode(new FunctionSource(Signal));
        using var node = new ResamplingNode { SourceSampleRate = SampleRate, Method = Create(ResamplingMethod.Sinc64).Method };
        node.AddInput(input);

        using AudioBuffer output = node.Process(AudioTestContexts.AtSample(0, Window, SampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(Create(ResamplingMethod.Sinc64).CreateNode(context, input), Is.SameAs(input));
            Assert.That(output.GetChannelData(0).ToArray(), Is.EqualTo(Expected(Signal, Window)));
        });
    }

    [TestCaseSource(nameof(s_methods))]
    public void Every_method_keeps_a_constant(ResamplingMethod method)
    {
        float[] output = Run(Create(method), OddRate, _ => 0.5f, Window * 2);

        // Past the start, which the silence before the timeline origin reaches.
        Assert.That(output[Window..], Has.All.EqualTo(0.5f).Within(1e-6f));
    }

    [TestCaseSource(nameof(s_methods))]
    public void Every_method_stays_in_time_with_the_source(ResamplingMethod method)
    {
        const int Click = 400;
        ResamplingEffect effect = Create(method);

        float[] output = Run(effect, SourceRate, sample => sample == Click ? 1f : 0f, Window);
        float peak = output.Max(Math.Abs);

        Assert.Multiple(() =>
        {
            Assert.That(effect.GetLatencySamples(SampleRate), Is.Zero);
            Assert.That(Array.FindIndex(output, sample => Math.Abs(sample) == peak), Is.EqualTo(Click * 6));
        });
    }

    [TestCaseSource(nameof(s_methods))]
    public void Every_method_gives_the_same_result_after_a_seek(ResamplingMethod method)
    {
        float[] continuous = Run(Create(method), OddRate, Signal, Window * 3);
        using var context = new AudioContext(SampleRate, 2) { ChainSampleRate = OddRate };
        AudioNode node = Create(method).CreateNode(context, context.AddNode(new FunctionSource(Signal)));
        using (node.Process(AudioTestContexts.AtSample(0, Window, SampleRate))) { }

        using AudioBuffer seeked = node.Process(AudioTestContexts.AtSample(Window * 2, Window, SampleRate));

        Assert.That(seeked.GetChannelData(0).ToArray(), Is.EqualTo(continuous[(Window * 2)..]));
    }

    [TestCaseSource(nameof(s_methods))]
    public void Every_method_gives_the_same_result_in_any_block_size(ResamplingMethod method)
    {
        Assert.That(Run(Create(method), OddRate, Signal, Window * 2, block: 997),
            Is.EqualTo(Run(Create(method), OddRate, Signal, Window * 2)));
    }

    [Test]
    public void The_source_is_read_at_its_own_rate()
    {
        var rates = new List<int>();

        Run(Create(ResamplingMethod.Sinc64), SourceRate, Signal, Window * 2, rates: rates);

        Assert.That(rates, Is.Not.Empty.And.All.EqualTo(SourceRate));
    }

    // Read as each output needs it, an 8 kHz source would be asked for one sample about every sixth output, and a
    // decoder above it would get thousands of requests a second.
    [TestCaseSource(nameof(s_methods))]
    public void The_source_is_read_a_block_at_a_time(ResamplingMethod method)
    {
        var reads = new List<int>();

        Run(Create(method), SourceRate, Signal, Window * 4, rates: reads);

        Assert.That(reads, Has.Count.EqualTo(4), "One read for each block of output.");
    }

    [TestCaseSource(nameof(s_methods))]
    public void Reading_stops_at_the_end_it_is_told_of(ResamplingMethod method)
    {
        const int End = Window * 2;
        var reads = new List<long>();
        float[] unbounded = Run(Create(method), SourceRate, Signal, End);
        using var context = new AudioContext(SampleRate, 2) { ChainSampleRate = SourceRate };
        AudioNode node = Create(method).CreateNode(context, context.AddNode(new FunctionSource(sample =>
        {
            reads.Add(sample);
            return Signal(sample);
        })));

        var output = new float[End];
        for (int start = 0; start < End; start += Window)
        {
            using AudioBuffer buffer = node.Process(
                AudioTestContexts.AtSample(start, Window, SampleRate, MediaTime.FromSamples(End, SampleRate)));
            buffer.GetChannelData(0).CopyTo(output.AsSpan(start));
        }

        Assert.Multiple(() =>
        {
            Assert.That(reads.Max(), Is.LessThan(End / 6), "Nothing past the end is read.");
            // Sinc 64 reaches 32 source samples, 192 output samples, on either side; before that the end makes no
            // difference.
            Assert.That(output[..(End - 200)], Is.EqualTo(unbounded[..(End - 200)]));
        });
    }

    // An edit can move the end without replacing the node. By the end of the first block, reading has reached 832 source
    // samples: past an end at 810, where live input stopped, and past 820, which the second block then moves the end to.
    [TestCase(true)]
    [TestCase(false)]
    public void An_end_that_moves_is_followed(bool later)
    {
        MediaTime first = MediaTime.FromSamples(later ? Window + 60 : Window * 3, SampleRate);
        MediaTime second = MediaTime.FromSamples(later ? Window * 3 : Window + 120, SampleRate);
        float[] expected = Run(Create(ResamplingMethod.Sinc64), SourceRate, Signal, Window * 2, end: second);
        using var context = new AudioContext(SampleRate, 2) { ChainSampleRate = SourceRate };
        AudioNode node = Create(ResamplingMethod.Sinc64).CreateNode(context, context.AddNode(new FunctionSource(Signal)));
        using (node.Process(AudioTestContexts.AtSample(0, Window, SampleRate, first))) { }

        using AudioBuffer moved = node.Process(AudioTestContexts.AtSample(Window, Window, SampleRate, second));

        Assert.That(moved.GetChannelData(0).ToArray(), Is.EqualTo(expected[Window..]));
    }

    [Test]
    public void The_tail_of_what_comes_before_it_comes_through()
    {
        const int End = Window;
        const int Lookahead = SampleRate / 100;
        // A 0 dB ceiling leaves the limiter transparent, so its tail is the 10 ms of input it holds, at the source's
        // rate.
        var limiter = new LimiterEffect { Lookahead = { Value = 10 }, Threshold = { Value = 0 } };
        using var context = new AudioContext(SampleRate, 2) { ChainSampleRate = SourceRate };
        AudioNode node = Create(ResamplingMethod.Sinc64).CreateNode(
            context, limiter.CreateNode(context, context.AddNode(new FunctionSource(sample => Sine(sample, 440, SourceRate)))));
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSamples(End, SampleRate));
        using (node.Process(new AudioProcessContext(range, SampleRate, new AnimationSampler(), range) { ProcessEndTime = range.End })) { }
        int latency = node.GetTotalLatencySamples(SampleRate);
        int drain = node.GetDrainLatencySamples(SampleRate);

        using AudioBuffer tail = node.Flush(AudioTestContexts.AtSample(End, latency, SampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(latency, Is.EqualTo(Lookahead), "The limiter's 80 samples at 8 kHz are 480 at 48 kHz.");
            Assert.That(drain, Is.EqualTo(Lookahead));
            // Away from where the tail runs out, the tone is a 440 Hz sine the source's rate holds.
            Assert.That(tail.GetChannelData(0)[..64].ToArray(),
                Is.EqualTo(Enumerable.Range(0, 64).Select(i => Sine(End + i - Lookahead, 440, SampleRate)).ToArray()).Within(1e-3f));
        });
    }

    // A method from a newer version or a hand-edited file holds, like the default.
    [Test]
    public void An_unknown_method_holds()
    {
        float[] output = Run(Create((ResamplingMethod)99), SourceRate, Signal, Window);

        Assert.That(output, Is.EqualTo(Run(Create(ResamplingMethod.Hold), SourceRate, Signal, Window)));
    }

    [Test]
    public void The_effect_adds_no_latency_to_what_comes_in()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ResamplingEffect().GetLatencySamples(SampleRate, 7), Is.EqualTo(7));
            Assert.That(new ResamplingEffect { IsEnabled = { Value = false } }.GetLatencySamples(SampleRate, 7), Is.EqualTo(7));
        });
    }

    // The window starts on a whole second, a whole number of the source's samples, and the sound at the source's own
    // rate is what reaches the effect, after the panner and the gain.
    [Test]
    public void A_low_rate_file_is_converted_with_the_method_asked_for()
    {
        WithTemporaryWave(SourceRate, SourceRate * 4, i => (Signal(i), -Signal(i)), source =>
        {
            float[] atSourceRate = Compose(source, null, SourceRate);
            float[] held = Compose(source, Create(ResamplingMethod.Hold), SampleRate);
            float[] linear = Compose(source, Create(ResamplingMethod.Linear), SampleRate);

            // The last source sample's line runs to one past the window.
            Assert.Multiple(() =>
            {
                Assert.That(held, Is.EqualTo(Expected(sample => atSourceRate[sample / 6], atSourceRate.Length * 6)));
                Assert.That(linear[..^6], Is.EqualTo(Expected(sample =>
                {
                    long index = sample / 6;
                    return (float)(atSourceRate[index] + (sample - index * 6) / 6d * (atSourceRate[index + 1] - atSourceRate[index]));
                }, atSourceRate.Length * 6 - 6)).Within(1e-6f));
            });
        });
    }

    [Test]
    public void Effects_before_the_resampling_run_at_the_source_rate_and_after_it_at_the_project_rate()
    {
        WithTemporaryWave(SourceRate, SourceRate * 4, i => (Signal(i), Signal(i)), source =>
        {
            var before = new ResamplingRateProbeEffect();
            var after = new ResamplingRateProbeEffect();
            var group = new AudioEffectGroup();
            group.Children.Add(before);
            group.Children.Add(Create(ResamplingMethod.Sinc16));
            group.Children.Add(after);
            var alone = new ResamplingRateProbeEffect();

            Compose(source, group, SampleRate);
            Compose(source, alone, SampleRate);

            Assert.Multiple(() =>
            {
                Assert.That(before.Rates, Is.Not.Empty.And.All.EqualTo(SourceRate));
                Assert.That(after.Rates, Is.Not.Empty.And.All.EqualTo(SampleRate));
                Assert.That(alone.Rates, Is.Not.Empty.And.All.EqualTo(SourceRate), "Without it, the conversion follows the effects.");
            });
        });
    }

    [Test]
    public void Without_the_effect_a_low_rate_file_still_plays_at_its_pitch()
    {
        WithTemporaryWave(SourceRate, SourceRate * 4, i => (Sine(i, 440, SourceRate), Sine(i, 440, SourceRate)), source =>
        {
            float[] output = Compose(source, null, SampleRate);

            Assert.Multiple(() =>
            {
                Assert.That(output, Has.Length.EqualTo(SampleRate * 2));
                Assert.That(ToneDb(output.AsSpan(Window, Window * 5), 440), Is.EqualTo(0).Within(0.5));
            });
        });
    }

    private static float[] Compose(SoundSource source, AudioEffect? effect, int sampleRate)
    {
        var sound = new SourceSound
        {
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(4)) },
            Source = { Value = source },
        };
        if (effect != null)
            sound.Effect.Value = effect;

        // Two seconds from the first.
        using var resource = sound.ToResource(CompositionContext.Default);
        using var composer = new Composer { SampleRate = sampleRate };
        var range = new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource),
            range, default, new CompositionEligibility([sound]));
        using AudioBuffer? output = composer.Compose(range, frame);
        return output!.GetChannelData(0).ToArray();
    }

    // Plays count samples at the project's rate from a source at sourceRate, through the effect.
    private static float[] Run(
        AudioEffect effect, int sourceRate, Func<long, float> source, int count,
        int block = Window, int channel = 0, MediaTime? end = null, List<int>? rates = null)
    {
        using var context = new AudioContext(SampleRate, 2) { ChainSampleRate = sourceRate };
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(source, rates)));
        var output = new float[count];
        for (int start = 0; start < count; start += block)
        {
            int part = Math.Min(block, count - start);
            using AudioBuffer buffer = node.Process(AudioTestContexts.AtSample(start, part, SampleRate, end));
            buffer.GetChannelData(channel).CopyTo(output.AsSpan(start, part));
        }

        return output;
    }

    private static float[] Expected(Func<long, float> sample, int count)
        => Enumerable.Range(0, count).Select(i => sample(i)).ToArray();

    // The level in dB of a tone at the project's rate, against the 0.5 sines the tests feed in. The span holds a whole
    // number of its periods.
    private static double ToneDb(ReadOnlySpan<float> samples, double frequency)
    {
        double cos = 0;
        double sin = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double phase = 2 * Math.PI * frequency * i / SampleRate;
            cos += samples[i] * Math.Cos(phase);
            sin += samples[i] * Math.Sin(phase);
        }

        double amplitude = 2 * Math.Sqrt(cos * cos + sin * sin) / samples.Length;
        return 20 * Math.Log10(Math.Max(amplitude, 1e-12) / 0.5);
    }

    // The right channel carries half the left, inverted, so a channel mixed up with another shows.
    private sealed class FunctionSource(Func<long, float> sample, List<int>? rates = null) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            rates?.Add(context.SampleRate);
            long start = (long)Math.Round(context.TimeRange.Start.ToSeconds() * context.SampleRate);
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(context.SampleRate, 2, count);
            Span<float> left = buffer.GetChannelData(0);
            Span<float> right = buffer.GetChannelData(1);
            for (int i = 0; i < count; i++)
            {
                left[i] = sample(start + i);
                right[i] = -left[i] / 2;
            }

            return RecordProcessedOutput(buffer);
        }
    }
}

// Passes its input through and records the rates it is asked for.
internal sealed partial class ResamplingRateProbeEffect : AudioEffect
{
    public List<int> Rates { get; } = [];

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.AddNode(new ResamplingRateProbeNode(Rates));
        context.Connect(inputNode, node);
        return node;
    }
}

internal sealed class ResamplingRateProbeNode(List<int> rates) : AudioNode
{
    public override AudioBuffer Process(AudioProcessContext context)
    {
        rates.Add(context.SampleRate);
        return RecordProcessedOutput(Inputs[0].Process(context));
    }
}
