using Beutl.FFmpegWorker;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public class FFmpegLoaderWorkerTests
{
    [Test]
    public void GetRootPath_FindsArchLinuxSystemLibraries()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Exercises Linux library discovery.");

        string root = FFmpegLoaderWorker.GetRootPath(path => path == "/usr/lib");

        Assert.That(root, Is.EqualTo("/usr/lib"));
    }

    [Test]
    public void GetRootPath_BundledLibrariesTakePrecedenceOverSystemLibraries()
    {
        if (!OperatingSystem.IsLinux()) Assert.Ignore("Exercises Linux library discovery.");
        string bundledPath = Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "ffmpeg");

        string root = FFmpegLoaderWorker.GetRootPath(path => path == bundledPath || path == "/usr/lib");

        Assert.That(root, Is.EqualTo(bundledPath));
    }
}
