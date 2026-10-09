using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.Serialization;
using NAudio.Wave;

using static Beutl.Audio.Effects.NoiseReductionParameters;
using static Beutl.UnitTests.Engine.Audio.SpectralNoiseReducerTests;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class NoiseReductionEffectTests
{
    private const int SampleRate = 48000;

    private static readonly float[] s_noise = Noise(SampleRate * 8, 0.05, 21);
    private static readonly float[] s_signal = Add(s_noise, ToneBursts(SampleRate * 8, 0.3f));

    [Test]
    public void CreateNode_WiresEveryPropertyAndConnectsTheInput()
    {
        var effect = new NoiseReductionEffect();
        using var context = new AudioContext(SampleRate, 2);
        var input = context.AddNode(new SignalSource(s_signal));

        var node = (NoiseReductionNode)effect.CreateNode(context, input);

        Assert.That(node.Reduction, Is.SameAs(effect.Reduction));
        Assert.That(node.Sensitivity, Is.SameAs(effect.Sensitivity));
        Assert.That(node.Smoothing, Is.SameAs(effect.Smoothing));
        Assert.That(node.Adaptation, Is.SameAs(effect.Adaptation));
        Assert.That(node.Inputs, Is.EqualTo(new[] { input }));
    }

    [Test]
    public void CreateNode_ReusesTheNodeAcrossGraphUpdates()
    {
        var effect = new NoiseReductionEffect();
        var input = new SignalSource(s_signal);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, input);
        using (node.Process(Context(0, SampleRate / 2))) { }

        context.BeginUpdate(context.Nodes.ToArray());
        AudioNode reused = effect.CreateNode(context, input);
        AudioNode other = new NoiseReductionEffect().CreateNode(context, input);
        context.EndUpdate();

        Assert.That(reused, Is.SameAs(node));
        Assert.That(other, Is.Not.SameAs(node));
    }

    [Test]
    public void Properties_StartAtTheirDefaults()
    {
        var effect = new NoiseReductionEffect();

        Assert.That(effect.Reduction.CurrentValue, Is.EqualTo(DefaultReductionDb));
        Assert.That(effect.Sensitivity.CurrentValue, Is.EqualTo(DefaultSensitivityDb));
        Assert.That(effect.Smoothing.CurrentValue, Is.EqualTo(DefaultSmoothing));
        Assert.That(effect.Adaptation.CurrentValue, Is.EqualTo(DefaultAdaptationSeconds));
    }

    [Test]
    public void ScanProperties_RegistersNamesAndRanges()
    {
        var effect = new NoiseReductionEffect();

        AssertNameAndRange(effect.Reduction, nameof(effect.Reduction), MinReductionDb, MaxReductionDb);
        AssertNameAndRange(effect.Sensitivity, nameof(effect.Sensitivity), MinSensitivityDb, MaxSensitivityDb);
        AssertNameAndRange(effect.Smoothing, nameof(effect.Smoothing), MinSmoothing, MaxSmoothing);
        AssertNameAndRange(effect.Adaptation, nameof(effect.Adaptation), MinAdaptationSeconds, MaxAdaptationSeconds);
    }

    private static IEnumerable<TestCaseData> ParameterRanges()
    {
        yield return new TestCaseData(MinReductionDb, DefaultReductionDb, MaxReductionDb).SetName("Reduction");
        yield return new TestCaseData(MinSensitivityDb, DefaultSensitivityDb, MaxSensitivityDb).SetName("Sensitivity");
        yield return new TestCaseData(MinSmoothing, DefaultSmoothing, MaxSmoothing).SetName("Smoothing");
        yield return new TestCaseData(MinAdaptationSeconds, DefaultAdaptationSeconds, MaxAdaptationSeconds).SetName("Adaptation");
    }

    [TestCaseSource(nameof(ParameterRanges))]
    public void NoiseReductionParameters_RangeIsConsistent(float min, float def, float max)
    {
        Assert.That(min, Is.LessThan(max));
        Assert.That(def, Is.InRange(min, max));
    }

    [Test]
    public void Normalize_ReplacesNonFiniteValuesAndClamps()
    {
        NoiseReductionSettings settings = Normalize(float.NaN, float.PositiveInfinity, -50, 1000);

        Assert.That(settings, Is.EqualTo(new NoiseReductionSettings(
            DefaultReductionDb, DefaultSensitivityDb, MinSmoothing, MaxAdaptationSeconds)));
    }

    [Test]
    public void Latency_IsNotReported()
    {
        var effect = new NoiseReductionEffect();
        var input = new SignalSource(s_signal, latency: 120);
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, input);

        Assert.That(effect.GetLatencySamples(SampleRate), Is.Zero);
        Assert.That(node.GetLatencySamples(SampleRate), Is.Zero);
        Assert.That(node.GetTotalLatencySamples(SampleRate), Is.EqualTo(120));
        Assert.That(node.GetDrainLatencySamples(SampleRate), Is.EqualTo(120));
    }

    [Test]
    public void ZeroReduction_IsTransparentAcrossChunksAndSeeks()
    {
        var source = new SignalSource(s_signal);
        NoiseReductionNode node = CreateNode(source, reduction: 0);

        foreach (var (start, count) in new[] { (0, 1000), (1000, 4000), (5000, 1), (5001, 48000), (200000, 9600), (100, 2000) })
        {
            using AudioBuffer output = node.Process(Context(start, count));

            Assert.That(output.GetChannelData(0).ToArray(), Is.EqualTo(s_signal.AsSpan(start, count).ToArray()),
                $"The output must be the input at [{start}, {start + count}) without any delay.");
            Assert.That(output.GetChannelData(1).ToArray(), Is.EqualTo(s_signal.AsSpan(start, count).ToArray()));
        }
    }

    [Test]
    public void Process_ReducesNoiseWithoutMovingTheBursts()
    {
        NoiseReductionNode node = CreateNode(new SignalSource(s_signal));

        using AudioBuffer output = node.Process(Context(0, SampleRate * 4));

        float[] left = output.GetChannelData(0).ToArray();
        float[] input = s_signal.AsSpan(0, SampleRate * 4).ToArray();
        float[] bursts = ToneBursts(SampleRate * 4, 0.3f);

        // Subtracting the clean bursts leaves only noise when the output is aligned; a two-sample shift
        // of the 1 kHz bursts alone would leave more than the input noise.
        Assert.That(Decibels(Power(Subtract(left, bursts), SampleRate, SampleRate * 4)
                             / Power(Subtract(input, bursts), SampleRate, SampleRate * 4)),
            Is.LessThan(-6));
    }

    [Test]
    public void Restart_MapsAFractionalStartToTheUpstreamSample()
    {
        NoiseReductionNode node = CreateNode(new SignalSource(s_signal), reduction: 0);

        // 313 ticks are 1.5 samples at 48 kHz; upstream nodes truncate this start to sample 1.
        var context = new AudioProcessContext(
            new TimeRange(TimeSpan.FromTicks(313), AudioProcessContext.GetDurationForSampleCount(4800, SampleRate)),
            SampleRate, new AnimationSampler(), null);
        using AudioBuffer output = node.Process(context);
        using AudioBuffer expected = new SignalSource(s_signal).Process(context);

        Assert.That(output.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void Process_ContinuousChunksMatchASingleRender()
    {
        NoiseReductionNode whole = CreateNode(new SignalSource(s_signal));
        NoiseReductionNode chunked = CreateNode(new SignalSource(s_signal));

        using AudioBuffer expected = whole.Process(Context(0, SampleRate * 3));
        var actual = new List<float>();
        int position = 0;
        foreach (int count in new[] { 1, 37, 4800, 16384, 47999, 48000, 26779 })
        {
            using AudioBuffer output = chunked.Process(Context(position, count));
            actual.AddRange(output.GetChannelData(0).ToArray());
            position += count;
        }

        Assert.That(position, Is.EqualTo(SampleRate * 3));
        Assert.That(actual, Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void Process_SeekRestartsIndependentlyOfEarlierAudio()
    {
        NoiseReductionNode seeking = CreateNode(new SignalSource(s_signal));
        NoiseReductionNode fresh = CreateNode(new SignalSource(s_signal));

        using (seeking.Process(Context(0, SampleRate))) { }
        using AudioBuffer afterSeek = seeking.Process(Context(SampleRate * 3, SampleRate));
        using AudioBuffer expected = fresh.Process(Context(SampleRate * 3, SampleRate));

        Assert.That(afterSeek.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void Process_DoesNotReadPastTheClipEnd()
    {
        const int duration = SampleRate;
        float[] trimmed = s_signal.AsSpan(0, duration).ToArray();
        var loudAfterEnd = new SignalSource(s_signal.Select((value, index) => index < duration ? value : 0.9f).ToArray());
        var silentAfterEnd = new SignalSource(trimmed);

        float[] loud = RenderClip(loudAfterEnd, duration, duration + 4800);
        float[] silent = RenderClip(silentAfterEnd, duration, duration + 4800);

        Assert.That(loudAfterEnd.LastReadEnd, Is.LessThanOrEqualTo(duration));
        Assert.That(loud, Is.EqualTo(silent), "Audio after the trim point must not affect the output.");
    }

    [Test]
    public void ZeroReduction_DrainsTheUpstreamTailLikeAGraphWithoutTheEffect()
    {
        const int duration = SampleRate;
        float[] withEffect = RenderClipWithLimiter(includeEffect: true, duration);
        float[] withoutEffect = RenderClipWithLimiter(includeEffect: false, duration);

        Assert.That(withEffect, Is.EqualTo(withoutEffect));
        Assert.That(withEffect.AsSpan(duration, 480).ToArray(), Has.Some.Not.EqualTo(0f),
            "The limiter's held samples must reach the clip's padding.");
    }

    [Test]
    public void Flush_ProcessesTheUpstreamTail()
    {
        var source = new SignalSource(s_signal);
        var limiter = new LimiterEffect();
        limiter.Lookahead.CurrentValue = 10;
        limiter.Threshold.CurrentValue = 0;
        using var context = new AudioContext(SampleRate, 2);
        context.AddNode(source);
        AudioNode limited = limiter.CreateNode(context, source);
        AudioNode reduced = new NoiseReductionEffect().CreateNode(context, limited);

        using (reduced.Process(Context(0, SampleRate))) { }
        int latency = reduced.GetDrainLatencySamples(SampleRate);
        using AudioBuffer tail = reduced.Flush(Context(SampleRate, latency));

        Assert.That(latency, Is.EqualTo(480));
        Assert.That(tail.SampleCount, Is.EqualTo(480));
        Assert.That(tail.GetChannelData(0).ToArray(), Has.Some.Not.EqualTo(0f));
    }

    [Test]
    public void Flush_AfterASeekReturnsSilence()
    {
        NoiseReductionNode node = CreateNode(new SignalSource(s_signal), reduction: 0);
        using (node.Process(Context(0, SampleRate))) { }

        using AudioBuffer unprocessed = CreateNode(new SignalSource(s_signal)).Flush(Context(0, 480));
        using AudioBuffer detached = node.Flush(Context(SampleRate * 2, 480));

        Assert.That(unprocessed.GetChannelData(0).ToArray(), Is.All.EqualTo(0f));
        Assert.That(detached.GetChannelData(0).ToArray(), Is.All.EqualTo(0f));
    }

    [Test]
    public void ShrinkingTheTerminal_DiscardsAudioReadPastIt()
    {
        NoiseReductionNode reused = CreateNode(new SignalSource(s_signal));
        NoiseReductionNode fresh = CreateNode(new SignalSource(s_signal));
        TimeSpan longEnd = TimeSpan.FromSeconds(2);

        // Processing up to one second reads a frame ahead, past this earlier end.
        TimeSpan shortEnd = AudioProcessContext.GetDurationForSampleCount(SampleRate + 400, SampleRate);

        using (reused.Process(Context(0, SampleRate, longEnd))) { }
        using AudioBuffer actual = reused.Process(Context(SampleRate, SampleRate / 2, shortEnd));
        using AudioBuffer expected = fresh.Process(Context(SampleRate, SampleRate / 2, shortEnd));

        Assert.That(actual.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
        Assert.That(actual.GetChannelData(0).Slice(400 + 1024).ToArray(), Is.All.EqualTo(0f),
            "Nothing is left to play a frame after the new end.");
    }

    [Test]
    public void ExtendingTheTerminal_PlaysTheNewlyExposedAudio()
    {
        NoiseReductionNode reused = CreateNode(new SignalSource(s_signal), reduction: 0);
        TimeSpan shortEnd = TimeSpan.FromSeconds(1);
        TimeSpan longEnd = TimeSpan.FromSeconds(2);

        using (reused.Process(Context(0, SampleRate, shortEnd))) { }
        using AudioBuffer extended = reused.Process(Context(SampleRate, SampleRate / 2, longEnd));

        Assert.That(extended.GetChannelData(0).ToArray(),
            Is.EqualTo(s_signal.AsSpan(SampleRate, SampleRate / 2).ToArray()));
    }

    [Test]
    public void ReplacingTheUpstream_RestartsTheAnalysis()
    {
        float[] other = Noise(SampleRate * 8, 0.2, 22);
        NoiseReductionNode node = CreateNode(new SignalSource(s_signal));
        NoiseReductionNode fresh = CreateNode(new SignalSource(other));
        using (node.Process(Context(0, SampleRate))) { }

        node.RemoveInput(node.Inputs[0]);
        node.AddInput(new SignalSource(other));
        using AudioBuffer actual = node.Process(Context(SampleRate, SampleRate / 2));
        using AudioBuffer expected = fresh.Process(Context(SampleRate, SampleRate / 2));

        Assert.That(actual.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void ChangingTheSampleRate_RestartsTheAnalysis()
    {
        NoiseReductionNode node = CreateNode(new SignalSource(s_signal), reduction: 0);
        using (node.Process(Context(0, SampleRate))) { }

        using AudioBuffer output = node.Process(Context(0, 44100, sampleRate: 44100));

        Assert.That(output.SampleRate, Is.EqualTo(44100));
        Assert.That(output.SampleCount, Is.EqualTo(44100));
    }

    [Test]
    public void AnimatedReduction_FollowsItsKeyframes()
    {
        var effect = new NoiseReductionEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 0, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 0, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1.5), Value = 24, Easing = new LinearEasing() });
        effect.Reduction.Animation = animation;
        using var context = new AudioContext(SampleRate, 2);
        var node = (NoiseReductionNode)effect.CreateNode(context, new SignalSource(s_noise));

        using AudioBuffer output = node.Process(Context(0, SampleRate * 3));

        float[] left = output.GetChannelData(0).ToArray();
        Assert.That(left.AsSpan(0, SampleRate - 1024).ToArray(), Is.EqualTo(s_noise.AsSpan(0, SampleRate - 1024).ToArray()));
        Assert.That(Decibels(Power(left, SampleRate * 2, SampleRate * 3) / Power(s_noise, SampleRate * 2, SampleRate * 3)),
            Is.LessThan(-20));
    }

    [Test]
    public void Serialization_PreservesTheEffectAndItsKeyframes()
    {
        var effect = new NoiseReductionEffect();
        effect.Reduction.CurrentValue = 18;
        effect.Sensitivity.CurrentValue = 6;
        effect.Smoothing.CurrentValue = 80;
        effect.Adaptation.CurrentValue = 4;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 6 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 30 });
        effect.Reduction.Animation = animation;
        var group = new AudioEffectGroup();
        group.Children.Add(effect);

        var json = CoreSerializer.SerializeToJsonObject(group);
        var restored = (AudioEffectGroup)CoreSerializer.DeserializeFromJsonObject(json, typeof(AudioEffectGroup));

        var restoredEffect = (NoiseReductionEffect)restored.Children.Single();
        Assert.That(restoredEffect.Reduction.CurrentValue, Is.EqualTo(18));
        Assert.That(restoredEffect.Sensitivity.CurrentValue, Is.EqualTo(6));
        Assert.That(restoredEffect.Smoothing.CurrentValue, Is.EqualTo(80));
        Assert.That(restoredEffect.Adaptation.CurrentValue, Is.EqualTo(4));
        Assert.That(restoredEffect.Reduction.Animation!.Interpolate(TimeSpan.FromSeconds(1)), Is.EqualTo(30));
    }

    [TestCase(0f)]
    [TestCase(12f)]
    public void Composer_AppliesTheEffectToRealWaveAudio(float reduction)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-noise-reduction-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)))
            {
                for (int i = 0; i < SampleRate * 4; i++)
                {
                    writer.WriteSample(s_signal[i]);
                    writer.WriteSample(s_signal[i]);
                }
            }

            var source = new SoundSource();
            source.ReadFrom(new Uri(path));
            var effect = new NoiseReductionEffect();
            effect.Reduction.CurrentValue = reduction;
            float[] processed = ComposeSound(source, effect);
            float[] original = ComposeSound(source, null);

            if (reduction == 0)
            {
                Assert.That(processed, Is.EqualTo(original), "At 0 dB the effect must not delay or alter the audio.");
            }
            else
            {
                float[] bursts = ToneBursts(SampleRate * 4, 0.3f).AsSpan(SampleRate, SampleRate * 2).ToArray();
                Assert.That(Decibels(Power(Subtract(processed, bursts), 0, SampleRate * 2)
                                     / Power(Subtract(original, bursts), 0, SampleRate * 2)),
                    Is.LessThan(-6));
            }
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }

    // Composes seconds 1 to 3 of a sound that starts at 0 and plays the whole source.
    private static float[] ComposeSound(SoundSource source, AudioEffect? effect)
    {
        var sound = new SourceSound
        {
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(4)),
            Source = { CurrentValue = source },
        };
        if (effect != null)
            sound.Effect.CurrentValue = effect;

        using var resource = sound.ToResource(CompositionContext.Default);
        using var composer = new Composer { SampleRate = SampleRate };
        var range = new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource),
            range, default, new CompositionEligibility([sound]));
        using AudioBuffer? output = composer.Compose(range, frame);
        return output!.GetChannelData(0).ToArray();
    }

    private static float[] RenderClip(AudioNode source, int duration, int window)
    {
        using var context = new AudioContext(SampleRate, 2);
        context.AddNode(source);
        AudioNode reduced = new NoiseReductionEffect().CreateNode(context, source);
        ClipNode clip = context.CreateClipNode(TimeSpan.Zero, AudioProcessContext.GetDurationForSampleCount(duration, SampleRate));
        context.Connect(reduced, clip);
        using AudioBuffer output = clip.Process(Context(0, window));
        return output.GetChannelData(0).ToArray();
    }

    private static float[] RenderClipWithLimiter(bool includeEffect, int duration)
    {
        var source = new SignalSource(s_signal);
        var limiter = new LimiterEffect();
        limiter.Lookahead.CurrentValue = 10;
        limiter.Threshold.CurrentValue = -12;
        var effect = new NoiseReductionEffect();
        effect.Reduction.CurrentValue = 0;
        using var context = new AudioContext(SampleRate, 2);
        context.AddNode(source);
        AudioNode node = limiter.CreateNode(context, source);
        if (includeEffect)
            node = effect.CreateNode(context, node);
        ClipNode clip = context.CreateClipNode(TimeSpan.Zero, AudioProcessContext.GetDurationForSampleCount(duration, SampleRate));
        context.Connect(node, clip);
        using AudioBuffer output = clip.Process(Context(0, duration + 960));
        return output.GetChannelData(0).ToArray();
    }

    private static NoiseReductionNode CreateNode(AudioNode source, float reduction = DefaultReductionDb)
    {
        var effect = new NoiseReductionEffect();
        effect.Reduction.CurrentValue = reduction;
        var node = new NoiseReductionNode
        {
            Reduction = effect.Reduction,
            Sensitivity = effect.Sensitivity,
            Smoothing = effect.Smoothing,
            Adaptation = effect.Adaptation
        };
        node.AddInput(source);
        return node;
    }

    private static void AssertNameAndRange(IProperty<float> property, string expectedName, float min, float max)
    {
        Assert.That(property.Name, Is.EqualTo(expectedName));

        property.CurrentValue = max + 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(max), $"{expectedName} must be clamped to its maximum.");

        property.CurrentValue = min - 1000f;
        Assert.That(property.CurrentValue, Is.EqualTo(min), $"{expectedName} must be clamped to its minimum.");
    }

    private static float[] Subtract(float[] first, float[] second)
        => first.Zip(second, (a, b) => a - b).ToArray();

    private static AudioProcessContext Context(int start, int count, TimeSpan? end = null, int sampleRate = SampleRate)
        => new(new TimeRange(
                TimeSpan.FromTicks((long)Math.Ceiling(start * (double)TimeSpan.TicksPerSecond / sampleRate)),
                AudioProcessContext.GetDurationForSampleCount(count, sampleRate)),
            sampleRate, new AnimationSampler(), null)
        {
            ProcessEndTime = end
        };

    // Plays a fixed buffer on both channels at absolute sample positions and silence elsewhere.
    private sealed class SignalSource(float[] samples, int latency = 0) : AudioNode
    {
        public long LastReadEnd { get; private set; }

        public override AudioBuffer Process(AudioProcessContext context)
        {
            long start = AudioMath.TimeToSampleIndex(context.TimeRange.Start, context.SampleRate);
            int count = context.GetSampleCount();
            LastReadEnd = Math.Max(LastReadEnd, start + count);
            var buffer = new AudioBuffer(context.SampleRate, 2, count);
            for (int channel = 0; channel < 2; channel++)
            {
                Span<float> data = buffer.GetChannelData(channel);
                for (int i = 0; i < count; i++)
                {
                    long index = start + i;
                    data[i] = index >= 0 && index < samples.Length ? samples[index] : 0f;
                }
            }

            return RecordProcessedOutput(buffer);
        }

        public override int GetLatencySamples(int sampleRate) => latency;
    }
}
