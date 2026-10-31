using System.Text.Json;
using Beutl.Audio;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioChannelMapTests
{
    [Test]
    public void SourceSound_RoundTripsAssignmentsAndOlderSoundsDefaultToAuto()
    {
        var sound = new SourceSound();
        Assert.That(sound.ChannelMap.IsAnimatable, Is.False);
        Assert.That(sound.ChannelMap.Value, Is.SameAs(AudioChannelMap.Auto));
        sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.Discrete(4), [2, -1]);
        var json = CoreSerializer.SerializeToJsonObject(sound);
        var restored = (SourceSound)CoreSerializer.DeserializeFromJsonObject(json, typeof(SourceSound));
        Assert.That(restored.ChannelMap.Value, Is.EqualTo(sound.ChannelMap.Value));
        json.Remove(nameof(SourceSound.ChannelMap));
        var legacy = (SourceSound)CoreSerializer.DeserializeFromJsonObject(json, typeof(SourceSound));
        Assert.That(legacy.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
    }

    [Test]
    public void Equality_UsesTheFormatSourceLayoutAndAssignmentsAndCopiesTheInput()
    {
        int[] channels = [2, 3];
        var layout = AudioChannelLayout.GetDefault(4);
        var map = new AudioChannelMap(AudioChannelFormat.Stereo, layout, channels);
        channels[0] = 0;
        var equal = new AudioChannelMap(AudioChannelFormat.Stereo, layout, [2, 3]);
        Assert.That(map, Is.EqualTo(equal));
        Assert.That(map.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
        Assert.That(map, Is.Not.EqualTo(map.WithSources([3, 2])));
        Assert.That(map, Is.Not.EqualTo(new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.Discrete(4), [2, 3])));
        Assert.That(map, Is.Not.EqualTo(new AudioChannelMap(AudioChannelFormat.Mono, layout, [2])));
        Assert.That(map.Equals(null), Is.False);
        Assert.That(new AudioChannelMap(AudioChannelFormat.Auto, layout, []), Is.EqualTo(AudioChannelMap.Auto));
    }

    [Test]
    public void JsonRoundTrip_PreservesAutoAndSilentAssignments([Values] AudioChannelFormat format)
    {
        var map = AudioChannelMap.CreateDefault(format, AudioChannelLayout.Surround71);
        if (format != AudioChannelFormat.Auto) map = map.WithSources(map.Sources.SetItem(0, -1));
        string json = JsonSerializer.Serialize(map);
        Assert.That(JsonSerializer.Deserialize<AudioChannelMap>(json), Is.EqualTo(map));
        Assert.That(JsonDocument.Parse(json).RootElement.GetProperty("Format").GetString(), Is.EqualTo(format.ToString()));
    }

    [Test]
    public void JsonShape_IsUsableWithoutKnowingTheConverter()
    {
        const string json = """{"Format":"Stereo","Sources":[2,-1],"SourceLayout":{"ChannelCount":4,"Speakers":51}}""";
        var map = JsonSerializer.Deserialize<AudioChannelMap>(json)!;
        Assert.That(map.SourceLayout, Is.EqualTo(AudioChannelLayout.GetDefault(4)));
        Assert.That(map.Sources, Is.EqualTo(new[] { 2, -1 }));
        Assert.That(JsonSerializer.Deserialize<AudioChannelMap>("""{"Format":"Auto"}"""), Is.EqualTo(AudioChannelMap.Auto));
    }

    [Test]
    public void Validation_RejectsInvalidFormatsLayoutsCountsAndIndices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioChannelMap((AudioChannelFormat)99, AudioChannelLayout.Stereo, [0, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioChannelMap(AudioChannelFormat.Mono, default, [0]));
        Assert.Throws<ArgumentException>(() => new AudioChannelMap(AudioChannelFormat.Auto, default, [0]));
        Assert.Throws<ArgumentException>(() => new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.Stereo, [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.Stereo, [0, 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Stereo, [-2]));
    }

    [TestCase("""{"Format":"Unknown"}""")]
    [TestCase("""{"Format":"Auto","Sources":[0]}""")]
    [TestCase("""{"Format":"Stereo","Sources":[0],"SourceLayout":{"ChannelCount":2,"Speakers":3}}""")]
    [TestCase("""{"Format":"Mono","Sources":[2],"SourceLayout":{"ChannelCount":2,"Speakers":3}}""")]
    [TestCase("""{"Format":"Mono","Sources":[0],"SourceLayout":{"ChannelCount":2,"Speakers":4}}""")]
    [TestCase("""{"Format":"Mono","Sources":[0]}""")]
    public void JsonValidation_RejectsInvalidMaps(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AudioChannelMap>(json));

    [Test]
    public void Resolve_MatchingSourceUsesTheClipFormat()
    {
        var layout = AudioChannelLayout.GetDefault(4);
        var map = new AudioChannelMap(AudioChannelFormat.Stereo, layout, [2, 3]);
        Assert.That(map.Resolve(layout), Is.SameAs(map));
        Assert.That(map.ResolveClipLayout(layout), Is.EqualTo(AudioChannelLayout.Stereo));
        Assert.That(AudioChannelMap.Auto.ResolveClipLayout(layout), Is.EqualTo(layout));
    }

    [TestCase(2, AudioSpeakers.FrontLeft | AudioSpeakers.FrontRight)]
    [TestCase(4, AudioSpeakers.FrontLeft | AudioSpeakers.FrontRight | AudioSpeakers.SideLeft | AudioSpeakers.SideRight)]
    [TestCase(4, AudioSpeakers.None)]
    public void Resolve_MismatchedSourceFallsBackToAuto(int channels, AudioSpeakers speakers)
    {
        var map = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.GetDefault(4), [2]);
        var actual = new AudioChannelLayout(channels, speakers);
        Assert.That(map.Resolve(actual), Is.SameAs(AudioChannelMap.Auto));
        Assert.That(map.ResolveClipLayout(actual), Is.EqualTo(actual));
    }

    [Test]
    public void Defaults_SelectTheFirstChannelsOrMatchingSpeakerPositions()
    {
        Assert.That(AudioChannelMap.CreateDefault(AudioChannelFormat.Mono, AudioChannelLayout.Surround51).Sources, Is.EqualTo(new[] { 0 }));
        Assert.That(AudioChannelMap.CreateDefault(AudioChannelFormat.Stereo, AudioChannelLayout.Mono).Sources, Is.EqualTo(new[] { 0, -1 }));
        Assert.That(AudioChannelMap.CreateDefault(AudioChannelFormat.Surround51, AudioChannelLayout.GetDefault(4)).Sources,
            Is.EqualTo(new[] { 0, 1, 2, 3, 2, 3 }));
        Assert.That(AudioChannelMap.CreateDefault(AudioChannelFormat.Surround71, AudioChannelLayout.Discrete(4)).Sources,
            Is.EqualTo(new[] { 0, 1, 2, 3, -1, -1, -1, -1 }));
    }

    [Test]
    public void ChannelDescriptions_FollowMaskOrderAndLeaveDiscreteChannelsNumbered()
    {
        var surround = AudioChannelLayout.Surround51;
        Assert.That(Enumerable.Range(0, 6).Select(ch => AudioChannelMap.GetChannelName(surround, ch)),
            Is.EqualTo(new[] { "L", "R", "C", "LFE", "Ls", "Rs" }));
        Assert.That(Enumerable.Range(0, 8).Select(ch => AudioChannelMap.GetChannelName(AudioChannelLayout.Surround71, ch)),
            Is.EqualTo(new[] { "L", "R", "C", "LFE", "Lrs", "Rrs", "Ls", "Rs" }));
        Assert.That(AudioChannelMap.DescribeSourceChannel(surround, 3), Is.EqualTo("4 (LFE)"));
        Assert.That(AudioChannelMap.DescribeSourceChannel(AudioChannelLayout.Discrete(4), 2), Is.EqualTo("3"));
        Assert.That(AudioChannelMap.GetChannelName(AudioChannelLayout.Mono, 0), Is.EqualTo("C"));
    }
}
