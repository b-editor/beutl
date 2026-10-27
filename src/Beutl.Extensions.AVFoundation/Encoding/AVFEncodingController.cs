using System.Buffers;
using System.Runtime.Versioning;
using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Extensions.AVFoundation.Interop;
using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Encoding;

[SupportedOSPlatform("macos")]
public class AVFEncodingController : EncodingController
{
    public AVFEncodingController(string outputFile) : base(outputFile) { }

    private const int AudioFrameSize = 1024;

    public override AVFVideoEncoderSettings VideoSettings { get; } = new();

    public override AVFAudioEncoderSettings AudioSettings { get; } = new();

    public override async ValueTask Encode(
        IFrameProvider frameProvider,
        ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        var videoConfig = BuildVideoConfig();
        var audioConfig = BuildAudioConfig((int)sampleProvider.SampleRate);

        bool isHdr = VideoSettings.IsHdr;
        // Convert pixels to the transfer/primaries of the encoded stream; SDR BT.709 keeps sRGB code values.
        var transfer = (BitmapColorTransfer)videoConfig.ColorTransfer;
        var primaries = (BitmapColorPrimaries)videoConfig.ColorPrimaries;
        BitmapColorSpace targetColorSpace = isHdr
            ? ColorSpaceMapper.BuildColorSpace(isHdr, transfer, primaries)
            : ColorSpaceMapper.BuildSdrOutputColorSpace(transfer, primaries);

        BeutlAVFException.ThrowIfFailed(
            BeutlAVFNative.beutl_avf_writer_create(
                OutputFile,
                ref videoConfig,
                ref audioConfig,
                out AVFWriterSafeHandle handle));

        using (handle)
        {
            BeutlAVFException.ThrowIfFailed(BeutlAVFNative.beutl_avf_writer_start(handle));

            bool encodeVideo = true;
            bool encodeAudio = true;
            long sampleCount = 0;
            long frameCount = 0;

            long frameRateNum = VideoSettings.FrameRate.Value.Numerator;
            long frameRateDen = VideoSettings.FrameRate.Value.Denominator;

            while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
            {
                long videoTs = frameRateNum > 0 ? frameCount * frameRateDen / frameRateNum : 0;
                long audioTs = sampleProvider.SampleRate > 0 ? sampleCount / sampleProvider.SampleRate : 0;

                if (encodeVideo && (!encodeAudio || videoTs <= audioTs))
                {
                    encodeVideo = await WriteVideoFrameAsync(
                        handle, frameProvider, frameCount, frameRateNum, frameRateDen,
                        targetColorSpace, isHdr);
                    frameCount++;
                    if (frameCount >= frameProvider.FrameCount)
                    {
                        encodeVideo = false;
                    }
                }
                else
                {
                    encodeAudio = await WriteAudioFrameAsync(handle, sampleProvider, sampleCount, AudioFrameSize);
                    sampleCount += AudioFrameSize;
                    if (sampleCount >= sampleProvider.SampleCount)
                    {
                        encodeAudio = false;
                    }
                }
            }

            // Report cancellation as OperationCanceledException, not a finalized truncated file.
            cancellationToken.ThrowIfCancellationRequested();

            BeutlAVFException.ThrowIfFailed(BeutlAVFNative.beutl_avf_writer_finish(handle));
        }
    }

    // Returns false once the provider has no more frames.
    private static async ValueTask<bool> WriteVideoFrameAsync(
        AVFWriterSafeHandle handle,
        IFrameProvider frameProvider,
        long frame,
        long frameRateNum,
        long frameRateDen,
        BitmapColorSpace targetColorSpace,
        bool isHdr)
    {
        using Bitmap? image = await frameProvider.ReadNextFrame();
        if (image is null)
            return false;

        long ptsNum = frame * frameRateDen;

        using var converted = isHdr
            // Skia converts the renderer's LinearSrgb working space to the HDR target
            // (e.g. Rec.2020 + PQ with luminance scaling baked into the gamut matrix),
            // emitting 16-bit-per-channel unpremultiplied RGBA for the Writer input.
            ? image.Convert(BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, targetColorSpace)
            : image.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, targetColorSpace);
        BeutlAVFException.ThrowIfFailed(
            BeutlAVFNative.beutl_avf_writer_append_video(
                handle,
                converted.Data,
                converted.Width,
                converted.Height,
                converted.RowBytes,
                ptsNum,
                (int)frameRateNum));
        return true;
    }

    // Returns false once the provider has no more samples.
    private static async ValueTask<bool> WriteAudioFrameAsync(
        AVFWriterSafeHandle handle,
        ISampleProvider sampleProvider,
        long startSample,
        int length)
    {
        using var sound = await sampleProvider.ReadNextSamples(length);
        if (sound is null)
            return false;

        float[] interleaved = ArrayPool<float>.Shared.Rent(sound.SampleCount * 2);
        try
        {
            AudioConversion.Interleave(sound, interleaved);
            unsafe
            {
                fixed (float* data = interleaved)
                {
                    BeutlAVFException.ThrowIfFailed(
                        BeutlAVFNative.beutl_avf_writer_append_audio(
                            handle,
                            (IntPtr)data,
                            sound.SampleCount,
                            startSample,
                            sound.SampleRate));
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }

        return true;
    }

    private BeutlVideoEncoderConfig BuildVideoConfig()
    {
        bool isHdr = VideoSettings.IsHdr;
        // Force HEVC when HDR is selected — H.264/JPEG have no HDR10 profile in VideoToolbox.
        int codec = isHdr
            ? (int)AVFVideoEncoderSettings.VideoCodec.HEVC
            : (int)VideoSettings.Codec.Value;

        var transfer = MapTransfer(VideoSettings.ColorTransfer.Value);
        var primaries = MapPrimaries(VideoSettings.ColorPrimaries.Value);
        var matrix = MapMatrix(VideoSettings.YCbCrMatrix.Value);
        // Keep native HDR/JPEG defaults; SDR H.264/HEVC must not rely on a player's color guesses.
        if (!isHdr && codec != (int)AVFVideoEncoderSettings.VideoCodec.JPEG)
        {
            if (transfer == BitmapColorTransfer.Unknown) transfer = BitmapColorTransfer.Bt709;
            if (primaries == BitmapColorPrimaries.Unknown) primaries = BitmapColorPrimaries.Bt709;
            if (matrix == BeutlYCbCrMatrix.Unknown) matrix = BeutlYCbCrMatrix.Bt709;
        }

        return new BeutlVideoEncoderConfig
        {
            Width = VideoSettings.DestinationSize.Value.Width,
            Height = VideoSettings.DestinationSize.Value.Height,
            SourceWidth = VideoSettings.SourceSize.Value.Width,
            SourceHeight = VideoSettings.SourceSize.Value.Height,
            Codec = codec,
            Bitrate = VideoSettings.Bitrate.Value,
            KeyframeInterval = VideoSettings.KeyframeRate.Value,
            ProfileLevelH264 = (int)VideoSettings.ProfileLevelH264.Value,
            FrameRateNum = (int)VideoSettings.FrameRate.Value.Numerator,
            FrameRateDen = (int)VideoSettings.FrameRate.Value.Denominator,
            JpegQuality = VideoSettings.JPEGQuality.Value,
            IsHdr = isHdr ? 1 : 0,
            ColorTransfer = (int)transfer,
            ColorPrimaries = (int)primaries,
            YCbCrMatrix = (int)matrix,
        };
    }

    private BeutlAudioEncoderConfig BuildAudioConfig(int sourceSampleRate)
    {
        int sampleRate = AudioSettings.SampleRate.Value > 0 ? AudioSettings.SampleRate.Value : sourceSampleRate;
        int flags = 0;
        if (AudioSettings.LinearPcmFloat.Value) flags |= 1;
        if (AudioSettings.LinearPcmBigEndian.Value) flags |= 2;
        if (AudioSettings.LinearPcmNonInterleaved.Value) flags |= 4;

        return new BeutlAudioEncoderConfig
        {
            SampleRate = sampleRate,
            ChannelCount = AudioSettings.Channels.Value,
            FormatFourCC = (int)AudioSettings.Format.Value,
            Bitrate = AudioSettings.Bitrate.Value,
            Quality = (int)AudioSettings.Quality.Value,
            SampleRateConverterQuality = (int)AudioSettings.SampleRateConverterQuality.Value,
            LinearPcmBitDepth = (int)AudioSettings.LinearPcmBitDepth.Value,
            LinearPcmFlags = flags,
        };
    }

    // AVFVideoEncoderSettings exposes a subset of BitmapColorTransfer/BitmapColorPrimaries
    // (shared numeric values for the members AVF exposes); members with no AVF equivalent
    // (e.g. BitmapColorTransfer.Rec2020/TwoDotTwo/Gamma28/Smpte428) are unreachable here, and
    // any not-yet-wired AVF value clamps to Unknown via the default arm.
    private static BitmapColorTransfer MapTransfer(AVFVideoEncoderSettings.ColorTransferCharacteristic t) => t switch
    {
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Srgb => BitmapColorTransfer.Srgb,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Linear => BitmapColorTransfer.Linear,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Bt709 => BitmapColorTransfer.Bt709,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Pq => BitmapColorTransfer.Pq,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Hlg => BitmapColorTransfer.Hlg,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Smpte240M => BitmapColorTransfer.Smpte240M,
        _ => BitmapColorTransfer.Unknown,
    };

    private static BitmapColorPrimaries MapPrimaries(AVFVideoEncoderSettings.ColorPrimariesType p) => p switch
    {
        AVFVideoEncoderSettings.ColorPrimariesType.Bt709 => BitmapColorPrimaries.Bt709,
        AVFVideoEncoderSettings.ColorPrimariesType.Rec2020 => BitmapColorPrimaries.Rec2020,
        AVFVideoEncoderSettings.ColorPrimariesType.Dcip3 => BitmapColorPrimaries.Dcip3,
        AVFVideoEncoderSettings.ColorPrimariesType.Smpte170M => BitmapColorPrimaries.Smpte170M,
        _ => BitmapColorPrimaries.Unknown,
    };

    private static BeutlYCbCrMatrix MapMatrix(AVFVideoEncoderSettings.YCbCrMatrixType m) => m switch
    {
        AVFVideoEncoderSettings.YCbCrMatrixType.Bt709 => BeutlYCbCrMatrix.Bt709,
        AVFVideoEncoderSettings.YCbCrMatrixType.Bt601 => BeutlYCbCrMatrix.Bt601,
        AVFVideoEncoderSettings.YCbCrMatrixType.Rec2020 => BeutlYCbCrMatrix.Rec2020,
        AVFVideoEncoderSettings.YCbCrMatrixType.Smpte240M => BeutlYCbCrMatrix.Smpte240M,
        _ => BeutlYCbCrMatrix.Unknown,
    };
}
