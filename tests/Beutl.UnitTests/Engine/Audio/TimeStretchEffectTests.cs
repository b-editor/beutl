using System.Collections.Immutable;
using System.Reflection;
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

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class TimeStretchEffectTests
{
    private const int SampleRate = 48000;

    [TestCase(25f, 48000)]
    [TestCase(50f, 44100)]
    [TestCase(50f, 48000)]
    [TestCase(150f, 48000)]
    [TestCase(200f, 44100)]
    [TestCase(200f, 48000)]
    [TestCase(400f, 48000)]
    public void Process_PreservesPitchAndStereoChannels(float speed, int sampleRate)
    {
        using var node = CreateNode(speed, new SignalNode(sampleRate,
            (channel, index) => Sine(index, sampleRate, channel == 0 ? 440 : 660)));

        using AudioBuffer output = node.Process(Context(0, sampleRate * 2, sampleRate));

        Assert.That(output.SampleCount, Is.EqualTo(sampleRate * 2));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        for (int channel = 0; channel < 2; channel++)
        {
            ReadOnlySpan<float> data = output.GetChannelData(channel).Slice(sampleRate / 5, sampleRate);
            Assert.That(EstimateFrequency(data, sampleRate), Is.EqualTo(channel == 0 ? 440 : 660).Within(3));
            Assert.That(Rms(data), Is.InRange(0.25, 0.38), "Time stretching must retain audible signal level.");
        }
    }

    [TestCase(50f)]
    [TestCase(200f)]
    public void Process_ScalesTheTimeOfSourceEvents(float speed)
    {
        double factor = speed / 100d;
        using var node = CreateNode(speed, new SignalNode(SampleRate, (_, index) =>
        {
            double time = index / (double)SampleRate;
            return time is >= 1 and < 1.6 ? Sine(index, SampleRate, 440) : 0;
        }));
        using AudioBuffer output = node.Process(Context(0, (int)((2 / factor + 0.5) * SampleRate)));
        ReadOnlySpan<float> data = output.GetChannelData(0);

        Assert.That(WindowRms(data, 0.5 / factor, 0.1 / factor), Is.LessThan(0.005));
        Assert.That(WindowRms(data, 1.25 / factor, 0.1 / factor), Is.GreaterThan(0.25));
        Assert.That(WindowRms(data, 1.9 / factor, 0.1 / factor), Is.LessThan(0.005));
    }

    [Test]
    public void Process_ContinuousChunksMatchSingleRender()
    {
        using var wholeNode = CreateNode(160f, ToneSource());
        using AudioBuffer whole = wholeNode.Process(Context(0, SampleRate));
        using var chunkedNode = CreateNode(160f, ToneSource());
        int[] chunkSizes = [1, 37, 128, 997, 1024, 2048];

        int start = 0;
        int chunk = 0;
        while (start < SampleRate)
        {
            int count = Math.Min(chunkSizes[chunk++ % chunkSizes.Length], SampleRate - start);
            using AudioBuffer part = chunkedNode.Process(Context(start, count));
            for (int channel = 0; channel < 2; channel++)
            {
                Assert.That(part.GetChannelData(channel).ToArray(),
                    Is.EqualTo(whole.GetChannelData(channel).Slice(start, count).ToArray()),
                    $"Chunk at sample {start} changed the continuous output.");
            }
            start += count;
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Process_AtNormalSpeedIsTransparent(int channels)
    {
        var source = new SignalNode(SampleRate, (_, index) => Sine(index, SampleRate, 440), channels);
        using var node = CreateNode(100f, source);
        using AudioBuffer output = node.Process(Context(0, 997));

        Assert.That(output, Is.SameAs(source.Buffers.Single()));
        Assert.That(output.ChannelCount, Is.EqualTo(channels));
    }

    [Test]
    public void Process_SeekResetsBufferedAudio()
    {
        using var node = CreateNode(50f, ToneSource());
        using (node.Process(Context(0, SampleRate))) { }
        using AudioBuffer afterSeek = node.Process(Context(SampleRate / 4, SampleRate / 2));
        using var fresh = CreateNode(50f, ToneSource());
        using AudioBuffer expected = fresh.Process(Context(SampleRate / 4, SampleRate / 2));

        Assert.That(afterSeek.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void Process_ChangingStaticSpeedReanchorsTheSource()
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50f;
        using var node = new SpeedNode { Speed = effect.Speed, PreservePitch = true };
        node.AddInput(ToneSource());
        using (node.Process(Context(0, SampleRate / 2))) { }
        effect.Speed.CurrentValue = 200f;

        using AudioBuffer changed = node.Process(Context(SampleRate / 2, SampleRate / 2));
        using var fresh = CreateNode(200f, ToneSource());
        using AudioBuffer expected = fresh.Process(Context(SampleRate / 2, SampleRate / 2));

        Assert.That(changed.GetChannelData(0).ToArray(), Is.EqualTo(expected.GetChannelData(0).ToArray()));
    }

    [Test]
    public void Process_SwappingTransitiveUpstreamClearsHistory()
    {
        using var upstream = new GainNode { Gain = Property.CreateAnimatable(100f) };
        upstream.AddInput(ToneSource());
        using var node = CreateNode(50f, upstream);
        using (node.Process(Context(0, SampleRate / 2))) { }
        upstream.ClearInputs();
        upstream.AddInput(new SignalNode(SampleRate, (_, _) => 0));

        using AudioBuffer output = node.Process(Context(SampleRate / 2, SampleRate / 2));

        Assert.That(output.GetChannelData(0).ToArray(), Is.All.Zero);
    }

    [Test]
    public void Process_ChangingSampleRateClearsHistory()
    {
        using var node = CreateNode(50f, ToneSource());
        using (node.Process(Context(0, SampleRate / 2))) { }
        node.ClearInputs();
        node.AddInput(new SignalNode(44100, (_, index) => Sine(index, 44100, 440)));

        using AudioBuffer output = node.Process(Context(44100 / 2, 44100, 44100));

        Assert.That(output.SampleRate, Is.EqualTo(44100));
        Assert.That(EstimateFrequency(output.GetChannelData(0).Slice(4410, 22050), 44100), Is.EqualTo(440).Within(3));
    }

    [Test]
    public void Process_DisposesConsumedSourceBuffers()
    {
        var source = ToneSource();
        using var node = CreateNode(200f, source);
        using AudioBuffer output = node.Process(Context(0, SampleRate));

        Assert.That(source.Buffers, Is.Not.Empty);
        foreach (AudioBuffer buffer in source.Buffers)
            Assert.Throws<ObjectDisposedException>(() => buffer.GetChannelData(0));
    }

    [Test]
    public void Process_ShortSourceIsFlushedAndPaddedWithSilence()
    {
        var source = new SignalNode(SampleRate, (_, index) => Sine(index, SampleRate, 440),
            length: SampleRate / 2);
        using var node = CreateNode(200f, source);

        using AudioBuffer output = node.Process(Context(0, SampleRate));

        Assert.That(Rms(output.GetChannelData(0).Slice(0, SampleRate / 8)), Is.GreaterThan(0.2));
        Assert.That(output.GetChannelData(0).Slice(SampleRate / 2).ToArray(), Is.All.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Process_AnimatedSpeedPreservesPitchAndSeekMapping(bool useGlobalClock)
    {
        var effect = new TimeStretchEffect { TimeRange = new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)) };
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = useGlobalClock };
        TimeSpan offset = useGlobalClock ? effect.TimeRange.Start : TimeSpan.Zero;
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = offset, Value = 50f, Easing = new LinearEasing() });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = offset + TimeSpan.FromSeconds(2), Value = 200f, Easing = new LinearEasing() });
        effect.Speed.Animation = animation;
        var source = ToneSource();
        using var node = new SpeedNode { Speed = effect.Speed, PreservePitch = true };
        node.AddInput(source);

        using AudioBuffer output = node.Process(Context(SampleRate, SampleRate));

        // Integral of 0.5 + 0.75*t over [0, 1] = 0.875 source seconds.
        Assert.That(source.Requests[0].Start, Is.EqualTo((long)(0.875 * SampleRate)).Within(1));
        Assert.That(EstimateFrequency(output.GetChannelData(0).Slice(SampleRate / 5, SampleRate / 2), SampleRate),
            Is.EqualTo(440).Within(3));
    }

    [TestCase(128)]
    [TestCase(256)]
    [TestCase(960)]
    [TestCase(2048)]
    public void Process_AnimatedSpeedIntegratesEveryOutputSample(int chunkFrames)
    {
        var effect = new TimeStretchEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 50f,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 200f,
            Easing = new LinearEasing()
        });
        effect.Speed.Animation = animation;
        using var node = new SpeedNode { Speed = effect.Speed, PreservePitch = true };
        node.AddInput(ToneSource());

        for (int start = 0; start < SampleRate; start += chunkFrames)
        {
            using AudioBuffer output = node.Process(Context(start, Math.Min(chunkFrames, SampleRate - start)));
        }

        double expected = 0;
        for (int i = 0; i < SampleRate; i++)
            expected += animation.Interpolate(TimeSpan.FromSeconds(i / (double)SampleRate)) / 100d;
        object processor = typeof(SpeedNode)
            .GetField("_processor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node)!;
        object stretcher = processor.GetType()
            .GetField("_timeStretch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(processor)!;
        double position = (double)stretcher.GetType()
            .GetField("_sourcePosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stretcher)!;
        Assert.That(position, Is.EqualTo(expected).Within(1e-5),
            "Continuous playback must use the same integrated mapping as seeking.");
    }

    [Test]
    public void Process_FortyMillisecondSourceAtFourTimesSpeedEndsAtTenMilliseconds()
    {
        using var node = CreateNode(400f, new SignalNode(SampleRate,
            (_, index) => Sine(index, SampleRate, 440), length: 1920));

        using AudioBuffer output = node.Process(Context(0, 4800));

        Assert.That(Rms(output.GetChannelData(0).Slice(0, 480)), Is.GreaterThan(0.2));
        Assert.That(output.GetChannelData(0).Slice(480).ToArray(), Is.All.Zero,
            "A 1920-frame source must not emit real audio beyond 480 frames at 400%.");
    }

    [Test]
    public void Process_OvershootingAnimationUsesBoundedSpeedForBothSeekingAndPlayback()
    {
        var effect = new TimeStretchEffect();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 25f });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = 400f,
            Easing = new BackEaseIn()
        });
        effect.Speed.Animation = animation;
        Assert.That(animation.Interpolate(TimeSpan.FromSeconds(0.4)), Is.LessThan(0),
            "This case must exercise an easing that would otherwise reverse the source mapping.");
        var source = ToneSource();
        using var node = new SpeedNode { Speed = effect.Speed, PreservePitch = true };
        node.AddInput(source);

        using AudioBuffer output = node.Process(Context(SampleRate / 2, SampleRate / 2));

        double expectedSourceFrames = 0;
        for (int i = 0; i < SampleRate / 2; i++)
        {
            float raw = animation.Interpolate(TimeSpan.FromSeconds(i / (double)SampleRate));
            expectedSourceFrames += Math.Clamp(raw, 25f, 400f) / 100d;
        }
        Assert.That(source.Requests[0].Start, Is.EqualTo(expectedSourceFrames).Within(1));
        Assert.That(EstimateFrequency(output.GetChannelData(0).Slice(SampleRate / 10, SampleRate / 4), SampleRate),
            Is.EqualTo(440).Within(3));
    }

    [Test]
    public void Process_PreservesOppositePhaseStereo()
    {
        using var node = CreateNode(50f, ToneSource());
        using AudioBuffer output = node.Process(Context(0, SampleRate));
        float[] negatedRight = output.GetChannelData(1).ToArray();
        for (int i = 0; i < negatedRight.Length; i++)
            negatedRight[i] = -negatedRight[i];

        Assert.That(negatedRight, Is.EqualTo(output.GetChannelData(0).ToArray()));
        Assert.That(EstimateFrequency(output.GetChannelData(0).Slice(SampleRate / 5, SampleRate / 2), SampleRate),
            Is.EqualTo(440).Within(3));
    }

    [TestCase(50f, 44100, 48000)]
    [TestCase(200f, 44100, 48000)]
    [TestCase(200f, 48000, 44100)]
    public void Composer_AppliesTheEffectToRealWaveAudio(float speed, int sourceRate, int outputRate)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-time-stretch-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 2)))
            {
                for (int i = 0; i < sourceRate * 3; i++)
                {
                    writer.WriteSample(Sine(i, sourceRate, 440));
                    writer.WriteSample(Sine(i, sourceRate, 660));
                }
            }
            var source = new SoundSource();
            source.ReadFrom(new Uri(path));
            var effect = new TimeStretchEffect();
            effect.Speed.CurrentValue = speed;
            var sound = new SourceSound
            {
                TimeRange = new TimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1)),
                Source = { CurrentValue = source },
                Effect = { CurrentValue = effect }
            };
            using var resource = sound.ToResource(CompositionContext.Default);
            using var composer = new Composer { SampleRate = outputRate };
            var range = new TimeRange(sound.Start, TimeSpan.FromSeconds(1));
            var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource),
                range, default, new CompositionEligibility([sound]));

            using AudioBuffer? output = composer.Compose(range, frame);

            Assert.That(output, Is.Not.Null);
            Assert.That(EstimateFrequency(output!.GetChannelData(0).Slice(outputRate / 5, outputRate / 2), outputRate),
                Is.EqualTo(440).Within(3));
            Assert.That(EstimateFrequency(output.GetChannelData(1).Slice(outputRate / 5, outputRate / 2), outputRate),
                Is.EqualTo(660).Within(3));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }

    [TestCase(48000, 48000, 1920, 400f)]
    [TestCase(48000, 48000, 24000, 200f)]
    [TestCase(44100, 48000, 1764, 400f)]
    [TestCase(48000, 48000, 240, 25f)]
    public void Composer_ShortWaveStopsAtItsScaledEndAndKeepsSlowedAudio(
        int sourceRate, int outputRate, int sourceFrames, float speed)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-short-stretch-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 2)))
            {
                for (int i = 0; i < sourceFrames; i++)
                {
                    float value = Sine(i, sourceRate, 440);
                    writer.WriteSample(value);
                    writer.WriteSample(-value);
                }
            }
            var source = new SoundSource();
            source.ReadFrom(new Uri(path));
            var effect = new TimeStretchEffect();
            effect.Speed.CurrentValue = speed;
            var sound = new SourceSound
            {
                TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                Source = { CurrentValue = source },
                Effect = { CurrentValue = effect },
                Gain = { CurrentValue = 75f }
            };
            using var resource = sound.ToResource(CompositionContext.Default);
            using var composer = new Composer { SampleRate = outputRate };
            var range = sound.TimeRange;
            var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource),
                range, default, new CompositionEligibility([sound]));

            using AudioBuffer? output = composer.Compose(range, frame);

            Assert.That(output, Is.Not.Null);
            int expected = (int)Math.Ceiling(sourceFrames / (double)sourceRate * outputRate / (speed / 100d));
            Assert.That(output!.GetChannelData(0).Slice(expected).ToArray(), Is.All.Zero,
                "Silence padding in SourceNode must not extend the real input budget.");
            if (speed < 100)
            {
                for (int start = 0; start < expected; start += 240)
                    Assert.That(Rms(output.GetChannelData(0).Slice(start, Math.Min(240, expected - start))),
                        Is.GreaterThan(0.15), "A slowed short sound must remain audible after its original duration.");
            }
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }

    [Test]
    public void Flush_DrainsWithoutReadingLiveSource()
    {
        var source = ToneSource();
        using var node = CreateNode(200f, source);
        using (node.Process(Context(0, SampleRate / 2))) { }
        int processCalls = source.Requests.Count;

        using AudioBuffer tail = node.Flush(Context(SampleRate / 2, SampleRate / 2));

        Assert.That(source.Requests.Count, Is.EqualTo(processCalls));
        Assert.That(tail.GetChannelData(0).Slice(SampleRate / 4).ToArray(), Is.All.Zero);
    }

    [TestCase(50f, 9600)]
    [TestCase(200f, 2400)]
    public void Latency_ScalesUpstreamDelayWithoutAddingLeadingSilence(float speed, int expected)
    {
        using var node = CreateNode(speed, new SignalNode(SampleRate, (_, _) => 0, latency: 4800));

        Assert.That(node.GetLatencySamples(SampleRate), Is.Zero);
        Assert.That(node.GetTotalLatencySamples(SampleRate), Is.EqualTo(expected));
        Assert.That(node.GetDrainLatencySamples(SampleRate), Is.EqualTo(expected));
    }

    [Test]
    public void CreateNode_ReusesTheProcessorAcrossGraphUpdates()
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 50f;
        var source = ToneSource();
        using var context = new AudioContext(SampleRate, 2);
        AudioNode node = effect.CreateNode(context, source);
        using (node.Process(Context(0, SampleRate / 2))) { }

        context.BeginUpdate(context.Nodes.ToArray());
        AudioNode reused = effect.CreateNode(context, source);
        context.EndUpdate();

        Assert.That(reused, Is.SameAs(node));
        Assert.That(((SpeedNode)reused).PreservePitch, Is.True);
    }

    [TestCase(50f, false)]
    [TestCase(200f, false)]
    [TestCase(50f, true)]
    [TestCase(200f, true)]
    public void EffectGroup_ReportsTheSameScaledLatencyAsItsGraph(float speed, bool nested)
    {
        var limiter = new LimiterEffect();
        limiter.Lookahead.CurrentValue = 10f;
        var stretch = new TimeStretchEffect();
        stretch.Speed.CurrentValue = speed;
        var group = new AudioEffectGroup();
        group.Children.Add(limiter);
        if (nested)
        {
            var inner = new AudioEffectGroup();
            inner.Children.Add(stretch);
            group.Children.Add(inner);
        }
        else
        {
            group.Children.Add(stretch);
        }
        using var context = new AudioContext(SampleRate, 2);
        AudioNode output = group.CreateNode(context, ToneSource());

        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo(output.GetTotalLatencySamples(SampleRate)));
        Assert.That(group.GetLatencySamples(SampleRate), Is.EqualTo((int)(480 / (speed / 100d))));
    }

    [Test]
    public void Serialization_PreservesTheEffectAndSpeedKeyframes()
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = 150f;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 50f });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 200f });
        effect.Speed.Animation = animation;
        var group = new AudioEffectGroup();
        group.Children.Add(effect);

        var json = CoreSerializer.SerializeToJsonObject(group);
        var restored = (AudioEffectGroup)CoreSerializer.DeserializeFromJsonObject(json, typeof(AudioEffectGroup));

        Assert.That(restored.Children.Single(), Is.TypeOf<TimeStretchEffect>());
        var restoredEffect = (TimeStretchEffect)restored.Children.Single();
        Assert.That(restoredEffect.Speed.CurrentValue, Is.EqualTo(150f));
        Assert.That(restoredEffect.Speed.Animation!.Interpolate(TimeSpan.Zero), Is.EqualTo(50f));
        Assert.That(restoredEffect.Speed.Animation.Interpolate(TimeSpan.FromSeconds(1)), Is.EqualTo(200f));
    }

    private static SpeedNode CreateNode(float speed, AudioNode source)
    {
        var effect = new TimeStretchEffect();
        effect.Speed.CurrentValue = speed;
        var node = new SpeedNode { Speed = effect.Speed, PreservePitch = true };
        node.AddInput(source);
        return node;
    }

    private static SignalNode ToneSource()
        => new(SampleRate, (channel, index) => (channel == 0 ? 1 : -1) * Sine(index, SampleRate, 440));

    private static float Sine(long index, int sampleRate, double frequency)
        => (float)(0.5 * Math.Sin(2 * Math.PI * frequency * index / sampleRate));

    private static AudioProcessContext Context(int start, int count, int sampleRate = SampleRate)
        => new(new TimeRange(
                TimeSpan.FromTicks((long)Math.Ceiling(start * (double)TimeSpan.TicksPerSecond / sampleRate)),
                AudioProcessContext.GetDurationForSampleCount(count, sampleRate)),
            sampleRate, new AnimationSampler(), null);

    private static double Rms(ReadOnlySpan<float> data)
    {
        double sum = 0;
        foreach (float value in data)
            sum += value * value;
        return Math.Sqrt(sum / data.Length);
    }

    private static double WindowRms(ReadOnlySpan<float> data, double start, double duration)
        => Rms(data.Slice((int)(start * SampleRate), (int)(duration * SampleRate)));

    private static double EstimateFrequency(ReadOnlySpan<float> data, int sampleRate)
    {
        int crossings = 0;
        double first = 0;
        double last = 0;
        for (int i = 1; i < data.Length; i++)
        {
            if (data[i - 1] <= 0 && data[i] > 0)
            {
                double position = i - 1 + -data[i - 1] / (double)(data[i] - data[i - 1]);
                if (crossings++ == 0)
                    first = position;
                last = position;
            }
        }
        return (crossings - 1) * sampleRate / (last - first);
    }

    private sealed class SignalNode(
        int sampleRate, Func<int, long, float> signal, int channels = 2, long length = long.MaxValue,
        int latency = 0) : AudioNode
    {
        public List<(long Start, int Count)> Requests { get; } = [];
        public List<AudioBuffer> Buffers { get; } = [];

        public override AudioBuffer Process(AudioProcessContext context)
        {
            long start = AudioMath.TimeToSampleIndex(context.TimeRange.Start, sampleRate);
            int count = (int)Math.Min(context.GetSampleCount(), Math.Max(0, length - start));
            Requests.Add((start, count));
            var buffer = new AudioBuffer(sampleRate, channels, count);
            Buffers.Add(buffer);
            for (int channel = 0; channel < channels; channel++)
            {
                Span<float> data = buffer.GetChannelData(channel);
                for (int i = 0; i < count; i++)
                    data[i] = signal(channel, start + i);
            }
            return RecordProcessedOutput(buffer);
        }

        public override int GetLatencySamples(int rate) => latency;
    }
}
