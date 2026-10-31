using System.Collections.Immutable;
using System.Reflection;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class MultichannelEngineTests
{
    private const int Rate = 44100;
    private static readonly TimeRange s_window = new(MediaTime.Zero, MediaTime.FromSeconds(0.1));

    private static SourceSound Sound(SoundSource source, double seconds = 1) => new()
    {
        Source = { Value = source },
        TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(seconds)) }
    };

    private static AudioBuffer Compose(Sound sound, Composer composer, TimeRange? range = null)
    {
        TimeRange window = range ?? s_window;
        using var resource = sound.ToResource(CompositionContext.Default);
        var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource), window,
            default, new CompositionEligibility([sound]));
        return composer.Compose(window, frame)!;
    }

    [TestCase(100f, 44100)]
    [TestCase(150f, 44100)]
    [TestCase(100f, 48000)]
    [TestCase(150f, 48000)]
    public void MonoThroughSpeedAndResampling_MatchesDuplicatedStereo(float speed, int sourceRate)
    {
        float[][] Render(int channels)
        {
            float[][] result = [];
            AudioTestWaves.WithTemporaryWave(sourceRate, sourceRate,
                (_, i) => ((i * 17 % 101) - 50) / 256f, source =>
                {
                    var sound = Sound(source);
                    sound.Speed.Value = speed;
                    using var context = new AudioContext(Rate, AudioChannelLayout.Stereo);
                    using var resource = sound.ToResource(CompositionContext.Default);
                    sound.Compose(context, resource);
                    Assert.That(context.Nodes.OfType<SourceNode>().Single().ClipLayout.ChannelCount, Is.EqualTo(channels));
                    Assert.That(context.Nodes.OfType<SpeedNode>().Single().ChannelCount, Is.EqualTo(channels));
                    using var composer = new Composer { SampleRate = Rate };
                    using var audio = Compose(sound, composer);
                    result = Enumerable.Range(0, 2).Select(ch => audio.GetChannelData(ch).ToArray()).ToArray();
                }, channels);
            return result;
        }
        var mono = Render(1);
        var stereo = Render(2);
        for (int ch = 0; ch < 2; ch++)
            Assert.That(mono[ch].Select(BitConverter.SingleToInt32Bits), Is.EqualTo(stereo[ch].Select(BitConverter.SingleToInt32Bits)));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    public void SurroundDefaults_PreserveChannelsAndLeaveUnusedSpeakersSilent(int channels)
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, (ch, i) => ((i * 17 + ch * 11) % 101 - 50) / 128f, source =>
        {
            using var composer = new Composer { SampleRate = Rate, ChannelLayout = AudioChannelLayout.Surround51 };
            using var audio = Compose(Sound(source), composer);
            Assert.That(audio.ChannelCount, Is.EqualTo(6));
            for (int ch = 0; ch < 6; ch++)
            {
                int sourceCh = Math.Min(ch, channels - 1);
                var expected = Enumerable.Range(0, audio.SampleCount).Select(i => channels < 6 && ch >= 2
                    ? 0f : ((i * 17 + sourceCh * 11) % 101 - 50) / 128f);
                Assert.That(audio.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                    Is.EqualTo(expected.Select(BitConverter.SingleToInt32Bits)));
            }
        }, channels, channels == 6 ? AudioChannelLayout.Surround51.Speakers : null);
    }

    [TestCase("speed")]
    [TestCase("stretch")]
    [TestCase("noise")]
    public void SurroundProcessing_KeepsTheSignalInItsChannel(string processing)
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, (ch, i) => ch == 4
            ? ((i * 17 % 101) - 50) / 256f : 0, source =>
        {
            var sound = Sound(source);
            if (processing == "speed") sound.Speed.Value = 150;
            if (processing == "stretch") sound.Effect.Value = new TimeStretchEffect { Speed = { Value = 150 } };
            if (processing == "noise") sound.Effect.Value = new NoiseReductionEffect();
            using var composer = new Composer { SampleRate = Rate, ChannelLayout = AudioChannelLayout.Surround51 };
            using var audio = Compose(sound, composer);
            Assert.That(audio.ChannelCount, Is.EqualTo(6));
            Assert.That(audio.GetChannelData(4).ToArray().Any(v => Math.Abs(v) > 1e-4f), Is.True);
            foreach (int ch in new[] { 0, 1, 2, 3, 5 })
                Assert.That(audio.GetChannelData(ch).ToArray(), Is.All.EqualTo(0).Within(1e-6));
        }, 6, AudioChannelLayout.Surround51.Speakers);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SurroundDelay_DrainsThroughPannerAndGroup(bool grouped)
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, (_, i) => i == 400 ? 0.25f : 0, source =>
        {
            var first = Sound(source, 0.03);
            first.Effect.Value = new DelayEffect
            {
                DelayTime = { Value = 20 }, Feedback = { Value = 0 }, DryMix = { Value = 100 }, WetMix = { Value = 100 }
            };
            // Delay is intentional timing, not latency. A lookahead retains its echo for Flush.
            first.Effect.Value = new AudioEffectGroup
            {
                Children = { first.Effect.Value!, new LimiterEffect { Lookahead = { Value = 20 } } }
            };
            Sound sound = first;
            if (grouped)
            {
                var second = Sound(source, 0.03);
                second.Effect.Value = new DelayEffect
                {
                    DelayTime = { Value = 10 }, Feedback = { Value = 0 }, DryMix = { Value = 100 }, WetMix = { Value = 100 }
                };
                second.Effect.Value = new AudioEffectGroup
                {
                    Children = { second.Effect.Value!, new LimiterEffect { Lookahead = { Value = 20 } } }
                };
                sound = new SoundGroup { Children = { first, second }, TimeRange = { Value = first.TimeRange.Value } };
            }
            using var resource = sound.ToResource(CompositionContext.Default);
            var eligibility = new CompositionEligibility([sound]);
            using var composer = new Composer { SampleRate = Rate, ChannelLayout = AudioChannelLayout.Surround51 };
            var window = sound.TimeRange.Value;
            var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource), window, default, eligibility);
            using var body = composer.Compose(window, frame);
            using var tail = composer.Flush(new TimeRange(window.End, MediaTime.FromSeconds(0.05)), eligibility);
            Assert.That(tail, Is.Not.Null);
            Assert.That(tail!.ChannelCount, Is.EqualTo(6));
            Assert.That(tail.GetChannelData(0).ToArray().Any(v => Math.Abs(v) > 0.1f), Is.True);
        }, 1);
    }

    [Test]
    public void ReusedSourceFlushedBeforeProcess_UsesItsClipLayout()
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, (_, _) => 0.25f, source =>
        {
            using var resource = source.ToResource(CompositionContext.Default);
            using var context = new AudioContext(Rate, AudioChannelLayout.Surround51);
            var first = context.CreateSourceNode(resource);
            first.ClipLayout = AudioChannelLayout.Stereo;
            context.BeginUpdate(context.Nodes.ToArray());
            var reused = context.CreateSourceNode(resource);
            context.EndUpdate();
            Assert.That(reused, Is.SameAs(first));
            using var silence = reused.Flush(AudioTestContexts.AtSample(0, 32, Rate));
            Assert.That(silence.ChannelCount, Is.EqualTo(1));
            Assert.That(silence.GetChannelData(0).ToArray(), Is.All.Zero);
        }, 1);
    }

    [Test]
    public void MixerAndInactiveClip_AreSeededBeforeTheirFirstProcess()
    {
        using var context = new AudioContext(Rate, AudioChannelLayout.Surround51);
        using var child = context.CreateChildContext();
        Assert.That(child.ChannelLayout, Is.EqualTo(context.ChannelLayout));
        var mixer = context.CreateMixerNode();
        var clip = context.CreateClipNode(MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));
        var process = AudioTestContexts.AtSample(0, 32, Rate);
        using var mixed = mixer.Flush(process);
        using var clipped = clip.Process(process);
        Assert.That(mixed.ChannelCount, Is.EqualTo(6));
        Assert.That(clipped.ChannelCount, Is.EqualTo(6));
    }

    [TestCase(1)]
    [TestCase(6)]
    [TestCase(8)]
    public void EmptyAndUnavailableGraphs_KeepTheProjectLayout(int channels)
    {
        var layout = AudioChannelLayout.GetDefault(channels);
        using var composer = new Composer { SampleRate = Rate, ChannelLayout = layout };
        var frame = new CompositionFrame([], s_window, default, CompositionEligibility.Empty);
        using var silent = composer.Compose(s_window, frame);
        using var tail = composer.Flush(new TimeRange(s_window.End, s_window.Duration));
        Assert.That(silent!.ChannelCount, Is.EqualTo(channels));
        Assert.That(tail!.ChannelCount, Is.EqualTo(channels));
        using var context = new AudioContext(Rate, layout);
        using var reads = new AudioReadScope();
        AudioReadScope.AddUnavailableSource(context, s_window);
        using var unavailable = context.GetOutputNodes().Single().Process(AudioTestContexts.AtSample(0, 32, Rate));
        Assert.That(unavailable.ChannelCount, Is.EqualTo(channels));
        Assert.That(reads.HasFailures, Is.True);
    }

    [Test]
    public void ReusedSpeedNode_UpdatesTheDeclaredClipChannelCount()
    {
        using var context = new AudioContext(Rate, AudioChannelLayout.Surround51);
        var speed = Property.CreateAnimatable(150f);
        var first = context.CreateSpeedNode(speed, 1);
        context.BeginUpdate(context.Nodes.ToArray());
        var second = context.CreateSpeedNode(speed);
        context.EndUpdate();
        Assert.That(second, Is.SameAs(first));
        Assert.That(second.ChannelCount, Is.EqualTo(6));
    }

    [TestCase("speed")]
    [TestCase("stretch")]
    [TestCase("noise")]
    public void ProcessorsRecreateWhenTheDeclaredInputChannelCountChanges(string kind)
    {
        using var source = new SwitchableInputNode();
        using AudioNode node = kind switch
        {
            "speed" => new SpeedNode { Speed = Property.CreateAnimatable(150f) },
            "stretch" => new TimeStretchNode { Speed = Property.CreateAnimatable(150f) },
            _ => new NoiseReductionNode
            {
                Reduction = Property.CreateAnimatable(12f), Sensitivity = Property.CreateAnimatable(3f),
                Smoothing = Property.CreateAnimatable(50f), Adaptation = Property.CreateAnimatable(2f)
            }
        };
        node.AddInput(source);
        using var stereo = node.Process(AudioTestContexts.AtSample(0, 1024, Rate));
        source.Channels = 6;
        switch (node)
        {
            case SpeedNode speed: speed.ChannelCount = 6; break;
            case TimeStretchNode stretch: stretch.ChannelCount = 6; break;
            case NoiseReductionNode noise: noise.ChannelCount = 6; break;
        }
        using var surround = node.Process(AudioTestContexts.AtSample(1024, 1024, Rate));
        Assert.That(stereo.ChannelCount, Is.EqualTo(2));
        Assert.That(surround.ChannelCount, Is.EqualTo(6));
    }

    private sealed class SwitchableInputNode : AudioNode
    {
        public int Channels = 2;
        public override AudioBuffer Process(AudioProcessContext context)
            => RecordProcessedOutput(AudioTestBuffers.CreateConstantBuffer(0.25f, context.GetSampleCount(), Channels, context.SampleRate));
    }

    [Test]
    public void ComposerMix_RejectsAMismatchedBuffer()
    {
        using var composer = new Composer { ChannelLayout = AudioChannelLayout.Surround51 };
        using var wrong = new AudioBuffer(Rate, 2, 32);
        var method = typeof(Composer).GetMethod("MixBuffers", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(composer, [new List<AudioBuffer> { wrong }]));
        Assert.That(error!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [TestCase(1)]
    [TestCase(6)]
    public void AudioSourceStream_InterleavesEveryChannelAndRejectsMismatch(int channels)
    {
        using var input = AudioTestBuffers.CreateBuffer(channels, 8, (ch, i) => ch + i / 16f);
        using var source = new BufferReplayNode(input);
        var stream = new AudioSourceStream(Rate, channels, static t => t);
        var samples = new float[8 * channels];
        int read = stream.Read(samples, source, AudioTestContexts.AtSample(0, 8, Rate), false);
        Assert.That(read, Is.EqualTo(samples.Length));
        for (int i = 0; i < 8; i++)
            for (int ch = 0; ch < channels; ch++) Assert.That(samples[i * channels + ch], Is.EqualTo(ch + i / 16f));
        var wrong = new AudioSourceStream(Rate, channels + 1, static t => t);
        Assert.Throws<InvalidOperationException>(() => wrong.Read(new float[8 * (channels + 1)], source,
            AudioTestContexts.AtSample(0, 8, Rate), false));
    }
}
