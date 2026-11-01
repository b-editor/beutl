using System.Buffers;
using Silk.NET.OpenAL;

namespace Beutl.Audio.Platforms.OpenAL;

public sealed unsafe class AudioContext : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly int _directChannelsMode;

    public AudioContext() : this(soft: false)
    {
    }

    public AudioContext(bool soft) : this(soft, null, [])
    {
    }

    // Takes ownership of the supplied device, allowing tests to use OpenAL Soft's loopback output.
    internal AudioContext(bool soft, Device* device, ReadOnlySpan<int> attributes)
    {
        _al = AL.GetApi(soft);
        _alc = ALContext.GetApi(soft);
        _device = device != null ? device : _alc.OpenDevice(null);
        fixed (int* attributeList = attributes)
        {
            _context = _alc.CreateContext(_device, attributeList);
        }
        VerifyContextError();

        MakeCurrent();

        bool supportsDirectChannels = _al.IsExtensionPresent("AL_SOFT_direct_channels");
        bool supportsDirectChannelsRemix = _al.IsExtensionPresent("AL_SOFT_direct_channels_remix");
        VerifyAudioError();
        _directChannelsMode = supportsDirectChannelsRemix ? 2 : supportsDirectChannels ? 1 : 0;

        int? outputMode = null;
        if (_alc.IsExtensionPresent(_device, "ALC_SOFT_output_mode"))
        {
            int mode;
            _alc.GetContextProperty(_device, (GetContextInteger)0x19AC, 1, &mode); // ALC_OUTPUT_MODE_SOFT
            VerifyContextError();
            outputMode = mode;
        }

        bool supportsMultichannel = _al.IsExtensionPresent("AL_EXT_MCFORMATS");
        VerifyAudioError();
        DeviceLayout = GetDeviceLayout(outputMode, supportsMultichannel);
    }

    public bool IsDisposed { get; private set; }

    public bool IsCurrent => _alc.GetCurrentContext() == _context;

    public AudioChannelLayout DeviceLayout { get; }

    internal static AudioChannelLayout GetDeviceLayout(int? outputMode, bool supportsMultichannel)
        => supportsMultichannel ? outputMode switch
        {
            0x1504 or 0x1505 => AudioChannelLayout.Surround51, // ALC_SURROUND_5_1_SOFT / ALC_SURROUND_6_1_SOFT
            0x1506 => AudioChannelLayout.Surround71, // ALC_SURROUND_7_1_SOFT
            _ => AudioChannelLayout.Stereo
        } : AudioChannelLayout.Stereo;

    internal static BufferFormat GetBufferFormat(AudioChannelLayout layout)
    {
        if (layout == AudioChannelLayout.Stereo)
            return BufferFormat.Stereo16;
        if (layout == AudioChannelLayout.Surround51)
            return (BufferFormat)0x120B; // AL_FORMAT_51CHN16
        if (layout == AudioChannelLayout.Surround71)
            return (BufferFormat)0x1211; // AL_FORMAT_71CHN16
        throw new ArgumentException("Unsupported OpenAL buffer layout.", nameof(layout));
    }

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

        if (_directChannelsMode != 0)
        {
            // Preserve speaker routing, remixing unmatched channels when supported.
            // AL_DIRECT_CHANNELS_SOFT: AL_REMIX_UNMATCHED_SOFT (2), otherwise AL_TRUE (1).
            _al.SetSourceProperty(src, (SourceInteger)0x1033, _directChannelsMode);
            VerifyAudioError();
        }

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

    public void BufferData(uint bid, AudioBuffer audio)
        => BufferData(bid, audio, AudioChannelLayout.Stereo);

    public void BufferData(uint bid, AudioBuffer audio, AudioChannelLayout layout)
    {
        if (audio.ChannelCount != layout.ChannelCount)
            throw new ArgumentException("The layout must match the buffer's channel count.", nameof(layout));

        BufferFormat format = GetBufferFormat(layout);
        int length = checked(audio.SampleCount * layout.ChannelCount);
        short[] interleaved = ArrayPool<short>.Shared.Rent(length);
        try
        {
            AudioConversion.Interleave(audio, interleaved);
            BufferData(bid, format, interleaved.AsSpan(0, length), audio.SampleRate);
        }
        finally
        {
            ArrayPool<short>.Shared.Return(interleaved);
        }
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
