using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;

namespace Beutl.AgentToolkit.Tests.Common;

public sealed class ToolPathsTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "tool-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestCase("still.png")]
    [TestCase("renders/still.png")]
    [TestCase("../still.png")]
    [TestCase("")]
    [TestCase("   ")]
    public void RequireAbsolute_RejectsAPathThatIsNotAbsolute(string path)
    {
        ToolError error = Assert.Throws<ReconcileException>(() => ToolPaths.RequireAbsolute(path, "outputPath"))!.Error;

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Target, Is.EqualTo("outputPath"));
        });
    }

    [TestCase(@"\renders\still.png")]
    [TestCase(@"C:renders\still.png")]
    public void RequireAbsolute_RejectsAWindowsPathThatDependsOnTheCurrentDrive(string path)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Drive-relative paths exist only on Windows.");

        Assert.Throws<ReconcileException>(() => ToolPaths.RequireAbsolute(path, "outputPath"));
    }

    [Test]
    public void RequireAbsolute_NormalizesDotSegments()
    {
        string path = Path.Combine(_root, "renders", "..", "still.png");

        Assert.That(ToolPaths.RequireAbsolute(path, "outputPath"), Is.EqualTo(Path.Combine(_root, "still.png")));
    }

    [Test]
    public void ResolveForWrite_AcceptsAnyAbsoluteDirectory()
    {
        string outside = Path.Combine(Path.GetTempPath(), "tool-paths-" + Guid.NewGuid().ToString("N"), "still.png");

        string resolved = ToolPaths.ResolveForWrite(outside, "outputPath");

        Assert.That(FilePathComparison.AreSameCanonicalPath(resolved, outside), Is.True);
    }

    [Test]
    public void ResolveForWrite_UsesTheOnDiskSpellingOfACaseAlias()
    {
        string actual = Directory.CreateDirectory(Path.Combine(_root, "Renders")).FullName;
        File.WriteAllText(Path.Combine(actual, "probe.txt"), "probe");
        string alias = Path.Combine(_root, "renders");
        if (!File.Exists(Path.Combine(alias, "probe.txt")))
            Assert.Ignore("Requires a case-insensitive filesystem.");

        string resolved = ToolPaths.ResolveForWrite(Path.Combine(alias, "still.png"), "outputPath");

        Assert.That(resolved, Is.EqualTo(Path.Combine(FilePathComparison.ResolveCanonicalPath(actual), "still.png")));
    }

    [Test]
    public void ResolveForWrite_FollowsADirectoryLink()
    {
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        string link = Path.Combine(_root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            Assert.Ignore("Symlink creation is not available in this environment.");
        }

        string resolved = ToolPaths.ResolveForWrite(Path.Combine(link, "still.png"), "outputPath");

        Assert.That(resolved, Is.EqualTo(Path.Combine(FilePathComparison.ResolveCanonicalPath(target), "still.png")));
    }
}
