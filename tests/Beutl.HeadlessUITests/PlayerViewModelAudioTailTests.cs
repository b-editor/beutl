using Beutl.Audio;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PlayerViewModelAudioTailTests
{
    [Test]
    public void SilenceTailBeyondSceneEnd_ClearsOnlyTheSamplesPastTheSceneEnd()
    {
        using AudioBuffer straddling = OneSecondOfOnes(8);
        using AudioBuffer before = OneSecondOfOnes(8);
        using AudioBuffer after = OneSecondOfOnes(8);

        PlayerViewModel.SilenceTailBeyondSceneEnd(straddling, MediaTime.Zero, MediaTime.FromSeconds(0.5));
        PlayerViewModel.SilenceTailBeyondSceneEnd(before, MediaTime.Zero, MediaTime.FromSeconds(2));
        PlayerViewModel.SilenceTailBeyondSceneEnd(after, MediaTime.FromSeconds(1), MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(straddling.GetChannelData(1).ToArray(), Is.EqualTo(new[] { 1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f }));
            Assert.That(before.GetChannelData(0).ToArray(), Is.All.EqualTo(1f));
            Assert.That(after.GetChannelData(0).ToArray(), Is.All.EqualTo(0f));
        });
    }

    private static AudioBuffer OneSecondOfOnes(int sampleRate)
    {
        var audio = new AudioBuffer(sampleRate, 2, sampleRate);
        audio.GetChannelData(0).Fill(1f);
        audio.GetChannelData(1).Fill(1f);
        return audio;
    }
}
