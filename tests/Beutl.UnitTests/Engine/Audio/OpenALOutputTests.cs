using Beutl.Audio;
using Silk.NET.OpenAL;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class OpenALOutputTests
{
    [TestCase(null, 2)]
    [TestCase(0x19AD, 2)] // Any
    [TestCase(0x1500, 2)] // Mono
    [TestCase(0x1501, 2)] // Stereo
    [TestCase(0x19AE, 2)] // Stereo basic
    [TestCase(0x19AF, 2)] // Stereo UHJ
    [TestCase(0x19B2, 2)] // Stereo HRTF
    [TestCase(0x1503, 2)] // Quad
    [TestCase(0x1504, 6)] // 5.1
    [TestCase(0x1505, 6)] // 6.1
    [TestCase(0x1506, 8)] // 7.1
    [TestCase(0x7FFFFFFF, 2)]
    public void OutputMode_SelectsSupportedLayoutAndFormat(int? mode, int channels)
    {
        AudioChannelLayout device = AudioContext.GetDeviceLayout(mode, supportsMultichannel: true);
        Assert.That(device, Is.EqualTo(AudioChannelLayout.GetDefault(channels)));
        Assert.That((int)AudioContext.GetBufferFormat(device), Is.EqualTo(channels switch
        {
            6 => 0x120B,
            8 => 0x1211,
            _ => (int)BufferFormat.Stereo16
        }));
    }

    [TestCase(0x1504)]
    [TestCase(0x1505)]
    [TestCase(0x1506)]
    public void WithoutMcFormats_FoldsSurroundToStereo(int mode)
    {
        AudioChannelLayout device = AudioContext.GetDeviceLayout(mode, supportsMultichannel: false);
        Assert.That(device, Is.EqualTo(AudioChannelLayout.Stereo));
        foreach (AudioChannelLayout project in new[] { AudioChannelLayout.Surround51, AudioChannelLayout.Surround71 })
        {
            var (layout, matrix) = AudioPlaybackOutput.Create(project, device);
            Assert.That(layout, Is.EqualTo(AudioChannelLayout.Stereo));
            Assert.That(AudioContext.GetBufferFormat(layout), Is.EqualTo(BufferFormat.Stereo16));
            Assert.That(matrix, Is.Not.Null);
            using var audio = AudioTestBuffers.CreateBuffer(project.ChannelCount, 2, (channel, _) => channel * 0.1f);
            using var folded = matrix!.Apply(audio);
            using var expected = AudioChannelMatrix.Create(project, AudioChannelLayout.Stereo).Apply(audio);
            for (int channel = 0; channel < 2; channel++)
                Assert.That(folded.GetChannelData(channel).ToArray(), Is.EqualTo(expected.GetChannelData(channel).ToArray()));
        }
    }

    [TestCase(1, 2, BufferFormat.Stereo16)]
    [TestCase(2, 2, BufferFormat.Stereo16)]
    [TestCase(6, 6, (BufferFormat)0x120B)]
    [TestCase(8, 8, (BufferFormat)0x1211)]
    public void SurroundDevice_UsesTheProjectLayoutWithStereoForMono(int projectChannels, int sentChannels, BufferFormat format)
    {
        var (layout, _) = AudioPlaybackOutput.Create(AudioChannelLayout.GetDefault(projectChannels),
            AudioContext.GetDeviceLayout(0x1506, supportsMultichannel: true));
        Assert.That(layout.ChannelCount, Is.EqualTo(sentChannels));
        Assert.That(AudioContext.GetBufferFormat(layout), Is.EqualTo(format));
    }
}
