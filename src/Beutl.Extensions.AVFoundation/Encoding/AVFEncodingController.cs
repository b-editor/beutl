using System.Runtime.Versioning;
using Beutl.Extensibility;
using Beutl.Media.Encoding;
using MonoMac.AudioToolbox;
using MonoMac.AVFoundation;
using MonoMac.CoreFoundation;
using MonoMac.CoreMedia;
using MonoMac.CoreVideo;
using MonoMac.Foundation;

namespace Beutl.Extensions.AVFoundation.Encoding;

[SupportedOSPlatform("macos")]
public class AVFEncodingController(string outputFile) : EncodingController(outputFile)
{
    public override AVFVideoEncoderSettings VideoSettings { get; } = new();

    public override AVFAudioEncoderSettings AudioSettings { get; } = new();

    private void ConfigureVideoInput(
        AVAssetWriter writer,
        out AVAssetWriterInput videoInput, out AVAssetWriterInputPixelBufferAdaptor adaptor)
    {
        videoInput = AVAssetWriterInput.Create(AVMediaType.Video, new AVVideoSettingsCompressed
        {
            Width = VideoSettings.DestinationSize.Width,
            Height = VideoSettings.DestinationSize.Height,
            Codec = VideoSettings.Codec.ToAVVideoCodec(),
            CodecSettings = new AVVideoCodecSettings
            {
                AverageBitRate = VideoSettings.Bitrate == -1 ? null : VideoSettings.Bitrate,
                MaxKeyFrameInterval = VideoSettings.KeyframeRate == -1 ? null : VideoSettings.KeyframeRate,
                JPEGQuality = VideoSettings.JPEGQuality < 0 ? null : VideoSettings.JPEGQuality,
                ProfileLevelH264 = VideoSettings.ProfileLevelH264.ToAVVideoProfileLevelH264(),
            },
        });
        videoInput.ExpectsMediaDataInRealTime = true;
        adaptor = AVAssetWriterInputPixelBufferAdaptor.Create(videoInput,
            new CVPixelBufferAttributes
            {
                PixelFormatType = CVPixelFormatType.CV32ARGB,
                Width = VideoSettings.SourceSize.Width,
                Height = VideoSettings.SourceSize.Width,
            });
        writer.AddInput(videoInput);
    }

    private void ConfigureAudioInput(
        AVAssetWriter writer,
        out AVAssetWriterInput audioInput)
    {
        var audioSettings = new AudioSettings
        {
            SampleRate = AudioSettings.SampleRate,
            EncoderBitRate = AudioSettings.Bitrate == -1 ? null : AudioSettings.Bitrate,
            NumberChannels = AudioSettings.Channels,
            Format = AudioSettings.Format.ToAudioFormatType(),
            AudioQuality =
                AudioSettings.Quality == AVFAudioEncoderSettings.AudioQuality.Default
                    ? null
                    : (AVAudioQuality?)AudioSettings.Quality,
            SampleRateConverterAudioQuality =
                AudioSettings.SampleRateConverterQuality == AVFAudioEncoderSettings.AudioQuality.Default
                    ? null
                    : (AVAudioQuality?)AudioSettings.SampleRateConverterQuality,
        };
        if (audioSettings.Format == AudioFormatType.LinearPCM)
        {
            audioSettings.LinearPcmFloat = AudioSettings.LinearPcmFloat;
            audioSettings.LinearPcmBigEndian = AudioSettings.LinearPcmBigEndian;
            audioSettings.LinearPcmBitDepth = (int?)AudioSettings.LinearPcmBitDepth;
            audioSettings.LinearPcmNonInterleaved = AudioSettings.LinearPcmNonInterleaved;
        }

        audioInput = AVAssetWriterInput.Create(AVMediaType.Audio, audioSettings);
        audioInput.ExpectsMediaDataInRealTime = true;
        writer.AddInput(audioInput);
    }

    private async ValueTask<bool> WriteAudioFrame(ISampleProvider sampleProvider, AVAssetWriterInput input, long offset,
        long length)
    {
        using var sound = await sampleProvider.Sample(offset, length);
        var sourceFormat =
            AudioStreamBasicDescription.CreateLinearPCM(sound.SampleRate, (uint)sound.NumChannels);
        sourceFormat.FormatFlags = AudioFormatFlags.IsFloat | AudioFormatFlags.IsPacked;
        sourceFormat.BitsPerChannel = 32;
        var fmtError = AudioStreamBasicDescription.GetFormatInfo(ref sourceFormat);
        if (fmtError != AudioFormatError.None) throw new Exception(fmtError.ToString());

        uint inputDataSize = (uint)(sound.SampleSize * sound.NumSamples);
        var time = new CMTime(offset, sound.SampleRate);
        var dataBuffer = InternalMethods.CreateCMBlockBufferWithMemoryBlock(
            inputDataSize, sound.Data, CMBlockBufferFlags.AlwaysCopyData);

        var formatDescription = InternalMethods.CreateAudioFormatDescription(sourceFormat);

        var sampleBuffer = CMSampleBuffer.CreateWithPacketDescriptions(dataBuffer, formatDescription,
            sound.NumSamples, time, null, out var error4);
        if (error4 != CMSampleBufferError.None) throw new Exception(error4.ToString());

        return input.AppendSampleBuffer(sampleBuffer);
    }

    private async ValueTask<bool> WriteVideoFrame(IFrameProvider frameProvider,
        AVAssetWriterInputPixelBufferAdaptor adaptor, long frame)
    {
        using var image = await frameProvider.RenderFrame(frame);
        var time = new CMTime(frame * VideoSettings.FrameRate.Denominator,
            (int)VideoSettings.FrameRate.Numerator);
        CVPixelBuffer? pixelBuffer = AVFSampleUtilities.ConvertToCVPixelBuffer(image);
        if (pixelBuffer == null)
        {
            return false;
        }

        return adaptor.AppendPixelBufferWithPresentationTime(pixelBuffer, time);
    }

    public override async ValueTask Encode(IFrameProvider frameProvider, ISampleProvider sampleProvider,
        CancellationToken cancellationToken)
    {
        var url = NSUrl.FromFilename(OutputFile);
        var writer = AVAssetWriter.FromUrl(url, AVFileType.Mpeg4, out var error);
        if (error != null) throw new Exception(error.LocalizedDescription);

        ConfigureVideoInput(writer, out var videoInput, out var videoAdaptor);
        ConfigureAudioInput(writer, out var audioInput);

        if (!writer.StartWriting())
        {
            throw new Exception("Failed to start writing");
        }

        writer.StartSessionAtSourceTime(CMTime.Zero);
        bool encodeAudio = true;
        bool encodeVideo = true;
        long sampleCount = 0;
        long frameCount = 0;

        while ((encodeVideo || encodeAudio) && !cancellationToken.IsCancellationRequested)
        {
            long videoTs = frameCount * VideoSettings.FrameRate.Denominator / VideoSettings.FrameRate.Numerator;
            long audioTs = sampleCount / sampleProvider.SampleRate;
            if (encodeVideo &&
                (!encodeAudio || videoTs <= audioTs))
            {
                encodeVideo = await WriteVideoFrame(frameProvider, videoAdaptor, frameCount);
                frameCount++;
                if (frameCount >= frameProvider.FrameCount)
                {
                    videoInput.MarkAsFinished();
                    encodeVideo = false;
                }
            }
            else
            {
                encodeAudio = await WriteAudioFrame(sampleProvider, audioInput, sampleCount, 1024);
                sampleCount += 1024;
                if (sampleCount >= sampleProvider.SampleCount)
                {
                    audioInput.MarkAsFinished();
                    encodeAudio = false;
                }
            }
        }

        writer.FinishWriting();
    }
}
