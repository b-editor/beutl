namespace Beutl.FFmpegIpc.Protocol.Messages;

// Dedicated, bounded real-time encoder. Raw pixels/samples use shared memory; only compressed
// packets cross the control pipe. This keeps native FFmpeg in the existing worker process.
public sealed record PreviewEncoderStart(string SharedMemoryName, int Capacity, int Width, int Height, bool Hdr, bool Audio);
public sealed record PreviewEncoderFrame(bool KeyFrame);
public sealed record PreviewEncodedPacket(byte[] Data);
