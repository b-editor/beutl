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
    public void AudioProducesOneOpusPacketWithoutBufferingASecondOfPlayback()
    {
        float[] samples = Enumerable.Range(0, 960 * 2).Select(i => 0.1f * MathF.Sin((i / 2) * MathF.Tau * 440 / 48000)).ToArray();
        byte[] packet = Encode(MemoryMarshal.AsBytes(samples.AsSpan()).ToArray(), 0, 0, false, true);
        Assert.That(packet.Length, Is.InRange(2, 1200));
    }

    private static byte[] Encode(byte[] pixels, int width, int height, bool hdr, bool audio)
    {
        using var buffer = SharedMemoryBuffer.Create($"preview-test-{Guid.NewGuid():N}", pixels.Length);
        using var handler = new PreviewEncodingHandler();
        var settings = new PreviewEncoderStart(buffer.Name, pixels.Length, width, height, hdr, audio);
        handler.Start(IpcMessage.Create(1, MessageType.StartPreviewEncoder, settings));
        buffer.Write(pixels);
        var result = handler.Encode(IpcMessage.Create(2, MessageType.EncodePreviewFrame, new PreviewEncoderFrame(true)));
        return result.RequirePayload<PreviewEncodedPacket>(MessageType.EncodePreviewFrameResult).Data;
    }
}
