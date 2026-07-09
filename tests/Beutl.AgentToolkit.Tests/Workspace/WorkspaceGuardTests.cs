using Beutl.AgentToolkit.Workspace;

namespace Beutl.AgentToolkit.Tests.Workspace;

public class WorkspaceGuardTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"workspace-{Guid.NewGuid():N}");
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

    [Test]
    public void ResolveForWrite_AllowsPathInsideRoot()
    {
        var guard = new WorkspaceGuard(_root);

        string resolved = guard.ResolveForWrite("renders/preview.png");

        Assert.That(resolved, Does.StartWith(Path.GetFullPath(_root)));
        Assert.That(resolved, Does.EndWith(Path.Combine("renders", "preview.png")));
    }

    [Test]
    public void ResolveForWrite_RejectsParentEscape()
    {
        var guard = new WorkspaceGuard(_root);

        Assert.Throws<WorkspaceBoundaryException>(() => guard.ResolveForWrite("../outside.png"));
    }

    [Test]
    public void ResolveForWrite_RejectsInRootSymlinkToOutside()
    {
        var outside = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        string link = Path.Combine(_root, "link");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("Symlink creation is not available in this environment.");
        }
        catch (PlatformNotSupportedException)
        {
            Assert.Ignore("Symlink creation is not available in this environment.");
        }

        try
        {
            var guard = new WorkspaceGuard(_root);
            Assert.Throws<WorkspaceBoundaryException>(() => guard.ResolveForWrite(Path.Combine("link", "escape.png")));
        }
        finally
        {
            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }
}
