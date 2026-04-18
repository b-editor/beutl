using System.Diagnostics.CodeAnalysis;
using Beutl.Extensions.AVFoundation.Interop;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.AVFoundation.Decoding;

public sealed class AVFReader : MediaReader
{
    private readonly ILogger _logger = Log.CreateLogger<AVFReader>();
    private AVFReaderSafeHandle? _handle;
    private BitmapColorType _videoColorType = BitmapColorType.Bgra8888;
    private BitmapAlphaType _videoAlphaType = BitmapAlphaType.Unpremul;
    private BitmapColorSpace _videoColorSpace = BitmapColorSpace.Srgb;

    public AVFReader(string file, MediaOptions options, AVFDecodingExtension extension)
    {
        var opts = new BeutlReaderOptions
        {
            MaxVideoBufferSize = extension.Settings?.MaxVideoBufferSize ?? 4,
            MaxAudioBufferSize = extension.Settings?.MaxAudioBufferSize ?? 20,
            ThresholdFrameCount = extension.Settings?.ThresholdFrameCount ?? 30,
            ThresholdSampleCount = extension.Settings?.ThresholdSampleCount ?? 30000,
        };

        bool wantsVideo = options.StreamsToLoad.HasFlag(MediaMode.Video);
        bool wantsAudio = options.StreamsToLoad.HasFlag(MediaMode.Audio);

        int modeFlags = 0;
        if (wantsVideo) modeFlags |= 1;
        if (wantsAudio) modeFlags |= 2;

        int r = BeutlAVFNative.beutl_avf_reader_open(file, modeFlags, ref opts, out _handle);
        BeutlAVFException.ThrowIfFailed(r);

        BeutlAVFException.ThrowIfFailed(BeutlAVFNative.beutl_avf_reader_has_video(_handle, out int hasVideo));
        BeutlAVFException.ThrowIfFailed(BeutlAVFNative.beutl_avf_reader_has_audio(_handle, out int hasAudio));

        HasVideo = hasVideo != 0;
        HasAudio = hasAudio != 0;

        // Fail construction when the caller explicitly asked for a stream that the file does
        // not contain. Callers (SoundSource, VideoSource) dereference VideoInfo/AudioInfo
        // immediately after a successful Open, so leaving these null would produce an NRE
        // on video-only or audio-only inputs.
        if (wantsVideo && !HasVideo)
        {
            _handle.Dispose();
            throw new InvalidOperationException($"No video track found in '{file}'.");
        }
        if (wantsAudio && !HasAudio)
        {
            _handle.Dispose();
            throw new InvalidOperationException($"No audio track found in '{file}'.");
        }

        if (HasVideo)
        {
            BeutlAVFException.ThrowIfFailed(
                BeutlAVFNative.beutl_avf_reader_get_video_info(_handle, out var vi));
            VideoInfo = new VideoStreamInfo(
                BeutlAVFNative.FourCCToString(vi.CodecFourCC),
                vi.NominalFrameCount,
                new PixelSize(vi.Width, vi.Height),
                new Rational(vi.FrameRateNum, vi.FrameRateDen));

            bool isHdr = vi.IsHdr != 0;
            _videoColorType = isHdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888;
            _videoColorSpace = ColorSpaceMapper.BuildColorSpace(
                isHdr,
                (BeutlTransferFunction)vi.TransferFunction,
                (BeutlColorPrimaries)vi.ColorPrimaries);
            _logger.LogInformation(
                "Video color space: {ColorSpace} ({Hdr})",
                _videoColorSpace, isHdr ? "HDR" : "SDR");
        }

        if (HasAudio)
        {
            BeutlAVFException.ThrowIfFailed(
                BeutlAVFNative.beutl_avf_reader_get_audio_info(_handle, out var ai));
            AudioInfo = new AudioStreamInfo(
                BeutlAVFNative.FourCCToString(ai.CodecFourCC),
                new Rational(ai.DurationNum, ai.DurationDen),
                ai.SampleRate,
                ai.ChannelCount);
        }
    }

    public override VideoStreamInfo VideoInfo { get; } = default!;

    public override AudioStreamInfo AudioInfo { get; } = default!;

    public override bool HasVideo { get; }

    public override bool HasAudio { get; }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        if (!HasVideo || _handle == null || _handle.IsClosed || _handle.IsInvalid)
        {
            return false;
        }

        var bitmap = new Bitmap(
            VideoInfo.FrameSize.Width, VideoInfo.FrameSize.Height,
            _videoColorType, _videoAlphaType, _videoColorSpace);
        try
        {
            int result = BeutlAVFNative.beutl_avf_reader_read_video(
                _handle, frame, bitmap.Data, bitmap.ByteCount, bitmap.RowBytes);
            if (result != 0)
            {
                _logger.LogWarning(
                    "beutl_avf_reader_read_video failed (code={Code}): {Message}",
                    result, BeutlAVFNative.GetLastErrorMessage());
                bitmap.Dispose();
                return false;
            }

            image = Ref<Bitmap>.Create(bitmap);
            return true;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
    {
        sound = null;
        if (!HasAudio || _handle == null || _handle.IsClosed || _handle.IsInvalid)
        {
            return false;
        }

        var buffer = new Pcm<Stereo32BitFloat>(AudioInfo.SampleRate, length);
        try
        {
            int capacityBytes = length * (int)buffer.SampleSize;
            int result = BeutlAVFNative.beutl_avf_reader_read_audio(
                _handle, start, length, buffer.Data, capacityBytes);
            if (result != 0)
            {
                _logger.LogWarning(
                    "beutl_avf_reader_read_audio failed (code={Code}): {Message}",
                    result, BeutlAVFNative.GetLastErrorMessage());
                buffer.Dispose();
                return false;
            }

            sound = Ref<IPcm>.Create(buffer);
            return true;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
