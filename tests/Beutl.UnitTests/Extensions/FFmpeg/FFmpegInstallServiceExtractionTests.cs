using System.IO.Compression;
using System.Reflection;
using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public sealed class FFmpegInstallServiceExtractionTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task ExtractZip_WritesEntriesUnderTheDestination()
    {
        string destination = Path.Combine(_root, "ffmpeg");
        string zip = CreateZip(("ffmpeg-n8.0/bin/avcodec.dll", "codec"), ("ffmpeg-n8.0/LICENSE.txt", "license"));

        await ExtractZipAsync(zip, destination);

        Assert.That(File.ReadAllText(Path.Combine(destination, "ffmpeg-n8.0", "bin", "avcodec.dll")), Is.EqualTo("codec"));
        Assert.That(File.ReadAllText(Path.Combine(destination, "ffmpeg-n8.0", "LICENSE.txt")), Is.EqualTo("license"));
    }

    // A sibling whose name starts with the destination's name ("ffmpeg-evil" next to "ffmpeg") shares its
    // path prefix, so the containment check has to compare against the destination plus a separator.
    [TestCase("../ffmpeg-evil/payload.dll", "ffmpeg-evil/payload.dll")]
    [TestCase("bin/../../ffmpeg-evil/payload.dll", "ffmpeg-evil/payload.dll")]
    [TestCase("../payload.dll", "payload.dll")]
    public void ExtractZip_RejectsEntriesOutsideTheDestination(string entryName, string escapedPath)
    {
        string destination = Path.Combine(_root, "ffmpeg");
        string zip = CreateZip((entryName, "payload"));

        Assert.ThrowsAsync<InvalidOperationException>(() => ExtractZipAsync(zip, destination));
        Assert.That(File.Exists(Path.Combine(_root, escapedPath)), Is.False);
    }

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(_root, "archive.zip");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach ((string name, string content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return path;
    }

    private static Task ExtractZipAsync(string zipPath, string destinationPath)
    {
        MethodInfo extract = typeof(FFmpegInstallService).GetMethod(
            "ExtractZipAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)extract.Invoke(new FFmpegInstallService(), [zipPath, destinationPath, CancellationToken.None])!;
    }
}
