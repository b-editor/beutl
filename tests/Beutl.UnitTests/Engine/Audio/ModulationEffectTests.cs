using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Media;

using static Beutl.UnitTests.Engine.Audio.AudioEffectAssertions;

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
    public void PitchShift_at_no_shift_is_the_input_half_a_window_later()
    {
        var effect = new PitchShiftEffect();
        int delay = effect.GetLatencySamples(SampleRate) / 2;

        float[] output = Run(effect, sample => Sine(sample), Window * 2);

        for (int i = delay; i < output.Length; i++)
            Assert.That(output[i], Is.EqualTo(Sine(i - delay)).Within(0.0001f), $"sample {i}");
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

    [Test]
    public void PitchShift_reports_the_tail_it_holds_and_lets_it_out_after_the_input_ends()
    {
        var effect = new PitchShiftEffect();
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, context.AddNode(new FunctionSource(sample => sample < Window ? 0.5f : 0f)));
        int latency = node.GetTotalLatencySamples(SampleRate);
        Assert.That(effect.GetLatencySamples(SampleRate), Is.EqualTo(latency));

        using (node.Process(AudioTestContexts.AtSample(0, Window, SampleRate))) { }
        using AudioBuffer tail = node.Flush(AudioTestContexts.AtSample(Window, latency, SampleRate));

        float first = tail.GetChannelData(0)[0];
        float last = tail.GetChannelData(0)[latency - 1];
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(last, Is.Zero);
        });
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
