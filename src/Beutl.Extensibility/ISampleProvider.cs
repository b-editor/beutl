using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Extensibility;

public interface ISampleProvider
{
    public long SampleCount { get; }

    public long SampleRate { get; }

    public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length);
}
