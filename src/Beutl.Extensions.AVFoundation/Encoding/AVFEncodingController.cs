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
                    await WriteVideoFrameAsync(handle, frameProvider, frameCount, frameRateNum, frameRateDen);
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
        long frameRateDen)
    {
        using var image = await frameProvider.RenderFrame(frame);
        using var bgra = image.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

        long ptsNum = frame * frameRateDen;
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
        return new BeutlVideoEncoderConfig
        {
            Width = VideoSettings.DestinationSize.Width,
            Height = VideoSettings.DestinationSize.Height,
            SourceWidth = VideoSettings.SourceSize.Width,
            SourceHeight = VideoSettings.SourceSize.Height,
            Codec = (int)VideoSettings.Codec,
            Bitrate = VideoSettings.Bitrate,
            KeyframeInterval = VideoSettings.KeyframeRate,
            ProfileLevelH264 = (int)VideoSettings.ProfileLevelH264,
            FrameRateNum = (int)VideoSettings.FrameRate.Numerator,
            FrameRateDen = (int)VideoSettings.FrameRate.Denominator,
            JpegQuality = VideoSettings.JPEGQuality,
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
}
