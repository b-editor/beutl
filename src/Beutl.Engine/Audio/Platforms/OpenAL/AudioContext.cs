using Silk.NET.OpenAL;

namespace Beutl.Audio.Platforms.OpenAL;

public sealed unsafe class AudioContext : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;

    public AudioContext()
    {
        _al = AL.GetApi();
        _alc = ALContext.GetApi();
        _device = _alc.OpenDevice(null);
        _context = _alc.CreateContext(_device, null);
        VerifyContextError();

        MakeCurrent();
    }

    public bool IsDisposed { get; private set; }

    public bool IsCurrent => _alc.GetCurrentContext() == _context;

    public float Gain
    {
        get
        {
            ThrowIfDisposed();
            MakeCurrent();
            _al.GetListenerProperty(ListenerFloat.Gain, out float v);

            VerifyError();

            return v;
        }
        set
        {
            ThrowIfDisposed();
            MakeCurrent();
            _al.SetListenerProperty(ListenerFloat.Gain, value);

            VerifyError();
        }
    }

    public void VerifyAudioError()
    {
        var error = _al.GetError();

        if (error is not AudioError.NoError)
        {
            throw new Exception(error.ToString());
        }
    }

    public void VerifyContextError()
    {
        var alcError = _alc.GetError(_device);

        if (alcError is not ContextError.NoError)
        {
            throw new Exception(alcError.ToString("g"));
        }
    }

    public void VerifyError()
    {
        VerifyAudioError();
        VerifyContextError();
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
            _alc.MakeContextCurrent(_context);

            VerifyError();
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        GC.SuppressFinalize(this);

        IsDisposed = true;
    }

    public uint GenBuffer()
    {
        uint buf = _al.GenBuffer();
        VerifyAudioError();

        return buf;
    }

    public uint[] GenBuffers(int count)
    {
        uint[] buf = _al.GenBuffers(count);
        VerifyAudioError();

        return buf;
    }

    public uint GenSource()
    {
        uint src = _al.GenSource();
        VerifyAudioError();

        return src;
    }

    public void DeleteBuffers(Span<uint> handle)
    {
        fixed (uint* ptr = handle)
        {
            _al.DeleteBuffers(handle.Length, ptr);
        }
    }

    public void DeleteBuffer(uint handle)
    {
        _al.DeleteBuffer(handle);
    }

    public void DeleteSource(uint handle)
    {
        _al.DeleteSource(handle);
        VerifyAudioError();
    }

    public void BufferData<TBuffer>(uint bid, BufferFormat format, Span<TBuffer> buffer, int freq)
        where TBuffer : unmanaged
    {
        var size = sizeof(TBuffer) * buffer.Length;
        fixed (void* ptr = buffer)
        {
            _al.BufferData(bid, format, ptr, size, freq);
        }

        VerifyAudioError();
    }

    public SourceState GetSourceState(uint source)
    {
        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int value);
        VerifyAudioError();
        return (SourceState)value;
    }

    public void GetSource(uint source, GetSourceInteger param, out int value)
    {
        _al.GetSourceProperty(source, param, out value);
        VerifyAudioError();
    }

    public void GetBuffer(uint bid, GetBufferInteger param, out int value)
    {
        _al.GetBufferProperty(bid, param, out value);
        VerifyAudioError();
    }

    public void SourcePlay(uint handle)
    {
        _al.SourcePlay(handle);
        VerifyAudioError();
    }

    public void SourceStop(uint handle)
    {
        _al.SourceStop(handle);
        VerifyAudioError();
    }

    public void SourcePause(uint handle)
    {
        _al.SourcePause(handle);
        VerifyAudioError();
    }

    public void SourceQueueBuffer(uint source, uint bid)
    {
        _al.SourceQueueBuffers(source, 1, &bid);
        VerifyAudioError();
    }

    public uint SourceUnqueueBuffer(uint source)
    {
        uint bid = 0;
        _al.SourceUnqueueBuffers(source, 1, &bid);
        VerifyAudioError();

        return bid;
    }

    public void Source(uint source, SourceInteger param, uint value)
    {
        _al.SetSourceProperty(source, param, value);
        VerifyAudioError();
    }
}
