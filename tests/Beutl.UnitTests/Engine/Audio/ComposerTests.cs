using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Audio;

public class ComposerTests
{
    [Test]
    public void Compose_EmptyFrame_ReturnsSilentBufferWithCeilingSampleCount()
    {
        // Composer.BuildFinalOutput の silence fallback (mixedBuffer == null) が
        // 他経路 (AudioProcessContext.GetSampleCount) と同じ Ceiling サンプル数で
        // バッファを確保することを担保する回帰テスト。
        // 226 ticks は 1 サンプル境界 (~226.7575 ticks @ 44100Hz) を下回るが、+1 した
        // 227 ticks は境界をわずかに越えるため、truncation だと 1 サンプル / Ceiling だと 2 サンプル。
        const int sampleRate = 44100;
        var oneSampleTicksFloor = TimeSpan.TicksPerSecond / sampleRate;
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(oneSampleTicksFloor + 1));
        var frame = new CompositionFrame(ImmutableArray<EngineObject.Resource>.Empty, range, default);

        using var composer = new Composer { SampleRate = sampleRate };
        using AudioBuffer? buffer = composer.Compose(range, frame);

        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer!.SampleRate, Is.EqualTo(sampleRate));
        Assert.That(buffer.ChannelCount, Is.EqualTo(2));
        Assert.That(buffer.SampleCount, Is.EqualTo(AudioProcessContext.GetSampleCount(range, sampleRate)));
        Assert.That(buffer.SampleCount, Is.EqualTo(2));
    }

    [Test]
    public void Compose_EmptyFrame_IntegerSecondDuration_MatchesGetSampleCount()
    {
        const int sampleRate = 48000;
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var frame = new CompositionFrame(ImmutableArray<EngineObject.Resource>.Empty, range, default);

        using var composer = new Composer { SampleRate = sampleRate };
        using AudioBuffer? buffer = composer.Compose(range, frame);

        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer!.SampleCount, Is.EqualTo(sampleRate));
    }
}
