using Beutl.Audio;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SoundClipLayoutTests
{
    [Test]
    public void GroupsAndScenesUseTheProjectLayout([Values] SpeakerLayout layout)
    {
        var project = new Project { ChannelLayout = { Value = layout } };
        var expected = project.GetChannelLayout();
        Assert.That(new SoundGroup().GetClipLayout(expected), Is.EqualTo(expected));
        Assert.That(new SceneSound().GetClipLayout(expected), Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(8)]
    public void SourceUsesItsLayoutAndResolvedChannelMap(int channels)
    {
        var layout = AudioChannelLayout.GetDefault(channels);
        AudioTestWaves.WithTemporaryWave(44100, 32, (_, _) => 0, source =>
        {
            var sound = new SourceSound { Source = { Value = source } };
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround51), Is.EqualTo(layout));
            sound.ChannelMap.Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Mono, layout);
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround71), Is.EqualTo(AudioChannelLayout.Mono));
            sound.ChannelMap.Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Stereo, layout);
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Mono), Is.EqualTo(AudioChannelLayout.Stereo));
            sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Discrete(3), [0]);
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Stereo), Is.EqualTo(layout), "A stale map resolves to Auto.");
        }, channelCount: channels, mask: layout.Speakers);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrUnreadableSourceFallsBackToTheProjectLayout(bool unreadable)
    {
        var sound = new SourceSound();
        if (unreadable)
            sound.Source.Value = SoundSource.Open(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav"));
        Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround51), Is.EqualTo(AudioChannelLayout.Surround51));
        Assert.That(sound.GetClipLayout(AudioChannelLayout.Stereo), Is.EqualTo(AudioChannelLayout.Stereo));
    }
}
