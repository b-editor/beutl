using Beutl.Media.Music.Samples;

namespace Beutl.Media.Music;

public interface IPcm : IDisposable, ICloneable
{
    int SampleRate { get; }

    int NumSamples { get; }

    TimeSpan Duration { get; }

    Rational DurationRational { get; }

    IntPtr Data { get; }

    bool IsDisposed { get; }

    Type SampleType { get; }

    Pcm<TConvert> Convert<TConvert>()
        where TConvert : unmanaged, ISample<TConvert>;

    void Amplifier(Sample level);
}
