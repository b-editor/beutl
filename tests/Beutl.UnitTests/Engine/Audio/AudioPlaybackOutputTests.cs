using Beutl.Audio;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioPlaybackOutputTests
{
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(6, true)]
    [TestCase(8, true)]
    public void UseMultichannelBackend_OnlySurroundProjectsUseTheNewPath(int channels, bool expected)
    {
        Assert.That(AudioPlaybackOutput.UseMultichannelBackend(AudioChannelLayout.GetDefault(channels)), Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> OutputLayouts()
    {
        AudioChannelLayout mono = AudioChannelLayout.Mono;
        AudioChannelLayout stereo = AudioChannelLayout.Stereo;
        AudioChannelLayout back51 = AudioChannelLayout.Surround51;
        var side51 = new AudioChannelLayout(6, (AudioSpeakers)0x60F);
        AudioChannelLayout surround71 = AudioChannelLayout.Surround71;
        var quad = new AudioChannelLayout(4, (AudioSpeakers)0x33);
        AudioChannelLayout[] projects = [mono, stereo, back51, surround71];
        (AudioChannelLayout Device, AudioChannelLayout[] Outputs)[] devices =
        [
            (default, [stereo, stereo, stereo, stereo]),
            (AudioChannelLayout.Discrete(8), [stereo, stereo, stereo, stereo]),
            (mono, [stereo, stereo, stereo, stereo]),
            (stereo, [stereo, stereo, stereo, stereo]),
            (back51, [stereo, stereo, back51, back51]),
            (side51, [stereo, stereo, side51, side51]),
            (surround71, [stereo, stereo, back51, surround71]),
            (quad, [stereo, stereo, quad, quad])
        ];
        foreach (var (device, outputs) in devices)
        {
            for (int i = 0; i < projects.Length; i++)
            {
                yield return new TestCaseData(projects[i], device, outputs[i])
                    .SetName($"Create_Project{projects[i].ChannelCount}_Device{device.ChannelCount}_{device.Speakers:X}");
            }
        }
    }

    [TestCaseSource(nameof(OutputLayouts))]
    public void Create_UsesSupportedSpeakersAndBeutlsFold(
        AudioChannelLayout project, AudioChannelLayout device, AudioChannelLayout expectedLayout)
    {
        var (layout, matrix) = AudioPlaybackOutput.Create(project, device);
        Assert.That(layout, Is.EqualTo(expectedLayout));
        Assert.That(matrix is null, Is.EqualTo(project == expectedLayout));

        using var input = AudioTestBuffers.CreateBuffer(project.ChannelCount, project.ChannelCount,
            (channel, sample) => channel == sample ? 0.5f : -0.125f);
        using var expected = AudioChannelMatrix.Create(project, expectedLayout).Apply(input);
        using AudioBuffer? folded = matrix?.Apply(input);
        AudioBuffer output = folded ?? input;

        Assert.That(output.SampleCount, Is.EqualTo(input.SampleCount));
        Assert.That(output.SampleRate, Is.EqualTo(input.SampleRate));
        if (project == expectedLayout)
            Assert.That(output, Is.SameAs(input));
        for (int channel = 0; channel < output.ChannelCount; channel++)
            Assert.That(output.GetChannelData(channel).ToArray(), Is.EqualTo(expected.GetChannelData(channel).ToArray()));
        Assert.That(input.GetChannelData(0)[0], Is.EqualTo(0.5f));
    }
}
