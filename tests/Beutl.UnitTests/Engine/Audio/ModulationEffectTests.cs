using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

using static Beutl.UnitTests.Engine.Audio.AudioEffectAssertions;
using static Beutl.UnitTests.Engine.Audio.AudioTestWaves;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class ModulationEffectTests
{
    private const int SampleRate = 48000;
    private const int Window = 4800;

    private static float Sine(long sample, double frequency = 440)
        => 0.5f * (float)Math.Sin(Math.PI * 2 * frequency * sample / SampleRate);

    [Test]
    public void The_properties_stay_in_their_ranges()
    {
        var pitch = new PitchShiftEffect();
        AssertNameAndRange(pitch.Semitones, "Semitones", PitchShiftEffect.SemitonesMin, PitchShiftEffect.SemitonesMax);
        var vibrato = new VibratoEffect();
        AssertNameAndRange(vibrato.Rate, "Rate", VibratoEffect.RateMin, VibratoEffect.RateMax);
        AssertNameAndRange(vibrato.Depth, "Depth", VibratoEffect.DepthMin, VibratoEffect.DepthMax);
        var ring = new RingModulatorEffect();
        AssertNameAndRange(ring.Frequency, "Frequency", RingModulatorEffect.FrequencyMin, RingModulatorEffect.FrequencyMax);
        AssertNameAndRange(ring.Mix, "Mix", 0, 100);
    }

    [Test]
    public void RingModulator_multiplies_the_input_with_a_sine()
    {
        var effect = new RingModulatorEffect { Frequency = { Value = 100 } };

        float[] output = Run(effect, _ => 0.5f, Window * 2);

        for (int i = 0; i < output.Length; i += 37)
            Assert.That(output[i], Is.EqualTo(Sine(i, 100)).Within(0.0005f), $"sample {i}");
    }

    [Test]
    public void RingModulator_with_no_mix_leaves_the_input_alone()
    {
        var effect = new RingModulatorEffect { Mix = { Value = 0 } };

        float[] output = Run(effect, sample => Sine(sample), Window);

        for (int i = 0; i < output.Length; i++)
            Assert.That(output[i], Is.EqualTo(Sine(i)).Within(0.00001f), $"sample {i}");
    }

    [Test]
    public void RingModulator_gives_the_same_result_after_a_seek()
    {
        var played = new RingModulatorEffect();
        float[] continuous = Run(played, _ => 0.5f, Window * 2);

        var seeked = new RingModulatorEffect();
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = seeked.CreateNode(context, context.AddNode(new FunctionSource(_ => 0.5f)));
        using AudioBuffer buffer = node.Process(AudioTestContexts.AtSample(Window, Window, SampleRate));

        Span<float> data = buffer.GetChannelData(0);
        for (int i = 0; i < Window; i += 37)
            Assert.That(data[i], Is.EqualTo(continuous[Window + i]).Within(0.001f), $"sample {i}");
    }

    // This easing carries the mix past the top of its range before it settles.
    [Test]
    public void RingModulator_does_not_amplify_while_easing_carries_the_mix_out_of_its_range()
    {
        var effect = new RingModulatorEffect { Frequency = { Value = 100 } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0 } });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = 100 },
            Easing = { Value = new BackEaseOut() },
        });
        effect.Mix.SetKeyFrameAnimation(animation);

        float[] output = Run(effect, _ => 0.5f, SampleRate);

        Assert.That(output, Has.All.InRange(-0.5001f, 0.5001f));
    }

    [Test]
    public void Vibrato_with_no_depth_leaves_the_input_alone()
    {
        var effect = new VibratoEffect { Depth = { Value = 0 } };

        float[] output = Run(effect, sample => Sine(sample), Window);

        for (int i = 0; i < output.Length; i++)
            Assert.That(output[i], Is.EqualTo(Sine(i)).Within(0.00001f), $"sample {i}");
    }

    // A delay swinging by D(1 - cos(wt)) bends the pitch by up to D * w, here 2 ms * 2 pi * 5 Hz = 6.3 %.
    [Test]
    public void Vibrato_bends_the_pitch_up_and_down_around_the_input()
    {
        var effect = new VibratoEffect { Rate = { Value = 5 }, Depth = { Value = 2 } };

        float[] output = Run(effect, sample => Sine(sample, 1000), SampleRate);

        var pitches = new List<double>();
        const int Part = SampleRate / 50;
        for (int start = 0; start + Part <= output.Length; start += Part)
            pitches.Add(Pitch(output.AsSpan(start, Part)));
        Assert.Multiple(() =>
        {
            Assert.That(pitches.Max(), Is.InRange(1040, 1085));
            Assert.That(pitches.Min(), Is.InRange(915, 960));
            Assert.That(Pitch(output), Is.EqualTo(1000).Within(10));
        });
    }

    // Easing leaves the range between the keyframes, to below zero on the way in and above the top on the way out.
    [TestCase(false)]
    [TestCase(true)]
    public void Vibrato_keeps_playing_while_easing_carries_the_depth_out_of_its_range(bool easeOut)
    {
        var effect = new VibratoEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0 } });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = VibratoEffect.DepthMax },
            Easing = { Value = easeOut ? new BackEaseOut() : new BackEaseIn() },
        });
        effect.Depth.SetKeyFrameAnimation(animation);

        float[] output = Run(effect, _ => 0.5f, SampleRate);

        // Any delay gives back a constant input, unless the reading point has left the delay line.
        Assert.That(output, Has.All.EqualTo(0.5f).Within(0.0001f));
    }

    // At the end of 0.1 s a 5 Hz swing is at its longest delay, twice the depth.
    [Test]
    public void Vibrato_reports_the_tail_it_holds_and_lets_it_out_after_the_input_ends()
    {
        var effect = new VibratoEffect { Rate = { Value = 5 }, Depth = { Value = VibratoEffect.DepthMax } };
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(sample => sample < Window ? 0.5f : 0f)));
        int latency = node.GetTotalLatencySamples(SampleRate);

        using (node.Process(AudioTestContexts.AtSample(0, Window, SampleRate))) { }
        using AudioBuffer tail = node.Flush(AudioTestContexts.AtSample(Window, latency, SampleRate));

        Assert.Multiple(() =>
        {
            Assert.That(latency, Is.EqualTo(1920));
            Assert.That(effect.GetLatencySamples(SampleRate), Is.EqualTo(latency));
            Assert.That(tail.GetChannelData(0)[0], Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(tail.GetChannelData(0)[latency - 1], Is.Zero);
        });
    }

    // Keyframes past the end of the clip are for a part of it that was trimmed away. Falling to no depth would read
    // what comes after the input instead of the tail.
    [Test]
    public void Vibrato_holds_its_last_values_while_the_tail_comes_out()
    {
        var effect = new VibratoEffect { Rate = { Value = 5 } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = VibratoEffect.DepthMax } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(0.1) }, Value = { Value = VibratoEffect.DepthMax } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(0.11) }, Value = { Value = 0 } });
        effect.Depth.SetKeyFrameAnimation(animation);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(sample => sample < Window ? 0.5f : 0f)));

        using (node.Process(AudioTestContexts.AtSample(0, Window, SampleRate))) { }
        using AudioBuffer tail = node.Flush(AudioTestContexts.AtSample(Window, 960, SampleRate));

        Assert.That(tail.GetChannelData(0)[480], Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void An_animated_Vibrato_reserves_the_longest_tail_and_a_still_one_its_own()
    {
        var still = new VibratoEffect { Depth = { Value = 2 } };
        var animated = new VibratoEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 2 } });
        animated.Depth.SetKeyFrameAnimation(animation);
        var disabled = new VibratoEffect { IsEnabled = { Value = false } };

        Assert.Multiple(() =>
        {
            Assert.That(still.GetLatencySamples(SampleRate), Is.EqualTo(192));
            Assert.That(animated.GetLatencySamples(SampleRate), Is.EqualTo(1920));
            Assert.That(disabled.GetLatencySamples(SampleRate, 7), Is.EqualTo(7));
        });
    }

    [TestCase(12f, 880d)]
    [TestCase(-12f, 220d)]
    [TestCase(7f, 659.26d)]
    [TestCase(-5f, 329.63d)]
    [TestCase(24f, 1760d)]
    public void PitchShift_moves_a_tone(float semitones, double expected)
    {
        var effect = new PitchShiftEffect { Semitones = { Value = semitones } };

        float[] output = Run(effect, sample => Sine(sample), SampleRate);

        Assert.That(Pitch(output.AsSpan(Window)), Is.EqualTo(expected).Within(expected * 0.06));
    }

    [Test]
    public void PitchShift_at_no_shift_is_the_input_itself()
    {
        var effect = new PitchShiftEffect();

        float[] output = Run(effect, sample => Sine(sample), Window * 2);

        for (int i = 0; i < output.Length; i++)
            Assert.That(output[i], Is.EqualTo(Sine(i)), $"sample {i}");
    }

    // Two reading points that are not in step cancel each other and the level swells at the rate they swap.
    [TestCase(12f, 220d)]
    [TestCase(12f, 440d)]
    [TestCase(7f, 440d)]
    [TestCase(-12f, 440d)]
    [TestCase(-5f, 261.63d)]
    [TestCase(1f, 987.77d)]
    public void PitchShift_keeps_a_held_tone_at_one_level(float semitones, double frequency)
    {
        var effect = new PitchShiftEffect { Semitones = { Value = semitones } };

        float[] output = Run(effect, sample => Sine(sample, frequency), SampleRate * 2);

        // Past the start, over windows long enough for a period of the lowest tone.
        const int Frame = SampleRate / 40;
        var levels = new List<double>();
        for (int start = SampleRate / 2; start + Frame <= output.Length; start += Frame)
        {
            double sum = 0;
            for (int i = start; i < start + Frame; i++)
                sum += output[i] * output[i];
            levels.Add(Math.Sqrt(sum / Frame));
        }

        Assert.Multiple(() =>
        {
            Assert.That((levels.Max() - levels.Min()) / levels.Average(), Is.LessThan(0.03));
            Assert.That(levels.Average(), Is.EqualTo(0.5 / Math.Sqrt(2)).Within(0.01));
        });
    }

    [Test]
    public void PitchShift_follows_keyframes()
    {
        var effect = new PitchShiftEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(0.5) }, Value = { Value = 12 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = 12 } });
        effect.Semitones.SetKeyFrameAnimation(animation);

        float[] output = Run(effect, sample => Sine(sample), SampleRate);

        Assert.That(Pitch(output.AsSpan(SampleRate * 3 / 4)), Is.EqualTo(880).Within(880 * 0.06));
    }

    // The input is read ahead instead, so a shifted sound starts where the original does and nothing is left over.
    [TestCase(12f)]
    [TestCase(-12f)]
    [TestCase(7f)]
    public void PitchShift_stays_in_time_with_the_input_and_holds_no_tail(float semitones)
    {
        const int Onset = SampleRate / 2;
        var effect = new PitchShiftEffect { Semitones = { Value = semitones } };
        Func<long, float> source = sample => sample < Onset ? 0f : Sine(sample);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(source)));

        float[] output = Run(effect, source, SampleRate);
        int first = Array.FindIndex(output, sample => Math.Abs(sample) > 0.01f);

        Assert.Multiple(() =>
        {
            Assert.That(effect.GetLatencySamples(SampleRate), Is.Zero);
            Assert.That(node.GetTotalLatencySamples(SampleRate), Is.Zero);
            // Less than a video frame at 60 fps, and neither of WSOLA's search windows.
            Assert.That(Math.Abs(first - Onset), Is.LessThan(SampleRate / 60), $"The sound starts at {first}.");
        });
    }

    [Test]
    public void PitchShift_stops_reading_at_the_end_it_is_told_of()
    {
        const int End = Window * 2;
        var reads = new List<long>();
        var effect = new PitchShiftEffect { Semitones = { Value = 12 } };
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(sample =>
        {
            reads.Add(sample);
            return Sine(sample);
        })));

        var blocks = new List<float>();
        for (int start = 0; start < End + Window; start += Window)
        {
            var range = new TimeRange(MediaTime.FromSamples(start, SampleRate), MediaTime.FromSamples(Window, SampleRate));
            using AudioBuffer buffer = node.Process(new AudioProcessContext(range, SampleRate, new Beutl.Animation.AnimationSampler(), range)
            {
                ProcessEndTime = MediaTime.FromSamples(End, SampleRate),
            });
            blocks.AddRange(buffer.GetChannelData(0).ToArray());
        }

        Assert.Multiple(() =>
        {
            Assert.That(reads.Max(), Is.LessThan(End), "The upstream's tail starts at the end, so nothing past it is read.");
            Assert.That(blocks.Skip(End + SampleRate / 100), Is.All.EqualTo(0f));
            Assert.That(blocks.Take(End - SampleRate / 10).Max(Math.Abs), Is.GreaterThan(0.3f));
        });
    }

    // Reading ahead in a file reads at positions plain playback never asks for, where an index error would show.
    [Test]
    public void PitchShift_reads_a_real_file_at_the_positions_it_should()
    {
        WithTemporaryWave(SampleRate, SampleRate * 4, i => (Sine(i), Sine(i)), source =>
        {
            float[] original = Compose(source, null);
            float[] unshifted = Compose(source, new PitchShiftEffect());
            // A keyframe that stays at no shift keeps the stretcher running without changing the sound.
            var held = new PitchShiftEffect();
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0 } });
            held.Semitones.SetKeyFrameAnimation(animation);
            float[] stretched = Compose(source, held);
            float[] shifted = Compose(source, new PitchShiftEffect { Semitones = { Value = 12 } });

            Assert.Multiple(() =>
            {
                Assert.That(unshifted, Is.EqualTo(original));
                Assert.That(stretched, Is.EqualTo(original).Within(0.001f));
                Assert.That(Pitch(shifted), Is.EqualTo(880).Within(880 * 0.06));
            });
        });
    }

    [Test]
    public void PitchShift_lets_the_tail_of_what_comes_before_it_through()
    {
        const int End = Window;
        // A 0 dB ceiling leaves the limiter transparent, so its tail is exactly the samples it holds.
        var limiter = new LimiterEffect { Lookahead = { Value = 10 }, Threshold = { Value = 0 } };
        var effect = new PitchShiftEffect { Semitones = { Value = 12 } };
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, limiter.CreateNode(context, context.AddNode(new FunctionSource(sample => Sine(sample)))));
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSamples(End, SampleRate));
        using (node.Process(new AudioProcessContext(range, SampleRate, new AnimationSampler(), range) { ProcessEndTime = range.End })) { }
        int latency = node.GetTotalLatencySamples(SampleRate);
        Assert.That(latency, Is.GreaterThan(0));

        using AudioBuffer tail = node.Flush(AudioTestContexts.AtSample(End, latency, SampleRate));

        Assert.That(tail.GetChannelData(0).ToArray().Max(Math.Abs), Is.GreaterThan(0.1f));
    }

    private static float[] Compose(SoundSource source, AudioEffect? effect)
    {
        var sound = new SourceSound
        {
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(4)) },
            Source = { Value = source },
        };
        if (effect != null)
            sound.Effect.Value = effect;

        using var resource = sound.ToResource(CompositionContext.Default);
        using var composer = new Composer { SampleRate = SampleRate };
        var range = new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource),
            range, default, new CompositionEligibility([sound]));
        using AudioBuffer? output = composer.Compose(range, frame);
        return output!.GetChannelData(0).ToArray();
    }

    [Test]
    public void PitchShift_starts_over_at_a_seek()
    {
        const int Target = SampleRate;
        var effect = new PitchShiftEffect { Semitones = { Value = 12 } };
        Func<long, float> source = sample => sample >= Target ? Sine(sample) : 0f;
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(source)));
        using (node.Process(AudioTestContexts.AtSample(0, Window, SampleRate))) { }

        using AudioBuffer after = node.Process(AudioTestContexts.AtSample(Target, Window, SampleRate));

        Assert.That(Pitch(after.GetChannelData(0)[(Window / 2)..]), Is.EqualTo(880).Within(880 * 0.06));
    }

    [Test]
    public void A_disabled_PitchShift_reports_only_what_comes_in()
    {
        var effect = new PitchShiftEffect { IsEnabled = { Value = false } };

        Assert.That(effect.GetLatencySamples(SampleRate, 7), Is.EqualTo(7));
    }

    private static float[] Run(AudioEffect effect, Func<long, float> source, int count)
    {
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(source)));
        var output = new float[count];
        for (int start = 0; start < count; start += Window)
        {
            int part = Math.Min(Window, count - start);
            using AudioBuffer buffer = node.Process(AudioTestContexts.AtSample(start, part, SampleRate));
            buffer.GetChannelData(0).CopyTo(output.AsSpan(start, part));
        }

        return output;
    }

    // The frequency in Hz that the rising zero crossings give.
    private static double Pitch(ReadOnlySpan<float> samples)
    {
        int first = -1;
        int last = -1;
        int crossings = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
            {
                if (first < 0) first = i;
                last = i;
                crossings++;
            }
        }

        return crossings < 2 ? 0 : (crossings - 1) * (double)SampleRate / (last - first);
    }

    private sealed class FunctionSource(Func<long, float> sample) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            long start = (long)Math.Round(context.TimeRange.Start.ToSeconds() * context.SampleRate);
            int count = context.GetSampleCount();
            var buffer = new AudioBuffer(context.SampleRate, 2, count);
            for (int channel = 0; channel < 2; channel++)
            {
                Span<float> data = buffer.GetChannelData(channel);
                for (int i = 0; i < count; i++)
                    data[i] = sample(start + i);
            }

            return RecordProcessedOutput(buffer);
        }
    }
}
