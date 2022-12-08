using Beutl.Media.Music.Samples;

namespace Beutl.Media.Music;

public interface IPcm : IDisposable, ICloneable
{
    int SampleRate { get; }

    int NumSamples { get; }
    
    int NumChannels { get; }

    nint SampleSize { get; }

    TimeSpan Duration { get; }

    Rational DurationRational { get; }

    IntPtr Data { get; }

    bool IsDisposed { get; }

    Type SampleType { get; }

    IPcm Slice(int start);
    
    IPcm Slice(int start, int length);

    Pcm<TConvert> Convert<TConvert>()
        where TConvert : unmanaged, ISample<TConvert>;

    void ConvertTo<TConvert>(Pcm<TConvert> dst)
        where TConvert : unmanaged, ISample<TConvert>;

    void Amplifier(Sample level);

    void GetChannelData(int channel, Span<byte> destination, out int bytesWritten);
}
