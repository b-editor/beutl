namespace BeUtl.Media.Audio.Pcm;

public interface IPcm<T>
    where T : unmanaged, IPcm<T>
{
    T Compose(T s);

    T ConvertFrom(Stereo32BitFloat data);

    Stereo32BitFloat ConvertTo();
}
