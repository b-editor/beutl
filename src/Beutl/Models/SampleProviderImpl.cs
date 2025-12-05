using System.Reactive.Subjects;
using Beutl.Audio.Composing;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;

namespace Beutl.Models;

public class SampleProviderImpl(Scene scene, SceneComposer composer, long sampleRate, Subject<TimeSpan> progress)
    : ISampleProvider
{
    private Pcm<Stereo32BitFloat>? _lastPcm;
    private long _lastOffset;

    public long SampleCount => (long)(scene.Duration.TotalSeconds * sampleRate);

    public long SampleRate => sampleRate;

    public async ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        try
        {
            if (ComposeThread.Dispatcher.CheckAccess())
            {
                return ComposeCore(offset, length);
            }
            else
            {
                return await ComposeThread.Dispatcher.InvokeAsync(() => ComposeCore(offset, length));
            }
        }
        finally
        {
            progress.OnNext(TimeSpan.FromTicks(TimeSpan.TicksPerSecond * (offset + length) / sampleRate));
        }
    }

    private Pcm<Stereo32BitFloat> ComposeCore(long offset, long length)
    {
        int lengthInt = (int)length;
        var pcm = new Pcm<Stereo32BitFloat>((int)sampleRate, lengthInt);
        int written = 0;
        while (written < lengthInt)
        {
            if (_lastPcm != null)
            {
                if (offset + written < _lastOffset + _lastPcm.NumSamples)
                {
                    var srcSpan2 = _lastPcm.DataSpan[(int)(offset - _lastOffset)..];
                    var dstSpan2 = pcm.DataSpan[written..];
                    var lengthToCopy2 = Math.Min(lengthInt - written, srcSpan2.Length);
                    srcSpan2[..lengthToCopy2].CopyTo(dstSpan2);
                    written += lengthToCopy2;
                    continue;
                }
            }

            var tmp = ComposeCore(offset + written);
            var srcSpan = tmp.DataSpan;
            var dstSpan = pcm.DataSpan;
            var lengthToCopy = Math.Min(lengthInt - written, srcSpan.Length);
            srcSpan[..Math.Min(lengthInt - written, srcSpan.Length)].CopyTo(dstSpan[written..]);
            written += lengthToCopy;
        }

        return pcm;
    }

    private Pcm<Stereo32BitFloat> ComposeCore(long offset)
    {
        _lastPcm?.Dispose();
        _lastPcm = null;
        var buffer = composer.Compose(new (TimeSpan.FromTicks(TimeSpan.TicksPerSecond * offset / sampleRate) + scene.Start,
                         TimeSpan.FromSeconds(1)))
                     ?? throw new InvalidOperationException("composer.Composeがnullを返しました。");
        var pcm = buffer.ToPcm();
        _lastPcm = pcm;
        _lastOffset = offset;

        return pcm;
    }
}
