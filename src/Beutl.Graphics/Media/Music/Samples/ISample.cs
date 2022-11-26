namespace Beutl.Media.Music.Samples;

public interface ISample<T>
    where T : unmanaged, ISample<T>
{
    T Compound(T s);

    T Amplifier(Sample level);

    static abstract T ConvertFrom(Sample src);

    static abstract Sample ConvertTo(T src);
}
