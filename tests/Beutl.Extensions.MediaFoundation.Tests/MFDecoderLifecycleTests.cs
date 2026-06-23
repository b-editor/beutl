using System.Runtime.Versioning;
using Beutl.Embedding.MediaFoundation.Decoding;
using Beutl.Media.Decoding;
using SharpGen.Runtime;
using SharpGen.Runtime.Diagnostics;
using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
[Platform("Win")]
[NonParallelizable]
[SupportedOSPlatform("windows")]
public class MFDecoderLifecycleTests
{
    private string _workDir = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Media Foundation is only available on Windows.");
        }

        SharpGen.Runtime.Configuration.EnableObjectTracking = true;
        SharpGen.Runtime.Configuration.EnableReleaseOnFinalizer = true;
        SharpGen.Runtime.Configuration.UseThreadStaticObjectTracking = true;
        MediaFactory.MFStartup();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (OperatingSystem.IsWindows())
        {
            MediaFactory.MFShutdown();
        }
    }

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-mf-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Test]
    public void Constructor_NoVideoStream_DisposesPartiallyCreatedComObjects()
    {
        string wav = WriteSineWav();
        int before = CountTrackedMediaFoundationObjects();

        Assert.Throws<NoVideoStreamException>(() =>
            _ = new MFDecoder(wav, new MediaOptions(MediaMode.Video), new MFDecodingExtension()));

        int after = CountTrackedMediaFoundationObjects();
        Assert.That(after, Is.EqualTo(before),
            "MFDecoder must deterministically dispose COM wrappers created before constructor failure.");
    }

    private static int CountTrackedMediaFoundationObjects()
    {
        int count = 0;
        foreach (WeakReference<CppObject> reference in ObjectTracker.FindActiveObjects())
        {
            if (reference.TryGetTarget(out CppObject? target)
                && target.GetType().Namespace?.StartsWith("Vortice.MediaFoundation", StringComparison.Ordinal) == true)
            {
                count++;
            }
        }

        return count;
    }

    private string WriteSineWav(int sampleRate = 44100, int channels = 2, double seconds = 0.2)
    {
        string path = Path.Combine(_workDir, "audio.wav");

        const short bitsPerSample = 16;
        int totalFrames = (int)(sampleRate * seconds);
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataSize = totalFrames * blockAlign;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        for (int i = 0; i < totalFrames; i++)
        {
            double t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * 440 * t) * short.MaxValue * 0.3);
            for (int ch = 0; ch < channels; ch++)
            {
                writer.Write(sample);
            }
        }

        return path;
    }
}
