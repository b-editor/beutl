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

    public int NumChannels => T.GetNumChannels();

    public nint SampleSize => sizeof(T);

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

    public void ConvertTo<TConvert>(Pcm<TConvert> dst)
        where TConvert : unmanaged, ISample<TConvert>
    {
        if (SampleRate != dst.SampleRate)
        {
            throw new Exception("Sounds with different SampleRates cannot be synthesized.");
        }

        Parallel.For(0, Math.Min(NumSamples, dst.NumSamples), i =>
        {
            dst.DataSpan[i] = TConvert.ConvertFrom(T.ConvertTo(DataSpan[i]));
        });
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

    public void Compound(Pcm<T> sound)
    {
        if (sound.SampleRate != SampleRate) throw new Exception("Sounds with different SampleRates cannot be synthesized.");

        Compound(0, sound);
    }

    public void Compound(int start, Pcm<T> sound)
    {
        if (sound.SampleRate != SampleRate) throw new Exception("Sounds with different SampleRates cannot be synthesized.");

        Parallel.For(start, NumSamples, i =>
        {
            int j = i - start;
            if (j < sound.NumSamples)
            {
                DataSpan[i] = T.Compound(DataSpan[i], sound.DataSpan[j]);
            }
        });
    }

    public Pcm<T> Resamples(int frequency)
    {
        if (SampleRate == frequency) return Clone();

        // 比率
        double ratio = SampleRate / (double)frequency;

        // 1チャンネルのサイズ
        int bits = sizeof(T) * 8;
        int size = (int)(frequency * bits * DurationRational.ToDouble() / bits);

        T* tmp = (T*)NativeMemory.AllocZeroed((nuint)(sizeof(T) * size));
        double index = 0f;
        for (int i = 0; i < size; i++)
        {
            index += ratio;
            int indexFloor = (int)Math.Floor(index - 1);
            if (0 <= indexFloor && indexFloor < DataSpan.Length)
            {
                tmp[i] = DataSpan[indexFloor];
            }
        }

        var result = new Pcm<T>(frequency, size);
        new Span<T>(tmp, size).CopyTo(result.DataSpan);

        NativeMemory.Free(tmp);

        return result;
    }

    public void Amplifier(Sample level)
    {
        Parallel.For(0, NumSamples, i => DataSpan[i] = T.Amplifier(DataSpan[i], level));
    }

    object ICloneable.Clone()
    {
        return Clone();
    }

    public void GetChannelData(int channel, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        foreach (T item in DataSpan)
        {
            T.GetChannelData(item, channel, destination, out int written);
            bytesWritten += written;
            destination = destination.Slice(written);
        }
    }

    IPcm IPcm.Slice(int start)
    {
        return Slice(start);
    }

    IPcm IPcm.Slice(int start, int length)
    {
        return Slice(start, length);
    }

    public Pcm<T> Slice(int start)
    {
        return new Pcm<T>(SampleRate, NumSamples - start, Data + start);
    }

    public Pcm<T> Slice(int start, int length)
    {
        return new Pcm<T>(SampleRate, length, Data + start);
    }
}
