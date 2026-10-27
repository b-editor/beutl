using System.Buffers.Binary;
using Beutl.Editor.Models;

namespace Beutl.PreviewStreaming;

// v1: kind:u8, playing:u8, channels:u16, sampleRate:u32, time:f64, playbackId:f64;
// all numbers are little-endian. Payload is JPEG (1) or interleaved PCM16 (2).
internal static class PreviewMediaPacket
{
    internal const int HeaderSize = 24;

    internal static byte[] Video(byte[] jpeg, bool playing, double time, long playbackId)
    {
        var packet = Create(1, playing, time, playbackId, jpeg.Length);
        jpeg.CopyTo(packet, HeaderSize);
        return packet;
    }

    internal static byte[] Audio(AudioFrameSnapshot audio)
    {
        var packet = Create(2, true, audio.StartTime.ToSeconds(), audio.PlaybackId, audio.Interleaved.Length * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2), (ushort)audio.ChannelCount);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)audio.SampleRate);
        for (int i = 0; i < audio.Interleaved.Length; i++)
        {
            float sample = audio.Interleaved[i];
            short value = float.IsFinite(sample) ? (short)Math.Clamp((int)(Math.Clamp(sample, -1, 1) * 32767), -32768, 32767) : (short)0;
            BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(HeaderSize + i * 2), value);
        }
        return packet;
    }

    private static byte[] Create(byte kind, bool playing, double time, long playbackId, int size)
    {
        var packet = new byte[HeaderSize + size];
        packet[0] = kind;
        packet[1] = playing ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteDoubleLittleEndian(packet.AsSpan(8), time);
        BinaryPrimitives.WriteDoubleLittleEndian(packet.AsSpan(16), playbackId);
        return packet;
    }
}
