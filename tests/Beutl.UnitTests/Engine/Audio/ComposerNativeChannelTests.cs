using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class ComposerNativeChannelTests
{
    [Test]
    public void Compose_DefaultSoundSettingsPreserveRealWaveSamplesExactly([Values(1, 2)] int channels, [Values(44100, 48000)] int sampleRate, [Values] bool grouped, [Values] bool effect)
    {
        static float Sample(int ch, int i) => ((i * 17 + ch * 11) % 101 - 50) / 128f;
        AudioTestWaves.WithTemporaryWave(sampleRate, sampleRate, Sample, source =>
        {
            var sound = new SourceSound
            {
                TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) },
                Source = { Value = source }
            };
            if (effect) sound.Effect.Value = new DelayEffect();
            Sound root = grouped ? new SoundGroup { Children = { sound }, TimeRange = { Value = sound.TimeRange.Value } } : sound;
            using var resource = root.ToResource(CompositionContext.Default);
            using var composer = new Composer { SampleRate = sampleRate };
            var range = new TimeRange(MediaTime.FromSamples(sampleRate / 5, sampleRate), MediaTime.FromSamples(sampleRate / 5, sampleRate));
            var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource), range, default, new CompositionEligibility([root]));
            using var output = composer.Compose(range, frame);
            Assert.That(output, Is.Not.Null);
            Assert.That(output!.ChannelCount, Is.EqualTo(2));
            Assert.That(output.SampleCount, Is.EqualTo(sampleRate / 5));
            if (effect)
            {
                // The pre-panner stereo graph: SourceNode folded to stereo before gain and effects.
                using var sourceResource = source.ToResource(CompositionContext.Default);
                using var legacy = new AudioContext(sampleRate, 2);
                var input = legacy.AddNode(new SourceNode { Source = sourceResource.Capture() });
                var gain = legacy.CreateGainNode(sound.Gain);
                legacy.Connect(input, gain);
                AudioNode effectNode = sound.Effect.Value!.CreateNode(legacy, gain);
                var clip = legacy.CreateClipNode(MediaTime.Zero, MediaTime.FromSeconds(1));
                legacy.Connect(effectNode, clip);
                using var expected = clip.Process(AudioTestContexts.Exact(range.Start, sampleRate / 5, sampleRate));
                for (int ch = 0; ch < 2; ch++)
                    Assert.That(output.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                        Is.EqualTo(expected.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits)));
            }
            else
            {
                for (int ch = 0; ch < 2; ch++)
                    Assert.That(output.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                        Is.EqualTo(Enumerable.Range(sampleRate / 5, sampleRate / 5)
                            .Select(i => BitConverter.SingleToInt32Bits(Sample(Math.Min(ch, channels - 1), i)))));
            }
        }, channels);
    }
}
