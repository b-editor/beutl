using System.Reactive.Subjects;
using Beutl.Audio;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Audio;

namespace Beutl.UnitTests.Editor;

[NonParallelizable]
public class SampleProviderImplTests
{
    [TestCase(SpeakerLayout.Surround51)]
    [TestCase(SpeakerLayout.Mono)]
    [TestCase(SpeakerLayout.Stereo)]
    public void ExportMatchesTheProjectMixFoldedToStereo(SpeakerLayout layout)
    {
        const int rate = 8000;
        const int samples = 12000;
        AudioTestWaves.WithTemporaryWave(rate, samples, (ch, i) => ((i * 17 + ch * 11) % 101 - 50) / 256f, source =>
        {
            var scene = new Scene(32, 32, "export-layout")
            {
                Duration = { Value = MediaTime.FromSamples(samples, rate) }
            };
            var project = new Project { ChannelLayout = { Value = layout }, Items = { scene } };
            var element = new Element { Length = { Value = scene.Duration.Value } };
            element.AddObject(new SourceSound
            {
                Source = { Value = source },
                CenterLevel = { Value = layout == SpeakerLayout.Surround51 ? 100 : 0 }
            });
            scene.Children.Add(element);

            using var playbackComposer = new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true)
            {
                SampleRate = rate,
                ChannelLayout = project.GetChannelLayout()
            };
            using var composed = playbackComposer.Compose(new TimeRange(MediaTime.Zero, scene.Duration.Value))!;
            using AudioBuffer? folded = layout == SpeakerLayout.Stereo ? null
                : AudioChannelMatrix.Create(project.GetChannelLayout(), AudioChannelLayout.Stereo).Apply(composed);
            AudioBuffer expected = folded ?? composed;
            Assert.That(expected.GetChannelData(0).ToArray().Any(value => value != 0), Is.True);
            if (layout == SpeakerLayout.Surround51)
            {
                Assert.That(composed.GetChannelData(0).ToArray(), Is.All.Zero);
                Assert.That(composed.GetChannelData(1).ToArray(), Is.All.Zero);
                Assert.That(composed.GetChannelData(2).ToArray().Any(value => value != 0), Is.True);
            }

            using var exportComposer = new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true)
            {
                SampleRate = rate,
                ChannelLayout = project.GetChannelLayout()
            };
            using var progress = new Subject<MediaTime>();
            using var provider = new SampleProviderImpl(scene, exportComposer, rate, progress);
            List<float>[] actual = [[], []];
            // Cross the one-second compose boundary and the shorter final chunk with encoder-sized reads.
            while (provider.ReadNextSamples(1024).AsTask().GetAwaiter().GetResult() is { } audio)
            {
                using (audio)
                {
                    Assert.That(audio.ChannelCount, Is.EqualTo(2));
                    for (int ch = 0; ch < 2; ch++) actual[ch].AddRange(audio.GetChannelData(ch).ToArray());
                }
            }
            for (int ch = 0; ch < 2; ch++)
            {
                Assert.That(actual[ch].Count, Is.EqualTo(12288));
                Assert.That(actual[ch].Take(samples).Select(BitConverter.SingleToInt32Bits),
                    Is.EqualTo(expected.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits)));
                Assert.That(actual[ch].Skip(samples), Is.All.Zero);
            }
            if (layout == SpeakerLayout.Mono)
                Assert.That(actual[0].Select(BitConverter.SingleToInt32Bits), Is.EqualTo(actual[1].Select(BitConverter.SingleToInt32Bits)));
        });
    }
}
