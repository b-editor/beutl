using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using Microsoft.Extensions.Logging;

using NAudio.Wave;

using static NAudio.Wave.MediaFoundationReader;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class MFReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<MFReader>();
    private readonly string _file;
    private readonly MediaOptions _options;

    private readonly IMediaFoundationVideoDecoder? _decoder;
    private readonly VideoStreamInfo? _videoInfo;

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
        Func<string, MediaOptions, MFDecodingExtension, IMediaFoundationVideoDecoder> createVideoDecoder,
        Func<string, MediaFoundationReaderSettings, MediaFoundationReader> createAudioReader)
    {
        ArgumentNullException.ThrowIfNull(createVideoDecoder);
        ArgumentNullException.ThrowIfNull(createAudioReader);

        _file = file;
        _options = options;
        try
        {
            if (options.StreamsToLoad.HasFlag(MediaMode.Video))
            {
                try
                {
                    _decoder = createVideoDecoder(file, new MediaOptions(MediaMode.Video), extension);
                    MFMediaInfo info = _decoder.GetMediaInfo();
                    _videoInfo = new VideoStreamInfo(
                        info.VideoFormatName ?? "Unknown",
                        info.TotalFrameCount,
                        new PixelSize(info.ImageFormat.Width, info.ImageFormat.Height),
                        new Rational(info.Fps.Numerator, info.Fps.Denominator));
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
                _audioReader = createAudioReader(_file, new MediaFoundationReaderSettings
                {
                    RequestFloatOutput = true
                });
                _waveFormat = _audioReader.WaveFormat;

                _provider = _audioReader.ToSampleProvider().ToStereo();

                _audioInfo = new AudioStreamInfo(
                    CodecName: _waveFormat.Encoding.ToString(),
                    Duration: new Rational(_audioReader.Length, _waveFormat.AverageBytesPerSecond),
                    SampleRate: _waveFormat.SampleRate,
                    NumChannels: _waveFormat.Channels);
                HasAudio = true;
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

    private static IMediaFoundationVideoDecoder CreateVideoDecoder(
        string file,
        MediaOptions options,
        MFDecodingExtension extension)
        => new MFDecoder(file, options, extension);

    private static MediaFoundationReader CreateAudioReader(string file, MediaFoundationReaderSettings settings)
        => new(file, settings);

    public override VideoStreamInfo VideoInfo => _videoInfo ?? throw new NotSupportedException();

    public override AudioStreamInfo AudioInfo => _audioInfo ?? throw new NotSupportedException();

    public override bool HasVideo { get; }

    public override bool HasAudio { get; }

    public override unsafe bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadVideoCore(frame, out image);
        }
        else
        {
            image = null;
            if (!HasVideo || _decoder == null || IsDisposed)
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
        if (!HasVideo || _decoder == null || IsDisposed)
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
                var result = new Bitmap(w, h);
                fixed (byte* srcPtr = yuy2Buffer)
                {
                    YuvConversion.Yuy2ToBgra(srcPtr, (byte*)result.Data, result.RowBytes, w, h);
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

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        if (MFThread.Dispatcher.CheckAccess())
        {
            return ReadAudioCore(start, length, out sound);
        }
        else
        {
            sound = null;
            if (IsDisposed || _audioReader == null || _waveFormat == null || _provider == null)
                return false;

            (bool result, Ref<IPcm>? sound1) = MFThread.Dispatcher.Invoke(() =>
            {
                bool ret = ReadAudioCore(start, length, out Ref<IPcm>? sound1);
                return (ret, sound1);
            });
            sound = sound1!;
            return result;
        }
    }

    private bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        sound = null;
        if (IsDisposed || _audioReader == null || _waveFormat == null || _provider == null)
            return false;

        _audioReader.CurrentTime = TimeSpan.FromSeconds(start / (double)_waveFormat.SampleRate);
        var tmp = new Pcm<Stereo32BitFloat>(_waveFormat.SampleRate, (int)(length / (double)_waveFormat.SampleRate * _waveFormat.SampleRate));

        float[] buffer = new float[tmp.NumSamples * 2];
        int count = _provider.Read(buffer, 0, buffer.Length);
        if (count >= 0)
        {
            buffer.CopyTo(MemoryMarshal.Cast<Stereo32BitFloat, float>(tmp.DataSpan));

            sound = Ref<IPcm>.Create(tmp);
            return true;
        }
        else
        {
            tmp.Dispose();
            return false;
        }
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
