namespace Beutl.Media.Music.Samples;

public interface ISample<T>
    where T : unmanaged, ISample<T>
{
    static abstract int GetNumChannels();

    static abstract T ConvertFrom(Sample src);

    static abstract Sample ConvertTo(T src);
}
