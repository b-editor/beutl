using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.Versioning;

using Beutl.Audio;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using Microsoft.Extensions.Logging;

using NAudio.Wave;
using Vortice.MediaFoundation;

using static NAudio.Wave.MediaFoundationReader;

using AudioBuffer = Beutl.Audio.AudioBuffer;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

// Media Foundation and NAudio.Wasapi exist only on Windows.
[SupportedOSPlatform("windows")]
public class MFReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<MFReader>();
    private readonly string _file;

    private readonly IMediaFoundationVideoDecoder? _decoder;
    private readonly VideoStreamInfo? _videoInfo;
    private readonly BitmapColorSpace _colorSpace = BitmapColorSpace.Srgb;

    private readonly AudioStreamInfo? _audioInfo;
    private readonly MediaFoundationReader? _audioReader;
    private readonly WaveFormat? _waveFormat;
    private readonly ISampleProvider? _provider;

    public MFReader(string file, MediaOptions options, MFDecodingExtension extension)
        : this(file, options, extension, CreateVideoDecoder, CreateAudioReader)
    {
    }

    internal MFReader(
        string file,
        MediaOptions options,
        MFDecodingExtension extension,
        Func<string, MFDecodingExtension, IMediaFoundationVideoDecoder> createVideoDecoder,
        Func<string, MediaFoundationReaderSettings, MediaFoundationReader> createAudioReader,
        Func<string, bool>? hasAudioStream = null)
    {
        ArgumentNullException.ThrowIfNull(createVideoDecoder);
        ArgumentNullException.ThrowIfNull(createAudioReader);
        hasAudioStream ??= MFStreamProbe.HasAudioStream;

        _file = file;
        try
        {
            if (options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                try
                {
                    _decoder = createVideoDecoder(file, extension);
                    MFMediaInfo info = _decoder.GetMediaInfo();
                    _videoInfo = new VideoStreamInfo(
                        info.VideoFormatName ?? "Unknown",
                        info.TotalFrameCount,
                        new PixelSize(info.ImageFormat.Width, info.ImageFormat.Height),
                        new Rational(info.Fps.Numerator, info.Fps.Denominator));
                    _colorSpace = BitmapColorSpaceMapping.BuildTargetColorSpace(info.Color.Transfer, info.Color.Primaries);
                    HasVideo = true;
                }
                catch (NoVideoStreamException) when (options.StreamsToLoad.HasFlag(MediaMode.Audio))
                {
                    // The file has no video stream; fall through so audio-only files (e.g. .mp3)
                    // can still be opened via the NAudio path below. Genuine video initialization
                    // failures are not caught here, so other decoders (e.g. FFmpeg) can retry.
                }
            }

            if (options.StreamsToLoad.HasFlag(MediaMode.Audio))
            {
                if (HasVideo && !hasAudioStream(_file))
                {
                    // The file has a usable video stream but no audio stream. Keep the reader
                    // open as video-only for the default AudioVideo mode. If an audio stream
                    // exists and NAudio still fails below, let the exception propagate so other
                    // decoders (e.g. FFmpeg) can retry with audio.
                    _logger.LogInformation("File contains no audio stream; continuing video-only.");
                }
                else
                {
                    _audioReader = createAudioReader(_file, new MediaFoundationReaderSettings
                    {
                        RequestFloatOutput = true
                    });
                    _waveFormat = _audioReader.WaveFormat;
                    _provider = _audioReader.ToSampleProvider();

                    _audioInfo = new AudioStreamInfo(
                        CodecName: _waveFormat.Encoding.ToString(),
                        Duration: new Rational(_audioReader.Length, _waveFormat.AverageBytesPerSecond),
                        SampleRate: _waveFormat.SampleRate,
                        NumChannels: _waveFormat.Channels)
                    {
                        ChannelLayout = ReadChannelLayout(_file, _waveFormat)
                    };
                    HasAudio = true;
                }
            }
        }
        catch
        {
            // Best-effort cleanup of any partially-initialized resources. Dispose() is
            // non-throwing (see Dispose(bool)), so this cannot replace the original
            // initialization exception that we rethrow below.
            Dispose();
            throw;
        }
    }

    private static IMediaFoundationVideoDecoder CreateVideoDecoder(string file, MFDecodingExtension extension)
        => new MFDecoder(file, extension);

    private static MediaFoundationReader CreateAudioReader(string file, MediaFoundationReaderSettings settings)
        => new(file, settings);

    private static AudioChannelLayout ReadChannelLayout(string file, WaveFormat format)
    {
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(1u);
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(file, attributes);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
            using var partialType = MediaFactory.MFCreateMediaType();
            partialType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio).CheckError();
            partialType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Float).CheckError();
            // Match NAudio's negotiated float output, including its HE-AAC rate/channel adjustment.
            partialType.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)format.Channels).CheckError();
            partialType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)format.SampleRate).CheckError();
            reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, partialType);
            using var currentType = reader.GetCurrentMediaType(SourceReaderIndex.FirstAudioStream);
            if (currentType.GetUInt32(MediaTypeAttributeKeys.AudioChannelMask, out uint mask).Success)
            {
                mask &= 0x3FFFF;
                if (BitOperations.PopCount(mask) == format.Channels)
                    return new AudioChannelLayout(format.Channels, (AudioSpeakers)mask);
            }
        }
        catch
        {
            // Optional channel metadata must not prevent an already-open audio stream from decoding.
        }

        return format.Channels <= 2
            ? AudioChannelLayout.GetDefault(format.Channels)
            : AudioChannelLayout.Discrete(format.Channels);
    }

    public override VideoStreamInfo VideoInfo => _videoInfo ?? throw new NotSupportedException();

    public override AudioStreamInfo AudioInfo => _audioInfo ?? throw new NotSupportedException();

    public override bool HasVideo { get; }

    public override bool HasAudio { get; }

    [MemberNotNullWhen(true, nameof(_decoder))]
    private bool CanReadVideo() => HasVideo && _decoder != null && !IsDisposed;

    [MemberNotNullWhen(true, nameof(_audioReader), nameof(_waveFormat), nameof(_provider))]
    private bool CanReadAudio() => !IsDisposed && _audioReader != null && _waveFormat != null && _provider != null;

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadVideoCore(frame, out image);
        }
        else
        {
            image = null;
            if (!CanReadVideo())
                return false;

            (bool result, Ref<Bitmap>? image1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadVideoCore(frame, out Ref<Bitmap>? image1);
                return (ret, image1);
            });
            image = image1!;
            return result;
        }
    }

    private unsafe bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        if (!CanReadVideo())
            return false;

        MFMediaInfo info = _decoder.GetMediaInfo();
        int w = info.ImageFormat.Width;
        int h = info.ImageFormat.Height;

        int yuy2Size = MFFrameBufferSize.CalculateYuy2(w, h);
        byte[] yuy2Buffer = ArrayPool<byte>.Shared.Rent(yuy2Size);
        try
        {
            int r;
            fixed (byte* ptr = yuy2Buffer)
            {
                r = _decoder.ReadFrame(frame, (nint)ptr);
            }

            if (r != 0)
            {
                var result = new Bitmap(w, h, colorSpace: _colorSpace);
                fixed (byte* srcPtr = yuy2Buffer)
                {
                    YuvConversion.Yuy2ToBgra(srcPtr, (byte*)result.Data, result.RowBytes, w, h,
                        info.Color.Matrix == VideoColorMatrix.Bt709, info.Color.Range == VideoColorRange.Full);
                }

                image = Ref<Bitmap>.Create(result);
                return true;
            }
            else
            {
                return false;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(yuy2Buffer);
        }
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadAudioCore(start, length, out sound);
        }
        else
        {
            sound = null;
            if (!CanReadAudio())
                return false;

            (bool result, AudioBuffer? sound1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadAudioCore(start, length, out AudioBuffer? sound1);
                return (ret, sound1);
            });
            sound = sound1!;
            return result;
        }
    }

    private bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
    {
        sound = null;
        if (!CanReadAudio())
            return false;

        // Media Foundation can reject a seek past EOF instead of returning an empty read.
        // Use the source position for EOF as well as seeking. Sample 0 is the
        // container start, like video frame zero.
        // _audioReader.Length is PCM bytes; zero can also mean an unknown duration.
        // Like SampleProviderReader.Read, length <= 0 (a frame count) produces empty PCM.
        long bytePosition = checked((long)start * _waveFormat.BlockAlign);
        if (length <= 0 || (_audioReader.Length > 0 && bytePosition >= _audioReader.Length))
        {
            sound = new AudioBuffer(_waveFormat.SampleRate, _waveFormat.Channels, 0);
            return true;
        }

        if (_audioReader.Length > 0)
        {
            // Compressed decoders can expose padding beyond MF_PD_DURATION.
            // Keep a crossing read inside the normalized media duration.
            length = (int)Math.Min(length, (_audioReader.Length - bytePosition) / _waveFormat.BlockAlign);
        }
        _audioReader.Position = bytePosition;
        sound = SampleProviderReader.Read(_provider, _waveFormat.SampleRate, _waveFormat.Channels, length);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Release each resource independently and never throw: a failure disposing one
        // must not leak the other, and this also runs from the constructor's
        // init-failure path where it must not mask the original exception.
        try
        {
            _decoder?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose the Media Foundation video decoder.");
        }

        try
        {
            _audioReader?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose the Media Foundation audio reader.");
        }
    }
}
