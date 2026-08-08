using Beutl.Editor.Components.FileBrowserTab.Services;

namespace Beutl.UnitTests.Editor.FileBrowser;

[TestFixture]
public class DirectoryWatcherServiceTests
{
    private string _projectRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _projectRoot = Path.Combine(
            Path.GetTempPath(),
            $"directory-watcher-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectRoot);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_projectRoot, recursive: true);
    }

    [TestCase(".git", true)]
    [TestCase(".git/index", true)]
    [TestCase("assets/.git/objects/ab/cdef", true)]
    [TestCase(".gitkeep", false)]
    [TestCase("assets/.gitkeep", false)]
    [TestCase(".github/workflows/ci.yml", false)]
    [TestCase("assets/.git-cache/file.bin", false)]
    public void Path_filter_excludes_only_exact_git_metadata_segments(
        string relativePath,
        bool expected)
    {
        string path = Path.Combine(
            _projectRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        using var service = new DirectoryWatcherService();

        Assert.That(service.ShouldExcludePath(path), Is.EqualTo(expected));
    }
}
