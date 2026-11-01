using System.Reactive.Subjects;
using Beutl.Audio;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Audio;

namespace Beutl.UnitTests.Editor;

[TestFixture, NonParallelizable]
public class SampleProviderLayoutTests
{
    [TestCase(2, 2)]
    [TestCase(6, 6)]
    [TestCase(8, 8)]
    [TestCase(6, 2)]
    [TestCase(6, 1)]
    [TestCase(2, 6)]
    public void Export_preserves_or_folds_every_channel_and_pads_the_tail(int inputChannels, int outputChannels)
    {
        const int rate = 8000;
        const int count = 12000;
        var inputLayout = AudioChannelLayout.GetDefault(inputChannels);
        var outputLayout = AudioChannelLayout.GetDefault(outputChannels);
        static float Sample(int ch, int i) => ((i * 17 + ch * 11) % 101 - 50) / 256f;
        AudioTestWaves.WithTemporaryWave(rate, count, Sample, source =>
        {
            var scene = new Scene(32, 32, "Samples") { Duration = { Value = MediaTime.FromSamples(count, rate) } };
            var element = new Element { Length = { Value = scene.Duration.Value } };
            element.Objects.Add(new SourceSound
            {
                Source = { Value = source },
                TimeRange = { Value = new TimeRange(MediaTime.Zero, scene.Duration.Value) }
            });
            scene.Children.Add(element);
            using var composer = new SceneComposer(scene, disableResourceShare: true, forceOriginalSource: true)
            {
                SampleRate = rate,
                ChannelLayout = inputLayout,
            };
            using var progress = new Subject<MediaTime>();
            using var provider = new SampleProviderImpl(scene, composer, rate, progress, outputLayout);
            using var input = new AudioBuffer(rate, inputChannels, count);
            for (int ch = 0; ch < inputChannels; ch++)
                for (int i = 0; i < count; i++)
                    input.GetChannelData(ch)[i] = Sample(ch, i);
            using var expected = AudioChannelMatrix.Create(inputLayout, outputLayout).Apply(input);
            Assert.That(provider.ChannelLayout, Is.EqualTo(outputLayout));
            int offset = 0;
            while (provider.ReadNextSamples(997).AsTask().GetAwaiter().GetResult() is { } audio)
            {
                using (audio)
                {
                    Assert.That(audio.ChannelCount, Is.EqualTo(outputChannels));
                    Assert.That(audio.SampleCount, Is.EqualTo(997));
                    int available = Math.Min(997, count - offset);
                    for (int ch = 0; ch < outputChannels; ch++)
                    {
                        Assert.That(audio.GetChannelData(ch)[..available].ToArray().Select(BitConverter.SingleToInt32Bits),
                            Is.EqualTo(expected.GetChannelData(ch).Slice(offset, available).ToArray().Select(BitConverter.SingleToInt32Bits)),
                            $"Channel {ch} at sample {offset}");
                        Assert.That(audio.GetChannelData(ch)[available..].ToArray(), Is.All.Zero);
                    }
                }
                offset += 997;
            }
            Assert.That(offset, Is.EqualTo(12961));
        }, inputChannels, inputLayout.Speakers);
    }
}
