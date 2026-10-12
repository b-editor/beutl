using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.FFmpegWorker.Tests;

internal static class FFmpegTestHelpers
{
    /// <summary>
    /// Loads the FFmpeg libraries, or skips the test when they or their dependencies are missing. The skip reasons
    /// match FFMPEG_MISSING in <c>.github/scripts/ci_tests.py</c>.
    /// </summary>
    public static void RequireFFmpeg()
    {
        // FFmpegEncodingController's logger needs a LoggerFactory, which only the real worker sets, so seed an empty one.
        Log.LoggerFactory = NullLoggerFactory.Instance;
        try
        {
            FFmpegLoaderWorker.Initialize();
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            Assert.Ignore("FFmpeg native libraries are not available.");
        }
        catch (DllNotFoundException)
        {
            Assert.Ignore("FFmpeg native library dependencies are not available.");
        }
    }

    public static FFmpegEncodingController CreateController(
        string path, PixelSize size, Rational frameRate, int sampleRate, int channels)
    {
        var controller = new FFmpegEncodingController(path, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize = size;
        controller.VideoSettings.DestinationSize = size;
        controller.VideoSettings.FrameRate = frameRate;
        controller.AudioSettings.SampleRate = sampleRate;
        controller.AudioSettings.Channels = channels;
        return controller;
    }

    // FFV1 keeps every pixel, so what the reader decodes can be compared with what the encoder was handed. Skips when
    // the FFV1 encoder of this FFmpeg build lacks the pixel format.
    public static FFmpegEncodingController CreateLosslessController(
        string path, PixelSize source, PixelSize destination, bool hdr)
    {
        AVPixelFormat encodedFormat = hdr ? AVPixelFormat.AV_PIX_FMT_GBRAP16LE : AVPixelFormat.AV_PIX_FMT_BGRA;
        MediaCodec? codec = MediaCodec.FindEncoder("ffv1");
        if (codec == null || !codec.GetPixelFmts().Contains(encodedFormat))
            Assert.Ignore($"The FFV1 encoder does not support {encodedFormat}.");

        var controller = new FFmpegEncodingController(path, new FFmpegEncodingSettings());
        controller.VideoSettings.SourceSize = source;
        controller.VideoSettings.DestinationSize = destination;
        controller.VideoSettings.FrameRate = new Rational(30, 1);
        controller.VideoSettings.Codec = new CodecRecord("ffv1", "FFV1");
        controller.VideoSettings.Options.Clear();
        controller.VideoSettings.Format = (int)encodedFormat;
        controller.VideoSettings.ColorTrc = hdr ? FFColorTransfer.SMPTE2084 : FFColorTransfer.IEC61966_2_1;
        controller.VideoSettings.ColorPrimaries = hdr ? FFColorPrimaries.BT2020 : FFColorPrimaries.BT709;
        controller.VideoSettings.ColorSpace = FFColorSpace.RGB;
        controller.VideoSettings.ColorRange = FFColorRange.JPEG;
        controller.AudioSettings.Codec = new CodecRecord("pcm_s16le", "PCM");
        controller.AudioSettings.Format = FFmpegAudioEncoderSettings.AudioFormat.S16;
        return controller;
    }

    // A frame the encoder was handed, in the layout and color space the reader returns after a lossless round trip.
    public static Bitmap ToDecodedLayout(Bitmap source, bool hdr)
    {
        BitmapColorSpace colorSpace = hdr
            ? ColorSpaceHelper.BuildHdrColorSpace(
                AVColorTransferCharacteristic.AVCOL_TRC_SMPTE2084, AVColorPrimaries.AVCOL_PRI_BT2020)
            : BitmapColorSpace.Srgb;
        return source.Convert(
            hdr ? BitmapColorType.Rgba16161616 : BitmapColorType.Bgra8888,
            hdr ? BitmapAlphaType.Unpremul : BitmapAlphaType.Premul,
            colorSpace);
    }
}

internal sealed class EmptySampleProvider : ISampleProvider
{
    public long SampleCount => 0;

    public long SampleRate => 44100;

    public ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length) =>
        throw new AssertionException("There are no audio samples to encode.");

    public void Dispose()
    {
    }
}
