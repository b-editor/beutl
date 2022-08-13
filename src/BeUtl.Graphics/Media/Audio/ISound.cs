namespace BeUtl.Media.Audio;

public interface ISound : IDisposable, ICloneable
{
    int SampleRate { get; }

    int NumSamples { get; }

    TimeSpan Duration { get; }

    Rational DurationRational { get; }

    bool IsDisposed { get; }
}
