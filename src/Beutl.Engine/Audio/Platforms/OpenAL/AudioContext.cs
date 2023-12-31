using OpenTK.Audio.OpenAL;

namespace Beutl.Audio.Platforms.OpenAL;

public sealed unsafe class AudioContext : IDisposable
{
    private readonly ALDevice _device;
    private readonly ALContext _context;

    public AudioContext()
    {
        _device = ALC.OpenDevice(null);
        _context = ALC.CreateContext(_device, (int[])null!);

        AlcError alcError = ALC.GetError(_device);

        if (alcError is not AlcError.NoError)
        {
            throw new Exception(alcError.ToString("g"));
        }

        MakeCurrent();
    }

    public bool IsDisposed { get; private set; }

    public bool IsCurrent => ALC.GetCurrentContext() == _context;

    public float Gain
    {
        get
        {
            ThrowIfDisposed();
            MakeCurrent();
            AL.GetListener(ALListenerf.Gain, out float v);

            CheckError();

            return v;
        }
        set
        {
            ThrowIfDisposed();
            MakeCurrent();
            AL.Listener(ALListenerf.Gain, value);

            CheckError();
        }
    }

    internal void CheckError()
    {
        ALError error = AL.GetError();

        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }

        AlcError alcError = ALC.GetError(_device);

        if (alcError is not AlcError.NoError)
        {
            throw new Exception(alcError.ToString("g"));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void MakeCurrent()
    {
        ThrowIfDisposed();

        if (!IsCurrent)
        {
            ALC.MakeContextCurrent(_context);

            CheckError();
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        ALC.MakeContextCurrent(ALContext.Null);
        ALC.DestroyContext(_context);
        ALC.CloseDevice(_device);
        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    public static int GenBuffer()
    {
        int buf = AL.GenBuffer();
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }

        return buf;
    }

    public static int GenSource()
    {
        int src = AL.GenSource();
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }

        return src;
    }

    public static void DeleteBuffer(int handle)
    {
        AL.DeleteBuffer(handle);
    }

    public static void DeleteSource(int handle)
    {
        AL.DeleteSource(handle);
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }
    }

    public static void BufferData<TBuffer>(int bid, ALFormat format, Span<TBuffer> buffer, int freq) where TBuffer : unmanaged
    {
        AL.BufferData<TBuffer>(bid, format, buffer, freq);
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }
    }

    public static void GetBuffer(int bid, ALGetBufferi param, out int value)
    {
        AL.GetBuffer(bid, param, out value);
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }
    }

    public static void SourcePlay(int handle)
    {
        AL.SourcePlay(handle);
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }
    }

    public static void SourceStop(int handle)
    {
        AL.SourceStop(handle);
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }
    }

    public static void SourcePause(int handle)
    {
        AL.SourcePause(handle);
        ALError error = AL.GetError();
        if (error is not ALError.NoError)
        {
            throw new Exception(AL.GetErrorString(error));
        }
    }
}
