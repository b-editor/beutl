using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class ComposerChannelMapTests
{
    private const int Rate = 44100;
    private static readonly AudioChannelLayout s_sourceLayout = AudioChannelLayout.GetDefault(4);
    private static float Sample(int channel, int i) => ((i * 17 + channel * 11) % 101 - 50) / 128f;

    [TestCase(AudioChannelFormat.Stereo, 2, 3)]
    [TestCase(AudioChannelFormat.Mono, 2, 2)]
    [TestCase(AudioChannelFormat.Stereo, -1, 3)]
    [TestCase(AudioChannelFormat.Mono, -1, -1)]
    public void Compose_ExplicitAssignmentsCopyTheSelectedWaveChannelsAtUnity(AudioChannelFormat format, int left, int right)
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, Sample, source =>
        {
            var map = new AudioChannelMap(format, s_sourceLayout, format == AudioChannelFormat.Mono ? [left] : [left, right]);
            var sound = CreateSound(source, map);
            using var composer = new Composer { SampleRate = Rate };
            using var output = Compose(sound, composer, 100, 1024);
            for (int channel = 0; channel < 2; channel++)
            {
                int selected = channel == 0 ? left : right;
                Assert.That(output.GetChannelData(channel).ToArray().Select(BitConverter.SingleToInt32Bits),
                    Is.EqualTo(Enumerable.Range(100, 1024).Select(i => BitConverter.SingleToInt32Bits(selected < 0 ? 0 : Sample(selected, i)))));
            }
        }, channelCount: 4, mask: s_sourceLayout.Speakers);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Compose_MismatchedSourceLayoutUsesAuto(bool sameCount)
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, Sample, source =>
        {
            var oldLayout = sameCount ? AudioChannelLayout.Discrete(4) : AudioChannelLayout.Surround51;
            var sound = CreateSound(source, new AudioChannelMap(AudioChannelFormat.Mono, oldLayout, [2]));
            using var composer = new Composer { SampleRate = Rate };
            using var actual = Compose(sound, composer, 0, 1024);
            sound.ChannelMap.Value = AudioChannelMap.Auto;
            using var fresh = new Composer { SampleRate = Rate };
            using var expected = Compose(sound, fresh, 0, 1024);
            AssertSameSamples(actual, expected);
        }, channelCount: 4, mask: s_sourceLayout.Speakers);
    }

    [Test]
    public void Compose_ChangedMapDoesNotRetainSamplesOrChannelCounts(
        [Values(100f, 150f)] float speed, [Values(44100, 48000)] int sourceRate, [Values] bool grouped)
    {
        AudioTestWaves.WithTemporaryWave(sourceRate, sourceRate, Sample, source =>
        {
            var sound = CreateSound(source, new AudioChannelMap(AudioChannelFormat.Stereo, s_sourceLayout, [0, 1]));
            sound.Speed.Value = speed;
            Sound root = grouped ? new SoundGroup { Children = { sound }, TimeRange = { Value = sound.TimeRange.Value } } : sound;
            using var resource = root.ToResource(CompositionContext.Default);
            using var composer = new Composer { SampleRate = Rate };
            using var first = Compose(root, composer, 0, 128, resource);
            AudioChannelMap[] maps =
            [
                new(AudioChannelFormat.Stereo, s_sourceLayout, [2, 3]),
                new(AudioChannelFormat.Mono, s_sourceLayout, [2]),
                AudioChannelMap.Auto
            ];
            for (int i = 0; i < maps.Length; i++)
            {
                sound.ChannelMap.Value = maps[i];
                bool changed = false;
                resource.Reconcile(root, CompositionContext.Default, ref changed);
                using var actual = Compose(root, composer, (i + 1) * 128, 128, resource);
                using var fresh = new Composer { SampleRate = Rate };
                using var expected = Compose(root, fresh, (i + 1) * 128, 128, resource);
                AssertSameSamples(actual, expected);
            }
        }, channelCount: 4, mask: s_sourceLayout.Speakers);
    }

    [Test]
    public void ReusedSource_ReceivesTheNewMapAndRecreatesBufferedNodesBeforeProcessing()
    {
        AudioTestWaves.WithTemporaryWave(Rate, Rate, Sample, source =>
        {
            var sound = CreateSound(source, new AudioChannelMap(AudioChannelFormat.Stereo, s_sourceLayout, [0, 1]));
            using var resource = sound.ToResource(CompositionContext.Default);
            using var context = new AudioContext(Rate, AudioChannelLayout.Stereo);
            sound.Compose(context, resource);
            var firstSource = context.Nodes.OfType<SourceNode>().Single();
            var firstSpeed = context.Nodes.OfType<SpeedNode>().Single();

            context.BeginUpdate(context.Nodes.ToArray());
            sound.Compose(context, resource);
            context.EndUpdate();
            Assert.That(context.Nodes.OfType<SourceNode>().Single(), Is.SameAs(firstSource));
            Assert.That(context.Nodes.OfType<SpeedNode>().Single(), Is.SameAs(firstSpeed));

            sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Mono, s_sourceLayout, [2]);
            bool changed = false;
            resource.Reconcile(sound, CompositionContext.Default, ref changed);
            Assert.That(resource.GetChannelMap(), Is.EqualTo(sound.ChannelMap.Value));
            Assert.That(changed, Is.True);
            context.BeginUpdate(context.Nodes.ToArray());
            sound.Compose(context, resource);
            context.EndUpdate();

            var reused = context.Nodes.OfType<SourceNode>().Single();
            Assert.That(reused, Is.SameAs(firstSource));
            Assert.That(reused.ChannelMap, Is.EqualTo(sound.ChannelMap.Value));
            var speed = context.Nodes.OfType<SpeedNode>().Single();
            Assert.That(speed, Is.Not.SameAs(firstSpeed));
            Assert.That(speed.ChannelCount, Is.EqualTo(1));
            using var silence = reused.Flush(AudioTestContexts.AtSample(0, 32, Rate));
            Assert.That(silence.ChannelCount, Is.EqualTo(1));
            Assert.That(silence.GetChannelData(0).ToArray(), Is.All.Zero);
        }, channelCount: 4, mask: s_sourceLayout.Speakers);
    }

    private static SourceSound CreateSound(SoundSource source, AudioChannelMap map) => new()
    {
        Source = { Value = source },
        ChannelMap = { Value = map },
        TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) }
    };

    private static AudioBuffer Compose(Sound sound, Composer composer, int start, int count, Sound.Resource? resource = null)
    {
        var range = new TimeRange(MediaTime.FromSamples(start, Rate), MediaTime.FromSamples(count, Rate));
        using var ownedResource = resource == null ? sound.ToResource(CompositionContext.Default) : null;
        resource ??= ownedResource!;
        var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource), range, default,
            new CompositionEligibility([sound]));
        return composer.Compose(range, frame)!;
    }

    private static void AssertSameSamples(AudioBuffer actual, AudioBuffer expected)
    {
        Assert.That(actual.ChannelCount, Is.EqualTo(expected.ChannelCount));
        for (int channel = 0; channel < expected.ChannelCount; channel++)
            Assert.That(actual.GetChannelData(channel).ToArray().Select(BitConverter.SingleToInt32Bits),
                Is.EqualTo(expected.GetChannelData(channel).ToArray().Select(BitConverter.SingleToInt32Bits)));
    }
}
