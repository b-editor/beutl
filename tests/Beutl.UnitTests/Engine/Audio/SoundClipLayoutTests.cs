using Beutl.Audio;
using Beutl.Engine.Expressions;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SoundClipLayoutTests
{
    [Test]
    public void GroupsAndScenesUseTheProjectLayout([Values] SpeakerLayout layout)
    {
        var project = new Project { ChannelLayout = { Value = layout } };
        var expected = project.GetChannelLayout();
        Assert.That(new SoundGroup().GetClipLayout(expected, MediaTime.Zero), Is.EqualTo(expected));
        Assert.That(new SceneSound().GetClipLayout(expected, MediaTime.Zero), Is.EqualTo(expected));
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
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround51, MediaTime.Zero), Is.EqualTo(layout));
            sound.ChannelMap.Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Mono, layout);
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround71, MediaTime.Zero), Is.EqualTo(AudioChannelLayout.Mono));
            sound.ChannelMap.Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Stereo, layout);
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Mono, MediaTime.Zero), Is.EqualTo(AudioChannelLayout.Stereo));
            sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Discrete(3), [0]);
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Stereo, MediaTime.Zero), Is.EqualTo(layout), "A stale map resolves to Auto.");
        }, channelCount: channels, mask: layout.Speakers);
    }

    [Test]
    public void SourceAndChannelMapExpressionsUseTheRequestedTime()
    {
        AudioTestWaves.WithTemporaryWave(44100, 32, (_, _) => 0, source =>
        {
            var provider = new SourceSound { Source = { Value = source } };
            var sound = new SourceSound();
            sound.Source.Expression = new ReferenceExpression<SoundSource?>(provider.Id.Value, nameof(SourceSound.Source));
            sound.ChannelMap.Expression = new StringExpression<AudioChannelMap>(
                "Time < 2 ? Beutl.Audio.AudioChannelMap.Auto : Beutl.Audio.AudioChannelMap.CreateDefault(Beutl.Audio.AudioChannelFormat.Stereo, Beutl.Audio.AudioChannelLayout.Surround51)");
            var root = new TestHierarchicalRoot();
            root.HierarchicalChildren.Add(provider);
            root.HierarchicalChildren.Add(sound);

            Assert.That(sound.Source.Value, Is.Null);
            Assert.That(sound.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround71, MediaTime.Zero), Is.EqualTo(AudioChannelLayout.Surround51));
            Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround71, MediaTime.FromSeconds(2)), Is.EqualTo(AudioChannelLayout.Stereo));
        }, channelCount: 6, mask: AudioChannelLayout.Surround51.Speakers);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrUnreadableSourceFallsBackToTheProjectLayout(bool unreadable)
    {
        var sound = new SourceSound();
        if (unreadable)
            sound.Source.Value = SoundSource.Open(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav"));
        Assert.That(sound.GetClipLayout(AudioChannelLayout.Surround51, MediaTime.Zero), Is.EqualTo(AudioChannelLayout.Surround51));
        Assert.That(sound.GetClipLayout(AudioChannelLayout.Stereo, MediaTime.Zero), Is.EqualTo(AudioChannelLayout.Stereo));
    }
}
