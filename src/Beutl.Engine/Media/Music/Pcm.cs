using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Media.Music.Samples;

namespace Beutl.Media.Music;

public sealed unsafe class Pcm<T> : IPcm
    where T : unmanaged, ISample<T>
{
    private readonly bool _requireDispose = true;
    private T* _pointer;

    public Pcm(int rate, int samples)
    {
        SampleRate = rate;
        NumSamples = samples;

        _pointer = (T*)NativeMemory.AllocZeroed((nuint)(samples * sizeof(T)));
    }

    public Pcm(int rate, int length, IntPtr data)
    {
        _requireDispose = false;
        SampleRate = rate;
        NumSamples = length;

        _pointer = (T*)data;
    }

    ~Pcm()
    {
        Dispose();
    }

    public Span<T> DataSpan
    {
        get
        {
            ThrowIfDisposed();

            return new Span<T>(_pointer, NumSamples);
        }
    }

    public int SampleRate { get; }

    public int NumSamples { get; }

    public int NumChannels => T.GetNumChannels();

    public nint SampleSize => sizeof(T);

    public TimeSpan Duration => TimeSpan.FromSeconds(NumSamples / (double)SampleRate);

    public bool IsDisposed { get; private set; }

    public IntPtr Data => (IntPtr)_pointer;

    public Pcm<TConvert> Convert<TConvert>()
        where TConvert : unmanaged, ISample<TConvert>
    {
        var result = new Pcm<TConvert>(SampleRate, NumSamples);

        Parallel.For(0, NumSamples, i =>
        {
            result.DataSpan[i] = TConvert.ConvertFrom(T.ConvertTo(DataSpan[i]));
        });

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void Dispose()
    {
        if (!IsDisposed && _requireDispose)
        {
            if (_pointer != null)
                NativeMemory.Free(_pointer);

            _pointer = null;
        }

        IsDisposed = true;
        GC.SuppressFinalize(this);
    }

    public Pcm<T> Clone()
    {
        ThrowIfDisposed();

        var img = new Pcm<T>(SampleRate, NumSamples);
        DataSpan.CopyTo(img.DataSpan);

        return img;
    }

    object ICloneable.Clone()
    {
        return Clone();
    }
}
