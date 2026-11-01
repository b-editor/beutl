using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Beutl.Audio.Platforms.AVFoundation.AVFoundationNative;

namespace Beutl.Audio.Platforms.AVFoundation;

[SupportedOSPlatform("macos")]
internal sealed class AVSampleBufferAudioRenderer : IDisposable
{
    private readonly object _gate = new();
    private readonly AudioChannelLayout _layout;
    private readonly int _sampleRate;
    private nint _renderer;
    private nint _synchronizer;
    private nint _format;
    private bool _stopped;

    public AVSampleBufferAudioRenderer(AudioChannelLayout layout, int sampleRate)
    {
        _layout = layout;
        _sampleRate = sampleRate;
        using var pool = new AutoreleasePool();
        try
        {
            _format = CreateFormatDescription(layout, sampleRate);
            _renderer = CreateObject("AVSampleBufferAudioRenderer");
            _synchronizer = CreateObject("AVSampleBufferRenderSynchronizer");
            SendObject(_synchronizer, s_addRenderer, _renderer);
            ThrowIfFailed();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsReadyForMoreMediaData
    {
        get
        {
            lock (_gate)
            {
                using var pool = new AutoreleasePool();
                ThrowIfFailed();
                return !_stopped && SendBool(_renderer, s_ready) != 0;
            }
        }
    }

    public double PositionSeconds
    {
        get
        {
            lock (_gate)
            {
                using var pool = new AutoreleasePool();
                ThrowIfFailed();
                nint timebase = SendIntPtr(_synchronizer, s_timebase);
                return timebase == 0 ? 0 : CMTimebaseGetTime(timebase).Seconds;
            }
        }
    }

    public bool TryEnqueue(AudioBuffer audio, long firstSample)
    {
        lock (_gate)
        {
            using var pool = new AutoreleasePool();
            ThrowIfFailed();
            if (_stopped || SendBool(_renderer, s_ready) == 0) return false;
            if (audio.ChannelCount != _layout.ChannelCount || audio.SampleRate != _sampleRate)
                throw new ArgumentException("The buffer must match the playback format.", nameof(audio));

            nint buffer = CreateSampleBuffer(_format, audio, firstSample);
            try
            {
                SendObject(_renderer, s_enqueue, buffer);
                ThrowIfFailed();
                return true;
            }
            finally
            {
                CFRelease(buffer);
            }
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            using var pool = new AutoreleasePool();
            ThrowIfFailed();
            // Cancellation may have stopped us while a buffer was being composed.
            if (!_stopped)
                SendRateTime(_synchronizer, s_setRateTime, 1f, new CMTime(0, _sampleRate));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            using var pool = new AutoreleasePool();
            // An invalid time preserves the current timebase position.
            if (_synchronizer != 0) SendRateTime(_synchronizer, s_setRateTime, 0f, default);
            if (_renderer != 0) SendVoid(_renderer, s_flush);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Stop();
            using var pool = new AutoreleasePool();
            if (_synchronizer != 0) SendVoid(_synchronizer, s_release);
            if (_renderer != 0) SendVoid(_renderer, s_release);
            if (_format != 0) CFRelease(_format);
            _synchronizer = _renderer = _format = 0;
        }
    }

    private void ThrowIfFailed()
    {
        ObjectDisposedException.ThrowIf(_renderer == 0, this);
        if (SendIntPtr(_renderer, s_status) != 2) return; // AVQueuedSampleBufferRenderingStatusFailed

        nint error = SendIntPtr(_renderer, s_error);
        nint description = SendIntPtr(error, s_localizedDescription);
        nint utf8 = SendIntPtr(description, s_utf8String);
        string? message = utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
        throw new AVSampleBufferAudioRendererException($"AVSampleBufferAudioRenderer failed: {message ?? "unknown error"}.");
    }

    private static nint CreateObject(string className)
    {
        nint cls = objc_getClass(className);
        if (cls == 0) throw new PlatformNotSupportedException($"{className} is unavailable.");
        nint instance = SendIntPtr(SendIntPtr(cls, s_alloc), s_init);
        if (instance == 0) throw new InvalidOperationException($"Could not initialize {className}.");
        return instance;
    }

    // The caller owns the returned CF object. These helpers also allow device-free interop tests.
    internal static unsafe nint CreateFormatDescription(AudioChannelLayout project, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(project.ChannelCount, nameof(project));
        var asbd = new AudioStreamBasicDescription
        {
            SampleRate = sampleRate,
            FormatId = 0x6c70636d, // kAudioFormatLinearPCM ('lpcm')
            FormatFlags = 1 | (1 << 3), // kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked
            BytesPerPacket = checked((uint)project.ChannelCount * sizeof(float)),
            FramesPerPacket = 1,
            BytesPerFrame = checked((uint)project.ChannelCount * sizeof(float)),
            ChannelsPerFrame = (uint)project.ChannelCount,
            BitsPerChannel = 32
        };
        var layout = new ChannelLayout();
        if (project == AudioChannelLayout.Surround51)
            layout.Tag = (121u << 16) | 6; // MPEG_5_1_A: L R C LFE Ls Rs
        else if (project == AudioChannelLayout.Surround71)
            layout.Tag = (189u << 16) | 8; // WAVE_7_1: L R C LFE Rls Rrs Ls Rs
        else if (project.HasPositions)
        {
            // Core Audio's bitmap uses the same WAVE speaker bits and ascending channel order.
            layout.Tag = 1u << 16; // UseChannelBitmap
            layout.Bitmap = (uint)project.Speakers;
        }
        else
        {
            layout.Tag = (147u << 16) | checked((ushort)project.ChannelCount); // DiscreteInOrder
        }

        int status = CMAudioFormatDescriptionCreate(0, in asbd, (nuint)sizeof(ChannelLayout), in layout,
            0, 0, 0, out nint format);
        if (status != 0 && format != 0) CFRelease(format);
        Check(status, nameof(CMAudioFormatDescriptionCreate));
        return format;
    }

    internal static unsafe nint CreateSampleBuffer(nint format, AudioBuffer audio, long firstSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstSample);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(audio.SampleCount);
        int values = checked(audio.SampleCount * audio.ChannelCount);
        nuint bytes = checked((nuint)values * sizeof(float));
        nint block = 0;
        nint buffer = 0;
        try
        {
            Check(CMBlockBufferCreateWithMemoryBlock(0, 0, bytes, 0, 0, 0, bytes,
                1, out block), nameof(CMBlockBufferCreateWithMemoryBlock)); // AssureMemoryNow
            Check(CMBlockBufferGetDataPointer(block, 0, out _, out _, out nint data),
                nameof(CMBlockBufferGetDataPointer));
            AudioConversion.Interleave(audio, new Span<float>((void*)data, values));
            Check(CMAudioSampleBufferCreateReadyWithPacketDescriptions(0, block, format, audio.SampleCount,
                new CMTime(firstSample, audio.SampleRate), 0, out buffer),
                nameof(CMAudioSampleBufferCreateReadyWithPacketDescriptions));
            return buffer;
        }
        catch
        {
            if (buffer != 0) CFRelease(buffer);
            throw;
        }
        finally
        {
            if (block != 0) CFRelease(block);
        }
    }
}

internal sealed class AVSampleBufferAudioRendererException(string message) : Exception(message);
