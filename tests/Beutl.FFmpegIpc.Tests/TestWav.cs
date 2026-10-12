namespace Beutl.FFmpegIpc.Tests;

/// <summary>Writes WAV inputs for the tests that start FFmpeg, here and in Beutl.FFmpegWorker.Tests.</summary>
internal static class TestWav
{
    // 0.1 s of 8 kHz mono 16-bit silence: decodable by FFmpeg's base demuxer, but without a video stream.
    public static void WriteSilentPcmWav(string path)
    {
        const int sampleRate = 8000;
        const int dataSize = 1600;

        using var writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
