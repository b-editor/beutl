namespace Beutl.Media.Audio;

public interface ISound : IDisposable, ICloneable
{
    int SampleRate { get; }

    int NumSamples { get; }

    TimeSpan Duration { get; }

    Rational DurationRational { get; }

    IntPtr Data { get; }

    bool IsDisposed { get; }

    Type SampleType { get; }
}
