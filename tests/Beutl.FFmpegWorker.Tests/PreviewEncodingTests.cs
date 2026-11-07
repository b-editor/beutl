using System.Runtime.InteropServices;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Handlers;
using FFmpeg.AutoGen.Abstractions;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture, NonParallelizable]
public sealed unsafe class PreviewEncodingTests
{
    [OneTimeSetUp]
    public void Setup() => FFmpegTestHelpers.RequireFFmpeg();

    [TestCase(false)]
    [TestCase(true)]
    public void VideoEncodesImmediatelyWithTheNegotiatedBitDepth(bool hdr)
    {
        const int width = 64, height = 32;
        byte[] pixels = new byte[width * height * (hdr ? 8 : 4)];
        if (hdr)
        {
            // PQ reference white and a highlight, both above the 8-bit precision boundary.
            var values = MemoryMarshal.Cast<byte, ushort>(pixels.AsSpan());
            for (int i = 0; i < values.Length; i += 4)
            {
                values[i] = values[i + 1] = values[i + 2] = (ushort)((i % (width * 4) < width * 2 ? 0.58 : 0.73) * ushort.MaxValue);
                values[i + 3] = ushort.MaxValue;
            }
        }
        else Array.Fill(pixels, (byte)200);
        byte[] encoded = Encode(pixels, width, height, hdr, false);
        AVCodecContext* decoder = ffmpeg.avcodec_alloc_context3(ffmpeg.avcodec_find_decoder(hdr ? AVCodecID.AV_CODEC_ID_VP9 : AVCodecID.AV_CODEC_ID_VP8));
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        try
        {
            Assert.That(ffmpeg.avcodec_open2(decoder, decoder->codec, null), Is.Zero);
            Assert.That(ffmpeg.av_new_packet(packet, encoded.Length), Is.Zero);
            encoded.CopyTo(new Span<byte>(packet->data, packet->size));
            Assert.That(ffmpeg.avcodec_send_packet(decoder, packet), Is.Zero);
            Assert.That(ffmpeg.avcodec_receive_frame(decoder, frame), Is.Zero);
            Assert.That(frame->width, Is.EqualTo(width));
            Assert.That(frame->height, Is.EqualTo(height));
            Assert.That(frame->format, Is.EqualTo((int)(hdr ? AVPixelFormat.AV_PIX_FMT_YUV420P10LE : AVPixelFormat.AV_PIX_FMT_YUV420P)));
            if (hdr)
            {
                Assert.That(decoder->profile, Is.EqualTo(2));
                ushort* luma = (ushort*)frame->data[0];
                Assert.That(luma[width - 4], Is.GreaterThan(luma[4] + 100), "Highlight must survive 10-bit encoding.");
            }
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&decoder);
        }
    }

    [Test]
    public void OnlyDirectVideoTakesAFullHdPicture()
    {
        const int width = 1920, height = 1080;
        byte[] pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)200);
        Assert.That(() => Encode(pixels, width, height, false, false, relayed: true),
            Throws.ArgumentException.With.Message.EqualTo("Invalid preview encoder dimensions."));
        Assert.That(Encode(pixels, width, height, false, false, relayed: false), Is.Not.Empty);
    }

    [TestCase(true, 640, 360)]
    [TestCase(false, 1280, 720)]
    public void CodingAPausedFrameAgainAddsDetailBeforeAnotherKeyFrame(bool relayed, int width, int height)
    {
        // The mobile preview codes a paused picture again and again (MobilePreviewServer.StillRefinement, 30
        // frames). The encoder has to keep adding detail, and schedule no key frame within twice that. A few
        // moving frames come first: the encoder gives its very first frame a far larger budget.
        const int moving = 10, held = 60;
        var random = new Random(1);
        byte[] Gray(int n)
        {
            byte[] gray = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    gray[y * width + x] = (byte)Math.Clamp(128 + 60 * Math.Sin(x / 7.0 + n) * Math.Cos(y / 5.0) + random.Next(-40, 41), 0, 255);
            return gray;
        }
        byte[] Bgra(byte[] gray)
        {
            byte[] pixels = new byte[gray.Length * 4];
            for (int i = 0; i < gray.Length; i++)
            {
                pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = gray[i];
                pixels[i * 4 + 3] = 255;
            }
            return pixels;
        }
        byte[] still = Gray(moving);
        using var buffer = SharedMemoryBuffer.Create($"preview-test-{Guid.NewGuid():N}", width * height * 4);
        using var handler = new PreviewEncodingHandler();
        handler.Start(IpcMessage.Create(1, MessageType.StartPreviewEncoder,
            new PreviewEncoderStart(buffer.Name, width * height * 4, width, height, false, false, relayed)));
        AVCodecContext* decoder = ffmpeg.avcodec_alloc_context3(ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_VP8));
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        var errors = new double[held];
        try
        {
            Assert.That(ffmpeg.avcodec_open2(decoder, decoder->codec, null), Is.Zero);
            for (int i = 0; i < moving + held; i++)
            {
                byte[] gray = i < moving ? Gray(i) : still;
                buffer.Write(Bgra(gray));
                // The paused frame starts from a requested key frame, as when a viewer asks for one while paused.
                bool keyFrame = i == 0 || i == moving;
                byte[] data = handler.Encode(IpcMessage.Create(2 + i, MessageType.EncodePreviewFrame, new PreviewEncoderFrame(keyFrame)))
                    .RequirePayload<PreviewEncodedPacket>(MessageType.EncodePreviewFrameResult).Data;
                // VP8 clears the frame tag's lowest bit on a key frame.
                Assert.That((data[0] & 1) == 0, Is.EqualTo(keyFrame), $"Key frame at frame {i}");
                Assert.That(ffmpeg.av_new_packet(packet, data.Length), Is.Zero);
                data.CopyTo(new Span<byte>(packet->data, packet->size));
                Assert.That(ffmpeg.avcodec_send_packet(decoder, packet), Is.Zero);
                ffmpeg.av_packet_unref(packet);
                Assert.That(ffmpeg.avcodec_receive_frame(decoder, frame), Is.Zero);
                if (i >= moving)
                {
                    double sum = 0;
                    for (int y = 0; y < height; y++)
                    {
                        byte* row = frame->data[0] + y * frame->linesize[0];
                        for (int x = 0; x < width; x++)
                        {
                            // A gray pixel becomes limited-range luma and nothing else.
                            double error = row[x] - (16 + still[y * width + x] * 219 / 255.0);
                            sum += error * error;
                        }
                    }
                    errors[i - moving] = sum / (width * height);
                }
                ffmpeg.av_frame_unref(frame);
            }
        }
        finally
        {
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avcodec_free_context(&decoder);
        }
        TestContext.Out.WriteLine($"Paused luma MSE: key frame {errors[0]:F1}, 10th {errors[9]:F1}, 30th {errors[29]:F1}, 60th {errors[59]:F1}");
        Assert.That(errors[29], Is.LessThan(errors[0] * 0.5), "Thirty frames of the same picture must add detail.");
    }

    [Test]
    public void AudioProducesOneOpusPacketWithoutBufferingASecondOfPlayback()
    {
        float[] samples = Enumerable.Range(0, 960 * 2).Select(i => 0.1f * MathF.Sin((i / 2) * MathF.Tau * 440 / 48000)).ToArray();
        byte[] packet = Encode(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), 0, 0, false, true);
        Assert.That(packet.Length, Is.InRange(2, 1200));
    }

    private static byte[] Encode(byte[] pixels, int width, int height, bool hdr, bool audio, bool relayed = true)
    {
        using var buffer = SharedMemoryBuffer.Create($"preview-test-{Guid.NewGuid():N}", pixels.Length);
        using var handler = new PreviewEncodingHandler();
        var settings = new PreviewEncoderStart(buffer.Name, pixels.Length, width, height, hdr, audio, relayed);
        handler.Start(IpcMessage.Create(1, MessageType.StartPreviewEncoder, settings));
        buffer.Write(pixels);
        var result = handler.Encode(IpcMessage.Create(2, MessageType.EncodePreviewFrame, new PreviewEncoderFrame(true)));
        return result.RequirePayload<PreviewEncodedPacket>(MessageType.EncodePreviewFrameResult).Data;
    }
}
