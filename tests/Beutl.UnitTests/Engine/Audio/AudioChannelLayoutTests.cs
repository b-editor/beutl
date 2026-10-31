using Beutl.Audio;
using Beutl.Media;
using Beutl.Media.Decoding;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioChannelLayoutTests
{
    [Test]
    public void Speakers_MatchWaveMaskBits()
    {
        Assert.That(Enum.GetValues<AudioSpeakers>().Select(value => (uint)value),
            Is.EqualTo(new[] { 0u }.Concat(Enumerable.Range(0, 18).Select(bit => 1u << bit))));
    }

    [Test]
    public void Presets_HaveTheExpectedCountsAndPositions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AudioChannelLayout.Mono, Is.EqualTo(new AudioChannelLayout(1, AudioSpeakers.FrontCenter)));
            Assert.That(AudioChannelLayout.Stereo, Is.EqualTo(new AudioChannelLayout(2, (AudioSpeakers)0x3)));
            Assert.That(AudioChannelLayout.Surround51, Is.EqualTo(new AudioChannelLayout(6, (AudioSpeakers)0x3F)));
            Assert.That(AudioChannelLayout.Surround71, Is.EqualTo(new AudioChannelLayout(8, (AudioSpeakers)0x63F)));
        });
    }

    [TestCase(1, 0x4u)]
    [TestCase(2, 0x3u)]
    [TestCase(3, 0x7u)]
    [TestCase(4, 0x33u)]
    [TestCase(5, 0x37u)]
    [TestCase(6, 0x3Fu)]
    [TestCase(7, 0x70Fu)]
    [TestCase(8, 0x63Fu)]
    [TestCase(9, 0u)]
    [TestCase(32, 0u)]
    public void GetDefault_UsesTheDocumentedTable(int count, uint mask)
    {
        var layout = AudioChannelLayout.GetDefault(count);
        Assert.That(layout, Is.EqualTo(new AudioChannelLayout(count, (AudioSpeakers)mask)));
        Assert.That(layout.HasPositions, Is.EqualTo(mask != 0));
    }

    [Test]
    public void SpeakerLookup_UsesAscendingBitsAndRejectsInvalidIndices()
    {
        var layout = new AudioChannelLayout(3, AudioSpeakers.FrontRight | AudioSpeakers.SideLeft | AudioSpeakers.TopBackRight);
        AudioSpeakers[] expected = [AudioSpeakers.FrontRight, AudioSpeakers.SideLeft, AudioSpeakers.TopBackRight];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(layout.GetSpeaker(i), Is.EqualTo(expected[i]));
            Assert.That(layout.IndexOf(expected[i]), Is.EqualTo(i));
        }
        Assert.That(layout.IndexOf(AudioSpeakers.FrontLeft), Is.EqualTo(-1));
        Assert.That(layout.IndexOf(AudioSpeakers.None), Is.EqualTo(-1));
        Assert.That(layout.IndexOf(layout.Speakers), Is.EqualTo(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSpeaker(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetSpeaker(3));
    }

    [Test]
    public void Discrete_HasNoSpeakerPositions()
    {
        var layout = AudioChannelLayout.Discrete(6);
        Assert.That(layout.HasPositions, Is.False);
        Assert.That(layout.ChannelCount, Is.EqualTo(6));
        Assert.That(layout.GetSpeaker(5), Is.EqualTo(AudioSpeakers.None));
        Assert.That(layout.IndexOf(AudioSpeakers.FrontLeft), Is.EqualTo(-1));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Construction_RejectsNonpositiveCounts(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioChannelLayout.Discrete(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioChannelLayout.GetDefault(count));
    }

    [Test]
    public void Construction_RejectsMismatchedMask()
    {
        Assert.Throws<ArgumentException>(() => new AudioChannelLayout(2, AudioSpeakers.FrontLeft));
        Assert.Throws<ArgumentException>(() => new AudioChannelLayout(1, AudioChannelLayout.Stereo.Speakers));
    }

    [Test]
    public void DefaultLayout_IsRejectedByConsumers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => default(AudioChannelLayout).GetSpeaker(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioChannelLayout.ForChannelCount(default, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => default(AudioChannelLayout).IndexOf(AudioSpeakers.FrontLeft));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioChannelMatrix.Create(default, AudioChannelLayout.Stereo));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioChannelMatrix.Create(AudioChannelLayout.Stereo, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioStreamInfo("test", new Rational(1), 48000, 6) { ChannelLayout = default });
    }

    [TestCase(1, AudioSpeakers.None)]
    [TestCase(2, AudioSpeakers.None)]
    [TestCase(6, AudioSpeakers.None)]
    [TestCase(9, AudioSpeakers.None)]
    [TestCase(2, AudioSpeakers.SideLeft | AudioSpeakers.SideRight)]
    public void ForChannelCount_PreservesMatchingDeclaredLayouts(int count, AudioSpeakers speakers)
    {
        var declared = new AudioChannelLayout(count, speakers);
        Assert.That(AudioChannelLayout.ForChannelCount(declared, count), Is.EqualTo(declared));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(9)]
    public void ForChannelCount_UsesDefaultOrderWhenTheDeclaredCountDiffers(int count)
    {
        Assert.That(AudioChannelLayout.ForChannelCount(AudioChannelLayout.Surround71, count),
            Is.EqualTo(AudioChannelLayout.GetDefault(count)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ForChannelCount_RejectsNonpositiveBufferCounts(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioChannelLayout.ForChannelCount(AudioChannelLayout.Stereo, count));
    }

    [Test]
    public void AudioStreamInfo_DefaultsToStereoRegardlessOfNativeCount()
    {
        var info = new AudioStreamInfo("legacy", new Rational(1), 48000, 6);
        Assert.That(info.NumChannels, Is.EqualTo(6));
        Assert.That(info.ChannelLayout, Is.EqualTo(AudioChannelLayout.Stereo));
        Assert.That(info with { ChannelLayout = AudioChannelLayout.Surround51 },
            Is.Not.EqualTo(info));
    }
}
