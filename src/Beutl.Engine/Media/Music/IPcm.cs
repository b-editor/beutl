using Beutl.Media.Music.Samples;

namespace Beutl.Media.Music;

public interface IPcm : IDisposable, ICloneable
{
    int SampleRate { get; }

    int NumSamples { get; }

    int NumChannels { get; }

    nint SampleSize { get; }

    TimeSpan Duration { get; }

    IntPtr Data { get; }

    bool IsDisposed { get; }

    Pcm<TConvert> Convert<TConvert>()
        where TConvert : unmanaged, ISample<TConvert>;
}
