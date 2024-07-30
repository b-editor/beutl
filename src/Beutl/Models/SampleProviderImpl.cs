using System.Reactive.Subjects;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;

namespace Beutl.Models;

public class SampleProviderImpl(Scene scene, SceneComposer composer, long sampleRate, Subject<TimeSpan> progress)
    : ISampleProvider
{
    public long SampleCount => (long)(scene.Duration.TotalSeconds * sampleRate);

    public long SampleRate => sampleRate;

    private Pcm<Stereo32BitFloat> ComposeCore(long offset)
    {
        return composer.Compose(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * offset / sampleRate))
               ?? throw new InvalidOperationException("composer.Composeがnullを返しました。");
    }

    public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        int lengthInt = (int)length;
        var pcm = new Pcm<Stereo32BitFloat>((int)sampleRate, lengthInt);
        int written = 0;
        while (written < lengthInt)
        {
            using var tmp = ComposeCore(offset + written);
            var srcSpan = tmp.DataSpan;
            var dstSpan = pcm.DataSpan;
            srcSpan[..Math.Min(lengthInt - written, srcSpan.Length)].CopyTo(dstSpan[written..]);
            written += srcSpan.Length;
        }

        progress.OnNext(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * (offset + length) / sampleRate));
        return ValueTask.FromResult(pcm);
    }
}
