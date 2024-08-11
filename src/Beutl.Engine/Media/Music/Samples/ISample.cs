namespace Beutl.Media.Music.Samples;

public interface ISample<T>
    where T : unmanaged, ISample<T>
{
    static abstract T Compound(T s1, T s2);

    static abstract T Amplifier(T s, Sample level);

    static abstract int GetNumChannels();

    static abstract void GetChannelData(T s, int channel, Span<byte> destination, out int bytesWritten);

    static abstract T ConvertFrom(Sample src);

    static abstract Sample ConvertTo(T src);
}
