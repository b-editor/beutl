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

    public Pcm(int rate, int samples, T* data)
    {
        _requireDispose = false;
        SampleRate = rate;
        NumSamples = samples;

        _pointer = data;
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

    public TimeSpan Duration => TimeSpan.FromSeconds(NumSamples / (double)SampleRate);

    public Rational DurationRational => new(NumSamples, SampleRate);

    public bool IsDisposed { get; private set; }

    public Type SampleType => typeof(T);

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
        if (IsDisposed) throw new ObjectDisposedException(nameof(Pcm<T>));
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

    public void Compound(Pcm<T> sound)
    {
        if (sound.SampleRate != SampleRate) throw new Exception("Sounds with different SampleRates cannot be synthesized.");

        Parallel.For(0, Math.Min(sound.NumSamples, NumSamples), i => DataSpan[i] = DataSpan[i].Compound(sound.DataSpan[i]));
    }

    public void Compound(int start, Pcm<T> sound)
    {
        if (sound.SampleRate != SampleRate) throw new Exception("Sounds with different SampleRates cannot be synthesized.");

        Parallel.For(
            start,
            Math.Min(sound.NumSamples, NumSamples),
            i => DataSpan[i] = DataSpan[i].Compound(sound.DataSpan[i - start]));
    }

    public Pcm<T> Resamples(int frequency)
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

        var result = new Pcm<T>(frequency, size);
        new Span<T>(tmp, size).CopyTo(result.DataSpan);

        NativeMemory.Free(tmp);

        return result;
    }

    public void Amplifier(Sample level)
    {
        Parallel.For(0, NumSamples, i => DataSpan[i] = DataSpan[i].Amplifier(level));
    }

    object ICloneable.Clone()
    {
        return Clone();
    }
}
