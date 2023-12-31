using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Effects;

internal sealed class SoundProcessorGroup : ISoundProcessor
{
    public ISoundProcessor[] Processors { get; set; } = [];

    public void Dispose()
    {
        foreach (ISoundProcessor? item in Processors.AsSpan())
        {
            item.Dispose();
        }

        Processors = [];
    }

    public void Process(in Pcm<Stereo32BitFloat> src, out Pcm<Stereo32BitFloat> dst)
    {
        Pcm<Stereo32BitFloat> cur = src;
        Pcm<Stereo32BitFloat>? tmp = null;
        foreach (ISoundProcessor item in Processors.AsSpan())
        {
            item.Process(cur, out tmp);

            if (cur != src && cur != tmp)
            {
                cur.Dispose();
            }

            cur = tmp;
        }

        dst = tmp ?? cur;
    }
}
