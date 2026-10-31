using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class ComposerNativeChannelTests
{
    [TestCase(1, 44100)]
    [TestCase(2, 44100)]
    [TestCase(1, 48000)]
    [TestCase(2, 48000)]
    public void Compose_DefaultSoundSettingsPreserveRealWaveSamplesExactly(int channels, int sampleRate)
    {
        static float Sample(int ch, int i) => ((i * 17 + ch * 11) % 101 - 50) / 128f;
        AudioTestWaves.WithTemporaryWave(sampleRate, sampleRate, Sample, source =>
        {
            var sound = new SourceSound
            {
                TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) },
                Source = { Value = source }
            };
            using var resource = sound.ToResource(CompositionContext.Default);
            using var composer = new Composer { SampleRate = sampleRate };
            var range = new TimeRange(MediaTime.FromSamples(sampleRate / 5, sampleRate), MediaTime.FromSamples(sampleRate / 5, sampleRate));
            var frame = new CompositionFrame(ImmutableArray.Create<EngineObject.Resource>(resource), range, default, new CompositionEligibility([sound]));
            using var output = composer.Compose(range, frame);
            Assert.That(output, Is.Not.Null);
            Assert.That(output!.ChannelCount, Is.EqualTo(2));
            Assert.That(output.SampleCount, Is.EqualTo(sampleRate / 5));
            for (int ch = 0; ch < 2; ch++)
                Assert.That(output.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits),
                    Is.EqualTo(Enumerable.Range(sampleRate / 5, sampleRate / 5)
                        .Select(i => BitConverter.SingleToInt32Bits(Sample(Math.Min(ch, channels - 1), i)))));
        }, channels);
    }
}
