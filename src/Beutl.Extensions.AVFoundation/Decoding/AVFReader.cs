using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Extensions.AVFoundation.Interop;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
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
        BeutlAVFNative.EnsureCompatibleVersion();

        var opts = new BeutlReaderOptions
        {
            MaxVideoBufferSize = extension.Settings.MaxVideoBufferSize.Value,
            MaxAudioBufferSize = extension.Settings.MaxAudioBufferSize.Value,
            ThresholdFrameCount = extension.Settings.ThresholdFrameCount.Value,
            ThresholdSampleCount = extension.Settings.ThresholdSampleCount.Value,
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

        // Like the other decoders, open the requested streams the file has, and fail only when it has none of them.
        if (!HasVideo && !HasAudio)
        {
            _handle.Dispose();
            throw new InvalidOperationException($"No requested track found in '{file}'.");
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
                (BitmapColorTransfer)vi.TransferFunction,
                (BitmapColorPrimaries)vi.ColorPrimaries);
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
                ai.OutputChannelCount)
            {
                ChannelLayout = ai.SpeakerMask == 0
                    ? AudioChannelLayout.Discrete(ai.OutputChannelCount)
                    : new AudioChannelLayout(ai.OutputChannelCount, (AudioSpeakers)ai.SpeakerMask)
            };
        }
    }

    public override VideoStreamInfo VideoInfo { get; } = default!;

    public override AudioStreamInfo AudioInfo { get; } = default!;

    public override bool HasVideo { get; }

    public override bool HasAudio { get; }

    [MemberNotNullWhen(true, nameof(_handle))]
    private bool IsHandleUsable() => _handle != null && !_handle.IsClosed && !_handle.IsInvalid;

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
    {
        image = null;
        if (!HasVideo || !IsHandleUsable())
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

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
    {
        sound = null;
        if (!HasAudio || !IsHandleUsable())
        {
            return false;
        }

        int channels = AudioInfo.ChannelLayout.ChannelCount;
        if (length <= 0)
        {
            sound = new AudioBuffer(AudioInfo.SampleRate, channels, 0);
            return true;
        }

        // Native code writes interleaved float32 in the declared layout; only the decoded part is kept.
        int capacityBytes = checked(length * channels * sizeof(float));
        float[] interleaved = ArrayPool<float>.Shared.Rent(capacityBytes / sizeof(float));
        try
        {
            int result;
            int decoded;
            unsafe
            {
                fixed (float* data = interleaved)
                {
                    result = BeutlAVFNative.beutl_avf_reader_read_audio(
                        _handle, start, length, (IntPtr)data, capacityBytes, out decoded);
                }
            }

            if (result != 0)
            {
                _logger.LogWarning(
                    "beutl_avf_reader_read_audio failed (code={Code}): {Message}",
                    result, BeutlAVFNative.GetLastErrorMessage());
                return false;
            }

            if ((uint)decoded > (uint)length)
            {
                throw new InvalidOperationException($"BeutlAVF returned an invalid decoded sample count: {decoded}.");
            }

            var audio = new AudioBuffer(AudioInfo.SampleRate, channels, decoded);
            AudioConversion.Deinterleave(interleaved.AsSpan(0, decoded * channels), audio);
            sound = audio;
            return true;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
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
