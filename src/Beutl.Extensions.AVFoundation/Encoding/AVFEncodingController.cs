using System.Runtime.Versioning;
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
        // For HDR, pre-compute the Skia target color space once so every frame's Bitmap.Convert
        // uses an identical (and correctly luminance-scaled) destination — matching the FFmpeg
        // encoder's behavior in ColorSpaceHelper.BuildHdrColorSpace.
        BitmapColorSpace? hdrTargetColorSpace = isHdr
            ? ColorSpaceMapper.BuildColorSpace(
                isHdr: true,
                MapTransfer(VideoSettings.ColorTransfer),
                MapPrimaries(VideoSettings.ColorPrimaries))
            : null;

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

            long frameRateNum = VideoSettings.FrameRate.Numerator;
            long frameRateDen = VideoSettings.FrameRate.Denominator;

            while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
            {
                long videoTs = frameRateNum > 0 ? frameCount * frameRateDen / frameRateNum : 0;
                long audioTs = sampleProvider.SampleRate > 0 ? sampleCount / sampleProvider.SampleRate : 0;

                if (encodeVideo && (!encodeAudio || videoTs <= audioTs))
                {
                    await WriteVideoFrameAsync(
                        handle, frameProvider, frameCount, frameRateNum, frameRateDen,
                        hdrTargetColorSpace);
                    frameCount++;
                    if (frameCount >= frameProvider.FrameCount)
                    {
                        encodeVideo = false;
                    }
                }
                else
                {
                    await WriteAudioFrameAsync(handle, sampleProvider, sampleCount, AudioFrameSize);
                    sampleCount += AudioFrameSize;
                    if (sampleCount >= sampleProvider.SampleCount)
                    {
                        encodeAudio = false;
                    }
                }
            }

            BeutlAVFException.ThrowIfFailed(BeutlAVFNative.beutl_avf_writer_finish(handle));
        }
    }

    private static async ValueTask WriteVideoFrameAsync(
        AVFWriterSafeHandle handle,
        IFrameProvider frameProvider,
        long frame,
        long frameRateNum,
        long frameRateDen,
        BitmapColorSpace? hdrTargetColorSpace)
    {
        using var image = await frameProvider.RenderFrame(frame);
        long ptsNum = frame * frameRateDen;

        if (hdrTargetColorSpace is not null)
        {
            // Skia converts the renderer's LinearSrgb working space to the HDR target
            // (e.g. Rec.2020 + PQ with luminance scaling baked into the gamut matrix),
            // emitting 16-bit-per-channel unpremultiplied RGBA for the Writer input.
            using var converted = image.Convert(
                BitmapColorType.Rgba16161616, BitmapAlphaType.Unpremul, hdrTargetColorSpace);
            BeutlAVFException.ThrowIfFailed(
                BeutlAVFNative.beutl_avf_writer_append_video(
                    handle,
                    converted.Data,
                    converted.Width,
                    converted.Height,
                    converted.RowBytes,
                    ptsNum,
                    (int)frameRateNum));
        }
        else
        {
            using var bgra = image.Convert(
                BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);
            BeutlAVFException.ThrowIfFailed(
                BeutlAVFNative.beutl_avf_writer_append_video(
                    handle,
                    bgra.Data,
                    bgra.Width,
                    bgra.Height,
                    bgra.RowBytes,
                    ptsNum,
                    (int)frameRateNum));
        }
    }

    private static async ValueTask WriteAudioFrameAsync(
        AVFWriterSafeHandle handle,
        ISampleProvider sampleProvider,
        long startSample,
        int length)
    {
        using var sound = await sampleProvider.Sample(startSample, length);
        BeutlAVFException.ThrowIfFailed(
            BeutlAVFNative.beutl_avf_writer_append_audio(
                handle,
                sound.Data,
                sound.NumSamples,
                startSample,
                sound.SampleRate));
    }

    private BeutlVideoEncoderConfig BuildVideoConfig()
    {
        bool isHdr = VideoSettings.IsHdr;
        // Force HEVC when HDR is selected — H.264/JPEG have no HDR10 profile in VideoToolbox.
        int codec = isHdr
            ? (int)AVFVideoEncoderSettings.VideoCodec.HEVC
            : (int)VideoSettings.Codec;

        return new BeutlVideoEncoderConfig
        {
            Width = VideoSettings.DestinationSize.Width,
            Height = VideoSettings.DestinationSize.Height,
            SourceWidth = VideoSettings.SourceSize.Width,
            SourceHeight = VideoSettings.SourceSize.Height,
            Codec = codec,
            Bitrate = VideoSettings.Bitrate,
            KeyframeInterval = VideoSettings.KeyframeRate,
            ProfileLevelH264 = (int)VideoSettings.ProfileLevelH264,
            FrameRateNum = (int)VideoSettings.FrameRate.Numerator,
            FrameRateDen = (int)VideoSettings.FrameRate.Denominator,
            JpegQuality = VideoSettings.JPEGQuality,
            IsHdr = isHdr ? 1 : 0,
            ColorTransfer = (int)MapTransfer(VideoSettings.ColorTransfer),
            ColorPrimaries = (int)MapPrimaries(VideoSettings.ColorPrimaries),
            YCbCrMatrix = (int)MapMatrix(VideoSettings.YCbCrMatrix),
        };
    }

    private BeutlAudioEncoderConfig BuildAudioConfig(int sourceSampleRate)
    {
        int sampleRate = AudioSettings.SampleRate > 0 ? AudioSettings.SampleRate : sourceSampleRate;
        int flags = 0;
        if (AudioSettings.LinearPcmFloat) flags |= 1;
        if (AudioSettings.LinearPcmBigEndian) flags |= 2;
        if (AudioSettings.LinearPcmNonInterleaved) flags |= 4;

        return new BeutlAudioEncoderConfig
        {
            SampleRate = sampleRate,
            ChannelCount = AudioSettings.Channels,
            FormatFourCC = (int)AudioSettings.Format,
            Bitrate = AudioSettings.Bitrate,
            Quality = (int)AudioSettings.Quality,
            SampleRateConverterQuality = (int)AudioSettings.SampleRateConverterQuality,
            LinearPcmBitDepth = (int)AudioSettings.LinearPcmBitDepth,
            LinearPcmFlags = flags,
        };
    }

    // Enum-to-tag mappings are 1:1 (the AVFVideoEncoderSettings enums use the same numeric
    // values as BeutlTransferFunction/BeutlColorPrimaries/BeutlYCbCrMatrix) but the helpers
    // below clamp to Unknown if a user adds a value that hasn't been wired through.
    private static BeutlTransferFunction MapTransfer(AVFVideoEncoderSettings.ColorTransferCharacteristic t) => t switch
    {
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Srgb => BeutlTransferFunction.Srgb,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Linear => BeutlTransferFunction.Linear,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Bt709 => BeutlTransferFunction.Bt709,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Pq => BeutlTransferFunction.Pq,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Hlg => BeutlTransferFunction.Hlg,
        AVFVideoEncoderSettings.ColorTransferCharacteristic.Smpte240M => BeutlTransferFunction.Smpte240M,
        _ => BeutlTransferFunction.Unknown,
    };

    private static BeutlColorPrimaries MapPrimaries(AVFVideoEncoderSettings.ColorPrimariesType p) => p switch
    {
        AVFVideoEncoderSettings.ColorPrimariesType.Bt709 => BeutlColorPrimaries.Bt709,
        AVFVideoEncoderSettings.ColorPrimariesType.Rec2020 => BeutlColorPrimaries.Rec2020,
        AVFVideoEncoderSettings.ColorPrimariesType.Dcip3 => BeutlColorPrimaries.Dcip3,
        AVFVideoEncoderSettings.ColorPrimariesType.Smpte170M => BeutlColorPrimaries.Smpte170M,
        _ => BeutlColorPrimaries.Unknown,
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
