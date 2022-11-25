using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Media.Audio.Pcm;

namespace Beutl.Media.Audio;

public sealed unsafe class Sound<T> : ISound
    where T : unmanaged, IPcm<T>
{
    private readonly bool _requireDispose = true;
    private T* _pointer;

    public Sound(int rate, int samples)
    {
        SampleRate = rate;
        NumSamples = samples;

        _pointer = (T*)NativeMemory.AllocZeroed((nuint)(samples * sizeof(T)));
    }

    public Sound(int rate, int samples, T* data)
    {
        _requireDispose = false;
        SampleRate = rate;
        NumSamples = samples;

        _pointer = data;
    }

    public Sound(int rate, int length, IntPtr data)
    {
        _requireDispose = false;
        SampleRate = rate;
        NumSamples = length;

        _pointer = (T*)data;
    }

    ~Sound()
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

    public TimeSpan Duration => TimeSpan.FromSeconds(NumSamples / (double)SampleRate);

    public Rational DurationRational => new(NumSamples, SampleRate);

    public bool IsDisposed { get; private set; }

    public Type SampleType => typeof(T);

    public IntPtr Data => (IntPtr)_pointer;

    public Sound<TConvert> Convert<TConvert>()
        where TConvert : unmanaged, IPcm<TConvert>
    {
        var result = new Sound<TConvert>(SampleRate, NumSamples);

        Parallel.For(0, NumSamples, i =>
        {
            ref TConvert dst = ref result.DataSpan[i];
            ref T src = ref DataSpan[i];

            dst = dst.ConvertFrom(src.ConvertTo());
        });

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(Sound<T>));
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

    public Sound<T> Clone()
    {
        ThrowIfDisposed();

        var img = new Sound<T>(SampleRate, NumSamples);
        DataSpan.CopyTo(img.DataSpan);

        return img;
    }

    public void Compose(Sound<T> sound)
    {
        if (sound.SampleRate != SampleRate) throw new Exception("Sounds with different SampleRates cannot be synthesized.");

        Parallel.For(0, Math.Min(sound.NumSamples, NumSamples), i => DataSpan[i] = DataSpan[i].Compose(sound.DataSpan[i]));
    }

    public Sound<T> Resamples(int frequency)
    {
        if (SampleRate == frequency) return Clone();

        // 比率
        float ratio = SampleRate / (float)frequency;

        // 1チャンネルのサイズ
        int size = (int)(frequency * DurationRational.ToSingle());

        T* tmp = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * size));
        float index = 0f;
        for (int i = 0; i < size; i++)
        {
            index += ratio;
            tmp[i] = DataSpan[(int)Math.Floor(index)];
        }

        var result = new Sound<T>(frequency, size);
        new Span<T>(tmp, size).CopyTo(result.DataSpan);

        NativeMemory.Free(tmp);

        return result;
    }

    object ICloneable.Clone()
    {
        return Clone();
    }
}
