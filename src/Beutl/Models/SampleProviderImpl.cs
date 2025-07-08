using System.Reactive.Subjects;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;

namespace Beutl.Models;

public class SampleProviderImpl(Scene scene, SceneComposer composer, long sampleRate, Subject<TimeSpan> progress)
    : ISampleProvider
{
    public long SampleCount => (long)(scene.Duration.TotalSeconds * sampleRate);

    public long SampleRate => sampleRate;

    public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        var range = new TimeRange(
            TimeSpan.FromTicks(TimeSpan.TicksPerSecond * offset / sampleRate) + scene.Start,
            TimeSpan.FromTicks(TimeSpan.TicksPerSecond * length / sampleRate));
        using var audioBuffer = composer.Compose(range)
                          ?? throw new InvalidOperationException("composer.Composeがnullを返しました。");

        progress.OnNext(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * (offset + length) / sampleRate));
        return ValueTask.FromResult(audioBuffer.ToPcm());
    }
}
