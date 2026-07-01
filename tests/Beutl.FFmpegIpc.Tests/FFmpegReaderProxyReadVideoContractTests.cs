using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Process-level IPC contract test: spawns the real GPL <c>Beutl.FFmpegWorker</c> (via the MIT
/// <see cref="FFmpegDecoderInfo"/> / <c>FFmpegReaderProxy</c> path) and pins the audio-only
/// <c>ReadVideo</c> error contract.
/// <para>
/// <c>DecodingHandler.HandleReadVideo</c> answers a <c>ReadVideo</c> request against an audio-only
/// reader with <c>IpcMessage.CreateError(...)</c> (no ring buffer, <c>HasVideo == false</c>). The
/// transport surfaces that error message as a thrown <see cref="FFmpegWorkerException"/>, so
/// <c>FFmpegReaderProxy.ReadVideo</c> throws rather than silently returning <c>false</c>. This test
/// pins that end-to-end contract so a future regression (e.g. swapping the error for a
/// <c>Success = false</c> response) is caught.
/// </para>
/// <para>
/// The worker binary is copied into the test output by the <c>CopyWorkerBinary</c> MSBuild target
/// (no compile-closure reference to the GPL project). When the worker binary or its FFmpeg natives
/// are unavailable (e.g. CI without FFmpeg), the test self-skips via <see cref="Assert.Ignore(string)"/>.
/// </para>
/// </summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxyReadVideoContractTests
{
    private string _audioOnlyPath = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _audioOnlyPath = Path.Combine(
            Path.GetTempPath(), $"beutl-ffmpeg-audioonly-{Guid.NewGuid():N}.wav");
        WriteSilentPcmWav(_audioOnlyPath);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        try
        {
            if (_audioOnlyPath != null && File.Exists(_audioOnlyPath))
                File.Delete(_audioOnlyPath);
        }
        catch (Exception)
        {
            // Best-effort cleanup of the temp fixture; a leftover file must not fail the run.
        }
    }

    [Test]
    public void ReadVideo_OnAudioOnlyFile_ThrowsFFmpegWorkerException()
    {
        if (!WorkerBinaryPresent())
        {
            Assert.Ignore(
                "FFmpeg worker binary not present in the test output; skipping the process-level contract test.");
        }

        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());

        // Open spawns the worker over IPC and returns the FFmpegReaderProxy. A null result means the
        // worker could not start (binary missing or FFmpeg natives absent) -> self-skip on CI.
        using MediaReader? reader = decoderInfo.Open(_audioOnlyPath, new MediaOptions(MediaMode.AudioVideo));
        if (reader is null)
        {
            Assert.Ignore(
                "FFmpeg worker/natives unavailable (could not open the audio fixture); skipping the process-level contract test.");
            return;
        }

        // Sanity: the fixture must be audio-only, so the reader exercises the HandleReadVideo
        // audio-only error branch (not the "unknown reader" or ring-buffer paths).
        Assert.Multiple(() =>
        {
            Assert.That(reader.HasAudio, Is.True, "the WAV fixture must expose an audio stream");
            Assert.That(reader.HasVideo, Is.False, "the WAV fixture must be audio-only (no video stream)");
        });

        Assert.That(
            () => reader.ReadVideo(0, out _),
            Throws.TypeOf<FFmpegWorkerException>(),
            "ReadVideo on an audio-only reader must surface the worker's CreateError as a thrown FFmpegWorkerException, not a silent false");
    }

    // The CopyWorkerBinary target lays the worker's output flat into the test bin dir. Nuke publishes
    // isolate it under an FFmpegWorker/ subdir, so probe both — mirroring FFmpegWorkerProcess.ResolveWorkerCommand.
    private static bool WorkerBinaryPresent()
    {
        string baseDir = AppContext.BaseDirectory;
        return File.Exists(Path.Combine(baseDir, "Beutl.FFmpegWorker.dll"))
            || File.Exists(Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker.dll"));
    }

    // Writes a tiny, valid audio-only PCM WAV (0.1s of 8 kHz mono 16-bit silence). FFmpeg decodes WAV
    // with its base demuxer, so no extra codec/native beyond the worker's FFmpeg load is required, and
    // no binary fixture is committed.
    private static void WriteSilentPcmWav(string path)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int numSamples = 800; // 0.1 second
        int blockAlign = channels * (bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataSize = numSamples * blockAlign;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize); // ChunkSize
        writer.Write("WAVE"u8);

        // fmt sub-chunk
        writer.Write("fmt "u8);
        writer.Write(16);                    // Subchunk1Size (PCM)
        writer.Write((short)1);              // AudioFormat = PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write(bitsPerSample);

        // data sub-chunk (silence)
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
