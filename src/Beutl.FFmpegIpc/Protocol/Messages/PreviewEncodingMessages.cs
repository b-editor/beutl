namespace Beutl.FFmpegIpc.Protocol.Messages;

// Dedicated, bounded real-time encoder. Raw pixels/samples use shared memory; only compressed
// packets cross the control pipe. This keeps native FFmpeg in the existing worker process.
// Relayed: whether the video reaches a viewer through a TURN relay (see PreviewEncodingFormat).
public sealed record PreviewEncoderStart(string SharedMemoryName, int Capacity, int Width, int Height, bool Hdr, bool Audio, bool Relayed);
public sealed record PreviewEncoderFrame(bool KeyFrame);
public sealed record PreviewEncodedPacket(byte[] Data);

// The stream both sides assume: the worker rejects a buffer of any other size and steps each pts
// by one frame or one audio packet. Video is BGRA, or RGBA64 for HDR; audio is interleaved float.
public static class PreviewEncodingFormat
{
    public const int SampleRate = 48000;
    public const int AudioChannels = 2;
    public const int AudioPacketSamples = 960;

    // A TURN relay is paid for by the byte, so video that any viewer receives through one keeps to a
    // small picture and rate. A direct LAN or STUN path costs nothing to relay and gets more of both.
    public static int FrameRate(bool relayed) => relayed ? 15 : 30;

    public static int MaxDimension(bool relayed) => relayed ? 1280 : 1920;

    public static int BytesPerPixel(bool hdr) => hdr ? 8 : 4;

    public static int BufferBytes(int width, int height, bool hdr, bool audio)
        => audio ? AudioPacketSamples * AudioChannels * sizeof(float) : checked(width * height * BytesPerPixel(hdr));
}
